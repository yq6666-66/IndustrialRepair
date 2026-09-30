using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 主框架：顶部标题/用户区 + 左侧分组权限导航（RBAC）+ 内容区 + 底部状态栏。
/// 内置会话超时看门狗（30 分钟无操作自动登出，任务书：会话超时处理）。
/// </summary>
public class FMain : UIForm
{
    public static bool RequestRelogin;

    private readonly UserInfo _user;
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Dictionary<string, UIButton> _navButtons = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(10) };
    private readonly UILabel _lblUser;
    private readonly UILabel _lblPage;
    private readonly UILabel _lblClock;
    private readonly System.Windows.Forms.Timer _sessionTimer = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new();

    private sealed record NavItem(string Key, string Text, string Perm);
    private sealed record NavGroup(string Title, NavItem[] Items);

    public FMain(UserInfo user)
    {
        _user = user;
        Text = "工业维修智能化零件分配平台";
        ClientSize = new Size(1280, 760);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        // ---- 顶部标题栏：标题 + 当前模块 + 用户区（右侧自适应窗口宽度） ----
        var header = new UIPanel { Dock = DockStyle.Top, Height = 64, FillColor = Color.FromArgb(41, 108, 202) };
        var title = new UILabel
        {
            Text = "工业维修智能化零件分配平台", ForeColor = Color.White,
            Font = new Font("微软雅黑", 13F, FontStyle.Bold),
            Location = new Point(18, 18), Size = new Size(360, 30), AutoSize = false
        };
        _lblPage = new UILabel
        {
            ForeColor = Color.FromArgb(205, 226, 255), Font = new Font("微软雅黑", 10.5F),
            Location = new Point(388, 21), Size = new Size(340, 24), AutoSize = false
        };
        _lblUser = new UILabel
        {
            Text = $"{user.RealName}（{user.RoleName}）  ·  权限 {user.Permissions.Count} 项",
            ForeColor = Color.White, Font = new Font("微软雅黑", 9.5F),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 480, 20), Size = new Size(280, 24),
            AutoSize = false, TextAlign = ContentAlignment.MiddleRight
        };
        var btnPwd = new UIButton
        {
            Text = "修改密码", ForeColor = Color.White, FillColor = Color.FromArgb(41, 108, 202),
            RectColor = Color.FromArgb(160, 200, 245), Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 190, 16), Size = new Size(86, 32), Cursor = Cursors.Hand
        };
        btnPwd.Click += (_, _) => new FChangePwd(_user.UserId).ShowDialog();
        var btnLogout = new UIButton
        {
            Text = "退出登录", ForeColor = Color.White, FillColor = Color.FromArgb(214, 69, 66),
            RectColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 96, 16), Size = new Size(86, 32), Cursor = Cursors.Hand
        };
        btnLogout.Click += (_, _) =>
        {
            if (!Ui.Ask("确定退出登录？")) return;
            new AuthService().Logout();
            RequestRelogin = true;
            Close();
        };
        header.Controls.AddRange(new Control[] { title, _lblPage, _lblUser, btnPwd, btnLogout });

        // ---- 左侧分组导航（按权限显隐） ----
        var nav = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Color.FromArgb(32, 41, 56) };
        NavGroup[] groups =
        {
            new("总 览", new[] { new NavItem("dashboard", "数据可视化大屏", "dashboard.view") }),
            new("维修业务", new[]
            {
                new NavItem("orders",  "工单中心",      "order.view"),
                new NavItem("picking", "智能算料·领料", "picking.generate"),
                new NavItem("report",  "维修报表",      "order.view")
            }),
            new("基础数据", new[]
            {
                new NavItem("parts",   "配件库管理", "part.manage"),
                new NavItem("devices", "设备与BOM",  "part.manage")
            }),
            new("系统集成", new[]
            {
                new NavItem("serial", "串口·扫码",    "serial.port"),
                new NavItem("iot",    "IoT·MQTT遥测", "serial.port")
            }),
            new("系统管理", new[] { new NavItem("users", "用户与日志", "user.manage") })
        };
        int y = 12;
        foreach (var grp in groups)
        {
            var visible = Array.FindAll(grp.Items, it => _user.HasPerm(it.Perm));
            if (visible.Length == 0) continue;
            nav.Controls.Add(new UILabel
            {
                Text = grp.Title, ForeColor = Color.FromArgb(125, 140, 162),
                Font = new Font("微软雅黑", 8.5F), Location = new Point(16, y),
                Size = new Size(160, 18), AutoSize = false
            });
            y += 24;
            foreach (var it in visible)
            {
                var key = it.Key;
                var btn = new UIButton
                {
                    Text = "  " + it.Text, TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(8, y), Size = new Size(184, 40),
                    FillColor = Color.FromArgb(32, 41, 56), RectColor = Color.FromArgb(32, 41, 56),
                    ForeColor = Color.White, Font = new Font("微软雅黑", 10F), Cursor = Cursors.Hand
                };
                btn.Click += (_, _) => ShowPage(key);
                nav.Controls.Add(btn);
                _navButtons[key] = btn;
                y += 46;
            }
            y += 8;
        }

        // ---- 底部状态栏：数据库信息 + 实时时钟 ----
        var status = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(238, 242, 247) };
        status.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(216, 222, 230));
            e.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
        };
        var lblDb = new UILabel
        {
            Text = "数据库：IndustrialRepairDb（LocalDB）· 16 表 / 9 存储过程 / 3 触发器 / 4 视图",
            ForeColor = Color.FromArgb(110, 122, 138), Font = new Font("微软雅黑", 8.5F),
            Location = new Point(14, 6), Size = new Size(580, 18), AutoSize = false
        };
        _lblClock = new UILabel
        {
            ForeColor = Color.FromArgb(110, 122, 138), Font = new Font("微软雅黑", 8.5F),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 448, 6), Size = new Size(430, 18),
            AutoSize = false, TextAlign = ContentAlignment.MiddleRight
        };
        status.Controls.AddRange(new Control[] { lblDb, _lblClock });
        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => TickClock();
        TickClock();
        _clockTimer.Start();

        // Dock 布局：后添加的先参与停靠（顶栏 → 导航 → 状态栏 → 内容填充）
        Controls.Add(_content);
        Controls.Add(status);
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

        Load += (_, _) => ShowPage("dashboard");
    }

    private void TickClock() =>
        _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " 星期" + "日一二三四五六"[(int)DateTime.Now.DayOfWeek]
            + "  ·  会话空闲上限 30 分钟";

    private void ShowPage(string key)
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

        // 高亮当前导航 + 页头显示当前模块名
        foreach (var (k, b) in _navButtons)
        {
            var selected = k == key;
            b.FillColor = selected ? Color.FromArgb(64, 134, 240) : Color.FromArgb(32, 41, 56);
            b.RectColor = b.FillColor;
            b.Font = new Font("微软雅黑", 10F, selected ? FontStyle.Bold : FontStyle.Regular);
            if (selected) _lblPage.Text = "／  " + b.Text.Trim();
        }
    }
}

/// <summary>页面激活时刷新数据</summary>
internal interface IRefreshable
{
    void RefreshData();
}
