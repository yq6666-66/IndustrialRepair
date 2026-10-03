using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>智能算料 / 领料</summary>
public class PickingRepository : IPickingRepository
{
    /// <summary>智能算料生成领料单（usp_GeneratePicking：BOM×冗余，事务+回滚）</summary>
    public int Generate(int orderId, string operatorName, decimal redundancy)
    {
        var outVals = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_GeneratePicking", new Dictionary<string, object?>
        {
            ["@OrderId"] = orderId, ["@Operator"] = operatorName, ["@Redundancy"] = redundancy
        }, new Dictionary<string, SqlDbType> { ["@NewPickingId"] = SqlDbType.Int }, out outVals);
        return Convert.ToInt32(outVals["@NewPickingId"]);
    }

    /// <summary>领料出库（usp_IssuePicking：事务扣库存，不足抛 SqlException 由上层转友好提示）</summary>
    public void Issue(int pickingId, string operatorName)
    {
        var outs = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_IssuePicking", new Dictionary<string, object?>
        { ["@PickingId"] = pickingId, ["@Operator"] = operatorName }, null, out outs);
    }

    public DataTable List(string? status, string? keyword)
    {
        return DbHelper.GetTable(
            @"SELECT PickingId,PickingNo,OrderNo,DeviceName,GeneratedBy,Redundancy,TotalAmount,Status,CreatedAt,IssuedAt,OrderId
              FROM dbo.vw_PickingDetail
              WHERE (@ST IS NULL OR Status=@ST)
                AND (@KW IS NULL OR PickingNo LIKE @KWL OR OrderNo LIKE @KWL)
              ORDER BY PickingId DESC",
            CommandType.Text,
            DbHelper.P("@ST", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status),
            DbHelper.P("@KW", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword),
            DbHelper.P("@KWL", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : $"%{keyword}%"));
    }

    public List<PickingItem> GetItems(int pickingId)
    {
        var list = new List<PickingItem>();
        var t = DbHelper.GetTable(
            @"SELECT ItemId,PickingId,PartId,PartCode,PartName,Qty,UnitPrice,Amount,Remark
              FROM dbo.vw_PickingDetail WHERE PickingId=@Id", CommandType.Text, DbHelper.P("@Id", pickingId));
        foreach (DataRow r in t.Rows)
            list.Add(new PickingItem
            {
                ItemId = (int)r["ItemId"], PickingId = (int)r["PickingId"], PartId = (int)r["PartId"],
                PartCode = r["PartCode"].ToString()!, PartName = r["PartName"].ToString()!,
                Qty = (int)r["Qty"], UnitPrice = (decimal)r["UnitPrice"], Amount = (decimal)r["Amount"],
                Remark = r["Remark"] == DBNull.Value ? null : r["Remark"].ToString()
            });
        return list;
    }
}
