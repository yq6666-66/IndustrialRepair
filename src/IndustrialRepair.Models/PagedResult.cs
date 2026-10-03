using System;
using System.Collections.Generic;
using System.Data;

namespace IndustrialRepair.Models;

/// <summary>分页结果（R1 大数据量）。</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageIndex, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasPrev => PageIndex > 1;
    public bool HasNext => PageIndex < TotalPages;
}

/// <summary>表格分页结果（DataTable 直接绑定 DataGridView）。</summary>
public record PagedTable(DataTable Items, int TotalCount, int PageIndex, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasPrev => PageIndex > 1;
    public bool HasNext => PageIndex < TotalPages;
}
