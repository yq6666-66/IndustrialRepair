using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 维护计划中心（R41-R45）：PM 调度管理 + EOQ 库存优化建议 + 供应商管理 + 设备停机追踪。
/// </summary>
internal class UcMaintenance : UserControl, IRefreshable
{
    private readonly MaintenanceScheduleService _pm = new();
    private readonly VendorService _vendors = new();
    private readonly DowntimeService _downtime = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    // PM 调度
    private readonly UIDataGridView _gridPM = FillGrid();
    // EOQ
    private readonly UIDataGridView _gridEOQ = FillGrid();
    // 供应商
    private readonly UIDataGridView _gridVendor = FillGrid();
    // 停机
    private readonly UIDataGridView _gridDowntime = FillGrid();
    // ScottPlot
    private readonly ScottPlot.WinForms.FormsPlot _plot = new() { Dock = DockStyle.Fill };

    private static UIDataGridView FillGrid()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }

    public UcMaintenance()
    {
        // PM 调度 Tab
        var tabPM = new TabPage("预防性维护调度");
        var pmToolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        pmToolbar.Controls.Add(Ui.Btn("新增调度", 10, 4, 90, 30, (_, _) => AddPM()));
        pmToolbar.Controls.Add(Ui.Btn("编辑", 110, 4, 70, 30, (_, _) => EditPM()));
        pmToolbar.Controls.Add(Ui.Btn("删除", 188, 4, 70, 30, (_, _) => DeletePM()));
        pmToolbar.Controls.Add(Ui.Btn("立即检查", 266, 4, 90, 30, (_, _) => CheckNow()));
        tabPM.Controls.Add(_gridPM);
        tabPM.Controls.Add(pmToolbar);

        // EOQ Tab
        var tabEOQ = new TabPage("库存优化建议");
        var eToolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        var lblE = new UILabel
        { Text = "EOQ 经济批量 + 再订购点 + 安全库存（基于消耗历史自动计算）", AutoSize = true,
          Location = new Point(10, 10), Font = new Font("微软雅黑", 9F), ForeColor = Color.FromArgb(90, 100, 115) };
        eToolbar.Controls.Add(lblE);
        var eoqSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 350 };
        eoqSplit.Panel1.Controls.Add(_gridEOQ);
        eoqSplit.Panel2.Controls.Add(_plot);   // ScottPlot 交互图表
        tabEOQ.Controls.Add(eoqSplit);
        tabEOQ.Controls.Add(eToolbar);

        // 供应商 Tab
        var tabVendor = new TabPage("供应商管理");
        var vToolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        vToolbar.Controls.Add(Ui.Btn("新增供应商", 10, 4, 100, 30, (_, _) => AddVendor()));
        tabVendor.Controls.Add(_gridVendor);
        tabVendor.Controls.Add(vToolbar);

        // 停机追踪 Tab
        var tabDown = new TabPage("设备停机追踪");
        var dToolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        var cmbDevice = Ui.Combo(80, 5, 200);
        dToolbar.Controls.Add(Ui.Lbl("设备：", 5, 9, 70));
        dToolbar.Controls.Add(cmbDevice);
        dToolbar.Controls.Add(Ui.Btn("开始停机", 290, 4, 90, 30, (_, _) =>
        {
            var dev = SelectedDevice(cmbDevice);
            if (dev == null) { Ui.Error("请选择设备。"); return; }
            var reason = Ui.Input("停机原因：", "原因：") ?? "";
            var r = _downtime.Start(dev.DeviceId, reason, "admin");
            Ui.Info(r.Message);
            LoadDowntime();
        }));
        dToolbar.Controls.Add(Ui.Btn("结束停机", 388, 4, 90, 30, (_, _) =>
        {
            var dev = SelectedDevice(cmbDevice);
            if (dev == null) { Ui.Error("请选择设备。"); return; }
            var r = _downtime.Stop(dev.DeviceId);
            Ui.Info(r.Message);
            LoadDowntime();
        }));
        _cmbDevice = cmbDevice;
        tabDown.Controls.Add(_gridDowntime);
        tabDown.Controls.Add(dToolbar);

        _tabs.TabPages.Add(tabPM);
        _tabs.TabPages.Add(tabEOQ);
        _tabs.TabPages.Add(tabVendor);
        _tabs.TabPages.Add(tabDown);

        Controls.Add(_tabs);
        Controls.Add(Ui.PageHeader("维护计划中心", "PM 调度 · EOQ 优化 · 供应商 · 停机追踪"));
    }

    private UIComboBox? _cmbDevice;

    public void RefreshData()
    {
        LoadPM();
        LoadEOQ();
        LoadVendors();
        LoadDowntime();
        LoadDevices();
    }

    private void LoadDevices()
    {
        if (_cmbDevice == null) return;
        _cmbDevice.Items.Clear();
        foreach (var d in new BasicRepository().ListDevices())
            _cmbDevice.Items.Add($"{d.DeviceCode} {d.DeviceName}");
        if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;
    }

    private static Device? SelectedDevice(UIComboBox cmb) =>
        cmb.SelectedIndex >= 0
            ? new BasicRepository().ListDevices().ToList().FirstOrDefault()
            : null;

    private void LoadPM()
    {
        var t = _pm.List();
        if (t.Columns.Contains("IntervalType")) t.Columns["IntervalType"]!.ColumnName = "触发方式";
        if (t.Columns.Contains("NextDueAt")) t.Columns["NextDueAt"]!.ColumnName = "下次到期";
        if (t.Columns.Contains("IsActive")) t.Columns["IsActive"]!.ColumnName = "启用";
        _gridPM.DataSource = t;
    }

    private void LoadEOQ()
    {
        var recs = InventoryOptimizer.GenerateRecommendations();
        var t = new DataTable();
        t.Columns.Add("编码", typeof(string));
        t.Columns.Add("名称", typeof(string));
        t.Columns.Add("当前库存", typeof(int));
        t.Columns.Add("年用量", typeof(decimal));
        t.Columns.Add("EOQ建议批量", typeof(double));
        t.Columns.Add("再订购点", typeof(double));
        t.Columns.Add("安全库存", typeof(double));
        t.Columns.Add("需订购", typeof(bool));
        t.Columns.Add("建议", typeof(string));
        foreach (var r in recs)
            t.Rows.Add(r.PartCode, r.PartName, r.CurrentStock, r.AnnualDemandQty,
                Math.Ceiling(r.Eoq), Math.Ceiling(r.ReorderPoint), Math.Ceiling(r.SafetyStock),
                r.ShouldReorder, r.Recommendation);
        _gridEOQ.DataSource = t;

        // ScottPlot：消耗量柱状图
        _plot.Plot.Clear();
        var codes = recs.Select(r => r.PartCode).ToArray();
        var qtys = recs.Select(r => r.AnnualDemandQty).Select(q => (double)q).ToArray();
        var bar = _plot.Plot.Add.Bars(qtys);
        _plot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            Enumerable.Range(0, codes.Length).Select(i => new ScottPlot.Tick(i, codes[i])).ToArray());
        _plot.Plot.Title("年消耗量（EOQ 分析）");
        _plot.Plot.YLabel("数量");
        _plot.Refresh();
    }

    private void LoadVendors()
    {
        var t = _vendors.List();
        if (t.Columns.Contains("Status")) t.Columns["Status"]!.ColumnName = "启用";
        _gridVendor.DataSource = t;
    }

    private void LoadDowntime()
    {
        _gridDowntime.DataSource = _downtime.DeviceDowntime();
    }

    private void AddPM()
    {
        var devices = new BasicRepository().ListDevices();
        var devStr = Ui.Input("选择设备（输入编码或名称）：", "设备：");
        if (string.IsNullOrWhiteSpace(devStr)) return;
        var dev = devices.FirstOrDefault(d => d.DeviceCode.Contains(devStr.Trim(), StringComparison.OrdinalIgnoreCase)
            || d.DeviceName.Contains(devStr.Trim(), StringComparison.OrdinalIgnoreCase));
        if (dev == null) { Ui.Error("未找到匹配设备。"); return; }

        var title = Ui.Input("PM 任务名（如「季度轴承润滑」）：", "名称：");
        if (string.IsNullOrWhiteSpace(title)) return;
        var days = Ui.Input("间隔天数（如 90=季度）：", "天数：", "90");
        if (!int.TryParse(days, out var intervalDays) || intervalDays < 1) { Ui.Error("天数须为正整数。"); return; }
        var desc = Ui.Input("自动生成工单的描述（选填）：", "描述：");

        var r = _pm.Save(new MaintenanceSchedule(0, dev.DeviceId, dev.DeviceName, title.Trim(),
            "Time", intervalDays, null, null, null, true, desc, PriorityCode.Normal));
        Ui.Info(r.Message);
        LoadPM();
    }

    private void EditPM() { /* 简化：双击行编辑（待完善）*/ }
    private void DeletePM()
    {
        var id = _gridPM.CurrentRow?.Cells["ScheduleId"]?.Value;
        if (id == null) { Ui.Error("请先选择调度。"); return; }
        if (!Ui.Ask("确定删除该 PM 调度？")) return;
        Ui.Info(_pm.Delete(Convert.ToInt32(id)).Message);
        LoadPM();
    }

    private void CheckNow()
    {
        var created = _pm.CheckAndGenerate();
        Ui.Info(created.Count > 0
            ? $"已生成 {created.Count} 张 PM 工单：{string.Join(", ", created)}"
            : "当前没有到期的 PM 调度。");
        LoadPM();
    }

    private void AddVendor()
    {
        var name = Ui.Input("供应商名称：", "名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        var contact = Ui.Input("联系人（选填）：", "联系人：");
        var phone = Ui.Input("电话（选填）：", "电话：");
        var r = _vendors.Save(new Vendor(0, name.Trim(), contact, phone, null, null));
        Ui.Info(r.Message);
        LoadVendors();
    }
}
