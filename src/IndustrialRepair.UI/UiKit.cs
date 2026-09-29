using System;
using System.Drawing;
using System.Windows.Forms;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>界面小工具：统一尺寸的控件工厂与消息提示。</summary>
internal static class Ui
{
    public static UIButton Btn(string text, int x, int y, int w = 88, int h = 34, EventHandler? onClick = null)
    {
        var b = new UIButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
        if (onClick != null) b.Click += onClick;
        return b;
    }

    public static UITextBox Txt(string watermark, int x, int y, int w = 200, bool password = false)
    {
        var t = new UITextBox
        {
            Watermark = watermark, Location = new Point(x, y), Size = new Size(w, 30),
            PasswordChar = password ? '*' : '\0'
        };
        return t;
    }

    public static UILabel Lbl(string text, int x, int y, int w = 90)
        => new() { Text = text, Location = new Point(x, y), Size = new Size(w, 24), AutoSize = false, TextAlign = ContentAlignment.MiddleRight };

    public static UIComboBox Combo(int x, int y, int w = 160)
        => new() { Location = new Point(x, y), Size = new Size(w, 30), DropDownStyle = UIDropDownStyle.DropDownList };

    public static UIDataGridView Grid(int x, int y, int w, int h)
    {
        var g = new UIDataGridView
        {
            Location = new Point(x, y), Size = new Size(w, h),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White
        };
        return g;
    }

    public static void Info(string msg) => MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static void Error(string msg) => MessageBox.Show(msg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
    public static bool Ask(string msg) =>
        MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static string? Input(string title, string label, string def = "")
    {
        var f = new UIForm { Text = title, ClientSize = new Size(360, 140), ShowTitle = false, StartPosition = FormStartPosition.CenterParent };
        var l = Lbl(label, 10, 20, 70);
        var t = new UITextBox { Location = new Point(90, 20), Size = new Size(250, 30), Text = def };
        var ok = Btn("确定", 90, 70, 80, 32, (_, _) => { f.Tag = t.Text.Trim(); f.Close(); });
        var cancel = Btn("取消", 190, 70, 80, 32, (_, _) => { f.Tag = null; f.Close(); });
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.ShowDialog();
        return f.Tag as string;
    }
}
