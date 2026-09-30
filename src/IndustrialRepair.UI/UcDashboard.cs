using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 数据可视化大屏：汇总卡片 + 工单状态柱图 + 近7日趋势折线 + 工程师完工 + 低库存表。
/// 对应任务书「数据可视化大屏」（SunnyUI UIBarChart / UILineChart / UIDataGridView）。
/// </summary>
internal class UcDashboard : UserControl, IRefreshable
{
    private readonly DashboardService _svc = new();
    private readonly Label[] _cards = new Label[5];
    private readonly UIBarChart _barStatus = new() { Dock = DockStyle.Fill };
    private readonly UILineChart _lineTrend = new() { Dock = DockStyle.Fill };
    private readonly UIBarChart _barEngineer = new() { Dock = DockStyle.Fill };
    private readonly UIDataGridView _gridLow = Ui.Grid(0, 0, 100, 100);

    public UcDashboard()
    {
        // ---- 汇总卡片 + CSV 导出（山海鲸等外部大屏的数据源） ----
        var top = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(6) };
        var btnCsv = Ui.Btn("导出CSV\n(大屏数据源)", 0, 0, 118, 60);
        btnCsv.Dock = DockStyle.Right;
        btnCsv.Font = new Font("微软雅黑", 8.5F);
        btnCsv.Click += (_, _) =>
        {
            try { Ui.Info($"大屏数据已导出：\n{new DashboardService().ExportCsv()}\n\n可直接作为山海鲸等大屏工具的数据源。"); }
            catch (Exception ex) { Ui.Error("导出失败：" + ex.Message); }
        };
        top.Controls.Add(btnCsv);
        string[] names = { "今日工单", "待派单", "维修中", "已完成", "本月材料成本(元)" };
        for (int i = 0; i < 5; i++)
        {
            var card = new Label
            {
                Text = $"{names[i]}\n--",
                Dock = DockStyle.Left, Width = (1180 - 40) / 5,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                BackColor = i switch { 1 => Color.FromArgb(230, 162, 60), 2 => Color.FromArgb(48, 116, 222), 3 => Color.FromArgb(80, 160, 90), 4 => Color.FromArgb(130, 90, 160), _ => Color.FromArgb(70, 130, 200) },
                ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(4)
            };
            _cards[i] = card;
            top.Controls.Add(card);
        }
        // 反转 Dock.Left 添加顺序
        for (int i = 0; i < top.Controls.Count; i++) top.Controls[i].BringToFront();

        // ---- 图表区 ----
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        table.Controls.Add(PanelOf("工单状态分布（件）", _barStatus), 0, 0);
        table.Controls.Add(PanelOf("近7日报修趋势（单）", _lineTrend), 1, 0);
        table.Controls.Add(PanelOf("维修工程师完工排行", _barEngineer), 0, 1);
        table.Controls.Add(PanelOf("库存预警（低于安全下限）", GridHost(_gridLow)), 1, 1);
        body.Controls.Add(table);

        Controls.Add(body);
        Controls.Add(top);
    }

    private static Control PanelOf(string title, Control inner)
    {
        var p = new UIPanel { Dock = DockStyle.Fill, Margin = new Padding(4), Radius = 8 };
        var head = new UILabel
        {
            Text = title, Dock = DockStyle.Top, Height = 30, Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 6, 0, 0)
        };
        inner.Dock = DockStyle.Fill;
        p.Controls.Add(inner);
        p.Controls.Add(head);
        return p;
    }

    private static Control GridHost(Control grid)
    {
        var p = new Panel { Dock = DockStyle.Fill };
        grid.Dock = DockStyle.Fill;
        grid.Anchor = AnchorStyles.None;
        p.Controls.Add(grid);
        return p;
    }

    public void RefreshData()
    {
        try
        {
            var (today, pending, working, done, cost) = _svc.Summary();
            string[] vals = { today.ToString(), pending.ToString(), working.ToString(), done.ToString(), cost.ToString("N2") };
            for (int i = 0; i < 5; i++)
                _cards[i].Text = $"{_cards[i].Text.Split('\n')[0]}\n{vals[i]}";

            var rows = _svc.Rows();

            var status = rows.Where(r => r.Kind == "状态分布").ToList();
            if (status.Count > 0)
                _barStatus.ShowData(
                    status.Select(r => OrderStatusCode.ToChinese(r.Label)).ToArray(),
                    status.Select(r => (double)r.Val).ToArray(), "工单状态分布");

            var trend = rows.Where(r => r.Kind == "近7日工单")
                            .OrderBy(r => r.Label).ToList();
            if (trend.Count > 0)
            {
                var option = new UILineOption();
                var series = option.AddSeries("报修工单数", false);
                foreach (var r in trend)
                    series.Add(r.Label.Length > 5 ? r.Label[5..] : r.Label, r.Val);
                _lineTrend.SetOption(option);
                _lineTrend.Refresh();
            }

            var rank = rows.Where(r => r.Kind == "工程师完工").OrderByDescending(r => r.Val).ToList();
            if (rank.Count > 0)
                _barEngineer.ShowData(
                    rank.Select(r => r.Label).ToArray(),
                    rank.Select(r => (double)r.Val).ToArray(), "工程师完工排行");

            var low = new BLL.PartService().LowStock();
            low.Columns["Gap"]!.ColumnName = "缺口";
            foreach (var col in new[] { "PartCode", "PartName", "Spec", "Unit", "StockQty", "MinStock", "CategoryName" })
                if (low.Columns.Contains(col)) low.Columns[col]!.ColumnName = col switch
                {
                    "PartCode" => "编码", "PartName" => "名称", "Spec" => "规格", "Unit" => "单位",
                    "StockQty" => "当前库存", "MinStock" => "下限", _ => "类别"
                };
            _gridLow.DataSource = low;
        }
        catch (Exception ex)
        {
            Ui.Error("大屏数据加载失败：" + DAL.DbHelper.FriendlyError(ex));
        }
    }
}
