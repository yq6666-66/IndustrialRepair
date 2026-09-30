using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>工单业务：报修（拍照压缩+识别）、派单、接单、完工、取消、报表</summary>
public class WorkOrderService
{
    private readonly WorkOrderRepository _orders = new();
    private readonly BasicRepository _basic = new();
    private readonly IImageRecognizer _recognizer = new RecognitionEngine();

    /// <summary>照片保存目录（本地压缩后上传的落盘位置）</summary>
    public static string PhotoDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "photos");

    public List<Device> Devices(string? keyword = null) => _basic.ListDevices(keyword);

    /// <summary>
    /// 创建报修工单：照片本地压缩保存 + 拍照识别（特征库匹配）+ 存储过程建单。
    /// candidates 传入界面预览用过的识别候选（避免重复识别且与预览一致），
    /// 原始候选全部随工单留痕：低置信度时明确标注"需人工确认"。
    /// </summary>
    public Result<string> Report(int deviceId, int reporterId, string faultDesc, string faultType,
                                 string priority, string? photoSourcePath,
                                 IReadOnlyList<RecognizeResult>? candidates = null)
    {
        if (deviceId <= 0) return Result<string>.Fail("请选择报修设备。");
        if (string.IsNullOrWhiteSpace(faultDesc)) return Result<string>.Fail("请填写故障描述。");
        var guard = Guard.Session("order.report");
        if (!guard.Success) return Result<string>.Fail(guard.Message);
        guard = Guard.SameOperator(reporterId);
        if (!guard.Success) return Result<string>.Fail(guard.Message);

        string? savedPath = null;
        string? recognized = null;
        string? recognizedJson = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(photoSourcePath) && File.Exists(photoSourcePath))
            {
                savedPath = ImageUtil.CompressSave(photoSourcePath, PhotoDir);   // 本地拍照压缩上传
                var top = candidates is { Count: > 0 } ? candidates : _recognizer.Recognize(savedPath);
                var best = top[0];
                var candText = string.Join("，", top.Select(r => $"{r.PartName} {r.Score}%"));
                recognized = (best.Score >= 60
                        ? $"自动识别：{best.PartName}({best.PartCode}) 置信度{best.Score}%"
                        : $"识别候选：{best.PartName}({best.PartCode}) 置信度{best.Score}%（低于60%需人工确认）")
                    + $"｜候选：{candText}";
                recognizedJson = SerializeCandidates(top);
            }
            var orderNo = _orders.Create(deviceId, reporterId, faultDesc.Trim(), faultType, priority, savedPath, recognized, recognizedJson);
            return Result<string>.Ok(orderNo, $"报修成功，工单号 {orderNo}。");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(DbHelper.FriendlyError(ex));
        }
    }

    public Result Assign(int orderId, int engineerId, int operatorId)
    {
        var guard = Guard.Session("order.assign");
        if (!guard.Success) return guard;
        guard = Guard.SameOperator(operatorId);
        if (!guard.Success) return guard;
        try { _orders.Assign(orderId, engineerId, operatorId); return Result.Ok("派单成功。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Take(int orderId, int engineerId)
    {
        var guard = Guard.Session("order.handle");
        if (!guard.Success) return guard;
        guard = Guard.SameOperator(engineerId);
        if (!guard.Success) return guard;
        guard = OrderOperator(orderId);
        if (!guard.Success) return guard;
        try { _orders.Take(orderId, engineerId); return Result.Ok("接单成功，开始维修。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Complete(int orderId, int operatorId, string? summary)
    {
        var guard = Guard.Session("order.handle");
        if (!guard.Success) return guard;
        guard = Guard.SameOperator(operatorId);
        if (!guard.Success) return guard;
        guard = OrderOperator(orderId);
        if (!guard.Success) return guard;
        try { _orders.Complete(orderId, operatorId, summary); return Result.Ok("工单已完成。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public Result Cancel(int orderId, int operatorId, string reason)
    {
        var guard = Guard.Session("order.handle");
        if (!guard.Success) return guard;
        guard = Guard.SameOperator(operatorId);
        if (!guard.Success) return guard;
        guard = OrderOperator(orderId);
        if (!guard.Success) return guard;
        try { _orders.Cancel(orderId, operatorId, reason); return Result.Ok("工单已取消。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    /// <summary>工单归属校验（工程师限本人获派单；管理员/调度放行）</summary>
    private Result OrderOperator(int orderId)
    {
        var order = Get(orderId);
        if (order == null) return Result.Fail("工单不存在。");
        return Guard.OrderOperator(order);
    }

    /*==================== 材料审阅（M1）====================*/

    /// <summary>识别候选序列化为版本化 JSON 文档（work_order.RecognizedJson）</summary>
    internal static string SerializeCandidates(IReadOnlyList<RecognizeResult> candidates) =>
        System.Text.Json.JsonSerializer.Serialize(new RecognizedDocument
        {
            Version = 1,
            Engine = "RecognitionEngineV2(ZXing+OpenCV)",
            Candidates = candidates.ToList()
        });

    /// <summary>回读工单的结构化识别候选（无 JSON 或解析失败返回空表，旧工单兜底）</summary>
    public List<RecognizeResult> GetCandidates(int orderId)
    {
        try
        {
            var json = Get(orderId)?.RecognizedJson;
            if (string.IsNullOrWhiteSpace(json)) return new List<RecognizeResult>();
            var doc = System.Text.Json.JsonSerializer.Deserialize<RecognizedDocument>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.Candidates ?? new List<RecognizeResult>();
        }
        catch (System.Text.Json.JsonException)
        {
            FileLog.Write($"工单 #{orderId} 的识别候选 JSON 解析失败，回退为空候选。");
            return new List<RecognizeResult>();
        }
    }

    /// <summary>
    /// 材料审阅（工程师/管理员）：确认重点零件或选择仅整机 BOM。
    /// 规则由 usp_ReviewMaterials 在事务内统一执行：
    /// BOM 内确认仅标记重点；BOM 外补充件必须人工正用量+理由；BomOnly 必填理由；
    /// 存在未出库领料单时禁止更改；审阅字段与留痕记录原子写入。
    /// </summary>
    public Result ReviewMaterials(MaterialReviewRequest request)
    {
        var guard = Guard.Session("order.handle");
        if (!guard.Success) return guard;
        var actor = AppSession.Current!;
        var order = Get(request.OrderId);
        if (order == null) return Result.Fail("工单不存在。");
        guard = Guard.OrderOperator(order);
        if (!guard.Success) return guard;

        if (request.Mode == MaterialReviewMode.PartConfirmed)
        {
            if (request.PartId is not > 0) return Result.Fail("请选择要确认的零件。");
            if (request.BaseQty is <= 0) return Result.Fail("人工基础用量必须为正数。");
        }
        else if (request.Mode != MaterialReviewMode.BomOnly)
            return Result.Fail("审阅模式无效。");

        try
        {
            _orders.ReviewMaterials(request.OrderId, request.Mode, request.PartId,
                request.BaseQty, request.Note, actor.UserId);
            return Result.Ok("材料审阅已保存。");
        }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public DataTable Query(string? status, string? keyword, int? userId, bool onlyMine) =>
        _orders.Query(status, keyword, userId, onlyMine);

    public WorkOrder? Get(int orderId) => _orders.GetById(orderId);
    public List<WorkOrderRecord> Records(int orderId) => _orders.GetRecords(orderId);
    public DataTable ReportSheet() => _orders.Report();
}
