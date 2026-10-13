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
    private readonly Label[] _cards = new Label[6];
    private readonly UIBarChart _barStatus = new() { Dock = DockStyle.Fill };
    private readonly UILineChart _lineTrend = new() { Dock = DockStyle.Fill };
    private readonly UIBarChart _barEngineer = new() { Dock = DockStyle.Fill };
    private readonly UIDataGridView _gridLow = Ui.Grid(0, 0, 100, 100);
    private readonly System.Windows.Forms.Timer _autoTimer = new();
    private UIButton _btnAuto = null!;
    private bool _autoRefresh;

    public UcDashboard()
    {
        // ---- 汇总卡片（6 张：含 R3 库存总价值）+ CSV 导出 + 自动刷新 + 全屏（OR4）----
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

        // F11 切换全屏（OR4）
        var parentForm = FindForm();
        if (parentForm != null)
            parentForm.KeyDown += (_, e) => { if (e.KeyCode == Keys.F11 && Visible) ToggleFullscreen(); };
        string[] names = { "今日工单", "待派单", "维修中", "已完成", "本月材料成本(元)", "库存总价值(元)" };
        for (int i = 0; i < 6; i++)
        {
            var card = new Label
            {
                Text = $"{names[i]}\n--",
                Dock = DockStyle.Left, Width = (1180 - 40) / 6,
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                BackColor = i switch { 1 => Color.FromArgb(230, 162, 60), 2 => Color.FromArgb(48, 116, 222), 3 => Color.FromArgb(80, 160, 90), 4 => Color.FromArgb(130, 90, 160), 5 => Color.FromArgb(160, 110, 60), _ => Color.FromArgb(70, 130, 200) },
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
        table.Controls.Add(PanelOf("近7日：报修工单与出库成本", _lineTrend), 1, 0);
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

    /// <summary>全屏切换（OR4：大屏页独占显示器，再按 F11/按钮恢复）。</summary>
    private void ToggleFullscreen()
    {
        var form = FindForm();
        if (form == null) return;
        if (form.FormBorderStyle == FormBorderStyle.None)
        {
            form.FormBorderStyle = FormBorderStyle.Sizable;
            form.WindowState = FormWindowState.Maximized;
            form.TopMost = false;
        }
        else
        {
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Maximized;
            form.TopMost = true;
            // 隐藏侧边导航（仅大屏内容可见）
            foreach (Control c in form.Controls)
                if (c is Panel p && p.Dock == DockStyle.Left && p.Width <= 220)
                    p.Visible = false;
        }
    }

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
        for (int i = 0; i < 6; i++)
            _cards[i].Text = $"{_cards[i].Text.Split('\n')[0]}\n{vals[i]}";

        var status = rows.Where(r => r.Kind == "状态分布").ToList();
        if (status.Count > 0)
            _barStatus.ShowData(
                status.Select(r => OrderStatusCode.ToChinese(r.Label)).ToArray(),
                status.Select(r => (double)r.Val).ToArray(), "工单状态分布");

        // 近 7 日双系列：报修工单数（左轴）+ 出库成本（右轴，R3）
        var trend = rows.Where(r => r.Kind == "近7日工单").OrderBy(r => r.Label).ToList();
        var costTrend = rows.Where(r => r.Kind == "近7日成本")
                            .OrderBy(r => r.Label).ToDictionary(r => r.Label, r => r.Val);
        if (trend.Count > 0)
        {
            var option = new UILineOption();
            var series = option.AddSeries("报修工单数", false);
            var costSeries = option.AddSeries("出库成本(元)", true);
            foreach (var r in trend)
            {
                var label = r.Label.Length > 5 ? r.Label[5..] : r.Label;
                series.Add(label, r.Val);
                costSeries.Add(label, costTrend.TryGetValue(r.Label, out var c) ? c : 0);
            }
            _lineTrend.SetOption(option);
            _lineTrend.Refresh();
        }

        var rank = rows.Where(r => r.Kind == "工程师完工").OrderByDescending(r => r.Val).ToList();
        if (rank.Count > 0)
            _barEngineer.ShowData(
                rank.Select(r => r.Label).ToArray(),
                rank.Select(r => (double)r.Val).ToArray(), "工程师完工排行");

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
