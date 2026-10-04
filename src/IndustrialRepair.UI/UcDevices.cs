using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 设备台账与 BOM 材料表维护：算料准确性的数据基础。
/// 左右分隔（SplitContainer）承载设备/BOM 双表，消除锚点互挤。
/// </summary>
internal class UcDevices : UserControl, IRefreshable
{
    private readonly BasicRepository _basic = new();
    private readonly PartService _parts = new();
    private readonly UIDataGridView _gridDev = FillGrid();
    private readonly UIDataGridView _gridBom = FillGrid();

    private static UIDataGridView FillGrid()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }

    public UcDevices()
    {
        var btnAdd = Ui.Btn("新增设备", 10, 8, 90, 32, (_, _) => EditDevice(null));
        var btnEdit = Ui.Btn("编辑", 105, 8, 70, 32, (_, _) =>
        {
            var d = SelectedDevice(); if (d == null) return;
            EditDevice(d);
        });
        var btnDel = Ui.Btn("删除", 180, 8, 70, 32, (_, _) =>
        {
            var d = SelectedDevice(); if (d == null) return;
            if (!Ui.Ask($"确认删除设备「{d.DeviceName}」？")) return;
            try { _basic.DeleteDevice(d.DeviceId); RefreshData(); }
            catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
        });

        var lblBom = new UILabel
        { Text = "BOM 材料表（单台维修常规用量）", AutoSize = true, Location = new Point(6, 12), Font = new Font("微软雅黑", 9.5F, FontStyle.Bold) };
        var btnBomAdd = Ui.Btn("添加BOM项", 260, 8, 95, 32, OnBomAdd);
        var btnBomQty = Ui.Btn("改用量", 360, 8, 75, 32, OnBomQty);
        var btnBomDel = Ui.Btn("移除", 440, 8, 70, 32, OnBomDel);

        // 左：设备（工具栏 + 表）；右：BOM（标题 + 工具栏 + 表）
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 42, 4, 0) };
        left.Controls.Add(_gridDev);
        left.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDel });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 42, 0, 0) };
        right.Controls.Add(_gridBom);
        right.Controls.AddRange(new Control[] { lblBom, btnBomAdd, btnBomQty, btnBomDel });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            SplitterDistance = 420, FixedPanel = FixedPanel.Panel1
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(Ui.PageHeader("设备与BOM", "设备台账 · BOM 用量维护（算料数据基础）"));

        _gridDev.SelectionChanged += (_, _) => LoadBom();
        Ui.BindRowDblClick(_gridDev, _ =>   // R22：双击查看维护历史
        {
            var d = SelectedDevice();
            if (d == null) return;
            using var dlg = new FDeviceHistory(d);
            dlg.ShowDialog();
        });
    }

    private Device? SelectedDevice() =>
        int.TryParse(_gridDev.CurrentRow?.Cells["DeviceId"]?.Value?.ToString(), out var id)
            ? _basic.ListDevices().FirstOrDefault(d => d.DeviceId == id) : null;

    public void RefreshData()
    {
        try
        {
            var devs = _basic.ListDevices();
            var t = new System.Data.DataTable();
            t.Columns.Add("DeviceId", typeof(int));
            t.Columns.Add("编码", typeof(string));
            t.Columns.Add("名称", typeof(string));
            t.Columns.Add("位置", typeof(string));
            foreach (var d in devs) t.Rows.Add(d.DeviceId, d.DeviceCode, d.DeviceName, d.Location);
            _gridDev.DataSource = t;
            LoadBom();
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    private void LoadBom()
    {
        var dev = SelectedDevice();
        if (dev == null) { _gridBom.DataSource = null; return; }
        var bom = _basic.ListBom(dev.DeviceId);
        var t = new System.Data.DataTable();
        t.Columns.Add("BomId", typeof(int));
        t.Columns.Add("编码", typeof(string));
        t.Columns.Add("名称", typeof(string));
        t.Columns.Add("单台用量", typeof(decimal));
        t.Columns.Add("备注", typeof(string));
        foreach (var b in bom) t.Rows.Add(b.BomId, b.PartCode, b.PartName, b.QtyPer, b.Remark);
        _gridBom.DataSource = t;
    }

    /// <summary>新增/编辑设备：规范对话框一次录入（编码/名称/位置/启用），替代连环输入弹窗</summary>
    private void EditDevice(Device? d)
    {
        using var dlg = new FDeviceEdit(d);
        if (dlg.ShowDialog() == DialogResult.OK) RefreshData();
    }

    private void OnBomAdd(object? s, EventArgs e)
    {
        var dev = SelectedDevice();
        if (dev == null) { Ui.Error("请先选择设备。"); return; }
        var code = Ui.Input("添加BOM项", "配件编码（如 P-ZC-001）：");
        if (string.IsNullOrWhiteSpace(code)) return;
        var part = _parts.Search(code.Trim()).FirstOrDefault(p => p.PartCode == code.Trim());
        if (part == null) { Ui.Error("配件编码不存在。"); return; }
        var qty = Ui.Input("添加BOM项", $"「{part.PartName}」单台用量：", "1");
        if (!decimal.TryParse(qty, out var q) || q <= 0) { Ui.Error("用量必须是正数。"); return; }
        var remark = Ui.Input("添加BOM项", "备注（选填）：");
        try { _basic.AddBom(dev.DeviceId, part.PartId, q, remark); LoadBom(); }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    private void OnBomQty(object? s, EventArgs e)
    {
        if (!int.TryParse(_gridBom.CurrentRow?.Cells["BomId"]?.Value?.ToString(), out var bomId) || bomId == 0)
        { Ui.Error("请先选择 BOM 项。"); return; }
        var qty = Ui.Input("修改用量", "新的单台用量：");
        if (!decimal.TryParse(qty, out var q) || q <= 0) { Ui.Error("用量必须是正数。"); return; }
        _basic.UpdateBomQty(bomId, q);
        LoadBom();
    }

    private void OnBomDel(object? s, EventArgs e)
    {
        if (!int.TryParse(_gridBom.CurrentRow?.Cells["BomId"]?.Value?.ToString(), out var bomId) || bomId == 0)
        { Ui.Error("请先选择 BOM 项。"); return; }
        if (!Ui.Ask("确认移除该 BOM 项？")) return;
        _basic.DeleteBom(bomId);
        LoadBom();
    }
}

/// <summary>设备新增/编辑对话框（编码/名称/位置/启用，一次录入）</summary>
internal class FDeviceEdit : UIForm
{
    private readonly Device? _device;
    private readonly UITextBox _code = Ui.Txt("如 DEV-009", 100, 20, 220);
    private readonly UITextBox _name = Ui.Txt("设备名称", 100, 58, 220);
    private readonly UITextBox _loc = Ui.Txt("安装位置（选填）", 100, 96, 220);
    private readonly UICheckBox _status = new() { Text = "启用", Location = new Point(98, 138), Size = new Size(80, 26), Checked = true };

    public FDeviceEdit(Device? d)
    {
        _device = d;
        Text = d == null ? "新增设备" : "编辑设备";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(360, 230);
        StartPosition = FormStartPosition.CenterParent;

        if (d != null)
        {
            _code.Text = d.DeviceCode; _name.Text = d.DeviceName;
            _loc.Text = d.Location ?? ""; _status.Checked = d.Status;
        }

        var ok = Ui.Btn("保存", 100, 176, 110, 34, OnSave);
        var back = Ui.Btn("取消", 220, 176, 110, 34, (_, _) => Close());
        Controls.AddRange(new Control[]
        {
            Ui.Lbl("编码：", 10, 20, 85), _code,
            Ui.Lbl("名称：", 10, 58, 85), _name,
            Ui.Lbl("位置：", 10, 96, 85), _loc,
            Ui.Lbl("状态：", 10, 138, 85), _status, ok, back
        });
    }

    private void OnSave(object? s, EventArgs e)
    {
        var invalid = new DeviceInputValidator().FirstError(new DeviceInput
        {
            DeviceCode = _code.Text.Trim(), DeviceName = _name.Text.Trim(),
            Location = _loc.Text, Status = _status.Checked
        });
        if (invalid != null) { Ui.Error(invalid); return; }
        try
        {
            new BasicRepository().SaveDevice(new Device
            {
                DeviceId = _device?.DeviceId ?? 0,
                DeviceCode = _code.Text.Trim(),
                DeviceName = _name.Text.Trim(),
                Location = string.IsNullOrWhiteSpace(_loc.Text) ? null : _loc.Text.Trim(),
                Status = _status.Checked
            });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }
}

/// <summary>维修报表（vw_RepairReport + 高成本工单 TopN，R3 时间范围筛选）</summary>
internal class UcReport : UserControl, IRefreshable
{
    private readonly WorkOrderService _svc = new();
    private readonly DashboardService _dash = new();
    private readonly UIDataGridView _grid = FillGrid2();
    private readonly UIDataGridView _gridCost = FillGrid2();
    private readonly UIDataGridView _gridConsumption = FillGrid2();
    private readonly UIDataGridView _gridDeviceCost = FillGrid2();
    private readonly UIComboBox _cmbRange = Ui.Combo(360, 8, 130);
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private static UIDataGridView FillGrid2()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }

    public UcReport()
    {
        var btn = Ui.Btn("刷新", 900, 10, 80, 32, (_, _) => RefreshData());
        var btnXls = Ui.Btn("导出Excel", 990, 10, 95, 32, (_, _) =>
        {
            try
            {
                var grid = _tabs.SelectedTab?.Controls.OfType<UIDataGridView>().FirstOrDefault();
                if (grid?.DataSource is not System.Data.DataTable t || t.Rows.Count == 0) { Ui.Error("无数据可导出。"); return; }
                Ui.Info("已导出：" + ExcelService.ExportTable(t, $"维修报表_{_tabs.SelectedTab?.Text}", _tabs.SelectedTab?.Text ?? "Sheet1", totalRow: true));
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        });

        _cmbRange.Items.AddRange(new object[] { "全部月份", "近 6 个月", "今年" });
        _cmbRange.SelectedIndex = 0;
        _cmbRange.DropDownStyle = UIDropDownStyle.DropDownList;
        _cmbRange.SelectedIndexChanged += (_, _) => RefreshData();

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 46 };
        var lbl = new UILabel
        {
            Text = "多维度维修报表", AutoSize = true,
            Location = new Point(10, 12), Font = new Font("微软雅黑", 9F), ForeColor = Color.FromArgb(90, 100, 115)
        };
        var lblRange = new UILabel { Text = "范围：", AutoSize = true, Location = new Point(310, 13), Font = new Font("微软雅黑", 9F) };
        toolbar.Controls.AddRange(new Control[] { lbl, lblRange, _cmbRange, btn, btnXls });

        // R21/R24：TabControl 四页签
        var tabMonthly = new TabPage("月度统计") ;
        tabMonthly.Controls.Add(_grid);
        var tabCost = new TabPage("高成本工单");
        tabCost.Controls.Add(_gridCost);
        var tabConsumption = new TabPage("消耗分析");
        tabConsumption.Controls.Add(_gridConsumption);
        var tabDevice = new TabPage("设备成本");
        tabDevice.Controls.Add(_gridDeviceCost);
        _tabs.TabPages.Add(tabMonthly);
        _tabs.TabPages.Add(tabCost);
        _tabs.TabPages.Add(tabConsumption);
        _tabs.TabPages.Add(tabDevice);

        Controls.Add(_tabs);
        Controls.Add(toolbar);
        Controls.Add(Ui.PageHeader("维修报表", "月度/成本/消耗/设备 · 多维度分析 · 可导出 Excel"));
    }

    public void RefreshData()
    {
        try
        {
            var t = _svc.ReportSheet();
            string? floor = _cmbRange.SelectedIndex switch
            {
                1 => DateTime.Now.AddMonths(-5).ToString("yyyy-MM"),
                2 => $"{DateTime.Now:yyyy}-01",
                _ => null
            };
            if (floor != null && t.Columns.Contains("月份"))
            {
                var view = new System.Data.DataView(t);
                view.RowFilter = $"月份 >= '{floor}'";
                t = view.ToTable();
            }
            _grid.DataSource = t;

            var cost = _dash.OrderCostTop(10);
            if (cost.Columns.Contains("Status")) cost.Columns["Status"]!.ColumnName = "状态";
            foreach (System.Data.DataRow r in cost.Rows)
                r["状态"] = OrderStatusCode.ToChinese(r["状态"].ToString()!);
            _gridCost.DataSource = cost;

            // R21：消耗分析（TOP10 按用量）
            _gridConsumption.DataSource = _dash.ConsumptionAnalysis(10, "TotalQty");

            // R24：设备成本汇总
            _gridDeviceCost.DataSource = _dash.DeviceCostReport();
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }
}
