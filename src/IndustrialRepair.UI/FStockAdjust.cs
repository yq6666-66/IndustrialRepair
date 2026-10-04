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
/// 库存盘点调整（R11）：列出全部配件 → 输入实盘数量 → 显示差异 → 确认调整。
/// 正差异自动入库（RefNo=PAN:时间戳），负差异直接 SQL 调减（记流水）。
/// </summary>
internal class FStockAdjust : UIForm
{
    private readonly UIDataGridView _grid;
    private readonly DataTable _table = new();
    private readonly PartService _svc = new();

    public FStockAdjust()
    {
        Text = "库存盘点调整";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterParent;

        // 表格：编码/名称/系统库存/实盘数量/差异
        _table.Columns.Add("PartId", typeof(int));
        _table.Columns.Add("编码", typeof(string));
        _table.Columns.Add("名称", typeof(string));
        _table.Columns.Add("系统库存", typeof(int));
        _table.Columns.Add("实盘数量", typeof(string));   // 允许编辑的文本
        _table.Columns.Add("差异", typeof(string));

        _grid = new UIDataGridView
        {
            Location = new Point(10, 10), Size = new Size(680, 420),
            Dock = DockStyle.Top, ReadOnly = false, AllowUserToAddRows = false,
            RowHeadersVisible = false, EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 40, RowTemplate = { Height = 34 }
        };
        Ui.ApplyGridTheme(_grid);
        _grid.CellValueChanged += OnCellChanged;

        var lblHint = new UILabel
        {
            Text = "在「实盘数量」列输入实际清点数，差异自动计算。确认后正差异入库、负差异调减。",
            AutoSize = true, Location = new Point(10, 440), Font = new Font("微软雅黑", 8.5F),
            ForeColor = Color.FromArgb(110, 122, 138)
        };

        var btnConfirm = Ui.Btn("确认调整", 200, 460, 110, 32, OnConfirm);
        var btnClose = Ui.Btn("取消", 330, 460, 100, 32, (_, _) => Close());

        Controls.AddRange(new Control[] { _grid, lblHint, btnConfirm, btnClose });
        LoadParts();
    }

    private void LoadParts()
    {
        _table.Rows.Clear();
        foreach (var p in _svc.Search(null).OrderBy(x => x.PartCode))
            _table.Rows.Add(p.PartId, p.PartCode, p.PartName, p.StockQty, "", "");
        _grid.DataSource = _table;
        _grid.Columns["PartId"]!.Visible = false;
        _grid.Columns["实盘数量"]!.ReadOnly = false;   // 唯一可编辑列
    }

    private void OnCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "实盘数量") return;

        var row = _grid.Rows[e.RowIndex];
        var systemQty = Convert.ToInt32(row.Cells["系统库存"].Value ?? 0);
        if (int.TryParse(row.Cells["实盘数量"].Value?.ToString(), out var actual))
        {
            var diff = actual - systemQty;
            row.Cells["差异"].Value = diff == 0 ? "—" : diff > 0 ? $"+{diff}" : diff.ToString();
            row.Cells["差异"].Style.ForeColor = diff > 0 ? Color.FromArgb(80, 160, 90) : diff < 0 ? Color.Firebrick : Color.Gray;
        }
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var adjustments = new List<(int PartId, int Diff)>();
        foreach (DataRow row in _table.Rows)
        {
            if (!int.TryParse(row["实盘数量"]?.ToString(), out var actual)) continue;
            var systemQty = Convert.ToInt32(row["系统库存"]);
            var diff = actual - systemQty;
            if (diff != 0) adjustments.Add((Convert.ToInt32(row["PartId"]), diff));
        }

        if (adjustments.Count == 0) { Ui.Info("无差异需要调整。"); return; }
        if (!Ui.Ask($"共 {adjustments.Count} 项差异待调整。\n正差异将入库、负差异将调减。\n\n确定执行？")) return;

        var refNo = $"PAN:{DateTime.Now:HHmmss}";
        var (ok, fail) = (0, 0);
        foreach (var (partId, diff) in adjustments)
        {
            try
            {
                if (diff > 0)
                {
                    _svc.StockIn(partId, diff, refNo);
                }
                else
                {
                    // R25 修复：负差异调减也写流水（与 usp_StockIn 对称）
                    const string sql = @"
UPDATE dbo.part SET StockQty = StockQty + @D WHERE PartId = @Id AND StockQty + @D >= 0;
IF @@ROWCOUNT > 0
    INSERT INTO dbo.part_stock_log(PartId, ChangeQty, AfterQty, RefNo)
    SELECT PartId, @D, StockQty, @R FROM dbo.part WHERE PartId = @Id;";
                    DbHelper.ExecuteNonQuery(sql, CommandType.Text,
                        DbHelper.P("@D", diff), DbHelper.P("@Id", partId), DbHelper.P("@R", refNo));
                }
                ok++;
            }
            catch { fail++; }
        }

        Ui.Info($"盘点调整完成：成功 {ok} 项 / 失败 {fail} 项（单号 {refNo}）。");
        LoadParts();
    }
}
