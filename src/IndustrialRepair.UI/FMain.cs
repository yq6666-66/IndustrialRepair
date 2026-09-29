using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 主框架：顶部用户区 + 左侧权限导航（RBAC）+ 内容区。
/// 内置会话超时看门狗（30 分钟无操作自动登出，任务书：会话超时处理）。
/// </summary>
public class FMain : UIForm
{
    public static bool RequestRelogin;

    private readonly UserInfo _user;
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
    private readonly UILabel _lblUser;
    private readonly System.Windows.Forms.Timer _sessionTimer = new();

    private record NavItem(string Key, string Text, string Perm);

    public FMain(UserInfo user)
    {
        _user = user;
        Text = "工业维修智能化零件分配平台";
        ClientSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        // ---- 顶部条 ----
        var header = new UIPanel { Dock = DockStyle.Top, Height = 56, FillColor = Color.FromArgb(48, 116, 222) };
        var title = new UILabel
        {
            Text = "工业维修智能化零件分配平台", ForeColor = Color.White,
            Font = new Font("微软雅黑", 12F, FontStyle.Bold),
            Location = new Point(16, 14), Size = new Size(320, 28), AutoSize = false
        };
        _lblUser = new UILabel
        {
            Text = $"{user.RealName}（{user.RoleName}）  权限 {user.Permissions.Count} 项",
            ForeColor = Color.White, Font = new Font("微软雅黑", 9.5F),
            Location = new Point(720, 16), Size = new Size(260, 24), AutoSize = false, TextAlign = ContentAlignment.MiddleRight
        };
        var btnPwd = new UIButton
        { Text = "修改密码", ForeColor = Color.White, FillColor = Color.FromArgb(48, 116, 222), RectColor = Color.White, Location = new Point(990, 12), Size = new Size(84, 32) };
        btnPwd.Click += (_, _) => new FChangePwd(_user.UserId).ShowDialog();
        var btnLogout = new UIButton
        { Text = "退出登录", ForeColor = Color.White, FillColor = Color.FromArgb(214, 69, 66), RectColor = Color.White, Location = new Point(1082, 12), Size = new Size(84, 32) };
        btnLogout.Click += (_, _) =>
        {
            if (!Ui.Ask("确定退出登录？")) return;
            new AuthService().Logout();
            RequestRelogin = true;
            Close();
        };
        header.Controls.AddRange(new Control[] { title, _lblUser, btnPwd, btnLogout });

        // ---- 左侧导航（按权限显隐） ----
        var nav = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Color.FromArgb(35, 45, 62), Padding = new Padding(8, 12, 8, 8) };
        NavItem[] items =
        {
            new("dashboard", "数据可视化大屏", "dashboard.view"),
            new("orders",    "工单中心",       "order.view"),
            new("picking",   "智能算料·领料",  "picking.generate"),
            new("parts",     "配件库管理",     "part.manage"),
            new("devices",   "设备与BOM",      "part.manage"),
            new("serial",    "串口·扫码",      "serial.port"),
            new("iot",       "IoT·MQTT遥测",   "serial.port"),
            new("users",     "用户与日志",     "user.manage"),
            new("report",    "维修报表",       "order.view")
        };
        int y = 6;
        foreach (var it in items)
        {
            if (!_user.HasPerm(it.Perm)) continue;
            var key = it.Key;
            var btn = new UIButton
            {
                Text = "  " + it.Text, TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(6, y), Size = new Size(172, 40),
                FillColor = Color.FromArgb(35, 45, 62), RectColor = Color.FromArgb(35, 45, 62),
                ForeColor = Color.White, Font = new Font("微软雅黑", 10F), Cursor = Cursors.Hand
            };
            btn.Click += (_, _) => ShowPage(key, btn);
            nav.Controls.Add(btn);
            y += 46;
        }

        Controls.Add(_content);
        Controls.Add(nav);
        Controls.Add(header);

        // ---- 会话超时看门狗：每分钟心跳 ----
        _sessionTimer.Interval = 60_000;
        _sessionTimer.Tick += (_, _) =>
        {
            if (AppSession.Touch()) return;
            _sessionTimer.Stop();
            MessageBox.Show("会话已超时（30 分钟无操作），请重新登录。", "会话超时",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            new AuthService().Logout();
            RequestRelogin = true;
            Close();
        };
        _sessionTimer.Start();

        Load += (_, _) => ShowPage("dashboard", null);
    }

    private void ShowPage(string key, UIButton? source)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "dashboard" => new UcDashboard(),
                "orders"    => new UcWorkOrders(_user),
                "picking"   => new UcPicking(_user),
                "parts"     => new UcParts(),
                "devices"   => new UcDevices(),
                "serial"    => new UcSerial(),
                "iot"       => new UcIoT(),
                "users"     => new UcUsers(),
                "report"    => new UcReport(),
                _ => new UcDashboard()
            };
            page.Dock = DockStyle.Fill;
            _pages[key] = page;
        }
        _content.Controls.Clear();
        _content.Controls.Add(page);
        AppSession.Touch();

        if (page is IRefreshable r) r.RefreshData();

        // 高亮当前导航
        var nav = Controls[1];
        foreach (Control c in nav.Controls)
            if (c is UIButton b)
                b.FillColor = ReferenceEquals(b, source)
                    ? Color.FromArgb(70, 130, 220)
                    : Color.FromArgb(35, 45, 62);
    }
}

/// <summary>页面激活时刷新数据</summary>
internal interface IRefreshable
{
    void RefreshData();
}
