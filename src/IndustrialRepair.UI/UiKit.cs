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
        var b = new UIButton
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            Font = new Font("微软雅黑", 9.5F), Cursor = Cursors.Hand
        };
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
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(226, 232, 240),
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 40,
            RowTemplate = { Height = 34 }
        };
        // 深色表头 + 斑马纹 + 品牌蓝选中高亮（全系统表格统一样式）
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 41, 56);
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(32, 41, 56);
        g.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        g.DefaultCellStyle.Font = new Font("微软雅黑", 9.5F);
        g.DefaultCellStyle.ForeColor = Color.FromArgb(48, 56, 68);
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(41, 108, 202);
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 252);
        g.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(41, 108, 202);
        g.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
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
