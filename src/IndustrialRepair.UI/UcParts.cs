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
/// 配件库管理：模糊检索 + CRUD + 入库 + 库存预警 + 流水（UIDataGridView）。
/// 对应任务书「配件信息录入、修改、删除、查询功能开发 / 配件信息模糊检索」。
/// </summary>
internal class UcParts : UserControl, IRefreshable
{
    private readonly PartService _svc = new();
    private readonly UITextBox _txtKey = Ui.Txt("编码 / 名称 / 规格 模糊搜索", 90, 12, 220);
    private readonly UIComboBox _cmbCat = Ui.Combo(400, 12, 140);
    private readonly UIDataGridView _grid = MakeFillGrid();

    private static UIDataGridView MakeFillGrid()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }
    private List<Part> _current = new();
    /// <summary>当前是否为配件列表模式（库存预警/流水视图下禁用行双击编辑）</summary>
    private bool _listMode = true;

    public UcParts()
    {
        // 工具栏两行：检索+维护 / 库存+导出（分行消除按钮重叠；相对工具栏坐标）
        var btnQuery = Ui.Btn("查询", 560, 10, 80, 32, (_, _) => RefreshData());
        var btnAdd = Ui.Btn("新增", 660, 10, 80, 32, (_, _) => EditPart(null));
        var btnEdit = Ui.Btn("编辑", 750, 10, 80, 32, (_, _) =>
        {
            var p = Selected(); if (p == null) return;
            EditPart(p);
        });
        var btnDel = Ui.Btn("删除", 840, 10, 80, 32, (_, _) =>
        {
            var p = Selected(); if (p == null) return;
            if (!Ui.Ask($"确认删除配件「{p.PartName}」？")) return;
            var r = _svc.Delete(p.PartId);
            if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
        });
        var btnIn = Ui.Btn("入库", 10, 46, 80, 32, (_, _) =>
        {
            var p = Selected(); if (p == null) return;
            var qty = Ui.Input("配件入库", $"「{p.PartName}」入库数量：");
            if (!int.TryParse(qty, out var n)) { if (qty != null) Ui.Error("数量必须是整数。"); return; }
            var r = _svc.StockIn(p.PartId, n, $"IN-{DateTime.Now:HHmmss}");
            if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
        });
        var btnLow = Ui.Btn("库存预警", 100, 46, 90, 32, (_, _) => { _txtKey.Text = ""; _cmbCat.SelectedIndex = 0; ShowLow(); });
        var btnLog = Ui.Btn("库存流水", 200, 46, 90, 32, (_, _) =>
        {
            var p = Selected();
            _listMode = false;
            _grid.DataSource = _svc.StockLogs(p?.PartId ?? 0);
            RenameColumns();
        });
        var btnQr = Ui.Btn("生成二维码", 300, 46, 100, 32, OnGenerateQr);
        var btnXls = Ui.Btn("导出Excel", 410, 46, 90, 32, OnExportExcel);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 84 };
        toolbar.Controls.AddRange(new Control[]
        {
            Ui.Lbl("关键字：", 4, 14, 80), _txtKey, Ui.Lbl("类别：", 346, 14, 48), _cmbCat,
            btnQuery, btnAdd, btnEdit, btnDel, btnIn, btnLow, btnLog, btnQr, btnXls
        });

        // Dock 布局（同 FMain 模式）：填充控件先加，标题条最后加
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(Ui.PageHeader("备件库管理", "检索 · 维护 · 入库 · 预警 · 流水 · 二维码 · Excel"));

        Ui.BindEnter(_txtKey, RefreshData);                     // 回车即查（抑制提示音）
        Ui.BindRowDblClick(_grid, _ =>                          // 双击=编辑（仅配件列表模式）
        {
            if (!_listMode) return;
            var p = Selected(); if (p == null) return;
            EditPart(p);
        });

        Load += (_, _) =>
        {
            _cmbCat.Items.Add("全部类别");
            foreach (var c in _svc.Categories()) _cmbCat.Items.Add(c.CategoryName);
            _cmbCat.SelectedIndex = 0;
        };
        RefreshData();
    }

    private Part? Selected() =>
        _grid.CurrentRow?.Cells["PartId"].Value is int id ? _current.FirstOrDefault(p => p.PartId == id) : null;

    public void RefreshData()
    {
        try
        {
            _listMode = true;
            int? cat = null;
            if (_cmbCat.SelectedIndex > 0)
                cat = _svc.Categories().FirstOrDefault(c => c.CategoryName == _cmbCat.Text)?.CategoryId;
            _current = _svc.Search(_txtKey.Text.Trim(), cat);
            var t = ToTable(_current);
            _grid.DataSource = t;
            RenameColumns();
        }
        catch (Exception ex) { Ui.Error(DAL.DbHelper.FriendlyError(ex)); }
    }

    private void ShowLow()
    {
        _listMode = false;
        _grid.DataSource = _svc.LowStock();
        RenameColumns();
    }

    private static System.Data.DataTable ToTable(List<Part> list)
    {
        var t = new System.Data.DataTable();
        t.Columns.Add("PartId", typeof(int));
        t.Columns.Add("编码", typeof(string));
        t.Columns.Add("名称", typeof(string));
        t.Columns.Add("类别", typeof(string));
        t.Columns.Add("规格", typeof(string));
        t.Columns.Add("单位", typeof(string));
        t.Columns.Add("单价", typeof(decimal));
        t.Columns.Add("库存", typeof(int));
        t.Columns.Add("下限", typeof(int));
        t.Columns.Add("状态", typeof(string));
        foreach (var p in list)
            t.Rows.Add(p.PartId, p.PartCode, p.PartName, p.CategoryName, p.Spec, p.Unit,
                       p.Price, p.StockQty, p.MinStock, p.Status ? "启用" : "停用");
        return t;
    }

    private void RenameColumns()
    {
        // 库存低于下限标红
        if (_grid.Columns.Contains("库存") && _grid.Columns.Contains("下限"))
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Cells["库存"].Value is int s && row.Cells["下限"].Value is int m && s < m)
                    row.DefaultCellStyle.ForeColor = Color.Firebrick;
    }

    /// <summary>生成配件二维码标签（开源库 QRCoder，内容 PART:编码，扫码闭环）</summary>
    private void OnGenerateQr(object? s, EventArgs e)
    {
        var p = Selected();
        if (p == null) { Ui.Error("请先选择配件。"); return; }
        try
        {
            btnGuard(s, () =>
            {
                var file = QrLabelService.Generate(p.PartCode, p.PartName);
                using var dlg = new FQrPreview(file, p);
                dlg.ShowDialog(this.FindForm());
            });
        }
        catch (Exception ex) { Ui.Error("二维码生成失败：" + ex.Message); }
    }

    /// <summary>当前列表导出 Excel（开源库 ClosedXML）</summary>
    private void OnExportExcel(object? s, EventArgs e)
    {
        try
        {
            if (_grid.DataSource is not System.Data.DataTable t || t.Rows.Count == 0)
            { Ui.Error("当前无数据可导出。"); return; }
            btnGuard(s, () =>
            {
                var file = ExcelService.ExportTable(t, "配件库存清单", "配件");
                Ui.Info("已导出：" + file);
            });
        }
        catch (Exception ex) { Ui.Error("导出失败：" + ex.Message); }
    }

    /// <summary>按钮防双击（回执期间禁用）</summary>
    private static void btnGuard(object? sender, Action body)
    {
        var btn = sender as Control;
        if (btn != null) btn.Enabled = false;
        try { body(); }
        finally { if (btn != null) btn.Enabled = true; }
    }

    private void EditPart(Part? p)
    {
        using var dlg = new FPartEdit(p, _svc.Categories());
        if (dlg.ShowDialog() == DialogResult.OK) RefreshData();
    }
}

/// <summary>二维码标签预览（QRCoder 生成结果）</summary>
internal class FQrPreview : UIForm
{
    public FQrPreview(string imageFile, Part part)
    {
        Text = $"二维码标签 - {part.PartCode}";
        ClientSize = new Size(340, 470);
        StartPosition = FormStartPosition.CenterParent;

        var pic = new PictureBox
        {
            Image = Image.FromFile(imageFile),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(30, 16), Size = new Size(280, 280),
            BorderStyle = BorderStyle.FixedSingle
        };
        var info = new UILabel
        {
            Text = $"{part.PartCode}  {part.PartName}\n{part.Spec}\n内容：PART:{part.PartCode}",
            Location = new Point(20, 306), Size = new Size(300, 60), AutoSize = false,
            Font = new Font("微软雅黑", 9F)
        };
        var btnSave = Ui.Btn("另存为…", 40, 390, 100, 34, (_, _) =>
        {
            using var sfd = new SaveFileDialog
            { Filter = "PNG 图片|*.png", FileName = $"QR_{part.PartCode}.png" };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                File.Copy(imageFile, sfd.FileName, true);
                Ui.Info("已保存：" + sfd.FileName);
            }
        });
        var btnClose = Ui.Btn("关闭", 200, 390, 100, 34, (_, _) => Close());

        Controls.AddRange(new Control[] { pic, info, btnSave, btnClose });
        FormClosed += (_, _) => pic.Image?.Dispose();
    }
}

/// <summary>配件新增/编辑对话框</summary>
internal class FPartEdit : UIForm
{
    private readonly Part? _part;
    private readonly List<PartCategory> _cats;
    private readonly UITextBox _code = Ui.Txt("如 P-ZC-001", 100, 20, 220);
    private readonly UITextBox _name = Ui.Txt("配件名称", 100, 58, 220);
    private readonly UIComboBox _cat = Ui.Combo(100, 96, 220);
    private readonly UITextBox _spec = Ui.Txt("规格型号（选填）", 100, 134, 220);
    private readonly UITextBox _unit = Ui.Txt("单位", 100, 172, 80, false);
    private readonly UITextBox _price = Ui.Txt("单价", 210, 172, 110);
    private readonly UITextBox _stock = Ui.Txt("库存", 100, 210, 80);
    private readonly UITextBox _min = Ui.Txt("下限", 210, 210, 110);

    public FPartEdit(Part? part, List<PartCategory> cats)
    {
        _part = part; _cats = cats;
        Text = part == null ? "新增配件" : "编辑配件";
        ClientSize = new Size(360, 320);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var c in cats) _cat.Items.Add(c.CategoryName);
        _unit.Text = "个"; _price.Text = "0"; _stock.Text = "0"; _min.Text = "0";

        if (part != null)
        {
            _code.Text = part.PartCode; _name.Text = part.PartName;
            _cat.Text = part.CategoryName; _spec.Text = part.Spec ?? "";
            _unit.Text = part.Unit; _price.Text = part.Price.ToString("F2");
            _stock.Text = part.StockQty.ToString(); _min.Text = part.MinStock.ToString();
        }

        var ok = Ui.Btn("保存", 100, 258, 110, 34, OnSave);
        var back = Ui.Btn("取消", 220, 258, 110, 34, (_, _) => Close());
        Controls.AddRange(new Control[]
        {
            Ui.Lbl("编码：", 10, 20, 85), _code,
            Ui.Lbl("名称：", 10, 58, 85), _name,
            Ui.Lbl("类别：", 10, 96, 85), _cat,
            Ui.Lbl("规格：", 10, 134, 85), _spec,
            Ui.Lbl("单价：", 10, 172, 85), _unit, _price,
            Ui.Lbl("库存：", 10, 210, 85), _stock, _min, ok, back
        });
    }

    private void OnSave(object? s, EventArgs e)
    {
        decimal.TryParse(_price.Text, out var price);
        int.TryParse(_stock.Text, out var stock);
        int.TryParse(_min.Text, out var min);
        var cat = _cats.FirstOrDefault(c => c.CategoryName == _cat.Text);
        if (cat == null) { Ui.Error("请选择类别。"); return; }

        var p = _part ?? new Part();
        p.PartCode = _code.Text.Trim();
        p.PartName = _name.Text.Trim();
        p.CategoryId = cat.CategoryId;
        p.Spec = string.IsNullOrWhiteSpace(_spec.Text) ? null : _spec.Text.Trim();
        p.Unit = string.IsNullOrWhiteSpace(_unit.Text) ? "个" : _unit.Text.Trim();
        p.Price = price; p.StockQty = stock; p.MinStock = min; p.Status = true;

        var r = new PartService().Save(p);
        if (!r.Success) { Ui.Error(r.Message); return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}
