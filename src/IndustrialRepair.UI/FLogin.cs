using System;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 登录窗体：借鉴 SunnyUI UILoginForm（内置插画 + 卡片式表单 + OnLogin 契约），
/// 图形验证码 → 口令（bcrypt 校验）→ 可选多因素认证 → 进入系统。
/// 含注册、找回密码、一键初始化数据库。
/// 依赖契约（探针核实 3.9.8）：OnLogin 为 bool(string,string)，返回 true 表示成功；
/// edtUser/edtPassword/btnLogin 为 private 不可重排，只能通过 UserName/Password 属性读写。
/// 布局契约（探针实测）：基类在 Shown 中按设计稿 750×450 重置窗体并钳死 Min/Max，
/// 插画为基类自绘背景（固定 750×450，非子控件）——因此绝不改窗体尺寸：
/// 追加内容全部落在基类不触碰的区域（卡片正下方的自建延伸面板 + 左下插画带）。
/// </summary>
public class FLogin : UILoginForm
{
    // 卡片延伸面板：衔接基类卡片（126+245=371 处收边），承载验证码行与提示槽
    private readonly UIPanel _extCard = new()
    {
        Location = new Point(433, 371), Size = new Size(190, 76),
        FillColor = Color.White, RectColor = Color.FromArgb(226, 232, 240)
    };
    private readonly UITextBox _txtCaptcha;
    private readonly PictureBox _picCaptcha = new() { Cursor = Cursors.Hand, Size = new Size(76, 29) };
    // 提示槽：默认灰字演示账号提示，出错时切换红字错误文案（共用同一位置避免重叠）
    private readonly UILabel _lblSlot = new()
    {
        AutoSize = false, Size = new Size(182, 32),
        BackColor = Color.White, ForeColor = Color.FromArgb(130, 140, 155),
        TextAlign = ContentAlignment.TopCenter,
        Font = new Font("微软雅黑", 7.5F)
    };

    private readonly AuthService _auth = new();
    public UserInfo? LoggedUser { get; private set; }

    // 底部功能行：左下插画带（白底幽灵按钮）
    private readonly FlowLayoutPanel _bottomRow = new()
    {
        Location = new Point(40, 404), Size = new Size(324, 40),
        FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent
    };

    private string _captcha = "";

    public FLogin()
    {
        Text = "登录";
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;

        // ---- UILoginForm 内置要素：左侧插画 + 右侧卡片（基类全权管理，不改尺寸）----
        Title = "工业维修智能化零件分配平台";
        SubText = "";   // 基类把 SubText 固定画在 (426,421)，与本窗提示槽重叠，故留空
        LoginImage = UILoginImage.Login1;
        UserNameWatermark = "用户名 / 工号";
        PasswordWatermark = "密码";
        ButtonLoginText = "登  录";
        ButtonCancelText = "退  出";
        LoginText = "账号登录";

        _txtCaptcha = Ui.Txt("验证码", 4, 6, 102);
        _txtCaptcha.FillColor = Color.White;
        _txtCaptcha.ForeColor = Color.FromArgb(32, 41, 56);
        _txtCaptcha.RectColor = Color.FromArgb(200, 206, 214);
        _txtCaptcha.WatermarkColor = Color.FromArgb(130, 140, 155);
        _picCaptcha.Location = new Point(110, 6);
        _lblSlot.Location = new Point(4, 40);
        _extCard.Controls.Add(_txtCaptcha);
        _extCard.Controls.Add(_picCaptcha);
        _extCard.Controls.Add(_lblSlot);
        Controls.Add(_extCard);
        ShowDemo();

        // ---- 底部功能行：注册 / 找回 / 初始化（白底幽灵按钮，浮于插画上）----
        var btnReg = GhostBtn("注册账号", (_, _) => new FRegister().ShowDialog());
        var btnFind = GhostBtn("找回密码", (_, _) => new FFindBack().ShowDialog());
        var btnInit = GhostBtn("一键初始化", OnInitDb);
        btnReg.Margin = new Padding(0, 4, 12, 0);
        btnFind.Margin = new Padding(0, 4, 12, 0);
        btnInit.Margin = new Padding(0, 4, 0, 0);
        _bottomRow.Controls.AddRange(new Control[] { btnReg, btnFind, btnInit });
        Controls.Add(_bottomRow);

        // ---- 登录契约：返回 true = 成功（自设 DialogResult 并关闭，不依赖框架行为）----
        OnLogin += TryLogin;
        _picCaptcha.Click += (_, _) => NewCaptcha();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && ActiveControl is not UIButton)
                TryLogin(UserName, Password);
        };

        NewCaptcha();
        LoadLastUser();   // R4：记住上次登录用户名（仅用户名，不含任何口令）
    }

    private static UIButton GhostBtn(string text, EventHandler onClick) => new()
    {
        Text = text, Size = new Size(100, 32),
        FillColor = Color.White, RectColor = Color.White,
        ForeColor = Theme.Current.Accent,
        Font = new Font("微软雅黑", 8.5F), Cursor = Cursors.Hand
    };

    /// <summary>提示槽：默认态显示演示账号（灰字）。</summary>
    private void ShowDemo() => SetSlot("演示：admin / warehouse /\nengineer / reporter · 123456",
        Color.FromArgb(130, 140, 155));

    private void SetSlot(string text, Color color)
    {
        _lblSlot.Text = text;
        _lblSlot.ForeColor = color;
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
                UserName = u.GetString() ?? "";
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

    private void ShowHint(string msg) => SetSlot(msg, Color.Firebrick);

    private void OnInitDb(object? s, EventArgs e)
    {
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(240, 244, 248));
        for (int i = 0; i < 5; i++)
            using (var pen = new Pen(Color.FromArgb(rnd.Next(120, 220), rnd.Next(120, 220), rnd.Next(120, 220)), 1.1f))
                g.DrawLine(pen, rnd.Next(bmp.Width), rnd.Next(bmp.Height), rnd.Next(bmp.Width), rnd.Next(bmp.Height));
        for (int i = 0; i < 70; i++)
            bmp.SetPixel(rnd.Next(bmp.Width), rnd.Next(bmp.Height), Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        // 76×29：4 字符起始 x=8、步进 16，旋转 ±12° 保证末字符完整
        for (int i = 0; i < 4; i++)
        {
            float x = 8 + i * 16 + rnd.Next(-2, 2), y = 5 + rnd.Next(-2, 2);
            using var font = new Font("Arial", 12 + rnd.Next(2), FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(rnd.Next(0, 120), rnd.Next(0, 120), rnd.Next(0, 160)));
            g.TranslateTransform(x, y);
            g.RotateTransform(rnd.Next(-12, 12));
            g.DrawString(code[i].ToString(), font, brush, 0, 0);
            g.ResetTransform();
        }
        var old = _picCaptcha.Image;
        _picCaptcha.Image = bmp;
        old?.Dispose();
        _txtCaptcha.Text = "";
    }

    /// <summary>UILoginForm 登录契约：返回 true = 登录成功（验证码 → bcrypt → 可选 MFA）。</summary>
    private bool TryLogin(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(_txtCaptcha.Text) ||
            !string.Equals(_txtCaptcha.Text.Trim(), _captcha, StringComparison.OrdinalIgnoreCase))
        {
            ShowHint("验证码不正确，点击图片刷新");
            NewCaptcha();
            return false;
        }
        NewCaptcha();   // R5：验证码一次性——通过校验即作废，重试必须使用新码

        var result = _auth.Login(userName.Trim(), password, ClientIp());
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
                if (string.IsNullOrWhiteSpace(code)) { ShowHint("已取消二次验证。"); NewCaptcha(); return false; }
                result = _auth.VerifyMfa(userName.Trim(), code);
            }
            if (!result.Success) { ShowHint(result.Message); NewCaptcha(); return false; }
        }

        LoggedUser = result.Data;
        SaveLastUser(userName.Trim());
        DialogResult = DialogResult.OK;
        Close();
        return true;
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
