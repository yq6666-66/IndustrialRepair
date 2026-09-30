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
        if (string.IsNullOrWhiteSpace(_code.Text)) { Ui.Error("请填写设备编码。"); return; }
        if (string.IsNullOrWhiteSpace(_name.Text)) { Ui.Error("请填写设备名称。"); return; }
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

/// <summary>维修报表（视图 vw_RepairReport）</summary>
internal class UcReport : UserControl, IRefreshable
{
    private readonly WorkOrderService _svc = new();
    private readonly UIDataGridView _grid = FillGrid2();

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
                if (_grid.DataSource is not System.Data.DataTable t || t.Rows.Count == 0) { Ui.Error("无数据可导出。"); return; }
                Ui.Info("已导出：" + ExcelService.ExportTable(t, "维修报表", "月度报表", totalRow: true));
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        });

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 46 };
        var lbl = new UILabel
        {
            Text = "按月统计：工单量 · 完成数 · 取消数 · 平均处理分钟 · 材料成本", AutoSize = true,
            Location = new Point(10, 12), Font = new Font("微软雅黑", 9F), ForeColor = Color.FromArgb(90, 100, 115)
        };
        toolbar.Controls.AddRange(new Control[] { lbl, btn, btnXls });

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(Ui.PageHeader("维修报表", "vw_RepairReport 月度视图 · 可导出 Excel"));
    }

    public void RefreshData()
    {
        try { _grid.DataSource = _svc.ReportSheet(); }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }
}
