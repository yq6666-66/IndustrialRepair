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
/// </summary>
internal class UcDevices : UserControl, IRefreshable
{
    private readonly BasicRepository _basic = new();
    private readonly PartService _parts = new();
    private readonly UIDataGridView _gridDev = Ui.Grid(10, 44, 420, 630);
    private readonly UIDataGridView _gridBom = Ui.Grid(460, 44, 710, 630);

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

        var lblBom = new UILabel { Text = "BOM 材料表（单台维修常规用量）", Location = new Point(460, 12), Size = new Size(260, 24), Font = new Font("微软雅黑", 9.5F, FontStyle.Bold) };
        var btnBomAdd = Ui.Btn("添加BOM项", 730, 8, 95, 32, OnBomAdd);
        var btnBomQty = Ui.Btn("改用量", 830, 8, 75, 32, OnBomQty);
        var btnBomDel = Ui.Btn("移除", 910, 8, 70, 32, OnBomDel);

        Controls.AddRange(new Control[]
        {
            btnAdd, btnEdit, btnDel, _gridDev, lblBom, btnBomAdd, btnBomQty, btnBomDel, _gridBom
        });
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

    private void EditDevice(Device? d)
    {
        var code = Ui.Input(d == null ? "新增设备" : "编辑设备", "设备编码：", d?.DeviceCode ?? "");
        if (string.IsNullOrWhiteSpace(code)) return;
        var name = Ui.Input(d == null ? "新增设备" : "编辑设备", "设备名称：", d?.DeviceName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var loc = Ui.Input(d == null ? "新增设备" : "编辑设备", "安装位置：", d?.Location ?? "");
        _basic.SaveDevice(new Device { DeviceId = d?.DeviceId ?? 0, DeviceCode = code.Trim(), DeviceName = name.Trim(), Location = loc?.Trim(), Status = true });
        RefreshData();
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

/// <summary>维修报表（视图 vw_RepairReport）</summary>
internal class UcReport : UserControl, IRefreshable
{
    private readonly WorkOrderService _svc = new();
    private readonly UIDataGridView _grid = Ui.Grid(10, 50, 1160, 620);
    private readonly UILabel _lbl = new()
    { Location = new Point(10, 12), Size = new Size(700, 26), Font = new Font("微软雅黑", 10F, FontStyle.Bold), Text = "维修报表（按月统计：工单量/完成率/平均处理时长/材料成本）" };

    public UcReport()
    {
        var btn = Ui.Btn("刷新", 730, 10, 80, 32, (_, _) => RefreshData());
        var btnXls = Ui.Btn("导出Excel", 820, 10, 95, 32, (_, _) =>
        {
            try
            {
                if (_grid.DataSource is not System.Data.DataTable t || t.Rows.Count == 0) { Ui.Error("无数据可导出。"); return; }
                Ui.Info("已导出：" + ExcelService.ExportTable(t, "维修报表", "月度报表", totalRow: true));
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        });
        Controls.AddRange(new Control[] { _lbl, btn, btnXls, _grid });
    }

    public void RefreshData()
    {
        try { _grid.DataSource = _svc.ReportSheet(); }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }
}
