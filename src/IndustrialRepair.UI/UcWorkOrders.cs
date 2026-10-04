using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 工单中心：报修（拍照上传+识别）→ 派单 → 接单 → 完工 → 取消；状态跟踪与流转记录。
/// 按钮按 RBAC 权限显隐。
/// </summary>
internal class UcWorkOrders : UserControl, IRefreshable
{
    private readonly WorkOrderService _svc = new();
    private readonly UserInfo _user;
    private readonly UITextBox _txtKey = Ui.Txt("工单号 / 设备 / 描述 / 报修人", 80, 12, 220);
    private readonly UIComboBox _cmbStatus = Ui.Combo(320, 12, 130);
    private readonly UICheckBox _chkMine = new() { Text = "仅我的", Location = new Point(470, 15), Size = new Size(70, 26) };
    private readonly UIDataGridView _grid = FillGrid();
    private readonly UiPager _pager = null!;   // 构造中初始化（需引用本页加载方法）
    private readonly UILabel _lblEmpty = new()
    {
        Dock = DockStyle.Top, Height = 26, Text = "未查询到符合条件的工单", TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(140, 150, 165), Font = new Font("微软雅黑", 9.5F), BackColor = Color.White, Visible = false
    };

    private static UIDataGridView FillGrid()
    {
        var g = Ui.Grid(0, 0, 100, 100);
        g.Dock = DockStyle.Fill;
        return g;
    }
    private readonly UserRepository _users = new();

    public UcWorkOrders(UserInfo user)
    {
        _user = user;
        _cmbStatus.Items.AddRange(new object[] { "全部状态", "待派单", "已派单", "维修中", "已完成", "已取消" });
        _cmbStatus.SelectedIndex = 0;

        var btnQuery = Ui.Btn("查询", 550, 10, 80, 32, (_, _) => LoadPage(1, _pageSize));
        var btnNew = Ui.Btn("新建报修", 650, 10, 95, 32, (_, _) =>
        {
            using var dlg = new FReportDialog(_user);
            if (dlg.ShowDialog() == DialogResult.OK) RefreshData();
        });
        var btnAssign = Ui.Btn("派单", 760, 10, 75, 32, OnAssign);
        var btnTake = Ui.Btn("接单", 840, 10, 75, 32, OnTake);
        var btnFinish = Ui.Btn("完工", 920, 10, 75, 32, OnFinish);
        var btnDetail = Ui.Btn("详情", 1000, 10, 75, 32, (_, _) => ShowDetail());
        var btnCancel = Ui.Btn("取消工单", 1080, 10, 90, 32, OnCancel);

        // 权限显隐（RBAC）
        btnNew.Visible = _user.HasPerm("order.report");
        btnAssign.Visible = _user.HasPerm("order.assign");
        btnTake.Visible = _user.HasPerm("order.handle");
        btnFinish.Visible = _user.HasPerm("order.handle");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 54 };
        toolbar.Controls.AddRange(new Control[]
        {
            Ui.Lbl("筛选：", 4, 14, 70), _txtKey, _cmbStatus, _chkMine,
            btnQuery, btnNew, btnAssign, btnTake, btnFinish, btnDetail, btnCancel
        });

        // Dock 布局（同 FMain 模式）：填充控件先加，标题条最后加；分页条在工具栏之下
        _pager = new UiPager((page, size) => LoadPage(page, size));
        Controls.Add(_grid);
        Controls.Add(_lblEmpty);
        Controls.Add(toolbar);
        Controls.Add(_pager);
        Controls.Add(Ui.PageHeader("工单中心", "报修 · 派单 · 接单 · 完工 · 材料审阅入口"));

        Ui.BindEnter(_txtKey, () => LoadPage(1, 50));   // 回车即查（回第一页）
        Ui.BindRowDblClick(_grid, _ => ShowDetail()); // 行双击=详情（与按钮同一入口）
        _grid.CellFormatting += GridSlaColor;         // OR5：SLA 超时颜色编码
    }

    /// <summary>SLA 颜色编码（OR5）：活跃工单停滞 >24h 黄底 / >48h 红底。</summary>
    private void GridSlaColor(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not System.Data.DataRowView drv) return;
        var status = drv.Row.Table.Columns.Contains("状态") ? drv.Row["状态"].ToString() : "";
        if (status is not ("待派单" or "已派单" or "维修中")) return;
        if (!drv.Row.Table.Columns.Contains("CreatedAt") || drv.Row["CreatedAt"] == DBNull.Value) return;
        var hours = (DateTime.Now - (DateTime)drv.Row["CreatedAt"]).TotalHours;
        if (hours > 48) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
        else if (hours > 24) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 224);
    }

    private static readonly Dictionary<string, string> StatusMap = new()
    {
        { "待派单", "Pending" }, { "已派单", "Assigned" }, { "维修中", "Working" },
        { "已完成", "Done" }, { "已取消", "Cancelled" }
    };

    /// <summary>刷新当前页（IRefreshable / 查询按钮入口）。</summary>
    public void RefreshData() => LoadPage(_currentPage, _pageSize);

    private int _currentPage = 1;
    private int _pageSize = 50;

    /// <summary>分页加载工单列表（R1：OFFSET-FETCH，避免全量拉取；R4：忙碌光标+空态）。</summary>
    private void LoadPage(int page, int size)
    {
        Ui.Busy(() =>
        {
            try
            {
                _currentPage = page; _pageSize = size;
                var status = _cmbStatus.SelectedIndex == 0 ? null : StatusMap[_cmbStatus.Text];
                var r = BLL.PagedQuery.WorkOrders(status, _txtKey.Text.Trim(), _chkMine.Checked, _user.UserId, page, size);
                var t = r.Items;
                if (t.Columns.Contains("Status")) t.Columns["Status"]!.ColumnName = "状态";
                if (t.Columns.Contains("Priority")) t.Columns["Priority"]!.ColumnName = "优先级";
                if (t.Columns.Contains("FaultDesc")) t.Columns["FaultDesc"]!.ColumnName = "故障描述";
                if (t.Columns.Contains("MaterialReviewMode"))
                {
                    t.Columns["MaterialReviewMode"]!.ColumnName = "材料审阅";
                    foreach (System.Data.DataRow row in t.Rows)
                        row["材料审阅"] = MaterialReviewMode.ToChinese(row["材料审阅"] == DBNull.Value ? null : row["材料审阅"].ToString());
                }
                foreach (System.Data.DataRow row in t.Rows)
                {
                    row["状态"] = OrderStatusCode.ToChinese(row["状态"].ToString()!);
                    row["优先级"] = PriorityCode.ToChinese(row["优先级"].ToString()!);
                }
                _grid.DataSource = t;
                _lblEmpty.Visible = t.Rows.Count == 0;
                _pager.Show(r);
            }
            catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
        });
    }

    private int SelectedOrderId() =>
        _grid.CurrentRow?.Cells["OrderNo"].Value != null && int.TryParse(_grid.CurrentRow?.Cells["OrderId"]?.Value?.ToString(), out var id) ? id : 0;

    private void OnAssign(object? s, EventArgs e)
    {
        var id = SelectedOrderId();
        if (id == 0) { Ui.Error("请先选择工单。"); return; }
        var engineers = _users.ListByRole("Engineer");
        if (engineers.Count == 0) { Ui.Error("系统暂无维修工程师账号。"); return; }

        using var dlg = new FAssignDialog(engineers);
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Selected == null) return;
        var r = _svc.Assign(id, dlg.Selected.UserId, _user.UserId);
        if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
    }

    private void OnTake(object? s, EventArgs e)
    {
        var id = SelectedOrderId();
        if (id == 0) { Ui.Error("请先选择工单。"); return; }
        var r = _svc.Take(id, _user.UserId);
        if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
    }

    private void OnFinish(object? s, EventArgs e)
    {
        var id = SelectedOrderId();
        if (id == 0) { Ui.Error("请先选择工单。"); return; }
        var summary = Ui.Input("完工登记", "维修总结：");
        if (summary == null) return;
        var r = _svc.Complete(id, _user.UserId, summary);
        if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
    }

    private void OnCancel(object? s, EventArgs e)
    {
        var id = SelectedOrderId();
        if (id == 0) { Ui.Error("请先选择工单。"); return; }
        var reason = Ui.Input("取消工单", "取消原因：");
        if (reason == null) return;
        var r = _svc.Cancel(id, _user.UserId, reason);
        if (r.Success) { Ui.Info(r.Message); RefreshData(); } else Ui.Error(r.Message);
    }

    private void ShowDetail()
    {
        var id = SelectedOrderId();
        if (id == 0) { Ui.Error("请先选择工单。"); return; }
        var order = _svc.Get(id);
        if (order == null) { Ui.Error("工单不存在。"); return; }
        using var dlg = new FOrderDetail(id, _user);
        dlg.ShowDialog();
        RefreshData();
    }
}

/// <summary>派单对话框</summary>
internal class FAssignDialog : UIForm
{
    public UserInfo? Selected { get; private set; }

    public FAssignDialog(List<UserInfo> engineers)
    {
        Text = "派单 - 选择维修工程师";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(360, 120 + engineers.Count * 34);
        StartPosition = FormStartPosition.CenterParent;
        int y = 20;
        foreach (var eng in engineers)
        {
            var one = eng;
            var b = Ui.Btn($"{one.RealName}（{one.UserName}）", 40, y, 280, 30, (_, _) =>
            {
                Selected = one;
                DialogResult = DialogResult.OK;
                Close();
            });
            Controls.Add(b);
            y += 34;
        }
    }
}

/// <summary>新建报修：设备 + 故障 + 优先级 + 拍照/选择照片（压缩保存）+ 实时识别</summary>
internal class FReportDialog : UIForm
{
    private readonly UserInfo _user;
    private readonly WorkOrderService _svc = new();
    private readonly UIComboBox _cmbDevice = Ui.Combo(90, 20, 300);
    private readonly UIComboBox _cmbType = Ui.Combo(90, 58, 130);
    private readonly UIComboBox _cmbPriority = Ui.Combo(260, 58, 130);
    private readonly UITextBox _txtDesc = new()
    {
        Location = new Point(90, 96), Size = new Size(300, 60), Multiline = true,
        Watermark = "故障现象描述"
    };
    private readonly PictureBox _photo = new()
    {
        Location = new Point(410, 20), Size = new Size(180, 140), SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(240, 240, 240), BorderStyle = BorderStyle.FixedSingle
    };
    private readonly UILabel _lblRecog = new()
    {
        Location = new Point(410, 166), Size = new Size(300, 60), AutoSize = false,
        ForeColor = Color.FromArgb(0, 122, 204), Font = new Font("微软雅黑", 8.5F), Text = "拍照识别：等待图片…"
    };
    private readonly UIButton btnPhoto;
    private readonly UIButton btnClear;
    private readonly UIButton btnOk;
    private WorkOrderService.PreparedPhoto? _prepared;   // 一次准备（压缩+识别），提交直接复用

    public FReportDialog(UserInfo user)
    {
        _user = user;
        Text = "新建报修工单";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(730, 360);
        StartPosition = FormStartPosition.CenterParent;

        // R8：快速报修模板（选中预填类型/优先级/描述）
        var cmbTemplate = new UIComboBox
        { Location = new Point(90, 96), Size = new Size(130, 30), DropDownStyle = UIDropDownStyle.DropDownList };
        cmbTemplate.Items.AddRange(new object[] { "—— 模板 ——", "轴承异响", "液压渗油", "电气跳闸", "皮带跑偏", "润滑不足" });
        cmbTemplate.SelectedIndex = 0;
        cmbTemplate.SelectedIndexChanged += (_, _) =>
        {
            var (type, prio, desc) = cmbTemplate.SelectedIndex switch
            {
                1 => ("机械", "紧急", "主轴运转异响，温度偏高，需检查轴承"),
                2 => ("液压", "严重", "液压站压力波动，油缸渗油，需更换密封件"),
                3 => ("电气", "紧急", "电柜接触器频繁跳闸，需检查电路"),
                4 => ("机械", "一般", "输送带跑偏，托辊异响，需调整张紧装置"),
                5 => ("机械", "一般", "导轨润滑不足，运行阻力增大，需补充润滑脂"),
                _ => ("", "", "")
            };
            if (cmbTemplate.SelectedIndex == 0) return;
            if (!string.IsNullOrEmpty(type)) _cmbType.Text = type;
            if (!string.IsNullOrEmpty(prio)) _cmbPriority.Text = prio;
            if (!string.IsNullOrEmpty(desc)) _txtDesc.Text = desc;
        };

        foreach (var d in _svc.Devices()) _cmbDevice.Items.Add($"{d.DeviceCode} {d.DeviceName}");
        if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;
        _cmbType.Items.AddRange(new object[] { "机械", "电气", "液压", "其他" });
        _cmbType.SelectedIndex = 0;
        _cmbPriority.Items.AddRange(new object[] { "一般", "紧急", "严重", "特修" });
        _cmbPriority.SelectedIndex = 0;

        btnPhoto = Ui.Btn("拍照 / 选择照片", 410, 232, 140, 34, OnPickPhoto);
        btnClear = Ui.Btn("清除", 560, 232, 70, 34, (_, _) =>
        {
            _prepared = null;
            _photo.Image?.Dispose();
            _photo.Image = null;
            _lblRecog.Text = "拍照识别：等待图片…";
        });
        btnOk = Ui.Btn("提交报修", 90, 300, 140, 36, OnSubmit);
        var btnBack = Ui.Btn("取消", 250, 300, 140, 36, (_, _) => Close());
        _txtDesc.Location = new Point(90, 130);   // R8：给模板行让位
        btnPhoto.Location = new Point(410, 262);
        btnClear.Location = new Point(560, 262);

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("设备：", 6, 22, 80), _cmbDevice,
            Ui.Lbl("类型：", 6, 60, 80), _cmbType, _cmbPriority,
            Ui.Lbl("模板：", 6, 96, 80), cmbTemplate,
            Ui.Lbl("描述：", 6, 134, 80), _txtDesc,
            _photo, _lblRecog, btnPhoto, btnClear, btnOk, btnBack
        });
    }

    /// <summary>
    /// 选照片：一次准备（压缩落盘+识别）在后台线程完成，期间禁用重选/清除/提交，
    /// 失败清空候选；窗体已关闭则不回写控件。提交直接复用准备结果（同一文件与候选）。
    /// </summary>
    private async void OnPickPhoto(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp", Title = "选择现场照片（拍照后选择）" };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        // 开始新处理：清空上一张的照片与候选（失败不得携带旧候选）
        _prepared = null;
        _photo.Image?.Dispose();
        _photo.Image = null;
        _lblRecog.Text = "照片准备中…（压缩与识别）";

        var source = ofd.FileName;
        var busy = new[] { btnPhoto, btnClear, btnOk };
        foreach (var b in busy) b.Enabled = false;
        try
        {
            var prepared = await Task.Run(() => _svc.PreparePhoto(source));
            if (IsDisposed) return;
            _prepared = prepared;
        _photo.Image?.Dispose();
        _photo.Image = Ui.LoadImageNoLock(prepared.SavedPath);
        _lblRecog.Text = prepared.Candidates.Count == 0
                ? "识别未匹配：需人工确认（可在工单详情中手动选择零件）"
                : "拍照识别 Top3（匹配分数）：\n" + string.Join("\n",
                      prepared.Candidates.Select(r => $"{r.PartName} {r.Score}%{(r.Score < 60 ? "（需人工核对）" : "")}"));
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _prepared = null;
            _lblRecog.Text = "照片处理失败，请重选或清除。";
            Ui.Error("图片处理失败：" + ex.Message);
        }
        finally
        {
            if (!IsDisposed)
                foreach (var b in busy) b.Enabled = true;
        }
    }

    private void OnSubmit(object? s, EventArgs e)
    {
        var devices = _svc.Devices();
        var idx = _cmbDevice.SelectedIndex;
        if (idx < 0 || idx >= devices.Count) { Ui.Error("请选择设备。"); return; }
        var priority = _cmbPriority.Text switch
        {
            "紧急" => PriorityCode.Urgent, "严重" => PriorityCode.Critical,
            "特修" => PriorityCode.Special, _ => PriorityCode.Normal
        };
        var r = _svc.SubmitReport(devices[idx].DeviceId, _user.UserId, _txtDesc.Text, _cmbType.Text, priority, _prepared);
        if (!r.Success) { Ui.Error(r.Message); return; }
        Ui.Info(r.Message + (_prepared == null ? "" : "\n照片已本地压缩保存。"));
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>工单详情：信息 + 照片 + 流转记录</summary>
internal class FOrderDetail : UIForm
{
    private readonly WorkOrderService _svc = new();
    private readonly PartService _parts = new();
    private readonly UserInfo _user;
    private int _orderId;
    private readonly UILabel _info = new()
    {
        Location = new Point(16, 14), Size = new Size(460, 196), AutoSize = false,
        Font = new Font("微软雅黑", 9F)
    };
    private readonly UILabel _lblReview = new()
    {
        Location = new Point(16, 214), Size = new Size(680, 22), AutoSize = false,
        Font = new Font("微软雅黑", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 122, 204)
    };
    private readonly Panel _reviewActions = new() { Location = new Point(14, 240), Size = new Size(690, 92) };
    private readonly PictureBox _photo = new()
    {
        Location = new Point(496, 14), Size = new Size(196, 140), SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(240, 240, 240), BorderStyle = BorderStyle.FixedSingle
    };
    private readonly UIDataGridView _grid = Ui.Grid(16, 400, 680, 170);   // OR7：下移给时间线留位

    /// <summary>工单详情：信息/照片/识别候选/材料审阅/流转时间线/流转记录（工程师可在此人工确认零件）</summary>
    public FOrderDetail(int orderId, UserInfo user)
    {
        _user = user;
        _orderId = orderId;
        Text = $"工单详情 #{orderId}";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(716, 620);
        StartPosition = FormStartPosition.CenterParent;

        var close = Ui.Btn("关闭", 300, 588, 100, 30, (_, _) => Close());
        Controls.AddRange(new Control[] { _info, _photo, _lblReview, _reviewActions, _timeline, _grid, close });
        Reload(orderId);
    }

    /// <summary>流转时间线面板（OR7：横向节点图，按时间排列）。</summary>
    private readonly Panel _timeline = new()
    { Location = new Point(16, 340), Size = new Size(680, 50), AutoScroll = false };

    /// <summary>构建流转时间线：节点圆 + 阶段名 + 操作人 + 距上一步时长。</summary>
    private void BuildTimeline(List<WorkOrderRecord> records)
    {
        _timeline.Controls.Clear();
        _timeline.BackColor = Theme.Current.PanelBg;

        if (records.Count == 0) return;
        var stages = records.OrderBy(r => r.CreatedAt).ToList();

        // 计算节点位置（均分宽度）
        var nodeW = Math.Max(90, _timeline.Width / Math.Max(stages.Count, 1));
        for (int i = 0; i < stages.Count; i++)
        {
            var rec = stages[i];
            var x = 8 + i * nodeW;

            // 连接线（最后一个节点不画）
            if (i < stages.Count - 1)
            {
                var line = new Panel
                {
                    Location = new Point(x + 14, 20), Size = new Size(nodeW - 28, 2),
                    BackColor = i < CurrentStageIndex(stages) ? Theme.Current.Accent : Theme.Current.Border,
                    Anchor = AnchorStyles.None
                };
                _timeline.Controls.Add(line);
            }

            // 节点圆（已完成=品牌蓝，当前=绿色，未到=灰色）
            var isDone = i <= CurrentStageIndex(stages);
            var isCurrent = i == CurrentStageIndex(stages);
            var dot = new Panel
            {
                Location = new Point(x + 6, 14), Size = new Size(14, 14),
                BackColor = isCurrent ? Theme.Current.Success : isDone ? Theme.Current.Accent : Theme.Current.Border,
                Anchor = AnchorStyles.None
            };
            // 圆角
            dot.Region = new Region(new Rectangle(0, 0, 14, 14));
            _timeline.Controls.Add(dot);

            // 阶段名
            var lbl = new UILabel
            {
                Text = WorkOrderRecord.ToChinese(rec.Action), AutoSize = true,
                Location = new Point(x, 0), Font = new Font("微软雅黑", 8.5F, isCurrent ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = isDone ? Theme.Current.TextPrimary : Theme.Current.TextSecondary
            };
            _timeline.Controls.Add(lbl);

            // 时间
            var lblTime = new UILabel
            {
                Text = rec.CreatedAt.ToString("MM-dd HH:mm"), AutoSize = true,
                Location = new Point(x, 30), Font = new Font("微软雅黑", 7.5F),
                ForeColor = Theme.Current.TextSecondary
            };
            _timeline.Controls.Add(lblTime);

            // 距上一步时长
            if (i > 0)
            {
                var gap = rec.CreatedAt - stages[i - 1].CreatedAt;
                var lblGap = new UILabel
                {
                    Text = gap.TotalHours >= 1 ? $"+{gap.TotalHours:F0}h" : $"+{gap.TotalMinutes:F0}m",
                    AutoSize = true, Location = new Point(x - 28, 20),
                    Font = new Font("微软雅黑", 7F),
                    ForeColor = gap.TotalHours > 24 ? Theme.Current.Danger : Theme.Current.TextSecondary
                };
                _timeline.Controls.Add(lblGap);
            }
        }
    }

    /// <summary>当前所处阶段索引（最后一个记录即当前；已取消取到取消）。</summary>
    private static int CurrentStageIndex(List<WorkOrderRecord> stages) => stages.Count - 1;

    /// <summary>释放照片（连同底层 MemoryStream，R4 流式加载配套）。</summary>
    private void DisposePhoto()
    {
        if (_photo.Image == null) return;
        (_photo.Image.Tag as IDisposable)?.Dispose();
        _photo.Image.Dispose();
        _photo.Image = null;
    }

    private void Reload(int orderId)
    {
        var o = _svc.Get(orderId);
        if (o == null) { Ui.Error("工单不存在。"); Close(); return; }

        _info.Text =
            $"工单号：{o.OrderNo}    设备：{o.DeviceCode} {o.DeviceName}\n" +
            $"状态：{OrderStatusCode.ToChinese(o.Status)}    优先级：{PriorityCode.ToChinese(o.Priority)}    " +
            $"报修人：{o.ReporterName}    工程师：{o.EngineerName ?? "-"}\n" +
            $"报修：{o.CreatedAt:MM-dd HH:mm}    接单：{(o.StartTime == null ? "-" : o.StartTime.Value.ToString("MM-dd HH:mm"))}    " +
            $"完工：{(o.FinishTime == null ? "-" : o.FinishTime.Value.ToString("MM-dd HH:mm"))}\n" +
            $"故障描述：{o.FaultDesc}\n识别结果：{o.Recognized ?? "-"}\n维修总结：{o.RepairSummary ?? "-"}";

        DisposePhoto();
        _photo.Image = Ui.LoadImageNoLock(o.PhotoPath);   // R4：字节流加载不锁文件

        _lblReview.Text = o.MaterialReviewMode == null
            ? "材料审阅：未审阅（生成领料单前需确认重点零件或选择仅整机BOM）"
            : o.MaterialReviewMode == MaterialReviewMode.BomOnly
                ? $"材料审阅：仅整机BOM —— {o.ReviewNote}（{o.ReviewedByName} {o.ReviewedAt:MM-dd HH:mm}）"
                : $"材料审阅：确认重点 {o.ConfirmedPartName}" +
                  (o.ConfirmedBaseQty == null ? "" : $"（BOM外人工用量 {o.ConfirmedBaseQty}）") +
                  $" —— {o.ReviewNote}（{o.ReviewedByName} {o.ReviewedAt:MM-dd HH:mm}）";

        BuildReviewActions(o);

        Text = $"工单详情 {o.OrderNo}";
        var records = _svc.Records(orderId);
        BuildTimeline(records);   // OR7：流转时间线

        var t = new System.Data.DataTable();
        t.Columns.Add("时间", typeof(string));
        t.Columns.Add("动作", typeof(string));
        t.Columns.Add("操作人", typeof(string));
        t.Columns.Add("备注", typeof(string));
        foreach (var r in records)
            t.Rows.Add(r.CreatedAt.ToString("MM-dd HH:mm:ss"), WorkOrderRecord.ToChinese(r.Action), r.Operator, r.Note);
        _grid.DataSource = t;
    }

    /// <summary>构建审阅操作区：候选确认按钮 + 手动选件 + 仅整机BOM（需 order.handle 且工单可审阅）</summary>
    private void BuildReviewActions(WorkOrder o)
    {
        _reviewActions.Controls.Clear();
        bool canReview = _user.HasPerm("order.handle") && o.Status is "Assigned" or "Working";
        if (!canReview)
        {
            _reviewActions.Controls.Add(new UILabel
            {
                Text = o.Status is "Done" or "Cancelled" ? "工单已结束，仅可查看审阅结果。" : "当前角色仅可查看（材料审阅需维修处理权限）。",
                Location = new Point(2, 6), AutoSize = true, Font = new Font("微软雅黑", 9F),
                ForeColor = Color.Gray
            });
            return;
        }

        int x = 2;
        var head = new UILabel
        {
            Text = "材料审阅操作：", AutoSize = true, Location = new Point(2, 8),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        _reviewActions.Controls.Add(head);
        x += 100;

        foreach (var cand in _svc.GetCandidates(o.OrderId))
        {
            if (cand.PartId <= 0) continue;   // 无效占位候选不生成按钮
            var b = Ui.Btn($"确认：{cand.PartName} {cand.Score}%", x, 2, 168, 30, (_, _) => ConfirmPart(cand.PartId, cand.PartName));
            if (cand.Score < 60) b.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            _reviewActions.Controls.Add(b);
            x += 176;
        }

        var manual = Ui.Btn("手动选择零件…", 2, 38, 130, 30, (_, _) => ManualPick());
        var bomOnly = Ui.Btn("仅按整机BOM配料…", 140, 38, 150, 30, (_, _) => PickBomOnly());
        var tip = new UILabel
        {
            Text = "候选来自拍照识别；低分候选需人工核对。BOM 内确认仅标记重点，BOM 外需填人工用量。",
            AutoSize = true, Location = new Point(300, 44), Font = new Font("微软雅黑", 8F), ForeColor = Color.Gray
        };
        _reviewActions.Controls.AddRange(new Control[] { manual, bomOnly, tip });
    }

    /// <summary>确认重点零件：BOM 内仅标记；BOM 外先提示必须人工用量+理由（两步引导）</summary>
    private void ConfirmPart(int partId, string partName)
    {
        var note = Ui.Input($"确认“{partName}”为维修重点。\n审阅理由（BOM 外补充件必填，BOM 内可留空）：", "理由：");
        if (note == null) return;
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(_orderId, MaterialReviewMode.PartConfirmed, partId, null, note));
        if (!r.Success && r.Message.Contains("人工基础用量"))
        {
            var qtyText = Ui.Input($"“{partName}”不在该设备 BOM 中，属 BOM 外补充件。\n请填写人工基础用量（正数，如 1.5）：", "用量：");
            if (qtyText == null || !decimal.TryParse(qtyText.Trim(), out var qty) || qty <= 0)
            { Ui.Error("用量必须为正数（如 1.5）。"); return; }
            r = _svc.ReviewMaterials(new MaterialReviewRequest(_orderId, MaterialReviewMode.PartConfirmed, partId, qty, note));
        }
        if (r.Success) { Ui.Info(r.Message); Reload(_orderId); }
        else Ui.Error(r.Message);
    }

    private void ManualPick()
    {
        var key = Ui.Input("输入配件编码或名称关键字：", "关键字：");
        if (string.IsNullOrWhiteSpace(key)) return;
        var hits = _parts.Search(key.Trim());
        if (hits.Count == 0) { Ui.Error("未找到匹配配件。"); return; }
        if (hits.Count > 1)
        {
            var list = string.Join("\n", hits.Take(6).Select((p, i) => $"{i + 1}. {p.PartCode} {p.PartName}"));
            var pick = Ui.Input($"匹配 {hits.Count} 项，请输入编号（1-{Math.Min(6, hits.Count)}）：\n{list}", "编号：");
            if (!int.TryParse(pick, out var idx) || idx < 1 || idx > Math.Min(6, hits.Count)) { Ui.Error("编号无效。"); return; }
            ConfirmPart(hits[idx - 1].PartId, hits[idx - 1].PartName);
            return;
        }
        ConfirmPart(hits[0].PartId, hits[0].PartName);
    }

    private void PickBomOnly()
    {
        var note = Ui.Input("选择“仅按整机 BOM 配料”。\n请填写理由（无照片/IoT/历史工单适用）：", "理由：");
        if (note == null) return;
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(_orderId, MaterialReviewMode.BomOnly, null, null, note));
        if (r.Success) { Ui.Info(r.Message); Reload(_orderId); }
        else Ui.Error(r.Message);
    }
}
