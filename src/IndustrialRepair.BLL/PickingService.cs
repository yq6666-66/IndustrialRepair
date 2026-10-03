using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>智能算料与领料：BOM×冗余系数生成清单、出库、CSV 导出</summary>
public class PickingService
{
    private readonly IPickingRepository _repo;
    private readonly IWorkOrderRepository _orders;

    /// <summary>默认构造（生产）。仓储可注入替代（R2 可测试性）。</summary>
    public PickingService(IPickingRepository? repo = null, IWorkOrderRepository? orders = null)
    {
        _repo = repo ?? new PickingRepository();
        _orders = orders ?? new WorkOrderRepository();
    }

    /// <summary>智能算料：根据工单设备 BOM×冗余系数自动生成领料清单（需登录 + picking.generate）</summary>
    public Result<int> Generate(int orderId, string operatorName, decimal redundancy)
    {
        if (redundancy < 1.0m || redundancy > 2.0m)
            return Result<int>.Fail("冗余系数应在 1.00 ~ 2.00 之间（任务书要求“留有一定冗余”）。");
        var guard = Guard.Session("picking.generate");
        if (!guard.Success) return Result<int>.Fail(guard.Message);
        guard = Guard.SameOperator(operatorName);
        if (!guard.Success) return Result<int>.Fail(guard.Message);
        var order = _orders.GetById(orderId);
        if (order == null) return Result<int>.Fail("工单不存在。");
        guard = Guard.OrderOperator(order);   // 工程师限本人获派单；库管/管理员放行
        if (!guard.Success) return Result<int>.Fail(guard.Message);
        try
        {
            var id = _repo.Generate(orderId, operatorName, redundancy);
            return Result<int>.Ok(id, $"领料单已生成，可根据明细核对后出库。");
        }
        catch (SqlException ex) { return Result<int>.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result<int>.Fail(ex.Message); }
    }

    /// <summary>领料出库（事务扣库存；库存不足自动回滚并给出缺口明细；需 picking.issue）</summary>
    public Result Issue(int pickingId, string operatorName)
    {
        var guard = Guard.Session("picking.issue");
        if (!guard.Success) return guard;
        guard = Guard.SameOperator(operatorName);
        if (!guard.Success) return guard;
        try { _repo.Issue(pickingId, operatorName); return Result.Ok("出库成功，库存已扣减并写入流水。"); }
        catch (SqlException ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public DataTable List(string? status, string? keyword) => _repo.List(status, keyword);
    public List<PickingItem> Items(int pickingId) => _repo.GetItems(pickingId);

    /// <summary>领料单 PDF 导出（QuestPDF，Community 许可；返回文件路径）</summary>
    public string ExportPdf(int pickingId) => PdfService.ExportPickingPdf(pickingId);

    /// <summary>导出领料清单 CSV（供仓库打印/对账）</summary>
    public string ExportCsv(int pickingId)
    {
        var list = _repo.GetItems(pickingId);
        var sb = new StringBuilder();
        sb.AppendLine("配件编码,配件名称,数量,单价,金额,备注");
        foreach (var i in list)
            sb.AppendLine($"{i.PartCode},{i.PartName},{i.Qty},{i.UnitPrice:F2},{i.Amount:F2},{i.Remark}");
        var total = 0m; foreach (var i in list) total += i.Amount;
        sb.AppendLine($",合计,,,,{total:F2}");
        var file = Path.Combine(Path.GetTempPath(), $"领料清单_{pickingId}_{DateTime.Now:HHmmss}.csv");
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));   // BOM：Excel 中文不乱码
        return file;
    }

    /// <summary>导出领料清单 Excel（开源库 ClosedXML：样式+合计行）</summary>
    public string ExportExcel(int pickingId)
    {
        var list = _repo.GetItems(pickingId);
        var t = new DataTable();
        t.Columns.Add("配件编码", typeof(string));
        t.Columns.Add("配件名称", typeof(string));
        t.Columns.Add("数量", typeof(int));
        t.Columns.Add("单价", typeof(decimal));
        t.Columns.Add("金额", typeof(decimal));
        t.Columns.Add("备注", typeof(string));
        foreach (var i in list)
            t.Rows.Add(i.PartCode, i.PartName, i.Qty, i.UnitPrice, i.Amount, i.Remark);
        return ExcelService.ExportTable(t, $"领料清单#{pickingId}", "领料清单", totalRow: true);
    }
}
