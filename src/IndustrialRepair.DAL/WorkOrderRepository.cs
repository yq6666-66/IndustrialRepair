using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>工单（存储过程流转 + 视图查询）</summary>
public class WorkOrderRepository
{
    /// <summary>创建报修工单（usp_CreateOrder），返回新工单号；recognizedJson 为结构化识别候选</summary>
    public string Create(int deviceId, int reporterId, string faultDesc, string? faultType,
                         string priority, string? photoPath, string? recognized, string? recognizedJson = null)
    {
        var outVals = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_CreateOrder", new Dictionary<string, object?>
        {
            ["@DeviceId"] = deviceId, ["@ReporterId"] = reporterId, ["@FaultDesc"] = faultDesc,
            ["@FaultType"] = faultType, ["@Priority"] = priority,
            ["@PhotoPath"] = photoPath, ["@Recognized"] = recognized, ["@RecognizedJson"] = recognizedJson
        }, new Dictionary<string, SqlDbType> { ["@NewOrderId"] = SqlDbType.Int }, out outVals);
        return Convert.ToInt32(outVals["@NewOrderId"]).ToString();
    }

    /// <summary>材料审阅（usp_ReviewMaterials）：审阅字段与留痕记录同一事务写入</summary>
    public void ReviewMaterials(int orderId, string mode, int? partId, decimal? baseQty,
                                string? note, int operatorId)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_ReviewMaterials", new Dictionary<string, object?>
        {
            ["@OrderId"] = orderId, ["@Mode"] = mode, ["@PartId"] = partId,
            ["@BaseQty"] = baseQty, ["@Note"] = note, ["@OperatorId"] = operatorId
        }, null, out outs);
    }

    public void Assign(int orderId, int engineerId, int operatorId)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_AssignOrder", new Dictionary<string, object?>
        { ["@OrderId"] = orderId, ["@EngineerId"] = engineerId, ["@OperatorId"] = operatorId }, null, out outs);
    }

    public void Take(int orderId, int engineerId)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_TakeOrder", new Dictionary<string, object?>
        { ["@OrderId"] = orderId, ["@EngineerId"] = engineerId }, null, out outs);
    }

    public void Complete(int orderId, int operatorId, string? summary)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_CompleteOrder", new Dictionary<string, object?>
        { ["@OrderId"] = orderId, ["@OperatorId"] = operatorId, ["@Summary"] = summary }, null, out outs);
    }

    public void Cancel(int orderId, int operatorId, string reason)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_CancelOrder", new Dictionary<string, object?>
        { ["@OrderId"] = orderId, ["@OperatorId"] = operatorId, ["@Reason"] = reason }, null, out outs);
    }

    /// <summary>工单综合查询（视图 vw_WorkOrder_Full）：状态/关键字/与我相关</summary>
    public DataTable Query(string? status, string? keyword, int? userId, bool onlyMine)
    {
        return DbHelper.GetTable(
            @"SELECT OrderId,OrderNo,Status,Priority,FaultType,FaultDesc,DeviceCode,DeviceName,
                     ReporterName,EngineerName,CreatedAt,AssignTime,FinishTime,PhotoPath,Recognized,RepairSummary
              FROM dbo.vw_WorkOrder_Full
              WHERE (@ST IS NULL OR Status=@ST)
                AND (@KW IS NULL OR OrderNo LIKE @KWL OR DeviceName LIKE @KWL OR FaultDesc LIKE @KWL OR ReporterName LIKE @KWL)
                AND (@Mine=0 OR ReporterId=@Uid OR EngineerId=@Uid)
              ORDER BY OrderId DESC",
            CommandType.Text,
            DbHelper.P("@ST", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status),
            DbHelper.P("@KW", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword),
            DbHelper.P("@KWL", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : $"%{keyword}%"),
            DbHelper.P("@Mine", onlyMine), DbHelper.P("@Uid", (object?)userId ?? DBNull.Value));
    }

    public WorkOrder? GetById(int orderId)
    {
        var t = DbHelper.GetTable(
            @"SELECT OrderId,OrderNo,Status,Priority,FaultType,FaultDesc,DeviceId,DeviceCode,DeviceName,
                     ReporterId,EngineerId,ReporterName,EngineerName,CreatedAt,AssignTime,StartTime,FinishTime,
                     PhotoPath,Recognized,RecognizedJson,RepairSummary,
                     MaterialReviewMode,ConfirmedPartId,ConfirmedPartCode,ConfirmedPartName,ConfirmedBaseQty,
                     ReviewNote,ReviewedBy,ReviewedByName,ReviewedAt
              FROM dbo.vw_WorkOrder_Full WHERE OrderId=@Id", CommandType.Text, DbHelper.P("@Id", orderId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new WorkOrder
        {
            OrderId = (int)r["OrderId"], OrderNo = r["OrderNo"].ToString()!, Status = r["Status"].ToString()!,
            Priority = r["Priority"].ToString()!,
            FaultType = r["FaultType"] == DBNull.Value ? null : r["FaultType"].ToString(),
            FaultDesc = r["FaultDesc"].ToString()!, DeviceId = (int)r["DeviceId"],
            DeviceCode = r["DeviceCode"].ToString()!,
            DeviceName = r["DeviceName"].ToString()!,
            ReporterId = (int)r["ReporterId"],
            EngineerId = r["EngineerId"] == DBNull.Value ? null : (int)r["EngineerId"],
            ReporterName = r["ReporterName"].ToString()!,
            EngineerName = r["EngineerName"] == DBNull.Value ? null : r["EngineerName"].ToString(),
            PhotoPath = r["PhotoPath"] == DBNull.Value ? null : r["PhotoPath"].ToString(),
            Recognized = r["Recognized"] == DBNull.Value ? null : r["Recognized"].ToString(),
            RecognizedJson = r["RecognizedJson"] == DBNull.Value ? null : r["RecognizedJson"].ToString(),
            RepairSummary = r["RepairSummary"] == DBNull.Value ? null : r["RepairSummary"].ToString(),
            MaterialReviewMode = r["MaterialReviewMode"] == DBNull.Value ? null : r["MaterialReviewMode"].ToString(),
            ConfirmedPartId = r["ConfirmedPartId"] == DBNull.Value ? null : (int)r["ConfirmedPartId"],
            ConfirmedPartCode = r["ConfirmedPartCode"] == DBNull.Value ? null : r["ConfirmedPartCode"].ToString(),
            ConfirmedPartName = r["ConfirmedPartName"] == DBNull.Value ? null : r["ConfirmedPartName"].ToString(),
            ConfirmedBaseQty = r["ConfirmedBaseQty"] == DBNull.Value ? null : (decimal)r["ConfirmedBaseQty"],
            ReviewNote = r["ReviewNote"] == DBNull.Value ? null : r["ReviewNote"].ToString(),
            ReviewedBy = r["ReviewedBy"] == DBNull.Value ? null : (int)r["ReviewedBy"],
            ReviewedByName = r["ReviewedByName"] == DBNull.Value ? null : r["ReviewedByName"].ToString(),
            ReviewedAt = r["ReviewedAt"] == DBNull.Value ? null : (DateTime?)r["ReviewedAt"],
            CreatedAt = (DateTime)r["CreatedAt"],
            AssignTime = r["AssignTime"] == DBNull.Value ? null : (DateTime?)r["AssignTime"],
            StartTime = r["StartTime"] == DBNull.Value ? null : (DateTime?)r["StartTime"],
            FinishTime = r["FinishTime"] == DBNull.Value ? null : (DateTime?)r["FinishTime"]
        };
    }

    /// <summary>流转记录（维修历史）</summary>
    public List<WorkOrderRecord> GetRecords(int orderId)
    {
        var list = new List<WorkOrderRecord>();
        var t = DbHelper.GetTable(
            "SELECT RecordId,OrderId,Action,Operator,Note,CreatedAt FROM dbo.work_order_record WHERE OrderId=@Id ORDER BY RecordId",
            CommandType.Text, DbHelper.P("@Id", orderId));
        foreach (DataRow r in t.Rows)
            list.Add(new WorkOrderRecord
            {
                RecordId = (int)r["RecordId"], OrderId = (int)r["OrderId"], Action = r["Action"].ToString()!,
                Operator = r["Operator"] == DBNull.Value ? null : r["Operator"].ToString(),
                Note = r["Note"] == DBNull.Value ? null : r["Note"].ToString(),
                CreatedAt = (DateTime)r["CreatedAt"]
            });
        return list;
    }

    /// <summary>维修报表（视图 vw_RepairReport）</summary>
    public DataTable Report() =>
        DbHelper.GetTable("SELECT [月份],[工单数],[完成数],[取消数],[平均处理分钟],[材料成本] FROM dbo.vw_RepairReport ORDER BY [月份]");
}
