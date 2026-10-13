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
    private readonly UILabel[] _cardValues = new UILabel[6];
    private readonly UIBarChart _barStatus = DarkChart(new UIBarChart());
    private readonly UILineChart _lineTrend = DarkChart(new UILineChart());
    private readonly UIBarChart _barEngineer = DarkChart(new UIBarChart());
    private readonly UIDataGridView _gridLow = Ui.Grid(0, 0, 100, 100);
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private UIButton _btnAuto = null!;
    private bool _autoRefresh;

    public UcDashboard()
    {
        // ---- 汇总卡片（6 张：含 R3 库存总价值）+ CSV 导出 + 自动刷新 + 全屏（OR4）----
        var top = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(6), BackColor = Theme.Current.PageBg };
        top.Controls.Add(BuildCards());   // Fill 先加入，后加入的按钮 Dock.Right 先停靠
        var btnCsv = Ui.Btn("导出CSV\n(大屏数据源)", 0, 0, 118, 60);
        btnCsv.Dock = DockStyle.Right;
        btnCsv.Font = new Font("微软雅黑", 8.5F);
        btnCsv.Click += (_, _) =>
        {
            try { Ui.Info($"大屏数据已导出：\n{new DashboardService().ExportCsv()}\n\n可直接作为山海鲸等大屏工具的数据源。"); }
            catch (Exception ex) { Ui.Error("导出失败：" + ex.Message); }
        };
        top.Controls.Add(btnCsv);

        // OR4：自动刷新（30 秒）+ 全屏按钮
        _btnAuto = Ui.Btn("自动刷新\n(关)", 0, 0, 96, 60);
        _btnAuto.Dock = DockStyle.Right;
        _btnAuto.Font = new Font("微软雅黑", 8.5F);
        _btnAuto.Click += (_, _) =>
        {
            _autoRefresh = !_autoRefresh;
            _btnAuto.Text = _autoRefresh ? "自动刷新\n(30s 开)" : "自动刷新\n(关)";
            if (_autoRefresh) _autoTimer.Start(); else _autoTimer.Stop();
        };
        top.Controls.Add(_btnAuto);

        var btnFull = Ui.Btn("全屏\n(F11)", 0, 0, 76, 60);
        btnFull.Dock = DockStyle.Right;
        btnFull.Font = new Font("微软雅黑", 8.5F);
        btnFull.Click += (_, _) => ToggleFullscreen();
        top.Controls.Add(btnFull);

        _autoTimer.Interval = 30_000;
        _autoTimer.Tick += (_, _) => SafeRefreshAsync();
        Disposed += (_, _) => { _autoTimer.Stop(); _autoTimer.Dispose(); };

        // ---- 图表区 ----
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = Theme.Current.PageBg };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Current.PageBg };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        table.Controls.Add(PanelOf("工单状态分布（件）", _barStatus), 0, 0);
        table.Controls.Add(PanelOf("近7日：报修工单与出库成本", _lineTrend), 1, 0);
        table.Controls.Add(PanelOf("维修工程师完工排行", _barEngineer), 0, 1);
        table.Controls.Add(PanelOf("库存预警（低于安全下限）", GridHost(_gridLow)), 1, 1);
        body.Controls.Add(table);

        Controls.Add(body);
        Controls.Add(top);
    }

    /// <summary>6 张 KPI 卡：深色卡片 + 左侧状态色条 + 电光数值，6 列等分随窗口伸缩。</summary>
    private TableLayoutPanel BuildCards()
    {
        var t = Theme.Current;
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = t.PageBg, Margin = new Padding(0) };
        for (int i = 0; i < 6; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        string[] names = { "今日工单", "待派单", "维修中", "已完成", "本月材料成本(元)", "库存总价值(元)" };
        Color[] accents = { t.Accent, t.Warning, t.Cyan, t.Success, t.Violet, t.Glow };
        for (int i = 0; i < 6; i++)
        {
            var card = new UIPanel
            {
                Dock = DockStyle.Fill, Margin = new Padding(4), Radius = 8,
                StyleCustomMode = true, FillColor = t.CardBg, RectColor = t.Border
            };
            var value = new UILabel
            {
                Text = "--", Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 6),
                Font = new Font("微软雅黑", 16F, FontStyle.Bold), ForeColor = accents[i],
                TextAlign = ContentAlignment.MiddleLeft
            };
            var name = new UILabel
            {
                Text = names[i], Dock = DockStyle.Top, Height = 28, Padding = new Padding(12, 6, 0, 0),
                Font = new Font("微软雅黑", 9F), ForeColor = t.LabelColor
            };
            var strip = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accents[i] };
            _cardValues[i] = value;
            card.Controls.Add(value);
            card.Controls.Add(name);
            card.Controls.Add(strip);
            row.Controls.Add(card, i, 0);
        }
        return row;
    }

    /// <summary>工单状态 → 主题语义色（柱图逐项着色）。</summary>
    private static Color StatusColor(string statusCode) => statusCode switch
    {
        OrderStatusCode.Pending => Theme.Current.Warning,
        OrderStatusCode.Assigned => Theme.Current.Cyan,
        OrderStatusCode.Working => Theme.Current.Accent,
        OrderStatusCode.Done => Theme.Current.Success,
        OrderStatusCode.Cancelled => Theme.Current.TextSecondary,
        _ => Theme.Current.Accent
    };

    /// <summary>图表统一切到 SunnyUI 深色样式，坐标与边框取主题色。</summary>
    private static T DarkChart<T>(T chart) where T : UIChart
    {
        Ui.ApplyChartPalette();
        chart.Dock = DockStyle.Fill;
        chart.ChartStyleType = UIChartStyleType.Dark;
        chart.ForeColor = Theme.Current.TextSecondary;
        chart.RectColor = Theme.Current.Border;
        return chart;
    }

    private static Control PanelOf(string title, Control inner)
    {
        var p = new UIPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(4), Radius = 8,
            StyleCustomMode = true, FillColor = Theme.Current.CardBg, RectColor = Theme.Current.Border
        };
        var head = new UILabel
        {
            Text = title, Dock = DockStyle.Top, Height = 30, Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 6, 0, 0),
            ForeColor = Theme.Current.TextPrimary
        };
        inner.Dock = DockStyle.Fill;
        p.Controls.Add(inner);
        p.Controls.Add(head);
        return p;
    }

    private static Control GridHost(Control grid)
    {
        // 注意：Dock=Fill 之后再设 Anchor 会把 Dock 重置回 None（控件退化成创建尺寸）——
        // 低库存表曾因此缩成 100×100 小方块，此处只保留 Dock。
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Current.CardBg };
        grid.Dock = DockStyle.Fill;
        p.Controls.Add(grid);
        return p;
    }

    /// <summary>线程安全刷新（定时器回调）。</summary>
    private void SafeRefresh()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(RefreshData);
        else RefreshData();
    }

    /// <summary>
    /// VR4：定时自动刷新走后台取数——远程 SQL Server 场景下 30 秒一次的查询
    /// 不再阻塞 UI 线程；取数完成后再回 UI 线程绘制。导航/F5 仍走同步 RefreshData
    /// （列宽恢复依赖 DataSource 同步就位）。
    /// </summary>
    private void SafeRefreshAsync()
    {
        if (IsDisposed || !IsHandleCreated) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            (int today, int pending, int working, int done, decimal cost, decimal stockValue) summary;
            List<DashboardRow> rows;
            System.Data.DataTable low;
            try
            {
                summary = _svc.Summary();
                rows = _svc.Rows();
                low = new PartService().LowStock();
            }
            catch (Exception ex)
            {
                BeginInvoke(() => Ui.Error("大屏数据加载失败：" + DAL.DbHelper.FriendlyError(ex)));
                return;
            }
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                try { ApplyData(summary, rows, low); }
                catch (Exception ex) { Ui.Error("大屏数据渲染失败：" + ex.Message); }
            });
        });
    }

    /// <summary>全屏切换（OR4：大屏页独占显示器，再按 F11/按钮恢复；导航显隐随状态机同步还原）。</summary>
    public void ToggleFullscreen()
    {
        var form = FindForm();
        if (form == null) return;
        var current = new DashboardFullscreen.State(form.FormBorderStyle == FormBorderStyle.None, NavPanels(form).All(p => p.Visible));
        var next = DashboardFullscreen.Toggle(current);
        form.FormBorderStyle = next.Fullscreen ? FormBorderStyle.None : FormBorderStyle.Sizable;
        form.WindowState = FormWindowState.Maximized;
        form.TopMost = next.Fullscreen;
        foreach (var nav in NavPanels(form)) nav.Visible = next.NavVisible;
    }

    /// <summary>主窗体左侧导航面板（宽度 ≤220 的左停靠 Panel）。</summary>
    private static IEnumerable<Panel> NavPanels(Control form) =>
        form.Controls.OfType<Panel>().Where(p => p.Dock == DockStyle.Left && p.Width <= 220);

    public void RefreshData()
    {
        try
        {
            var summary = _svc.Summary();
            var rows = _svc.Rows();
            var low = new PartService().LowStock();
            ApplyData(summary, rows, low);
        }
        catch (Exception ex)
        {
            Ui.Error("大屏数据加载失败：" + DAL.DbHelper.FriendlyError(ex));
        }
    }

    /// <summary>VR4：绘制阶段（必须在 UI 线程调用）——同步与后台两条取数路径共用。</summary>
    private void ApplyData(
        (int today, int pending, int working, int done, decimal cost, decimal stockValue) summary,
        List<DashboardRow> rows,
        System.Data.DataTable low)
    {
        string[] vals = { summary.today.ToString(), summary.pending.ToString(), summary.working.ToString(), summary.done.ToString(), summary.cost.ToString("N2"), summary.stockValue.ToString("N2") };
        for (int i = 0; i < 6; i++) _cardValues[i].Text = vals[i];

        // 柱图手工建 Option：series.AddData(名, 值, 色) 的逐项颜色会覆盖样式默认色（SunnyUI 源码：series.Colors[j] 优先）。
        var status = rows.Where(r => r.Kind == "状态分布").ToList();
        var optStatus = new UIBarOption();
        optStatus.Title.Text = "工单状态分布";
        var seriesStatus = new UIBarSeries(0) { Name = "工单状态分布" };
        if (status.Count > 0)
        {
            foreach (var r in status)
            {
                var label = OrderStatusCode.ToChinese(r.Label);
                optStatus.XAxis.Data.Add(label);
                seriesStatus.AddData(label, (double)r.Val, StatusColor(r.Label));
            }
        }
        else
        {
            optStatus.XAxis.Data.Add("暂无数据");
            seriesStatus.AddData("暂无数据", 0, Theme.Current.TextSecondary);
        }
        optStatus.AddSeries(seriesStatus);
        _barStatus.SetOption(optStatus);
        _barStatus.Refresh();

        // 近 7 日双系列：报修工单数（左轴）+ 出库成本（右轴，R3）。
        // 标签在客户端固定生成最近 7 天，缺数补 0——即使库中无数据也画出正确的时间轴。
        var trendMap = rows.Where(r => r.Kind == "近7日工单").ToDictionary(r => r.Label, r => r.Val);
        var costMap = rows.Where(r => r.Kind == "近7日成本").ToDictionary(r => r.Label, r => r.Val);
        var option = new UILineOption { XAxisType = UIAxisType.DateTime };
        option.Title.Text = "";
        var series = option.AddSeries("报修工单数", false);
        var costSeries = option.AddSeries("出库成本(元)", true);
        series.CustomColor = true;
        series.Color = Theme.Current.Accent;
        series.XAxisDateTimeFormat = "MM-dd";
        costSeries.CustomColor = true;
        costSeries.Color = Theme.Current.Warning;
        costSeries.XAxisDateTimeFormat = "MM-dd";
        for (int i = 6; i >= 0; i--)
        {
            var day = DateTime.Today.AddDays(-i);
            var key = day.ToString("yyyy-MM-dd");
            series.Add(day, trendMap.TryGetValue(key, out var v) ? v : 0);
            costSeries.Add(day, costMap.TryGetValue(key, out var c) ? c : 0);
        }
        _lineTrend.SetOption(option);
        _lineTrend.Refresh();

        var rank = rows.Where(r => r.Kind == "工程师完工").OrderByDescending(r => r.Val).ToList();
        var optRank = new UIBarOption();
        optRank.Title.Text = "工程师完工排行";
        var seriesRank = new UIBarSeries(0) { Name = "工程师完工排行" };
        var palette = new[] { Theme.Current.Accent, Theme.Current.Cyan, Theme.Current.Violet, Theme.Current.Success, Theme.Current.Warning };
        if (rank.Count > 0)
        {
            for (int i = 0; i < rank.Count; i++)
            {
                optRank.XAxis.Data.Add(rank[i].Label);
                seriesRank.AddData(rank[i].Label, (double)rank[i].Val, palette[i % palette.Length]);
            }
        }
        else
        {
            optRank.XAxis.Data.Add("暂无数据");
            seriesRank.AddData("暂无数据", 0, Theme.Current.TextSecondary);
        }
        optRank.AddSeries(seriesRank);
        _barEngineer.SetOption(optRank);
        _barEngineer.Refresh();

        low.Columns["Gap"]!.ColumnName = "缺口";
        foreach (var col in new[] { "PartCode", "PartName", "Spec", "Unit", "StockQty", "MinStock", "CategoryName" })
            if (low.Columns.Contains(col)) low.Columns[col]!.ColumnName = col switch
            {
                "PartCode" => "编码", "PartName" => "名称", "Spec" => "规格", "Unit" => "单位",
                "StockQty" => "当前库存", "MinStock" => "下限", _ => "类别"
            };
        // DG2：显式列 + BindingSource
        Ui.BindExplicit(_gridLow, low, "dash-low-v1",
            new GridCol("编码", "编码", FillWeight: 85),
            new GridCol("名称", "名称", FillWeight: 120),
            new GridCol("规格", "规格", FillWeight: 110),
            new GridCol("单位", "单位", FillWeight: 45),
            new GridCol("当前库存", "当前库存", FillWeight: 70),
            new GridCol("下限", "下限", FillWeight: 55),
            new GridCol("缺口", "缺口", FillWeight: 55),
            new GridCol("类别", "类别", FillWeight: 70));
    }
}

/// <summary>大屏全屏状态机（纯函数）：进入全屏隐藏导航，退出全屏必须还原。</summary>
public static class DashboardFullscreen
{
    public readonly record struct State(bool Fullscreen, bool NavVisible);

    public static State Toggle(State current) =>
        current.Fullscreen ? new State(false, true) : new State(true, false);
}
