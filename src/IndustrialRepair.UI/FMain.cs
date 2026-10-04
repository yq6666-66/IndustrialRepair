using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private Panel? _header;
    private UIButton? _btnTheme;
    private string _lastPageKey = "";
    private UILabel? _lblHealth;

    private sealed record NavItem(string Key, string Text, string Perm);
    private sealed record NavGroup(string Title, NavItem[] Items);

    public FMain(UserInfo user)
    {
        _user = user;
        Text = "工业维修智能化零件分配平台";
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(1024, 600);   // R30：防止窗口过小导致按钮不可达
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        // ---- 顶部标题栏：标题 + 当前模块 + 用户区（右侧自适应窗口宽度） ----
        // 用普通 Panel 而非 UIPanel：SunnyUI UILabel 在 UIPanel 上底色会发白（导航/状态栏所在普通 Panel 无此问题）
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Current.HeaderBar };
        var title = new UILabel
        {
            Text = "工业维修智能化零件分配平台", ForeColor = Color.White, BackColor = Color.Transparent,
            Font = new Font("微软雅黑", 13F, FontStyle.Bold),
            Location = new Point(18, 18), Size = new Size(360, 30), AutoSize = false
        };
        _lblPage = new UILabel
        {
            ForeColor = Color.FromArgb(205, 226, 255), BackColor = Color.Transparent,
            Font = new Font("微软雅黑", 10.5F),
            Location = new Point(388, 21), Size = new Size(340, 24), AutoSize = false
        };
        _lblUser = new UILabel
        {
            Text = $"{user.RealName}（{user.RoleName}）  ·  权限 {user.Permissions.Count} 项",
            ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("微软雅黑", 9.5F),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 480, 20), Size = new Size(280, 24),
            AutoSize = false, TextAlign = ContentAlignment.MiddleRight
        };
        var btnTheme = new UIButton
        {
            Text = Theme.IsDark ? "☀ 亮色" : "🌙 暗色", ForeColor = Color.White,
            FillColor = Color.Transparent, RectColor = Color.FromArgb(160, 200, 245),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 590, 16), Size = new Size(86, 32), Cursor = Cursors.Hand
        };
        btnTheme.Click += (_, _) =>
        {
            Theme.Toggle();
            btnTheme.Text = Theme.IsDark ? "☀ 亮色" : "🌙 暗色";
        };
        _header = header; _btnTheme = btnTheme;
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
        header.Controls.AddRange(new Control[] { title, _lblPage, _lblUser, btnTheme, btnPwd, btnLogout });
        _header = header; _btnTheme = btnTheme;

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
            new("管理", new[]
            {
                new NavItem("maintenance", "维护计划中心", "dashboard.view"),   // R41-R45：PM/EOQ/供应商/停机
                new NavItem("users", "用户与日志", "user.manage")
            })
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
        var lblAbout = new UILabel
        {
            Text = "关于", AutoSize = true, Location = new Point(ClientSize.Width - 520, 6),
            Font = new Font("微软雅黑", 8.5F), ForeColor = Color.FromArgb(41, 108, 202),
            Cursor = Cursors.Hand
        };
        lblAbout.Click += (_, _) => new FAbout().ShowDialog(this);

        // R13：连接健康指示灯（时钟 Timer 每 30 秒刷新颜色）
        _lblHealth = new UILabel
        {
            Text = "● 已连接", AutoSize = true, Location = new Point(ClientSize.Width - 570, 6),
            Font = new Font("微软雅黑", 8.5F), ForeColor = Color.FromArgb(80, 160, 90),
            Cursor = Cursors.Hand, Tag = "health"
        };
        status.Controls.AddRange(new Control[] { lblDb, _lblClock, lblAbout, _lblHealth });
        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => TickClock();
        TickClock();
        _clockTimer.Start();

        // Dock 布局：后添加的先参与停靠（顶栏 → 导航 → 状态栏 → 内容填充）
        Controls.Add(_content);
        Controls.Add(status);
        Controls.Add(nav);
        Controls.Add(header);

        // OR1：主题切换广播 → 刷新框架颜色 + 遍历所有表格重着色
        Theme.Changed += () => BeginInvoke(() =>
        {
            if (IsDisposed) return;
            var t = Theme.Current;
            if (_header != null) _header.BackColor = t.HeaderBar;
            nav.BackColor = t.NavBg;
            status.BackColor = t.PanelBg;
            BackColor = t.PageBg;
            foreach (Control page in _content.Controls)
                ApplyThemeRecursive(page);
        });

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

        // ---- NR3 托盘与通知中心：MQ 自动报修/定时任务预警跨页面气泡提醒 ----
        // 说明：H.NotifyIcon 2.x 已无 WinForms 目标（核心包为 WPF），故用 WinForms 内置 NotifyIcon
        _tray = new NotifyIcon
        {
            Text = "工业维修智能化零件分配平台",
            Icon = CreateAppIcon(),
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                WindowState = FormWindowState.Maximized;
                Activate();
            }
        };
        NotificationCenter.Raised += OnNotification;
        ScheduleHost.Start();   // NR4：登录后启动定时任务（库存预警汇总/超时工单扫描）
        FormClosed += (_, _) =>
        {
            NotificationCenter.Raised -= OnNotification;
            ScheduleHost.Stop();
            // R3：保存当前页面列宽
            if (_content.Controls.Count > 0 && !string.IsNullOrEmpty(_lastPageKey))
                foreach (Control c in _content.Controls[0].Controls)
                    if (c is UIDataGridView g) Ui.SaveColumnWidths(g, _lastPageKey);
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
        };
    }

    private readonly NotifyIcon? _tray;

    private void OnNotification(AppNotification n)
    {
        if (IsDisposed || _tray == null) return;
        BeginInvoke(() => _tray.ShowBalloonTip(3000, n.Title, n.Body,
            n.Level switch
            {
                NotificationLevel.Alert => ToolTipIcon.Error,
                NotificationLevel.Warning => ToolTipIcon.Warning,
                _ => ToolTipIcon.Info
            }));
    }

    /// <summary>运行时绘制托盘图标（品牌蓝圆角 + 白色"修"字），无需外部资源。</summary>
    private static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(41, 108, 202));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, 12, 12, 180, 90);
            path.AddArc(20, 0, 12, 12, 270, 90);
            path.AddArc(20, 20, 12, 12, 0, 90);
            path.AddArc(0, 20, 12, 12, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
            using var font = new Font("微软雅黑", 14F, FontStyle.Bold);
            var textSize = g.MeasureString("修", font);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString("修", font, textBrush, (32 - textSize.Width) / 2, (32 - textSize.Height) / 2 + 1);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>递归重着色（OR1：主题切换时遍历所有子控件）。</summary>
    private static void ApplyThemeRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case UIDataGridView grid:
                    Ui.ApplyGridTheme(grid);
                    break;
                case Panel p when p.BackColor != Color.Transparent:
                    if (p.BackColor == ThemePalette.Light.PageBg || p.BackColor == ThemePalette.Dark.PageBg
                        || p.BackColor == ThemePalette.Light.PanelBg || p.BackColor == ThemePalette.Dark.PanelBg
                        || p.BackColor == ThemePalette.Light.NavBg || p.BackColor == ThemePalette.Dark.NavBg)
                        p.BackColor = Theme.Current.PanelBg;
                    break;
            }
            if (c.Controls.Count > 0) ApplyThemeRecursive(c);
        }
    }

    private void TickClock()
    {
        _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " 星期" + "日一二三四五六"[(int)DateTime.Now.DayOfWeek]
            + "  ·  F5 刷新 · Ctrl+K 搜索";

        // R13：连接健康指示（每次时钟滴答检测一次）
        if (_lblHealth != null)
        {
            try
            {
                IndustrialRepair.DAL.DbHelper.ExecuteScalar("SELECT 1");
                _lblHealth.Text = "● 已连接";
                _lblHealth.ForeColor = Color.FromArgb(80, 160, 90);
            }
            catch
            {
                _lblHealth.Text = "● 断开";
                _lblHealth.ForeColor = Color.Firebrick;
            }
        }
    }

    /// <summary>F5 刷新 / Ctrl+K 全局搜索（R4 + OR6）。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5 && _content.Controls.Count > 0 && _content.Controls[0] is IRefreshable page)
        {
            Ui.Busy(page.RefreshData);
            return true;
        }
        if (keyData == (Keys.Control | Keys.K))
        {
            using var dlg = new FGlobalSearch(_user, ShowPage);
            dlg.ShowDialog(this);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

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
                "maintenance" => new UcMaintenance(),
                _ => new UcDashboard()
            };
            page.Dock = DockStyle.Fill;
            _pages[key] = page;
        }
        _content.Controls.Clear();
        _content.Controls.Add(page);
        AppSession.Touch();

        if (page is IRefreshable r) r.RefreshData();

        // R3：恢复列宽（DataSource 已就位）
        foreach (Control c in page.Controls)
            if (c is UIDataGridView g) Ui.RestoreColumnWidths(g, key);

        // R3：保存上一个页面的列宽（在 Clear 前无法取到，此处延迟到下次切换或关闭）
        _lastPageKey = key;

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
