using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>工单（存储过程流转 + 视图查询）</summary>
public class WorkOrderRepository
{
    /// <summary>创建报修工单（usp_CreateOrder），返回新工单号</summary>
    public string Create(int deviceId, int reporterId, string faultDesc, string? faultType,
                         string priority, string? photoPath, string? recognized)
    {
        var outVals = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_CreateOrder", new Dictionary<string, object?>
        {
            ["@DeviceId"] = deviceId, ["@ReporterId"] = reporterId, ["@FaultDesc"] = faultDesc,
            ["@FaultType"] = faultType, ["@Priority"] = priority,
            ["@PhotoPath"] = photoPath, ["@Recognized"] = recognized
        }, new Dictionary<string, SqlDbType> { ["@NewOrderId"] = SqlDbType.Int }, out outVals);
        return Convert.ToInt32(outVals["@NewOrderId"]).ToString();
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
            @"SELECT OrderId,OrderNo,Status,Priority,FaultType,FaultDesc,DeviceCode,DeviceName,
                     ReporterName,EngineerName,CreatedAt,AssignTime,StartTime,FinishTime,PhotoPath,Recognized,RepairSummary
              FROM dbo.vw_WorkOrder_Full WHERE OrderId=@Id", CommandType.Text, DbHelper.P("@Id", orderId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new WorkOrder
        {
            OrderId = (int)r["OrderId"], OrderNo = r["OrderNo"].ToString()!, Status = r["Status"].ToString()!,
            Priority = r["Priority"].ToString()!,
            FaultType = r["FaultType"] == DBNull.Value ? null : r["FaultType"].ToString(),
            FaultDesc = r["FaultDesc"].ToString()!, DeviceCode = r["DeviceCode"].ToString()!,
            DeviceName = r["DeviceName"].ToString()!,
            ReporterName = r["ReporterName"].ToString()!,
            EngineerName = r["EngineerName"] == DBNull.Value ? null : r["EngineerName"].ToString(),
            CreatedAt = (DateTime)r["CreatedAt"],
            AssignTime = r["AssignTime"] == DBNull.Value ? null : (DateTime?)r["AssignTime"],
            StartTime = r["StartTime"] == DBNull.Value ? null : (DateTime?)r["StartTime"],
            FinishTime = r["FinishTime"] == DBNull.Value ? null : (DateTime?)r["FinishTime"],
            PhotoPath = r["PhotoPath"] == DBNull.Value ? null : r["PhotoPath"].ToString(),
            Recognized = r["Recognized"] == DBNull.Value ? null : r["Recognized"].ToString(),
            RepairSummary = r["RepairSummary"] == DBNull.Value ? null : r["RepairSummary"].ToString()
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
