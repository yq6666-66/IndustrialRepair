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
    private readonly UiPager _pager = null!;
    private readonly UILabel _lblEmpty = new()
    {
        Dock = DockStyle.Top, Height = 26, Text = "未查询到配件（可调整关键字或类别）", TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.Current.TextSecondary, Font = new Font("微软雅黑", 9.5F), BackColor = Theme.Current.CardBg, Visible = false
    };
    /// <summary>当前是否为配件列表模式（库存预警/流水视图下禁用行双击编辑）</summary>
    private bool _listMode = true;

    private static UIDataGridView MakeFillGrid()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }

    public UcParts()
    {
        // 工具栏两行：检索+维护 / 库存+导出（分行消除按钮重叠；相对工具栏坐标）
        var btnQuery = Ui.Btn("查询", 560, 10, 80, 32, (_, _) => LoadParts(1, _pageSize));
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
            ShowLogs(p?.PartId ?? 0);
        });
        var btnQr = Ui.Btn("生成二维码", 300, 46, 100, 32, OnGenerateQr);
        var btnXls = Ui.Btn("导出Excel", 410, 46, 90, 32, OnExportExcel);
        var btnPdf = Ui.Btn("导出PDF", 510, 46, 90, 32, (_, _) =>
        {
            try
            {
                if (!_listMode) { Ui.Error("库存预警/流水视图不支持 PDF 导出，请先查询配件列表。"); return; }
                if (Ui.TableOf(_grid) is not { } t || t.Rows.Count == 0)
                { Ui.Error("当前无数据可导出。"); return; }
                var view = t.Copy();
                view.Columns.Remove("PartId");
                Ui.Info("已导出：" + BLL.PdfService.ExportTablePdf("配件库存清单", view, qrText: "PART-INVENTORY"));
            }
            catch (Exception ex) { Ui.Error("PDF 导出失败：" + ex.Message); }
        });
        var btnImport = Ui.Btn("批量导入", 610, 46, 90, 32, OnBatchImport);
        var btnLabels = Ui.Btn("批量打印标签", 710, 46, 105, 32, (_, _) =>
        {
            try
            {
                if (!_listMode || Ui.TableOf(_grid) is not { } t || t.Rows.Count == 0)
                { Ui.Error("请先查询要打印标签的配件列表。"); return; }
                var view = t.Copy();
                view.Columns.Remove("PartId");
                Ui.Info("标签 PDF 已生成：" + BLL.PdfService.ExportPartsLabelPdf(view));
            }
            catch (Exception ex) { Ui.Error("标签生成失败：" + ex.Message); }
        });
        var btnAdjust = Ui.Btn("盘点", 825, 46, 70, 32, (_, _) =>
        {
            using var dlg = new FStockAdjust();
            dlg.ShowDialog();
            LoadParts(1, _pageSize);
        });
        // DG4：批量操作（勾选列 + 全选/清空 + 批量删除·单事务）
        var btnSelAll = Ui.Btn("全选", 905, 46, 70, 32, OnToggleSelectAll);
        var btnBatchDel = Ui.Btn("批量删除", 985, 46, 90, 32, OnBatchDelete);
        _grid.CellContentClick += OnGridCellClick;

        var toolbar = Ui.Toolbar();
        toolbar.Controls.AddRange(new Control[]
        {
            Ui.Lbl("关键字：", 4, 14, 80), _txtKey, Ui.Lbl("类别：", 346, 14, 48), _cmbCat,
            btnQuery, btnAdd, btnEdit, btnDel, btnIn, btnLow, btnLog, btnQr, btnXls, btnPdf, btnImport, btnLabels, btnAdjust, btnSelAll, btnBatchDel
        });

        // Dock 布局（同 FMain 模式）：填充控件先加，标题条最后加；分页条在工具栏之下
        _pager = new UiPager((page, size) => LoadParts(page, size));
        Controls.Add(_grid);
        Controls.Add(_lblEmpty);
        Controls.Add(toolbar);
        Controls.Add(_pager);
        Controls.Add(Ui.PageHeader("备件库管理", "检索 · 维护 · 入库 · 预警 · 流水 · 二维码 · Excel"));

        Ui.BindEnter(_txtKey, () => LoadParts(1, _pageSize));   // 回车即查（回第一页）
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
        LoadParts(1, _pageSize);
    }

    /// <summary>从当前行重建 Part（分页列表不再持有全量 _current）。</summary>
    private Part? Selected()
    {
        var row = _grid.CurrentRow;
        if (row == null || row.Cells["PartId"].Value is not int id) return null;
        try
        {
            return new Part
            {
                PartId = id,
                PartCode = row.Cells["编码"].Value?.ToString() ?? "",
                PartName = row.Cells["名称"].Value?.ToString() ?? "",
                CategoryName = row.Cells["类别"].Value?.ToString() ?? "",
                Spec = row.Cells["规格"].Value?.ToString(),
                Unit = row.Cells["单位"].Value?.ToString() ?? "个",
                Price = row.Cells["单价"].Value is decimal d ? d : 0m,
                StockQty = row.Cells["库存"].Value is int s ? s : 0,
                MinStock = row.Cells["下限"].Value is int m ? m : 0,
            };
        }
        catch { return null; }
    }

    private int _currentPage = 1;
    private int _pageSize = 50;

    /// <summary>刷新当前页（IRefreshable / 切页签入口）。</summary>
    public void RefreshData() => LoadParts(_currentPage, _pageSize);

    /// <summary>分页加载配件列表（R1 分页；NR10 精确无命中自动模糊回退）。</summary>
    private void LoadParts(int page, int size)
    {
        Ui.Busy(() =>
        {
            try
            {
                _listMode = true;
                _currentPage = page; _pageSize = size;
                int? cat = null;
                if (_cmbCat.SelectedIndex > 0)
                    cat = _svc.Categories().FirstOrDefault(c => c.CategoryName == _cmbCat.Text)?.CategoryId;
                var keyword = _txtKey.Text.Trim();
                var r = BLL.PagedQuery.Parts(keyword, cat, page, size);

                // NR10：精确零命中 → FuzzySharp 编辑距离回退
                var fuzzyUsed = false;
                if (r.Items.Rows.Count == 0 && !string.IsNullOrWhiteSpace(keyword) && cat == null)
                {
                    var ranked = PartSearchScorer.Rank(keyword, _svc.Search(null));
                    if (ranked.Count > 0)
                    {
                        var codes = ranked.Select(x => x.Part.PartCode).ToList();
                        var t = new System.Data.DataTable();
                        t.Columns.Add("PartId", typeof(int));
                        t.Columns.Add("PartCode", typeof(string));
                        t.Columns.Add("PartName", typeof(string));
                        t.Columns.Add("CategoryName", typeof(string));
                        t.Columns.Add("Spec", typeof(string));
                        t.Columns.Add("Unit", typeof(string));
                        t.Columns.Add("Price", typeof(decimal));
                        t.Columns.Add("StockQty", typeof(int));
                        t.Columns.Add("MinStock", typeof(int));
                        t.Columns.Add("Status", typeof(bool));
                        foreach (var (_, p) in ranked)
                            t.Rows.Add(p.PartId, p.PartCode, p.PartName, p.CategoryName, p.Spec, p.Unit, p.Price, p.StockQty, p.MinStock, p.Status);
                        BindList(Translate(t));
                        _lblEmpty.Visible = false;
                        _lblEmpty.Text = $"未精确命中，模糊搜索 \"{keyword}\" 返回 {ranked.Count} 条（编辑距离匹配）";
                        _lblEmpty.Visible = false;   // 有模糊结果不显示空态
                        // 模糊模式显示提示行（借 PageHeader 副标题太侵入，用 label 文本区）
                        return;
                    }
                }

                BindList(Translate(r.Items));
                _lblEmpty.Text = "未查询到配件（可调整关键字或类别）";
                _lblEmpty.Visible = Ui.TableOf(_grid)?.Rows.Count == 0;
                _pager.Show(r);
                RenameColumns();
            }
            catch (Exception ex) { Ui.Error(DAL.DbHelper.FriendlyError(ex)); }
        });
    }

    private static System.Data.DataTable Translate(System.Data.DataTable src)
    {
        foreach (var (from, to) in new[]
                 { ("PartId","PartId"), ("PartCode","编码"), ("PartName","名称"), ("CategoryName","类别"),
                   ("Spec","规格"), ("Unit","单位"), ("Price","单价"), ("StockQty","库存"), ("MinStock","下限") })
            if (src.Columns.Contains(from)) src.Columns[from]!.ColumnName = to;
        if (!src.Columns.Contains("状态"))
        {
            src.Columns.Add("状态", typeof(string));
            foreach (System.Data.DataRow r in src.Rows)
                r["状态"] = (bool)r["Status"] ? "启用" : "停用";
        }
        src.Columns.Remove("Status");
        if (src.Columns.Contains("FeatureJson")) src.Columns.Remove("FeatureJson");
        if (src.Columns.Contains("CategoryId")) src.Columns.Remove("CategoryId");
        // DG4：批量操作勾选列（内存态，不落库）
        if (!src.Columns.Contains("选中"))
        {
            src.Columns.Add("选中", typeof(bool));
            foreach (System.Data.DataRow r in src.Rows) r["选中"] = false;
        }
        src.Columns["选中"]!.SetOrdinal(0);
        return src;
    }

    /// <summary>DG2：主列表显式列绑定（AutoGenerateColumns=false + BindingSource）。</summary>
    private void BindList(System.Data.DataTable dt) =>
        Ui.BindExplicit(_grid, dt, "parts-v2",
            new GridCol("选中", "选", FillWeight: 35, IsCheck: true),
            new GridCol("PartId", "ID", FillWeight: 20, Visible: false),
            new GridCol("编码", "编码", FillWeight: 90),
            new GridCol("名称", "名称", FillWeight: 130),
            new GridCol("类别", "类别", FillWeight: 70),
            new GridCol("规格", "规格", FillWeight: 120),
            new GridCol("单位", "单位", FillWeight: 45),
            new GridCol("单价", "单价", Format: "N2", FillWeight: 65),
            new GridCol("库存", "库存", FillWeight: 60),
            new GridCol("下限", "下限", FillWeight: 55),
            new GridCol("状态", "状态", FillWeight: 55));

    /// <summary>DG4：勾选列点击翻转（只读网格下手动维护内存勾选态）+ 重绘。</summary>
    private void OnGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "选中") return;
        var t = Ui.TableOf(_grid);
        if (t == null || e.RowIndex >= t.Rows.Count) return;
        var row = t.Rows[e.RowIndex];
        row["选中"] = !(row["选中"] is bool b && b);
        _grid.InvalidateRow(e.RowIndex);
    }

    /// <summary>DG4：全选 / 清空切换。</summary>
    private void OnToggleSelectAll(object? sender, EventArgs e)
    {
        if (!_listMode) { Ui.Error("请在配件列表视图使用全选。"); return; }
        var t = Ui.TableOf(_grid);
        if (t == null || !t.Columns.Contains("选中")) return;
        var btn = sender as UIButton;
        var allChecked = t.Rows.Count > 0 && t.Rows.Cast<System.Data.DataRow>().All(r => r["选中"] is bool b && b);
        var target = !allChecked;
        foreach (System.Data.DataRow r in t.Rows) r["选中"] = target;
        if (btn != null) btn.Text = target ? "清空" : "全选";
        _grid.Refresh();
    }

    /// <summary>DG4：批量删除（单事务 + 引用保护，对齐课程 SqlTransaction 范式）。</summary>
    private void OnBatchDelete(object? sender, EventArgs e)
    {
        if (!_listMode) { Ui.Error("请在配件列表视图勾选后再批量删除。"); return; }
        var t = Ui.TableOf(_grid);
        if (t == null || !t.Columns.Contains("选中")) return;
        var ids = t.Rows.Cast<System.Data.DataRow>()
            .Where(r => r["选中"] is bool b && b)
            .Select(r => Convert.ToInt32(r["PartId"])).ToList();
        if (ids.Count == 0) { Ui.Error("请先勾选要删除的配件（首列复选框，或点「全选」）。"); return; }
        if (!Ui.Ask($"确定批量删除勾选的 {ids.Count} 个配件？\n被 BOM/领料单/库存流水等业务数据引用的配件将自动跳过。")) return;
        try
        {
            var (deleted, skipped) = _svc.BatchDelete(ids);
            var msg = $"批量删除完成：成功 {deleted} 个（单事务提交）。";
            if (skipped.Count > 0)
                msg += $"\n跳过 {skipped.Count} 个（被业务引用）：{string.Join("、", skipped.Take(8))}{(skipped.Count > 8 ? " 等" : "")}";
            Ui.Info(msg);
            RefreshData();
        }
        catch (Exception ex) { Ui.Error(DAL.DbHelper.FriendlyError(ex)); }
    }

    private void ShowLow()
    {
        _listMode = false;
        // DG2：低库存视图独立列集（列名直接中文化，复用 RenameColumns 的标红逻辑）
        Ui.BindExplicit(_grid, _svc.LowStock(), "parts-low-v1",
            new GridCol("PartCode", "编码", FillWeight: 90),
            new GridCol("PartName", "名称", FillWeight: 130),
            new GridCol("Spec", "规格", FillWeight: 120),
            new GridCol("Unit", "单位", FillWeight: 45),
            new GridCol("StockQty", "库存", FillWeight: 60),
            new GridCol("MinStock", "下限", FillWeight: 55),
            new GridCol("Gap", "缺口", FillWeight: 55),
            new GridCol("CategoryName", "类别", FillWeight: 70));
        RenameColumns();
    }

    /// <summary>DG2：库存流水视图显式列（原自动列显示英文表头）。</summary>
    private void ShowLogs(int partId)
    {
        _listMode = false;
        Ui.BindExplicit(_grid, _svc.StockLogs(partId), "parts-logs-v1",
            new GridCol("StockLogId", "ID", FillWeight: 20, Visible: false),
            new GridCol("PartCode", "编码", FillWeight: 90),
            new GridCol("PartName", "名称", FillWeight: 130),
            new GridCol("ChangeQty", "变动数量", FillWeight: 70),
            new GridCol("AfterQty", "结存", FillWeight: 60),
            new GridCol("RefNo", "参考单号", FillWeight: 100),
            new GridCol("CreatedAt", "时间", Format: "MM-dd HH:mm", FillWeight: 90));
    }

    private void RenameColumns()
    {
        // 库存低于下限标红
        if (_grid.Columns.Contains("库存") && _grid.Columns.Contains("下限"))
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Cells["库存"].Value is int s && row.Cells["下限"].Value is int m && s < m)
                    row.DefaultCellStyle.ForeColor = Theme.Current.Danger;
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
            if (Ui.TableOf(_grid) is not { } t || t.Rows.Count == 0)
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

    /// <summary>批量导入（OR3）：Excel 上传 → FluentValidation 逐行校验 → 预览 → 确认写入。</summary>
    private void OnBatchImport(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        { Filter = "Excel 文件|*.xlsx", Title = "选择配件批量导入文件（.xlsx）" };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        try
        {
            Ui.Busy(() =>
            {
                var svc = new PartImportService();
                var results = svc.Validate(ofd.FileName);
                var ok = results.Count(r => r.Success);
                var fail = results.Count(r => !r.Success);
                var errors = results.Where(r => !r.Success)
                    .Take(10)
                    .Select(r => $"行{r.RowNumber}: {r.PartCode} — {r.Error}");
                var detail = fail > 0 ? $"\n\n错误（前 10 条）：\n{string.Join("\n", errors)}{(fail > 10 ? $"\n... 共 {fail} 条错误" : "")}" : "";

                if (!Ui.Ask($"校验结果：{ok} 行通过 / {fail} 行失败{detail}\n\n确定导入 {ok} 条通过数据？（失败行跳过）"))
                    return;
                var (imported, skipped) = svc.Import(ofd.FileName);
                Ui.Info($"导入完成：成功 {imported} 条 / 跳过 {skipped} 条。");
                LoadParts(1, _pageSize);
            });
        }
        catch (Exception ex) { Ui.Error("导入失败：" + ex.Message); }
    }
}

/// <summary>二维码标签预览（QRCoder 生成结果）</summary>
internal class FQrPreview : UIForm
{
    public FQrPreview(string imageFile, Part part)
    {
        Text = $"二维码标签 - {part.PartCode}";
        Ui.CloseOnEsc(this);
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
        Ui.CloseOnEsc(this);
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

        // NR6：FluentValidation 集中校验（与服务层同一套规则）
        var invalid = new PartInputValidator().FirstError(new PartInput
        {
            PartCode = _code.Text.Trim(), PartName = _name.Text.Trim(), CategoryName = _cat.Text,
            Spec = _spec.Text, Unit = string.IsNullOrWhiteSpace(_unit.Text) ? "个" : _unit.Text.Trim(),
            Price = price, StockQty = stock, MinStock = min
        });
        if (invalid != null) { Ui.Error(invalid); return; }

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
