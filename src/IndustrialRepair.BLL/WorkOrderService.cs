using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>工单业务：报修（拍照压缩+识别）、派单、接单、完工、取消、报表</summary>
public class WorkOrderService
{
    private readonly WorkOrderRepository _orders = new();
    private readonly BasicRepository _basic = new();
    private readonly RecognitionEngine _recognizer = new();

    /// <summary>照片保存目录（本地压缩后上传的落盘位置）</summary>
    public static string PhotoDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "photos");

    public List<Device> Devices(string? keyword = null) => _basic.ListDevices(keyword);

    /// <summary>
    /// 创建报修工单：照片本地压缩保存 + 拍照识别（特征库匹配）+ 存储过程建单。
    /// </summary>
    public Result<string> Report(int deviceId, int reporterId, string faultDesc, string faultType,
                                 string priority, string? photoSourcePath)
    {
        if (deviceId <= 0) return Result<string>.Fail("请选择报修设备。");
        if (string.IsNullOrWhiteSpace(faultDesc)) return Result<string>.Fail("请填写故障描述。");

        string? savedPath = null;
        string? recognized = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(photoSourcePath) && File.Exists(photoSourcePath))
            {
                savedPath = ImageUtil.CompressSave(photoSourcePath, PhotoDir);   // 本地拍照压缩上传
                var best = _recognizer.Recognize(savedPath)[0];
                if (best != null && best.Score >= 60)
                    recognized = $"自动识别：{best.PartName}({best.PartCode}) 置信度{best.Score}%";
                else if (best != null)
                    recognized = $"识别候选：{best.PartName}({best.PartCode}) 置信度{best.Score}%（低于60%需人工确认）";
            }
            var orderNo = _orders.Create(deviceId, reporterId, faultDesc.Trim(), faultType, priority, savedPath, recognized);
            return Result<string>.Ok(orderNo, $"报修成功，工单号 {orderNo}。");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(DbHelper.FriendlyError(ex));
        }
    }

    public Result Assign(int orderId, int engineerId, int operatorId)
    {
        try { _orders.Assign(orderId, engineerId, operatorId); return Result.Ok("派单成功。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Take(int orderId, int engineerId)
    {
        try { _orders.Take(orderId, engineerId); return Result.Ok("接单成功，开始维修。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Complete(int orderId, int operatorId, string? summary)
    {
        try { _orders.Complete(orderId, operatorId, summary); return Result.Ok("工单已完成。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Cancel(int orderId, int operatorId, string reason)
    {
        try { _orders.Cancel(orderId, operatorId, reason); return Result.Ok("工单已取消。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public DataTable Query(string? status, string? keyword, int? userId, bool onlyMine) =>
        _orders.Query(status, keyword, userId, onlyMine);

    public WorkOrder? Get(int orderId) => _orders.GetById(orderId);
    public List<WorkOrderRecord> Records(int orderId) => _orders.GetRecords(orderId);
    public DataTable ReportSheet() => _orders.Report();
}
