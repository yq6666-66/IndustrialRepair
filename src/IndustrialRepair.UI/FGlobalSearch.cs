using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 全局搜索中心（OR6）：Ctrl+K 唤起，跨工单/配件/设备/页面导航的快速跳转。
/// 关键字实时搜索（名称/编码 LIKE），方向键选择，回车跳转。
/// </summary>
internal class FGlobalSearch : UIForm
{
    private readonly UITextBox _txtSearch;
    private readonly UIDataGridView _grid;
    private readonly UserInfo _user;
    private List<SearchHit> _hits = new();
    private readonly Action<string> _navigate;

    private sealed record SearchHit(string Type, string Id, string Title, string Subtitle, string PageKey);

    /// <summary>搜索命中后的回调（pageKey = FMain 导航键）。</summary>
    public FGlobalSearch(UserInfo user, Action<string> navigate)
    {
        _user = user;
        _navigate = navigate;

        Text = "全局搜索";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowTitle = true;

        _txtSearch = new UITextBox
        {
            Location = new Point(12, 10), Size = new Size(536, 34),
            Watermark = "搜索工单号 / 设备名 / 配件名/编码 / 页面名称…"
        };
        _txtSearch.TextChanged += (_, _) => Search(_txtSearch.Text.Trim());

        _grid = Ui.Grid(12, 52, 536, 350);
        _grid.CellDoubleClick += (_, e) => Jump(e.RowIndex);
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Jump(_grid.CurrentRow?.Index ?? -1); };

        Controls.AddRange(new Control[] { _txtSearch, _grid });
        ActiveControl = _txtSearch;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Search("");   // 初始显示页面导航 + 最近操作
    }

    private void Search(string keyword)
    {
        _hits.Clear();
        var user = _user;

        // 页面导航（始终显示）
        var pages = new[]
        {
            ("dashboard", "数据可视化大屏", "首页总览"),
            ("orders", "工单中心", "报修/派单/接单/完工"),
            ("picking", "智能算料·领料", "BOM×冗余/出库"),
            ("parts", "备件库管理", "配件/入库/预警"),
            ("devices", "设备与BOM", "台账/用量维护"),
            ("serial", "串口·扫码", "扫码枪/模拟器"),
            ("iot", "IoT·MQTT遥测", "温度曲线/自动报修"),
            ("users", "用户与日志", "账号/备份/日志"),
            ("report", "维修报表", "月度统计/成本榜"),
        };
        foreach (var (key, title, sub) in pages)
        {
            if (string.IsNullOrWhiteSpace(keyword) ||
                title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                sub.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                _hits.Add(new SearchHit("页面", key, title, sub, key));
        }

        if (!string.IsNullOrWhiteSpace(keyword) && keyword.Length >= 2)
        {
            try
            {
                // 工单（设备名/描述/工单号）
                foreach (DataRow r in BLL.PagedQuery.WorkOrders(null, keyword, false, 0, 1, 5).Items.Rows)
                    _hits.Add(new SearchHit("工单", r["OrderId"].ToString()!,
                        $"{r["OrderNo"]} — {r["DeviceName"]}",
                        $"{r["状态"]} · {r["ReporterName"]} · {Convert.ToDateTime(r["CreatedAt"]):MM-dd HH:mm}", "orders"));

                // 配件
                foreach (var p in new PartService().Search(keyword).Take(5))
                    _hits.Add(new SearchHit("配件", p.PartId.ToString(),
                        $"{p.PartCode} — {p.PartName}", $"{p.Spec} · 库存 {p.StockQty}{p.Unit}", "parts"));

                // 设备
                foreach (var d in new WorkOrderService().Devices(keyword).Take(3))
                    _hits.Add(new SearchHit("设备", d.DeviceId.ToString(),
                        $"{d.DeviceCode} — {d.DeviceName}", d.Location ?? "", "devices"));
            }
            catch { /* 搜索异常不影响页面导航 */ }
        }

        var t = new System.Data.DataTable();
        t.Columns.Add("类型", typeof(string));
        t.Columns.Add("标题", typeof(string));
        t.Columns.Add("详情", typeof(string));
        foreach (var h in _hits)
            t.Rows.Add(h.Type, h.Title, h.Subtitle);
        _grid.DataSource = t;
    }

    private void Jump(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _hits.Count) return;
        var hit = _hits[rowIndex];
        _navigate(hit.PageKey);
        Close();
    }
}
