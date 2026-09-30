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
/// </summary>
public class FLogin : UIForm
{
    private readonly UITextBox _txtUser;
    private readonly UITextBox _txtPwd;
    private readonly UITextBox _txtCaptcha;
    private readonly PictureBox _picCaptcha = new() { Cursor = Cursors.Hand };
    private readonly UILabel _lblHint = new()
    {
        AutoSize = false, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter
    };

    private readonly AuthService _auth = new();
    public UserInfo? LoggedUser { get; private set; }

    private string _captcha = "";

    public FLogin()
    {
        Text = "登录 · 工业维修智能化零件分配平台";
        ClientSize = new Size(860, 540);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Color.FromArgb(240, 243, 248);

        // ---- 左侧品牌区（渐变 + 装饰圆） ----
        var brand = new Panel { Dock = DockStyle.Left, Width = 380 };
        brand.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(brand.ClientRectangle,
                Color.FromArgb(22, 66, 134), Color.FromArgb(45, 116, 214), 90f))
                g.FillRectangle(brush, brand.ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(38, 255, 255, 255), 2f))
            {
                g.DrawEllipse(pen, -70, 340, 230, 230);
                g.DrawEllipse(pen, 266, 36, 170, 170);
            }
            using (var brush = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                g.FillEllipse(brush, 302, 428, 92, 92);
                g.FillEllipse(brush, -34, 56, 124, 124);
            }
        };

        UILabel Brand(string text, int y, int h, float size, Color color, bool bold = false) => new()
        {
            Text = text, Location = new Point(36, y), Size = new Size(320, h), AutoSize = false,
            BackColor = Color.Transparent, ForeColor = color,
            Font = new Font("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular)
        };

        brand.Controls.AddRange(new Control[]
        {
            Brand("工业维修", 66, 52, 28F, Color.White, true),
            Brand("智能化零件分配平台", 122, 34, 17F, Color.White, true),
            Brand("拍照 · 识别 · 智能算料一体化维修系统", 162, 22, 9.5F, Color.FromArgb(178, 205, 240)),
            Brand("●  拍照识别：OpenCV + ZXing 双引擎", 292, 24, 9.5F, Color.FromArgb(226, 236, 250)),
            Brand("●  BOM 智能算料 · 冗余系数可调", 326, 24, 9.5F, Color.FromArgb(226, 236, 250)),
            Brand("●  MQTT 设备遥测 · 越限自动报修", 360, 24, 9.5F, Color.FromArgb(226, 236, 250)),
            Brand("●  RBAC 权限 · bcrypt · 全程留痕", 394, 24, 9.5F, Color.FromArgb(226, 236, 250)),
            Brand("v1.0 · WinForm + ADO.NET + SQL Server 三层架构", 486, 20, 8.5F, Color.FromArgb(148, 178, 216))
        });

        // ---- 右侧登录卡片 ----
        var card = new UIPanel
        {
            Location = new Point(420, 66), Size = new Size(400, 384),
            FillColor = Color.White, Radius = 12, RectColor = Color.FromArgb(226, 232, 240)
        };
        var lblWelcome = new UILabel
        {
            Text = "欢迎登录", ForeColor = Color.FromArgb(32, 41, 56), Font = new Font("微软雅黑", 15F, FontStyle.Bold),
            Location = new Point(32, 26), Size = new Size(336, 30), AutoSize = false
        };
        var lblSub = new UILabel
        {
            Text = "请输入账号信息进入系统", ForeColor = Color.Gray, Font = new Font("微软雅黑", 9F),
            Location = new Point(32, 58), Size = new Size(336, 20), AutoSize = false
        };

        _txtUser = Ui.Txt("用户名 / 工号", 32, 96, 336);
        _txtPwd = Ui.Txt("密码", 32, 142, 336, password: true);
        _txtCaptcha = Ui.Txt("验证码", 32, 188, 196);
        _picCaptcha.Location = new Point(240, 186);
        _picCaptcha.Size = new Size(128, 34);

        var btnLogin = new UIButton
        {
            Text = "登  录", Location = new Point(32, 236), Size = new Size(336, 44),
            FillColor = Color.FromArgb(41, 108, 202), RectColor = Color.FromArgb(41, 108, 202),
            ForeColor = Color.White, Font = new Font("微软雅黑", 11F, FontStyle.Bold), Cursor = Cursors.Hand
        };
        btnLogin.Click += OnLogin;

        _lblHint.Location = new Point(32, 288);
        _lblHint.Size = new Size(336, 20);
        _lblHint.Font = new Font("微软雅黑", 8.5F);

        var btnReg = Ui.Btn("注册账号", 32, 322, 100, 30, (_, _) => new FRegister().ShowDialog());
        var btnFind = Ui.Btn("找回密码", 142, 322, 100, 30, (_, _) => new FFindBack().ShowDialog());
        var btnInit = Ui.Btn("一键初始化", 252, 322, 116, 30, OnInitDb);

        card.Controls.AddRange(new Control[]
        {
            lblWelcome, lblSub, _txtUser, _txtPwd, _txtCaptcha, _picCaptcha,
            btnLogin, _lblHint, btnReg, btnFind, btnInit
        });

        var demo = new UILabel
        {
            Text = "演示账号：admin / warehouse / engineer / reporter · 密码 123456",
            Location = new Point(380, 480), Size = new Size(480, 20), AutoSize = false,
            ForeColor = Color.FromArgb(130, 140, 155), Font = new Font("微软雅黑", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _picCaptcha.Click += (_, _) => NewCaptcha();
        Controls.AddRange(new Control[] { card, demo, brand });
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnLogin(null, EventArgs.Empty); };
        NewCaptcha();
    }

    private void OnInitDb(object? s, EventArgs e)
    {
        _lblHint.Text = "";
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
            _lblHint.Text = "图形验证码不正确（点击图片可刷新）。";
            NewCaptcha();
            return;
        }

        var result = _auth.Login(_txtUser.Text.Trim(), _txtPwd.Text);
        if (!result.Success)
        {
            if (result.Message.StartsWith("MFA_REQUIRED|"))
            {
                var parts = result.Message.Split('|');
                var code = Ui.Input($"多因素认证：验证码已发送至 {parts[2]}\n（演示环境验证码：{parts[1]}）", "验证码：");
                if (string.IsNullOrWhiteSpace(code)) { _lblHint.Text = "已取消二次验证。"; NewCaptcha(); return; }
                result = _auth.VerifyMfa(_txtUser.Text.Trim(), code);
            }
            if (!result.Success) { _lblHint.Text = result.Message; NewCaptcha(); return; }
        }

        LoggedUser = result.Data;
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
        ClientSize = new Size(380, 300); StartPosition = FormStartPosition.CenterParent;

        var name = Ui.Txt("用户名（登录用）", 100, 20, 230);
        var real = Ui.Txt("真实姓名", 100, 58, 230);
        var phone = Ui.Txt("手机号（选填）", 100, 96, 230);
        var email = Ui.Txt("邮箱（找回密码用）", 100, 134, 230);
        var pwd = Ui.Txt("密码（≥6位）", 100, 172, 230, password: true);

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
        ClientSize = new Size(390, 290); StartPosition = FormStartPosition.CenterParent;

        var name = Ui.Txt("用户名", 110, 20, 240);
        var email = Ui.Txt("注册时填写的邮箱", 110, 58, 240);
        var code = Ui.Txt("邮箱验证码", 110, 96, 120);
        var pwd = Ui.Txt("新密码（≥6位）", 110, 134, 240, password: true);

        var send = Ui.Btn("发送验证码", 240, 92, 110, 32, (_, _) =>
        {
            var r = new AuthService().RequestResetCode(name.Text.Trim(), email.Text.Trim());
            if (r.Success) Ui.Info($"{r.Message}\n验证码：{r.Data}"); else Ui.Error(r.Message);
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
        ClientSize = new Size(370, 230); StartPosition = FormStartPosition.CenterParent;

        var old = Ui.Txt("当前密码", 100, 20, 230, password: true);
        var pwd = Ui.Txt("新密码（≥6位）", 100, 58, 230, password: true);
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
