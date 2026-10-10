using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>配件库业务：CRUD 校验、入库、低库存</summary>
public class PartService
{
    private readonly PartRepository _repo = new();

    public List<Part> Search(string? keyword, int? categoryId = null, bool includeDisabled = false) =>
        _repo.Search(keyword, categoryId, includeDisabled);

    public List<PartCategory> Categories() => _repo.ListCategories();

    public Result<Part> Save(Part p)
    {
        if (string.IsNullOrWhiteSpace(p.PartCode)) return Result<Part>.Fail("请填写配件编码。");
        if (string.IsNullOrWhiteSpace(p.PartName)) return Result<Part>.Fail("请填写配件名称。");
        if (p.Price < 0 || p.StockQty < 0 || p.MinStock < 0) return Result<Part>.Fail("价格与库存不能为负数。");
        if (p.CategoryId <= 0) return Result<Part>.Fail("请选择类别。");
        try
        {
            if (p.PartId == 0) { _repo.Insert(p); return Result<Part>.Ok(p, "新增配件成功。"); }
            _repo.Update(p);
            return Result<Part>.Ok(p, "保存成功。");
        }
        catch (Exception ex) { return Result<Part>.Fail(DbHelper.FriendlyError(ex)); }
    }

    public Result Delete(int partId)
    {
        try { _repo.Delete(partId); return Result.Ok("删除成功。"); }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>配件入库（调用存储过程，事务保证 库存+流水+预警 一致）</summary>
    public Result StockIn(int partId, int qty, string refNo)
    {
        if (qty <= 0) return Result.Fail("入库数量必须大于 0。");
        try { _repo.StockIn(partId, qty, string.IsNullOrWhiteSpace(refNo) ? "MANUAL" : refNo); return Result.Ok($"入库 {qty} 成功。"); }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    public DataTable LowStock() => _repo.LowStock();
    public DataTable StockLogs(int partId) => _repo.StockLogs(partId);
}
