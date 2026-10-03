using System;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>分页查询封装：统一调用 usp_List*Paged 过程并组装修配结果。</summary>
public static class PagedQuery
{
    /// <summary>工单分页（条件与全量 Query 一致；返回列已裁剪适合列表）。</summary>
    public static PagedTable WorkOrders(string? status, string? keyword, bool onlyMine, int userId,
                                        int pageIndex, int pageSize)
    {
        var (ds, outs) = DbHelper.GetDataSetProc("dbo.usp_ListWorkOrdersPaged",
            new Dictionary<string, object?>
            {
                ["@Status"] = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status,
                ["@Keyword"] = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim(),
                ["@OnlyMine"] = onlyMine, ["@Uid"] = (object?)userId ?? DBNull.Value,
                ["@PageIndex"] = pageIndex, ["@PageSize"] = pageSize
            },
            new Dictionary<string, SqlDbType> { ["@TotalCount"] = SqlDbType.Int });
        return Build(ds, Convert.ToInt32(outs["@TotalCount"] ?? 0), pageIndex, pageSize);
    }

    /// <summary>配件分页（关键字 + 类别）。</summary>
    public static PagedTable Parts(string? keyword, int? categoryId, int pageIndex, int pageSize)
    {
        var (ds, outs) = DbHelper.GetDataSetProc("dbo.usp_ListPartsPaged",
            new Dictionary<string, object?>
            {
                ["@Keyword"] = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim(),
                ["@CategoryId"] = (object?)categoryId ?? DBNull.Value,
                ["@PageIndex"] = pageIndex, ["@PageSize"] = pageSize
            },
            new Dictionary<string, SqlDbType> { ["@TotalCount"] = SqlDbType.Int });
        return Build(ds, Convert.ToInt32(outs["@TotalCount"] ?? 0), pageIndex, pageSize);
    }

    /// <summary>领料单分页（状态）。</summary>
    public static PagedTable Pickings(string? status, int pageIndex, int pageSize)
    {
        var (ds, outs) = DbHelper.GetDataSetProc("dbo.usp_ListPickingsPaged",
            new Dictionary<string, object?>
            {
                ["@Status"] = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status,
                ["@PageIndex"] = pageIndex, ["@PageSize"] = pageSize
            },
            new Dictionary<string, SqlDbType> { ["@TotalCount"] = SqlDbType.Int });
        return Build(ds, Convert.ToInt32(outs["@TotalCount"] ?? 0), pageIndex, pageSize);
    }

    private static PagedTable Build(DataSet ds, int total, int pageIndex, int pageSize) =>
        new(ds.Tables[0], total, pageIndex, pageSize);
}
