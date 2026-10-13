using System;
using System.ComponentModel;   // R27：ListSortDirection
using System.Drawing;
using System.IO;
using System.Reflection;       // DG1：双缓冲反射
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>DG1：显式列绑定规格（列名=DataPropertyName，可设表头/格式/权重/可见/可编辑/复选）。</summary>
internal readonly record struct GridCol(
    string Name, string Header, string? Format = null,
    float FillWeight = 100, bool Visible = true, bool Editable = false, bool IsCheck = false);

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
        // R26：自动挂 ToolTip（按钮文字即提示）
        _tip.SetToolTip(b, text);
        return b;
    }

    private static readonly ToolTip _tip = new() { AutoPopDelay = 3000, InitialDelay = 400 };

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

    /// <summary>DG1：每个网格的绑定源缓存（BindExplicit 走 BindingSource 中介层）。</summary>
    private static readonly ConditionalWeakTable<DataGridView, BindingSource> _sources = new();

    /// <summary>DG1：每个网格的布局签名（显式列列集签名，用于列宽存档防错位）。</summary>
    private static readonly ConditionalWeakTable<DataGridView, string> _layoutSig = new();

    private sealed class SortState { public string? Col; public bool Asc = true; }
    private static readonly ConditionalWeakTable<DataGridView, SortState> _sortState = new();

    /// <summary>
    /// DG1：取网格当前绑定的 DataTable（兼容直绑与 BindingSource 包装两种方式），
    /// 供导出（Excel/PDF）统一读取数据源。
    /// </summary>
    public static System.Data.DataTable? TableOf(DataGridView g) => g.DataSource switch
    {
        System.Data.DataTable t => t,
        BindingSource bs => bs.DataSource as System.Data.DataTable,
        _ => null
    };

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
            BackgroundColor = Theme.Current.GridRow, BorderStyle = BorderStyle.None,
            GridColor = Theme.Current.Border,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 40,
            RowTemplate = { Height = 34 }
        };
        // DG1：双缓冲防滚动闪烁（DataGridView 的该属性为 protected，反射开启）
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(g, true);
        // DG1：DataError 全局兜底——类型不匹配/格式化失败不再冒泡崩溃
        g.DataError += (_, e) =>
        {
            try
            {
                BLL.FileLog.Write(
                    new Exception($"网格数据错误：列 {e.ColumnIndex} 行 {e.RowIndex} —— {e.Exception?.Message}"),
                    "DataGridView.DataError");
            }
            catch { /* 日志失败不阻塞界面 */ }
            e.ThrowException = false;
            e.Cancel = false;
        };
        AttachGridContextMenu(g);
        // R27：列头点击排序；DG1：BindingSource 网格走其 Sort（DataView 排序），其余保持 DataGridView.Sort
        g.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex < 0) return;
            var col = g.Columns[e.ColumnIndex];
            if (_sources.TryGetValue(g, out var bs))
            {
                if (!_sortState.TryGetValue(g, out var st)) { st = new SortState(); _sortState.Add(g, st); }
                st.Asc = st.Col == col.DataPropertyName ? !st.Asc : true;
                st.Col = col.DataPropertyName;
                try { bs.Sort = $"[{col.DataPropertyName}] {(st.Asc ? "ASC" : "DESC")}"; } catch { }
            }
            else
            {
                g.Sort(col, g.SortOrder == SortOrder.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending);
            }
        };
        ApplyGridTheme(g);
        return g;
    }

    /// <summary>
    /// DG1：显式列绑定（老师教程：AutoGenerateColumns=false + 显式定义列 + BindingSource 中介层）。
    /// 幂等：列集与 specs 一致时仅替换数据源；不一致时重建列。layoutSignature 供列宽存档防错位。
    /// </summary>
    public static void BindExplicit(UIDataGridView g, System.Data.DataTable table,
        string layoutSignature, params GridCol[] specs)
    {
        g.AutoGenerateColumns = false;
        bool rebuild = g.Columns.Count != specs.Length;
        for (int i = 0; !rebuild && i < specs.Length; i++)
            rebuild = g.Columns[i].DataPropertyName != specs[i].Name;
        if (rebuild)
        {
            g.DataSource = null;
            g.Columns.Clear();
            foreach (var s in specs)
            {
                DataGridViewColumn col = s.IsCheck
                    ? new DataGridViewCheckBoxColumn { ThreeState = false }
                    : new DataGridViewTextBoxColumn();
                col.Name = s.Name;
                col.DataPropertyName = s.Name;
                col.HeaderText = s.Header;
                col.FillWeight = s.FillWeight;
                col.Visible = s.Visible;
                col.ReadOnly = !s.Editable;
                if (!string.IsNullOrEmpty(s.Format))
                    col.DefaultCellStyle = new DataGridViewCellStyle { Format = s.Format };
                g.Columns.Add(col);
            }
        }
        _layoutSig.Remove(g);
        _layoutSig.Add(g, layoutSignature);
        if (g.ReadOnly)
            foreach (DataGridViewColumn col in g.Columns) col.ReadOnly = true;
        else if (specs.Any(s => s.Editable) && rebuild)
            g.ReadOnly = false;   // 可编辑网格：网格级只读关闭，非编辑列由列级 ReadOnly 兜住

        if (!_sources.TryGetValue(g, out var bs))
        {
            bs = new BindingSource();
            _sources.Add(g, bs);
        }
        bs.DataSource = table;
        g.DataSource = bs;
    }

    /// <summary>DG1：右键菜单——复制单元格 / 本行 / 全表（含表头，TSV，可直接粘进 Excel）。</summary>
    public static void AttachGridContextMenu(UIDataGridView g)
    {
        var menu = new ContextMenuStrip { Font = new Font("微软雅黑", 9F) };
        var miCell = new ToolStripMenuItem("复制单元格");
        miCell.Click += (_, _) =>
        {
            var v = g.CurrentCell?.FormattedValue?.ToString();
            if (!string.IsNullOrEmpty(v)) TrySetClipboard(v);
        };
        var miRow = new ToolStripMenuItem("复制本行");
        miRow.Click += (_, _) =>
        {
            if (g.CurrentRow == null) return;
            TrySetClipboard(RowText(g.CurrentRow));
        };
        var miAll = new ToolStripMenuItem("复制全表（含表头）");
        miAll.Click += (_, _) =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join("\t", g.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible).Select(c => c.HeaderText)));
            foreach (DataGridViewRow r in g.Rows)
                if (!r.IsNewRow) sb.AppendLine(RowText(r));
            if (sb.Length > 0) TrySetClipboard(sb.ToString());
        };
        menu.Items.AddRange(new ToolStripItem[] { miCell, miRow, miAll });
        g.ContextMenuStrip = menu;
    }

    private static string RowText(DataGridViewRow r) =>
        string.Join("\t", r.Cells.Cast<DataGridViewCell>()
            .Where(c => c.OwningColumn.Visible)
            .Select(c => c.FormattedValue?.ToString() ?? ""));

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* 剪贴板被占用时静默 */ }
    }

    /// <summary>
    /// DG1：可编辑列校验（老师范式：CellValidating 阻止离开 + ErrorText 行内提示 +
    /// EditingControlShowing 数字键过滤）。
    /// </summary>
    public static void AttachEditableValidation(UIDataGridView g, string columnName, int min = 0)
    {
        g.CellValidating += (_, e) =>
        {
            if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
            if (g.Columns[e.ColumnIndex].Name != columnName) return;
            var text = e.FormattedValue?.ToString() ?? "";
            if (text.Length == 0) return;   // 清空视为未盘点，允许
            if (!int.TryParse(text, out var v) || v < min)
            {
                // 行头隐藏时 Row.ErrorText 不可见，单元格级 ErrorText 显示红点图标
                g.Rows[e.RowIndex].ErrorText = $"请输入不小于 {min} 的整数。";
                g.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = $"请输入不小于 {min} 的整数。";
                e.Cancel = true;
            }
        };
        g.CellValidated += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < g.Rows.Count)
            {
                g.Rows[e.RowIndex].ErrorText = "";
                if (e.ColumnIndex >= 0 && e.ColumnIndex < g.Columns.Count)
                    g.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
            }
        };
        g.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox tb)
            {
                tb.KeyPress -= NumericOnlyKeyPress;
                tb.KeyPress += NumericOnlyKeyPress;
            }
        };
    }

    private static void NumericOnlyKeyPress(object? s, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
    }

    /// <summary>按当前主题着色表格（OR1：暗/亮切换后调用刷新所有表格）。</summary>
    public static void ApplyGridTheme(UIDataGridView g)
    {
        var t = Theme.Current;
        g.BackgroundColor = t.GridRow;
        g.GridColor = t.Border;
        g.ColumnHeadersDefaultCellStyle.BackColor = t.GridHeader;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.GridHeader;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
        g.DefaultCellStyle.Font = new Font("微软雅黑", 9.5F);
        g.DefaultCellStyle.ForeColor = t.TextPrimary;
        g.DefaultCellStyle.SelectionBackColor = t.GridSelection;
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.DefaultCellStyle.BackColor = t.GridRow;
        g.AlternatingRowsDefaultCellStyle.BackColor = t.GridAltRow;
        g.AlternatingRowsDefaultCellStyle.SelectionBackColor = t.GridSelection;
        g.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
        g.AlternatingRowsDefaultCellStyle.ForeColor = t.TextPrimary;
    }

    /* ---- R3：表格列宽持久化（DG1：显式列网格的存档键追加布局签名，避免列集变化后错位）---- */

    /// <summary>表格列宽持久化键前缀。</summary>
    private const string ColWidthKey = "gridColWidths";

    private static string ColWidthPrefKey(DataGridView grid, string pageKey) =>
        _layoutSig.TryGetValue(grid, out var sig)
            ? $"{ColWidthKey}:{pageKey}:{sig}"
            : $"{ColWidthKey}:{pageKey}";

    /// <summary>恢复表格列宽（从 ui.json）；在 DataSource 设置后调用。</summary>
    public static void RestoreColumnWidths(DataGridView grid, string pageKey)
    {
        try
        {
            var prefs = LoadUiPrefs();
            if (!prefs.TryGetValue(ColWidthPrefKey(grid, pageKey), out var widths)) return;
            var parts = widths.Split(',');
            for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++)
                if (int.TryParse(parts[i], out var w) && w > 10 && w < 2000)
                    grid.Columns[i].Width = w;
        }
        catch { }
    }

    /// <summary>保存表格列宽到 ui.json。</summary>
    public static void SaveColumnWidths(DataGridView grid, string pageKey)
    {
        try
        {
            var widths = string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Width));
            var prefs = LoadUiPrefs();
            prefs[ColWidthPrefKey(grid, pageKey)] = widths;
            SaveUiPrefs(prefs);
        }
        catch { }
    }

    private static Dictionary<string, string> LoadUiPrefs()
    {
        try
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndustrialRepair", "ui.json");
            if (!File.Exists(file)) return new Dictionary<string, string>();
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }
        catch { return new Dictionary<string, string>(); }
    }

    private static void SaveUiPrefs(Dictionary<string, string> prefs)
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialRepair", "ui.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var json = System.Text.Json.JsonSerializer.Serialize(prefs);
        File.WriteAllText(file, json);
    }

    /// <summary>忙碌光标包装：数据库/导出等操作期间显示等待光标，异常也复位。</summary>
    public static void Busy(Action body)
    {
        Cursor.Current = Cursors.WaitCursor;
        try { body(); }
        finally { Cursor.Current = Cursors.Default; }
    }

    /// <summary>ESC 关闭对话框（无 CancelButton 的轻量对话框统一行为）。</summary>
    public static void CloseOnEsc(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
    }

    /// <summary>
    /// 照片流式加载（R4）：Image.FromFile 会锁文件至 Dispose，改字节流加载，
    /// 底层 MemoryStream 存入 Image.Tag 防止提前回收。
    /// </summary>
    public static Image? LoadImageNoLock(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var ms = new MemoryStream(File.ReadAllBytes(path));
            var img = Image.FromStream(ms);
            img.Tag = ms;
            return img;
        }
        catch { return null; }
    }

    public static void Info(string msg) => MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static void Error(string msg) => MessageBox.Show(msg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
    public static bool Ask(string msg) =>
        MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>页面标题条（浅灰底 + 左侧品牌竖线），Dock 到页面顶部。</summary>
    public static Control PageHeader(string title, string subtitle = "")
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(243, 246, 250), Padding = new Padding(12, 0, 8, 0) };
        bar.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(Color.FromArgb(41, 108, 202));
            e.Graphics.FillRectangle(accent, 0, 8, 4, bar.Height - 16);
        };
        var l1 = new UILabel
        {
            Text = title, AutoSize = true, ForeColor = Color.FromArgb(32, 41, 56),
            Font = new Font("微软雅黑", 11F, FontStyle.Bold), Location = new Point(8, 8)
        };
        bar.Controls.Add(l1);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var l2 = new UILabel
            {
                Text = subtitle, AutoSize = true, ForeColor = Color.FromArgb(120, 132, 148),
                Font = new Font("微软雅黑", 8.5F), Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(16 + (int)(title.Length * 16.5f) + 24, 13)
            };
            bar.Controls.Add(l2);
        }
        return bar;
    }

    /// <summary>回车触发一次查询（仅单行输入控件；抑制提示音）。</summary>
    public static void BindEnter(Control input, Action query)
    {
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            query();
        };
    }

    /// <summary>
    /// 表格行双击触发操作：忽略表头、无行与共享行，回调取当前行；
    /// 权限与取数仍走与按钮相同的入口（回调自行校验）。
    /// </summary>
    public static void BindRowDblClick(DataGridView grid, Action<DataGridViewRow> action)
    {
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            if (row.IsNewRow || row.DataBoundItem == null) return;
            action(row);
        };
    }

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

/// <summary>
/// 分页条（R1 大数据量）：首页/上一页/下一页/末页 + 页码信息 + 每页条数。
/// 挂到页面 Dock.Bottom；数据加载后调用 Show(分页结果) 刷新状态。
/// </summary>
internal sealed class UiPager : Panel
{
    private readonly UIButton _first = Ui.Btn("≪ 首页", 8, 2, 76, 28);
    private readonly UIButton _prev = Ui.Btn("‹ 上一页", 90, 2, 84, 28);
    private readonly UIButton _next = Ui.Btn("下一页 ›", 180, 2, 84, 28);
    private readonly UIButton _last = Ui.Btn("末页 ≫", 270, 2, 76, 28);
    private readonly UILabel _info = new()
    { AutoSize = false, Size = new Size(230, 28), TextAlign = ContentAlignment.MiddleRight, Font = new Font("微软雅黑", 9F) };
    private readonly UILabel _lblSize = new() { Text = "每页", AutoSize = true };
    private readonly UIComboBox _size = new() { Size = new Size(80, 30), DropDownStyle = UIDropDownStyle.DropDownList };
    private readonly Action<int, int> _load;   // (pageIndex, pageSize)
    private int _page = 1, _totalPages = 1;

    public UiPager(Action<int, int> load)
    {
        _load = load;
        Dock = DockStyle.Bottom;
        Height = 34;
        BackColor = Color.FromArgb(243, 246, 250);

        _size.Items.AddRange(new object[] { "50", "100", "200" });
        _size.SelectedIndex = 0;
        _size.SelectedIndexChanged += (_, _) => { if (!_loading) _load(1, SizeValue); };
        Resize += (_, _) => LayoutRight();

        _first.Click += (_, _) => _load(1, SizeValue);
        _prev.Click += (_, _) => _load(Math.Max(1, _page - 1), SizeValue);
        _next.Click += (_, _) => _load(Math.Min(_totalPages, _page + 1), SizeValue);
        _last.Click += (_, _) => _load(_totalPages, SizeValue);

        Controls.AddRange(new Control[] { _first, _prev, _next, _last, _lblSize, _size, _info });
        LayoutRight();
    }

    private bool _loading;

    private void LayoutRight()
    {
        _info.Location = new Point(Math.Max(360, Width - 236), 2);
        _info.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _size.Location = new Point(Math.Max(280, Width - 330), 2);
        _size.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lblSize.Location = new Point(Math.Max(240, Width - 372), 8);
        _lblSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    }

    private int SizeValue => int.Parse(_size.Text);

    /// <summary>按分页结果刷新按钮可用态与页码文本（初始挂载时避免触发联动加载）。</summary>
    public void Show<T>(IndustrialRepair.Models.PagedResult<T> r) =>
        Show(r.PageIndex, r.TotalCount, r.PageSize, r.HasPrev, r.HasNext);

    /// <summary>表格分页结果重载。</summary>
    public void Show(IndustrialRepair.Models.PagedTable r) =>
        Show(r.PageIndex, r.TotalCount, r.PageSize, r.HasPrev, r.HasNext);

    private void Show(int page, int total, int size, bool hasPrev, bool hasNext)
    {
        _loading = true;
        _page = page;
        _totalPages = Math.Max(1, size <= 0 ? 1 : (total + size - 1) / size);
        _info.Text = $"第 {_page}/{_totalPages} 页 · 共 {total} 条";
        _first.Enabled = _prev.Enabled = hasPrev;
        _next.Enabled = _last.Enabled = hasNext;
        _loading = false;
    }
}
