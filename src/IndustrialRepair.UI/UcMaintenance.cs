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
        { Text = "EOQ 经济批量 + 再订购点 + 安全库存", AutoSize = true,
          Location = new Point(10, 10), Font = new Font("微软雅黑", 9F), ForeColor = Color.FromArgb(90, 100, 115) };
        var btnStockIn = Ui.Btn("按建议入库", 320, 4, 100, 30, (_, _) => EoqStockIn());
        eToolbar.Controls.Add(lblE);
        eToolbar.Controls.Add(btnStockIn);
        var eoqSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 350 };
        eoqSplit.Panel1.Controls.Add(_gridEOQ);
        eoqSplit.Panel2.Controls.Add(_plot);   // ScottPlot 交互图表
        tabEOQ.Controls.Add(eoqSplit);
        tabEOQ.Controls.Add(eToolbar);

        // 供应商 Tab
        var tabVendor = new TabPage("供应商管理");
        var vToolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        vToolbar.Controls.Add(Ui.Btn("新增", 10, 4, 70, 30, (_, _) => AddVendor()));
        vToolbar.Controls.Add(Ui.Btn("编辑", 88, 4, 70, 30, (_, _) => EditVendor()));
        vToolbar.Controls.Add(Ui.Btn("删除", 166, 4, 70, 30, (_, _) => DeleteVendor()));
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
            // FR12：停机原因分类下拉（替代自由输入）
            var reasons = new[] { "计划维护", "故障停机", "电气故障", "机械故障", "操作失误", "待料", "其他" };
            var reason = Ui.Input($"停机原因（可选：{string.Join("/", reasons)}）：", "原因：", "计划维护") ?? "计划维护";
            var r = _downtime.Start(dev.DeviceId, reason, AppSession.Current?.UserName ?? "system");
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
        _tabs.SelectedIndexChanged += (_, _) =>   // FR19：切 Tab 刷新对应数据
        {
            switch (_tabs.SelectedIndex)
            {
                case 0: LoadPM(); break;
                case 1: LoadEOQ(); break;
                case 2: LoadVendors(); break;
                case 3: LoadDowntime(); break;
            }
        };

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

    private List<Device> _deviceCache = new();

    private void LoadDevices()
    {
        if (_cmbDevice == null) return;
        _deviceCache = new BasicRepository().ListDevices().ToList();
        _cmbDevice.Items.Clear();
        foreach (var d in _deviceCache)
            _cmbDevice.Items.Add($"{d.DeviceCode} {d.DeviceName}");
        if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;
    }

    /// <summary>FR2 修复：按选中索引映射正确 DeviceId（原实现恒返第一台）。</summary>
    private Device? SelectedDevice(UIComboBox cmb) =>
        cmb != null && cmb.SelectedIndex >= 0 && cmb.SelectedIndex < _deviceCache.Count
            ? _deviceCache[cmb.SelectedIndex]
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
        _gridDowntime.DataSource = _downtime.AvailabilityReport(30);   // FR11：含可用率
    }

    private void AddPM()
    {
        using var dlg = new FPMEdit(null);
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return;
        var r = _pm.Save(dlg.Result);
        Ui.Info(r.Message);
        LoadPM();
    }

    private void EditPM()
    {
        var row = _gridPM.CurrentRow;
        if (row == null) { Ui.Error("请先选择调度。"); return; }
        var id = Convert.ToInt32(row.Cells["ScheduleId"]?.Value ?? 0);
        if (id == 0) return;
        // 从 List 结果构建编辑对象
        var t = _pm.List();
        var dr = t.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["ScheduleId"]) == id);
        if (dr == null) return;
        var existing = new MaintenanceSchedule(
            id, Convert.ToInt32(dr["DeviceId"]), dr["DeviceName"].ToString()!,
            dr["Title"].ToString()!, dr["IntervalType"].ToString()!,
            dr["IntervalDays"] == DBNull.Value ? null : Convert.ToInt32(dr["IntervalDays"]),
            dr["IntervalHours"] == DBNull.Value ? null : Convert.ToDecimal(dr["IntervalHours"]),
            dr["LastExecutedAt"] == DBNull.Value ? null : Convert.ToDateTime(dr["LastExecutedAt"]),
            dr["NextDueAt"] == DBNull.Value ? null : Convert.ToDateTime(dr["NextDueAt"]),
            Convert.ToBoolean(dr["IsActive"]),
            dr["TemplateDesc"] == DBNull.Value ? null : dr["TemplateDesc"].ToString(),
            dr["TemplatePriority"].ToString()!);
        using var dlg = new FPMEdit(existing);
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return;
        var r2 = _pm.Save(dlg.Result);
        Ui.Info(r2.Message);
        LoadPM();
    }
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
        using var dlg = new FVendorEdit(null);
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return;
        var r = _vendors.Save(dlg.Result);
        Ui.Info(r.Message);
        LoadVendors();
    }

    private void EditVendor()
    {
        var row = _gridVendor.CurrentRow;
        if (row == null) { Ui.Error("请先选择供应商。"); return; }
        var id = Convert.ToInt32(row.Cells["VendorId"]?.Value ?? 0);
        var t = _vendors.List();
        var dr = t.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["VendorId"]) == id);
        if (dr == null) return;
        var existing = new Vendor(id, dr["VendorName"].ToString()!,
            dr["Contact"] == DBNull.Value ? null : dr["Contact"].ToString(),
            dr["Phone"] == DBNull.Value ? null : dr["Phone"].ToString(),
            dr["Email"] == DBNull.Value ? null : dr["Email"].ToString(),
            dr["Address"] == DBNull.Value ? null : dr["Address"].ToString(),
            Convert.ToBoolean(dr["Status"]));
        using var dlg = new FVendorEdit(existing);
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return;
        var r2 = _vendors.Save(dlg.Result);
        Ui.Info(r2.Message);
        LoadVendors();
    }

    private void DeleteVendor()
    {
        var row = _gridVendor.CurrentRow;
        if (row == null) { Ui.Error("请先选择供应商。"); return; }
        var name = row.Cells["VendorName"]?.Value?.ToString() ?? "";
        if (!Ui.Ask($"确定删除供应商「{name}」？")) return;
        var id = Convert.ToInt32(row.Cells["VendorId"]?.Value ?? 0);
        try
        {
            DbHelper.ExecuteNonQuery("DELETE dbo.part_vendor WHERE VendorId=@Id; DELETE dbo.vendor WHERE VendorId=@Id",
                CommandType.Text, DbHelper.P("@Id", id));
            Ui.Info("已删除。");
            LoadVendors();
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>FR8：EOQ 一键入库——对 ShouldReorder 配件按 EOQ 建议批量入库。</summary>
    private void EoqStockIn()
    {
        var recs = InventoryOptimizer.GenerateRecommendations().Where(r => r.ShouldReorder).ToList();
        if (recs.Count == 0) { Ui.Info("当前没有需订购的配件。"); return; }
        if (!Ui.Ask($"共 {recs.Count} 项需订购，确定按 EOQ 建议批量入库？")) return;

        var partSvc = new PartService();
        var refNo = $"EOQ:{DateTime.Now:HHmmss}";
        var (ok, fail) = (0, 0);
        foreach (var rec in recs)
        {
            try
            {
                var parts = partSvc.Search(rec.PartCode);
                var part = parts.FirstOrDefault(p => p.PartCode == rec.PartCode);
                if (part == null) { fail++; continue; }
                partSvc.StockIn(part.PartId, (int)Math.Ceiling(rec.Eoq), refNo);
                ok++;
            }
            catch { fail++; }
        }
        Ui.Info($"EOQ 入库完成：成功 {ok} / 失败 {fail}（单号 {refNo}）。");
        LoadEOQ();
    }
}
