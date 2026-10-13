using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>配件 / 类别 / 库存</summary>
public class PartRepository
{
    /// <summary>模糊检索（编码/名称/规格），兼容类别与启停过滤</summary>
    public List<Part> Search(string? keyword, int? categoryId = null, bool includeDisabled = false)
    {
        var sql = @"SELECT p.PartId,p.PartCode,p.PartName,p.CategoryId,c.CategoryName,p.Spec,p.Unit,
                           p.Price,p.StockQty,p.MinStock,p.FeatureJson,p.Status
                    FROM dbo.part p JOIN dbo.part_category c ON c.CategoryId=p.CategoryId
                    WHERE (@KW IS NULL OR p.PartCode LIKE @KWL OR p.PartName LIKE @KWL OR p.Spec LIKE @KWL)
                      AND (@Cat IS NULL OR p.CategoryId=@Cat)
                      AND (@IncDis=1 OR p.Status=1)
                    ORDER BY p.PartCode";
        var t = DbHelper.GetTable(sql, CommandType.Text,
            DbHelper.P("@KW", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword),
            DbHelper.P("@KWL", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : $"%{keyword}%"),
            DbHelper.P("@Cat", (object?)categoryId ?? DBNull.Value),
            DbHelper.P("@IncDis", includeDisabled));
        return ToList(t);
    }

    public Part? GetById(int partId)
    {
        var t = DbHelper.GetTable(
            @"SELECT p.PartId,p.PartCode,p.PartName,p.CategoryId,c.CategoryName,p.Spec,p.Unit,
                     p.Price,p.StockQty,p.MinStock,p.FeatureJson,p.Status
              FROM dbo.part p JOIN dbo.part_category c ON c.CategoryId=p.CategoryId
              WHERE p.PartId=@Id", CommandType.Text, DbHelper.P("@Id", partId));
        return t.Rows.Count == 0 ? null : ToList(t)[0];
    }

    public List<Part> ToList(DataTable t)
    {
        var list = new List<Part>();
        foreach (DataRow r in t.Rows)
            list.Add(new Part
            {
                PartId = (int)r["PartId"],
                PartCode = r["PartCode"].ToString()!,
                PartName = r["PartName"].ToString()!,
                CategoryId = (int)r["CategoryId"],
                CategoryName = r["CategoryName"].ToString()!,
                Spec = r["Spec"] == DBNull.Value ? null : r["Spec"].ToString(),
                Unit = r["Unit"].ToString()!,
                Price = (decimal)r["Price"],
                StockQty = (int)r["StockQty"],
                MinStock = (int)r["MinStock"],
                FeatureJson = r["FeatureJson"] == DBNull.Value ? null : r["FeatureJson"].ToString(),
                Status = (bool)r["Status"]
            });
        return list;
    }

    public int Insert(Part p) =>
        Convert.ToInt32(DbHelper.ExecuteScalar(
            @"INSERT INTO dbo.part(PartCode,PartName,CategoryId,Spec,Unit,Price,StockQty,MinStock,FeatureJson,Status)
              VALUES(@Code,@Name,@Cat,@Spec,@Unit,@Price,@Stock,@Min,@Feat,@Status);
              SELECT SCOPE_IDENTITY();",
            CommandType.Text,
            DbHelper.P("@Code", p.PartCode), DbHelper.P("@Name", p.PartName), DbHelper.P("@Cat", p.CategoryId),
            DbHelper.P("@Spec", (object?)p.Spec ?? DBNull.Value), DbHelper.P("@Unit", p.Unit),
            DbHelper.P("@Price", p.Price), DbHelper.P("@Stock", p.StockQty), DbHelper.P("@Min", p.MinStock),
            DbHelper.P("@Feat", (object?)p.FeatureJson ?? DBNull.Value), DbHelper.P("@Status", p.Status)));

    public void Update(Part p) =>
        DbHelper.ExecuteNonQuery(
            @"UPDATE dbo.part SET PartCode=@Code,PartName=@Name,CategoryId=@Cat,Spec=@Spec,Unit=@Unit,
                     Price=@Price,StockQty=@Stock,MinStock=@Min,FeatureJson=@Feat,Status=@Status
              WHERE PartId=@Id",
            CommandType.Text,
            DbHelper.P("@Code", p.PartCode), DbHelper.P("@Name", p.PartName), DbHelper.P("@Cat", p.CategoryId),
            DbHelper.P("@Spec", (object?)p.Spec ?? DBNull.Value), DbHelper.P("@Unit", p.Unit),
            DbHelper.P("@Price", p.Price), DbHelper.P("@Stock", p.StockQty), DbHelper.P("@Min", p.MinStock),
            DbHelper.P("@Feat", (object?)p.FeatureJson ?? DBNull.Value), DbHelper.P("@Status", p.Status),
            DbHelper.P("@Id", p.PartId));

    public void Delete(int partId) =>
        DbHelper.ExecuteNonQuery("DELETE FROM dbo.part WHERE PartId=@Id", CommandType.Text, DbHelper.P("@Id", partId));

    /// <summary>
    /// DG4：批量删除配件（对齐课程「批量删除 + SqlTransaction」范式）——
    /// 单一事务内逐个处理：被业务引用（BOM / 领料明细 / 库存流水 / 库存预警 / 工单确认零件）
    /// 的配件跳过并记录编码，其余连同其供应商关联（配置数据）一并物理删除；
    /// 任一步骤异常整体回滚（要么全提交、要么全撤销）。
    /// </summary>
    public (int Deleted, List<string> SkippedCodes) BatchDelete(List<int> partIds)
    {
        int deleted = 0;
        var skipped = new List<string>();
        DbHelper.RunInTransaction((conn, tran) =>
        {
            foreach (var id in partIds)
            {
                // 1) 引用检查：被任一业务表引用即跳过
                int refs;
                using (var check = new SqlCommand(
                    @"SELECT
                        (SELECT COUNT(*) FROM dbo.bom              WHERE PartId=@Id)
                      + (SELECT COUNT(*) FROM dbo.picking_item     WHERE PartId=@Id)
                      + (SELECT COUNT(*) FROM dbo.part_stock_log   WHERE PartId=@Id)
                      + (SELECT COUNT(*) FROM dbo.stock_alert      WHERE PartId=@Id)
                      + (SELECT COUNT(*) FROM dbo.work_order       WHERE ConfirmedPartId=@Id)", conn, tran))
                {
                    check.Parameters.AddWithValue("@Id", id);
                    refs = Convert.ToInt32(check.ExecuteScalar() ?? 0);
                }
                if (refs > 0)
                {
                    using var codeCmd = new SqlCommand("SELECT PartCode FROM dbo.part WHERE PartId=@Id", conn, tran);
                    codeCmd.Parameters.AddWithValue("@Id", id);
                    skipped.Add(codeCmd.ExecuteScalar()?.ToString() ?? id.ToString());
                    continue;
                }
                // 2) 供应商关联为纯配置数据，随配件一并删除
                using (var unlink = new SqlCommand("DELETE FROM dbo.part_vendor WHERE PartId=@Id", conn, tran))
                {
                    unlink.Parameters.AddWithValue("@Id", id);
                    unlink.ExecuteNonQuery();
                }
                // 3) 物理删除
                using (var del = new SqlCommand("DELETE FROM dbo.part WHERE PartId=@Id", conn, tran))
                {
                    del.Parameters.AddWithValue("@Id", id);
                    deleted += del.ExecuteNonQuery();
                }
            }
        });
        return (deleted, skipped);
    }

    public List<PartCategory> ListCategories()
    {
        var list = new List<PartCategory>();
        var t = DbHelper.GetTable("SELECT CategoryId,CategoryName FROM dbo.part_category ORDER BY CategoryId");
        foreach (DataRow r in t.Rows)
            list.Add(new PartCategory { CategoryId = (int)r["CategoryId"], CategoryName = r["CategoryName"].ToString()! });
        return list;
    }

    /// <summary>配件入库（存储过程 usp_StockIn：事务扣加库存+流水+预警闭环）</summary>
    public void StockIn(int partId, int qty, string refNo)
    {
        var outVals = new Dictionary<string, object?>();
        DbHelper.ExecProc("dbo.usp_StockIn",
            new Dictionary<string, object?> { ["@PartId"] = partId, ["@Qty"] = qty, ["@RefNo"] = refNo },
            null, out outVals);
    }

    /// <summary>低库存预警（视图 vw_LowStock）</summary>
    public DataTable LowStock() =>
        DbHelper.GetTable("SELECT PartCode,PartName,Spec,Unit,StockQty,MinStock,CategoryName,Gap FROM dbo.vw_LowStock ORDER BY Gap DESC");

    /// <summary>库存流水（partId&le;0 表示全部配件）</summary>
    public DataTable StockLogs(int partId, int top = 100) =>
        DbHelper.GetTable($"SELECT TOP ({top}) l.StockLogId,p.PartCode,p.PartName,l.ChangeQty,l.AfterQty,l.RefNo,l.CreatedAt " +
                          "FROM dbo.part_stock_log l JOIN dbo.part p ON p.PartId=l.PartId " +
                          "WHERE (@Id IS NULL OR l.PartId=@Id) ORDER BY l.StockLogId DESC",
            CommandType.Text, DbHelper.P("@Id", partId > 0 ? partId : DBNull.Value));
}
