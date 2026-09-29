using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 智能算料 · 领料：按工单 BOM×冗余系数自动生成清单 → 核对 → 出库扣库存 → 导出。
/// 对应任务书「智能算料（配料）」「领料清单生成」「存储过程/事务/触发器」。
/// </summary>
internal class UcPicking : UserControl, IRefreshable
{
    private readonly PickingService _svc = new();
    private readonly WorkOrderService _orders = new();
    private readonly UserInfo _user;
    private readonly UIComboBox _cmbStatus = Ui.Combo(90, 12, 130);
    private readonly UIDataGridView _grid = Ui.Grid(10, 54, 1160, 320);
    private readonly UIDataGridView _items = Ui.Grid(10, 400, 1160, 280);
    private readonly UILabel _lblSummary = new()
    {
        Location = new Point(10, 690), Size = new Size(1160, 26), AutoSize = false,
        Font = new Font("微软雅黑", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 122, 204), Text = ""
    };

    public UcPicking(UserInfo user)
    {
        _user = user;
        _cmbStatus.Items.AddRange(new object[] { "全部状态", "待出库", "已出库" });
        _cmbStatus.SelectedIndex = 0;

        var btnQuery = Ui.Btn("查询", 240, 10, 80, 32, (_, _) => RefreshData());
        var btnGen = Ui.Btn("为工单算料", 340, 10, 105, 32, OnGenerate);
        var btnIssue = Ui.Btn("领料出库", 460, 10, 95, 32, OnIssue);
        var btnExport = Ui.Btn("导出Excel", 570, 10, 95, 32, OnExport);

        btnGen.Visible = _user.HasPerm("picking.generate");
        btnIssue.Visible = _user.HasPerm("picking.issue");

        var lblItems = new UILabel
        { Text = "领料明细", Location = new Point(10, 380), Size = new Size(200, 22), Font = new Font("微软雅黑", 10F, FontStyle.Bold) };

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("状态：", 10, 14, 76), _cmbStatus, btnQuery, btnGen, btnIssue, btnExport,
            _grid, lblItems, _items, _lblSummary
        });
        _grid.SelectionChanged += (_, _) => LoadItems();
    }

    private static string? StatusOf(UIComboBox cmb) => cmb.SelectedIndex switch
    {
        1 => PickingStatusCode.Pending, 2 => PickingStatusCode.Issued, _ => null
    };

    public void RefreshData()
    {
        try
        {
            var t = _svc.List(StatusOf(_cmbStatus), null);
            if (t.Columns.Contains("Status")) t.Columns["Status"]!.ColumnName = "状态";
            foreach (System.Data.DataRow r in t.Rows)
                r["状态"] = PickingStatusCode.ToChinese(r["状态"].ToString()!);
            _grid.DataSource = t;
            _lblSummary.Text = "";
            LoadItems();
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    private int SelectedPickingId() =>
        int.TryParse(_grid.CurrentRow?.Cells["PickingId"]?.Value?.ToString(), out var id) ? id : 0;

    private void LoadItems()
    {
        var id = SelectedPickingId();
        if (id == 0) { _items.DataSource = null; return; }
        var list = _svc.Items(id);
        var t = new System.Data.DataTable();
        t.Columns.Add("编码", typeof(string));
        t.Columns.Add("名称", typeof(string));
        t.Columns.Add("数量", typeof(int));
        t.Columns.Add("单价", typeof(decimal));
        t.Columns.Add("金额", typeof(decimal));
        t.Columns.Add("备注", typeof(string));
        decimal total = 0;
        foreach (var i in list) { t.Rows.Add(i.PartCode, i.PartName, i.Qty, i.UnitPrice, i.Amount, i.Remark); total += i.Amount; }
        _items.DataSource = t;
        _lblSummary.Text = $"共 {list.Count} 项材料，合计金额 {total:N2} 元";
    }

    /// <summary>选择“已派单/维修中”的工单进行算料</summary>
    private void OnGenerate(object? s, EventArgs e)
    {
        try
        {
            var eligible = _orders.Query("Assigned", null, null, false)
                        .Copy()
                        .Rows.Cast<System.Data.DataRow>()
                        .Select(r => (OrderId: (int)r["OrderId"], No: (string)r["OrderNo"], Dev: (string)r["DeviceName"]))
                        .ToList();
            var working = _orders.Query("Working", null, null, false);
            foreach (System.Data.DataRow r in working.Rows)
                eligible.Add(((int)r["OrderId"], (string)r["OrderNo"], (string)r["DeviceName"]));

            if (eligible.Count == 0) { Ui.Info("当前没有可算料的工单（需已派单或维修中）。"); return; }

            using var dlg = new FCalcDialog(eligible);
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var r2 = _svc.Generate(dlg.OrderId, _user.UserName, dlg.Redundancy);
            if (r2.Success) { Ui.Info($"算料完成：{r2.Message}\n领料单号已生成，请核对明细后出库。"); RefreshData(); }
            else Ui.Error(r2.Message);
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    private void OnIssue(object? s, EventArgs e)
    {
        var id = SelectedPickingId();
        if (id == 0) { Ui.Error("请先选择领料单。"); return; }
        if (!Ui.Ask("确认出库？库存将扣减并写入流水（库存不足将整体回滚）。")) return;
        var r = _svc.Issue(id, _user.UserName);
        if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
    }

    private void OnExport(object? s, EventArgs e)
    {
        var id = SelectedPickingId();
        if (id == 0) { Ui.Error("请先选择领料单。"); return; }
        try
        {
            // Excel（ClosedXML）为主，CSV 保留兼容
            if (s is Control btn) { btn.Enabled = false; try { Ui.Info("已导出：" + _svc.ExportExcel(id)); } finally { btn.Enabled = true; } }
            else Ui.Info("已导出：" + _svc.ExportExcel(id));
        }
        catch (Exception ex) { Ui.Error(ex.Message); }
    }
}

/// <summary>算料对话框：选择工单 + 冗余系数</summary>
internal class FCalcDialog : UIForm
{
    public int OrderId { get; private set; }
    public decimal Redundancy { get; private set; } = 1.10m;

    private readonly UITextBox _txtRed = Ui.Txt("1.10", 100, 20, 100);
    private readonly UIComboBox _cmb = Ui.Combo(100, 58, 380);

    public FCalcDialog(List<(int OrderId, string No, string Dev)> orders)
    {
        Text = "智能算料 - 选择工单";
        ClientSize = new Size(500, 180);
        StartPosition = FormStartPosition.CenterParent;
        foreach (var o in orders) _cmb.Items.Add($"[{o.No}] {o.Dev}");
        if (_cmb.Items.Count > 0) _cmb.SelectedIndex = 0;

        var ok = Ui.Btn("生成领料单", 240, 110, 130, 34, (_, _) =>
        {
            if (!decimal.TryParse(_txtRed.Text, out var red) || red < 1.0m || red > 2.0m)
            { Ui.Error("冗余系数需在 1.00~2.00（任务书要求留有一定冗余）。"); return; }
            OrderId = orders[_cmb.SelectedIndex].OrderId;
            Redundancy = red;
            DialogResult = DialogResult.OK;
            Close();
        });
        var back = Ui.Btn("取消", 380, 110, 90, 34, (_, _) => Close());
        Controls.AddRange(new Control[]
        {
            Ui.Lbl("冗余系数：", 6, 22, 88), _txtRed,
            Ui.Lbl("工单：", 6, 60, 88), _cmb, ok, back
        });
    }
}
