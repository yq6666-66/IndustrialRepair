using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>数据可视化大屏数据源</summary>
public class DashboardService
{
    private readonly BasicRepository _basic = new();

    public List<DashboardRow> Rows() => _basic.GetDashboard();

    /// <summary>
    /// 大屏汇总数据导出为 CSV（规划：山海鲸等外部大屏先接导出的统计数据，
    /// 课程提供数据库连接能力后可改实时数据源）。UTF-8 带 BOM，Excel/山海鲸均可直接打开。
    /// </summary>
    public string ExportCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("类别,指标,数值");
        foreach (var r in Rows())
            sb.AppendLine($"{r.Kind},{Escape(r.Label)},{r.Val}");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialRepair", "export");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        return file;
    }

    private static string Escape(string field) =>
        field.Contains(',') || field.Contains('"') ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    /// <summary>顶部汇总：今日工单/待派单/维修中/已完成/本月材料成本</summary>
    public (int today, int pending, int working, int done, decimal monthCost) Summary()
    {
        var t = DbHelper.GetTable(
            @"SELECT
                ISNULL(SUM(CASE WHEN CONVERT(date, CreatedAt)=CONVERT(date, GETDATE()) THEN 1 ELSE 0 END), 0) AS TodayCount,
                ISNULL(SUM(CASE WHEN Status='Pending' THEN 1 ELSE 0 END), 0) AS Pending,
                ISNULL(SUM(CASE WHEN Status='Working' THEN 1 ELSE 0 END), 0) AS Working,
                ISNULL(SUM(CASE WHEN Status='Done' THEN 1 ELSE 0 END), 0) AS DoneCount,
                (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.picking_list
                 WHERE Status='Issued' AND CONVERT(NVARCHAR(7), IssuedAt, 120)=CONVERT(NVARCHAR(7), GETDATE(), 120)) AS MonthCost
              FROM dbo.work_order");
        var r = t.Rows[0];
        return ((int)r["TodayCount"], (int)r["Pending"], (int)r["Working"], (int)r["DoneCount"], (decimal)r["MonthCost"]);
    }
}
