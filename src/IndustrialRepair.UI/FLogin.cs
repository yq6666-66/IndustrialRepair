using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 登录窗体：图形验证码 → 口令（bcrypt 校验）→ 可选多因素认证 → 进入系统。
/// 含注册、找回密码、一键初始化数据库。左侧品牌区 + 右侧卡片式表单。
/// 布局原则：文本标签一律 AutoSize / 大余量容器，固定像素盒会裁切 CJK 文字。
/// </summary>
public class FLogin : UIForm
{
    private readonly UITextBox _txtUser;
    private readonly UITextBox _txtPwd;
    private readonly UITextBox _txtCaptcha;
    private readonly PictureBox _picCaptcha = new() { Cursor = Cursors.Hand, Size = new Size(128, 34) };
    private readonly UILabel _lblHint = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, BackColor = Color.White,
        ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("微软雅黑", 8.5F), Visible = false
    };

    private readonly AuthService _auth = new();
    public UserInfo? LoggedUser { get; private set; }

    private string _captcha = "";

    public FLogin()
    {
        Text = "登录";
        ClientSize = new Size(940, 600);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Color.FromArgb(240, 243, 248);

        // ---- 左侧品牌区：渐变绘成 BackgroundImage，让透明子控件正确透出 ----
        var brand = new Panel
        {
            Dock = DockStyle.Left, Width = 360,
            BackgroundImage = BrandImage(360, 600), BackgroundImageLayout = ImageLayout.Stretch
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Color.Transparent, Padding = new Padding(34, 60, 12, 0), AutoScroll = false
        };

        UILabel BrandLabel(string text, float size, Color color, bool bold = false, int topGap = 0) => new()
        {
            Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = color,
            Font = new Font("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0, topGap, 0, 0)
        };

        flow.Controls.Add(BrandLabel("工业维修", 26F, Color.White, bold: true));
        flow.Controls.Add(BrandLabel("智能化零件分配平台", 17F, Color.White, bold: true, topGap: 6));
        flow.Controls.Add(BrandLabel("拍照 · 识别 · 智能算料一体化维修系统", 9.5F, Color.FromArgb(178, 205, 240), topGap: 8));
        flow.Controls.Add(BrandLabel("●  拍照识别：OpenCV + ZXing 双引擎", 9.5F, Color.FromArgb(226, 236, 250), topGap: 86));
        flow.Controls.Add(BrandLabel("●  BOM 智能算料 · 冗余系数可调", 9.5F, Color.FromArgb(226, 236, 250), topGap: 18));
        flow.Controls.Add(BrandLabel("●  MQTT 设备遥测 · 越限自动报修", 9.5F, Color.FromArgb(226, 236, 250), topGap: 18));
        flow.Controls.Add(BrandLabel("●  RBAC 权限 · bcrypt · 全程留痕", 9.5F, Color.FromArgb(226, 236, 250), topGap: 18));

        var brandBottom = new Panel
        {
            Dock = DockStyle.Bottom, AutoSize = true, BackColor = Color.Transparent,
            Padding = new Padding(34, 8, 12, 14)
        };
        brandBottom.Controls.Add(BrandLabel("v1.0 · WinForm + ADO.NET + SQL Server", 8.5F, Color.FromArgb(148, 178, 216)));

        brand.Controls.Add(flow);
        brand.Controls.Add(brandBottom);

        // ---- 右侧登录卡片：单列表格布局，行高随内容自适应 ----
        var card = new UIPanel
        {
            Location = new Point(396, 80), Size = new Size(516, 442),
            FillColor = Color.White, Radius = 12, RectColor = Color.FromArgb(226, 232, 240)
        };
        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12,
            BackColor = Color.White, Padding = new Padding(28, 24, 28, 14)
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 12; i++)
            tlp.RowStyles.Add(i == 2 || i == 5 ? new RowStyle(SizeType.Absolute, 10)
                          : i == 9 ? new RowStyle(SizeType.Percent, 100)
                          : i == 11 ? new RowStyle(SizeType.Absolute, 26)
                          : new RowStyle(SizeType.AutoSize));

        UILabel CellLabel(string text, float size, Color color, bool bold = false, int bottom = 0) => new()
        {
            Text = text, AutoSize = true, BackColor = Color.White, ForeColor = color,
            Font = new Font("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0, 0, 0, bottom)
        };

        _txtUser = Ui.Txt("用户名 / 工号", 0, 0, 300);
        _txtPwd = Ui.Txt("密码", 0, 0, 300, password: true);
        _txtCaptcha = Ui.Txt("验证码", 0, 0, 210);

        var captchaRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, BackColor = Color.White, Margin = new Padding(0)
        };
        _txtCaptcha.Margin = new Padding(0, 0, 10, 0);
        _picCaptcha.Margin = new Padding(0);
        captchaRow.Controls.Add(_txtCaptcha);
        captchaRow.Controls.Add(_picCaptcha);

        var btnLogin = new UIButton
        {
            Text = "登  录", Dock = DockStyle.Fill, Size = new Size(300, 46),
            FillColor = Color.FromArgb(41, 108, 202), RectColor = Color.FromArgb(41, 108, 202),
            ForeColor = Color.White, Font = new Font("微软雅黑", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand, Margin = new Padding(0, 10, 0, 4)
        };
        btnLogin.Click += OnLogin;

        var btnReg = Ui.Btn("注册账号", 0, 0, 108, 30, (_, _) => new FRegister().ShowDialog());
        var btnFind = Ui.Btn("找回密码", 0, 0, 108, 30, (_, _) => new FFindBack().ShowDialog());
        var btnInit = Ui.Btn("一键初始化", 0, 0, 116, 30, OnInitDb);
        btnReg.Margin = new Padding(0, 4, 12, 0);
        btnFind.Margin = new Padding(0, 4, 12, 0);
        btnInit.Margin = new Padding(0, 4, 0, 0);
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, BackColor = Color.White, Margin = new Padding(0)
        };
        btnRow.Controls.AddRange(new Control[] { btnReg, btnFind, btnInit });

        var demo = new UILabel
        {
            Text = "演示账号：admin / warehouse / engineer / reporter · 密码 123456",
            Dock = DockStyle.Fill, AutoSize = false, BackColor = Color.White,
            ForeColor = Color.FromArgb(130, 140, 155), Font = new Font("微软雅黑", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0)
        };

        tlp.Controls.Add(CellLabel("欢迎登录", 15F, Color.FromArgb(32, 41, 56), bold: true, bottom: 2), 0, 0);
        tlp.Controls.Add(CellLabel("请输入账号信息进入系统", 9F, Color.Gray), 0, 1);
        tlp.Controls.Add(_txtUser, 0, 3);
        tlp.Controls.Add(_txtPwd, 0, 4);
        tlp.Controls.Add(captchaRow, 0, 6);
        tlp.Controls.Add(btnLogin, 0, 7);
        tlp.Controls.Add(_lblHint, 0, 8);
        tlp.Controls.Add(btnRow, 0, 10);   // 第 9 行为弹性留白
        tlp.Controls.Add(demo, 0, 11);

        card.Controls.Add(tlp);

        _picCaptcha.Click += (_, _) => NewCaptcha();
        Controls.Add(card);
        Controls.Add(brand);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnLogin(null, EventArgs.Empty); };
        NewCaptcha();
        LoadLastUser();   // R4：记住上次登录用户名（仅用户名，不含任何口令）
    }

    /// <summary>本地偏好：上次登录用户名（%LocalAppData%\IndustrialRepair\ui.json）。</summary>
    private static string PrefFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "ui.json");

    private void LoadLastUser()
    {
        try
        {
            if (!File.Exists(PrefFile)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PrefFile));
            if (doc.RootElement.TryGetProperty("lastUser", out var u))
                _txtUser.Text = u.GetString() ?? "";
        }
        catch { /* 偏好读取失败忽略 */ }
    }

    private static void SaveLastUser(string userName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
            File.WriteAllText(PrefFile, $"{{\"lastUser\":{System.Text.Json.JsonSerializer.Serialize(userName)}}}");
        }
        catch { /* 偏好写入失败忽略 */ }
    }

    /// <summary>本机首个非回环 IPv4（登录日志留真实来源，替代写死 127.0.0.1）。</summary>
    private static string ClientIp()
    {
        try
        {
            return System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                     && !a.ToString().StartsWith("127."))?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private void ShowHint(string msg)
    {
        _lblHint.Text = msg;
        _lblHint.Visible = true;
    }

    /// <summary>品牌区背景：渐变 + 装饰圆（一次绘成位图，透明子控件可正确透出）。</summary>
    private static Bitmap BrandImage(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, w, h),
            Color.FromArgb(22, 66, 134), Color.FromArgb(45, 116, 214), 90f))
            g.FillRectangle(brush, 0, 0, w, h);
        using (var pen = new Pen(Color.FromArgb(38, 255, 255, 255), 2f))
        {
            g.DrawEllipse(pen, -70, h - 260, 230, 230);
            g.DrawEllipse(pen, 246, 36, 170, 170);
        }
        using (var brush = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
        {
            g.FillEllipse(brush, 282, h - 112, 92, 92);
            g.FillEllipse(brush, -34, 56, 124, 124);
        }
        return bmp;
    }

    private void OnInitDb(object? s, EventArgs e)
    {
        _lblHint.Visible = false;
        var log = new DbInitializer().EnsureAll();
        MessageBox.Show(log, "数据库初始化", MessageBoxButtons.OK, MessageBoxIcon.Information);
        NewCaptcha();
    }

    private void NewCaptcha()
    {
        const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var rnd = new Random();
        var code = new char[4];
        for (int i = 0; i < 4; i++) code[i] = chars[rnd.Next(chars.Length)];
        _captcha = new string(code);

        var bmp = new Bitmap(_picCaptcha.Width, _picCaptcha.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(240, 244, 248));
        for (int i = 0; i < 6; i++)
            using (var pen = new Pen(Color.FromArgb(rnd.Next(120, 220), rnd.Next(120, 220), rnd.Next(120, 220)), 1.2f))
                g.DrawLine(pen, rnd.Next(bmp.Width), rnd.Next(bmp.Height), rnd.Next(bmp.Width), rnd.Next(bmp.Height));
        for (int i = 0; i < 80; i++)
            bmp.SetPixel(rnd.Next(bmp.Width), rnd.Next(bmp.Height), Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        for (int i = 0; i < 4; i++)
        {
            float x = 12 + i * 28 + rnd.Next(-3, 3), y = 5 + rnd.Next(-3, 3);
            using var font = new Font("Arial", 15 + rnd.Next(3), FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(rnd.Next(0, 120), rnd.Next(0, 120), rnd.Next(0, 160)));
            g.TranslateTransform(x, y);
            g.RotateTransform(rnd.Next(-14, 14));
            g.DrawString(code[i].ToString(), font, brush, 0, 0);
            g.ResetTransform();
        }
        var old = _picCaptcha.Image;
        _picCaptcha.Image = bmp;
        old?.Dispose();
        _txtCaptcha.Text = "";
    }

    private void OnLogin(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtCaptcha.Text) ||
            !string.Equals(_txtCaptcha.Text.Trim(), _captcha, StringComparison.OrdinalIgnoreCase))
        {
            ShowHint("图形验证码不正确（点击图片可刷新）。");
            NewCaptcha();
            return;
        }
        NewCaptcha();   // R5：验证码一次性——通过校验即作废，重试必须使用新码

        var result = _auth.Login(_txtUser.Text.Trim(), _txtPwd.Text, ClientIp());
        if (!result.Success)
        {
            if (result.Message.StartsWith("MFA_REQUIRED|"))
            {
                var parts = result.Message.Split('|');
                // R1：验证码位为空 = 已真实发送到邮箱；非空 = 演示模式直接回显
                var hint = string.IsNullOrEmpty(parts[1])
                    ? $"多因素认证：验证码已发送至 {parts[2]}，请查收邮件。"
                    : $"多因素认证：验证码已发送至 {parts[2]}\n（演示环境验证码：{parts[1]}）";
                var code = Ui.Input(hint, "验证码：");
                if (string.IsNullOrWhiteSpace(code)) { ShowHint("已取消二次验证。"); NewCaptcha(); return; }
                result = _auth.VerifyMfa(_txtUser.Text.Trim(), code);
            }
            if (!result.Success) { ShowHint(result.Message); NewCaptcha(); return; }
        }

        LoggedUser = result.Data;
        SaveLastUser(_txtUser.Text.Trim());
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>注册（默认报修人角色）</summary>
public class FRegister : UIForm
{
    public FRegister()
    {
        Text = "注册账号";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(380, 300); StartPosition = FormStartPosition.CenterParent;

        var name = Ui.Txt("用户名（登录用）", 100, 20, 230);
        var real = Ui.Txt("真实姓名", 100, 58, 230);
        var phone = Ui.Txt("手机号（选填）", 100, 96, 230);
        var email = Ui.Txt("邮箱（找回密码用）", 100, 134, 230);
        var pwd = Ui.Txt("密码（至少8位，含字母和数字）", 100, 172, 230, password: true);

        var ok = Ui.Btn("注册", 100, 218, 110, 34, (_, _) =>
        {
            var r = new AuthService().Register(name.Text.Trim(), pwd.Text, real.Text.Trim(),
                string.IsNullOrWhiteSpace(phone.Text) ? null : phone.Text.Trim(),
                string.IsNullOrWhiteSpace(email.Text) ? null : email.Text.Trim());
            if (r.Success) { Ui.Info(r.Message); Close(); }
            else Ui.Error(r.Message);
        });
        var back = Ui.Btn("取消", 220, 218, 110, 34, (_, _) => Close());

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("用户名：", 10, 20, 85), name,
            Ui.Lbl("姓名：", 10, 58, 85), real,
            Ui.Lbl("手机号：", 10, 96, 85), phone,
            Ui.Lbl("邮箱：", 10, 134, 85), email,
            Ui.Lbl("密码：", 10, 172, 85), pwd, ok, back
        });
    }
}

/// <summary>找回密码（用户名+邮箱 → 验证码 → 新密码）</summary>
public class FFindBack : UIForm
{
    public FFindBack()
    {
        Text = "找回密码";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(390, 290); StartPosition = FormStartPosition.CenterParent;

        var name = Ui.Txt("用户名", 110, 20, 240);
        var email = Ui.Txt("注册时填写的邮箱", 110, 58, 240);
        var code = Ui.Txt("邮箱验证码", 110, 96, 120);
        var pwd = Ui.Txt("新密码（至少8位，含字母和数字）", 110, 134, 240, password: true);

        var send = Ui.Btn("发送验证码", 240, 92, 110, 32, (_, _) =>
        {
            var r = new AuthService().RequestResetCode(name.Text.Trim(), email.Text.Trim());
            if (r.Success)
                Ui.Info(string.IsNullOrEmpty(r.Data) ? r.Message : $"{r.Message}\n验证码：{r.Data}");
            else Ui.Error(r.Message);
        });
        var ok = Ui.Btn("重置密码", 110, 180, 110, 34, (_, _) =>
        {
            var r = new AuthService().ResetPassword(name.Text.Trim(), code.Text, pwd.Text);
            if (r.Success) { Ui.Info(r.Message); Close(); } else Ui.Error(r.Message);
        });
        var back = Ui.Btn("取消", 230, 180, 110, 34, (_, _) => Close());

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("用户名：", 15, 20, 90), name,
            Ui.Lbl("邮箱：", 15, 58, 90), email,
            Ui.Lbl("验证码：", 15, 96, 90), code, send,
            Ui.Lbl("新密码：", 15, 134, 90), pwd, ok, back
        });
    }
}

/// <summary>修改密码（当前登录用户）</summary>
public class FChangePwd : UIForm
{
    public FChangePwd(int userId) : this(userId, null) { }

    public FChangePwd(int userId, string? userName)
    {
        Text = "修改密码";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(370, 230); StartPosition = FormStartPosition.CenterParent;

        var old = Ui.Txt("当前密码", 100, 20, 230, password: true);
        var pwd = Ui.Txt("新密码（至少8位，含字母和数字）", 100, 58, 230, password: true);
        var pwd2 = Ui.Txt("再次输入新密码", 100, 96, 230, password: true);

        var ok = Ui.Btn("确定", 100, 144, 110, 34, (_, _) =>
        {
            if (pwd.Text != pwd2.Text) { Ui.Error("两次输入的新密码不一致。"); return; }
            var r = new AuthService().ChangePassword(userId, old.Text, pwd.Text);
            if (r.Success) { Ui.Info(r.Message); Close(); } else Ui.Error(r.Message);
        });
        var back = Ui.Btn("取消", 220, 144, 110, 34, (_, _) => Close());

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("当前密码：", 10, 20, 85), old,
            Ui.Lbl("新密码：", 10, 58, 85), pwd,
            Ui.Lbl("确认：", 10, 96, 85), pwd2, ok, back
        });
    }
}
