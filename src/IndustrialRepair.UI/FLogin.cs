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
/// 含注册、找回密码、一键初始化数据库。
/// </summary>
public class FLogin : UIForm
{
    private readonly UITextBox _txtUser = Ui.Txt("用户名", 130, 78, 220);
    private readonly UITextBox _txtPwd = Ui.Txt("密码", 130, 120, 220, password: true);
    private readonly UITextBox _txtCaptcha = Ui.Txt("验证码", 130, 162, 100);
    private readonly PictureBox _picCaptcha = new() { Location = new Point(240, 158), Size = new Size(110, 36), Cursor = Cursors.Hand };
    private readonly UILabel _lblHint = new()
    {
        Location = new Point(60, 250), Size = new Size(330, 40), AutoSize = false,
        ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleCenter
    };

    private readonly AuthService _auth = new();
    public UserInfo? LoggedUser { get; private set; }

    private string _captcha = "";

    public FLogin()
    {
        Text = "登录";
        ClientSize = new Size(450, 400);
        StartPosition = FormStartPosition.CenterScreen;
        ShowTitle = true;
        TitleFont = new Font("微软雅黑", 13F, FontStyle.Bold);

        var title = new UILabel
        {
            Text = "工业维修智能化零件分配平台",
            Location = new Point(40, 26), Size = new Size(370, 34), AutoSize = false,
            Font = new Font("微软雅黑", 15F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter
        };
        var subTitle = new UILabel
        {
            Text = "拍照 · 识别 · 智能算料一体化维修系统",
            Location = new Point(40, 60), Size = new Size(370, 20), AutoSize = false,
            Font = new Font("微软雅黑", 9F), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleCenter
        };

        var lblUser = Ui.Lbl("用户名：", 40, 78, 80);
        var lblPwd = Ui.Lbl("密码：", 40, 120, 80);
        var lblCap = Ui.Lbl("验证码：", 40, 162, 80);

        var btnLogin = Ui.Btn("登 录", 130, 205, 220, 38, OnLogin);
        btnLogin.Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);

        var btnReg = Ui.Btn("注册账号", 60, 296, 100, 30, (_, _) => new FRegister().ShowDialog());
        var btnFind = Ui.Btn("找回密码", 170, 296, 100, 30, (_, _) => new FFindBack().ShowDialog());
        var btnInit = Ui.Btn("一键初始化", 280, 296, 110, 30, OnInitDb);

        var demo = new UILabel
        {
            Text = "默认账号：admin / warehouse / engineer / reporter，密码 123456",
            Location = new Point(40, 340), Size = new Size(370, 20), AutoSize = false,
            ForeColor = Color.Gray, Font = new Font("微软雅黑", 8.5F), TextAlign = ContentAlignment.MiddleCenter
        };

        _picCaptcha.Click += (_, _) => NewCaptcha();
        Controls.AddRange(new Control[] { title, subTitle, lblUser, _txtUser, lblPwd, _txtPwd, lblCap, _txtCaptcha, _picCaptcha, btnLogin, _lblHint, btnReg, btnFind, btnInit, demo });
        AcceptButton = null;
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
            float x = 10 + i * 26 + rnd.Next(-3, 3), y = 6 + rnd.Next(-3, 3);
            using var font = new Font("Arial", 16 + rnd.Next(3), FontStyle.Bold);
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
