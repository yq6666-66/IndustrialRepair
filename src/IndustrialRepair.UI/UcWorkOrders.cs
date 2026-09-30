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
    private readonly UIDataGridView _grid = Ui.Grid(10, 54, 1160, 620);
    private readonly UserRepository _users = new();

    public UcWorkOrders(UserInfo user)
    {
        _user = user;
        _cmbStatus.Items.AddRange(new object[] { "全部状态", "待派单", "已派单", "维修中", "已完成", "已取消" });
        _cmbStatus.SelectedIndex = 0;

        var btnQuery = Ui.Btn("查询", 550, 10, 80, 32, (_, _) => RefreshData());
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

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("筛选：", 4, 14, 70), _txtKey, _cmbStatus, _chkMine,
            btnQuery, btnNew, btnAssign, btnTake, btnFinish, btnDetail, btnCancel, _grid
        });
    }

    private static readonly Dictionary<string, string> StatusMap = new()
    {
        { "待派单", "Pending" }, { "已派单", "Assigned" }, { "维修中", "Working" },
        { "已完成", "Done" }, { "已取消", "Cancelled" }
    };

    public void RefreshData()
    {
        try
        {
            var status = _cmbStatus.SelectedIndex == 0 ? null : StatusMap[_cmbStatus.Text];
            var t = _svc.Query(status, _txtKey.Text.Trim(), _user.UserId, _chkMine.Checked);
            // 翻译显示列
            if (t.Columns.Contains("Status")) t.Columns["Status"]!.ColumnName = "状态";
            if (t.Columns.Contains("Priority")) t.Columns["Priority"]!.ColumnName = "优先级";
            foreach (System.Data.DataRow r in t.Rows)
            {
                r["状态"] = OrderStatusCode.ToChinese(r["状态"].ToString()!);
                r["优先级"] = PriorityCode.ToChinese(r["优先级"].ToString()!);
            }
            _grid.DataSource = t;
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
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
        using var dlg = new FOrderDetail(order, _svc.Records(id));
        dlg.ShowDialog();
    }
}

/// <summary>派单对话框</summary>
internal class FAssignDialog : UIForm
{
    public UserInfo? Selected { get; private set; }

    public FAssignDialog(List<UserInfo> engineers)
    {
        Text = "派单 - 选择维修工程师";
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
    private string? _photoPath;

    public FReportDialog(UserInfo user)
    {
        _user = user;
        Text = "新建报修工单";
        ClientSize = new Size(730, 330);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var d in _svc.Devices()) _cmbDevice.Items.Add($"{d.DeviceCode} {d.DeviceName}");
        if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;
        _cmbType.Items.AddRange(new object[] { "机械", "电气", "液压", "其他" });
        _cmbType.SelectedIndex = 0;
        _cmbPriority.Items.AddRange(new object[] { "一般", "紧急", "严重", "特修" });
        _cmbPriority.SelectedIndex = 0;

        var btnPhoto = Ui.Btn("拍照 / 选择照片", 410, 232, 140, 34, OnPickPhoto);
        var btnClear = Ui.Btn("清除", 560, 232, 70, 34, (_, _) => { _photoPath = null; _photo.Image = null; _lblRecog.Text = "拍照识别：等待图片…"; });
        var btnOk = Ui.Btn("提交报修", 90, 270, 140, 36, OnSubmit);
        var btnBack = Ui.Btn("取消", 250, 270, 140, 36, (_, _) => Close());

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("设备：", 6, 22, 80), _cmbDevice,
            Ui.Lbl("类型：", 6, 60, 80), _cmbType, _cmbPriority,
            Ui.Lbl("描述：", 6, 100, 80), _txtDesc,
            _photo, _lblRecog, btnPhoto, btnClear, btnOk, btnBack
        });
    }

    private void OnPickPhoto(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp", Title = "选择现场照片（拍照后选择）" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        try
        {
            _photoPath = ofd.FileName;
            _photo.Image?.Dispose();
            _photo.Image = Image.FromFile(ofd.FileName);
            // 本地压缩保存后识别（与提交时一致）
            var saved = ImageUtil.CompressSave(ofd.FileName, WorkOrderService.PhotoDir);
            var top = new RecognitionEngine().Recognize(saved);
            _lblRecog.Text = "拍照识别 Top3：\n" + string.Join("\n", top.Select(r => $"{r.PartName} {r.Score}%"));
        }
        catch (Exception ex) { Ui.Error("图片处理失败：" + ex.Message); }
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
        var r = _svc.Report(devices[idx].DeviceId, _user.UserId, _txtDesc.Text, _cmbType.Text, priority, _photoPath);
        if (!r.Success) { Ui.Error(r.Message); return; }
        Ui.Info(r.Message + (_photoPath == null ? "" : "\n照片已本地压缩保存。"));
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>工单详情：信息 + 照片 + 流转记录</summary>
internal class FOrderDetail : UIForm
{
    public FOrderDetail(WorkOrder o, List<WorkOrderRecord> records)
    {
        Text = $"工单详情 {o.OrderNo}";
        ClientSize = new Size(720, 470);
        StartPosition = FormStartPosition.CenterParent;

        var info = new UILabel
        {
            Location = new Point(16, 16), Size = new Size(460, 210), AutoSize = false,
            Text = $"工单号：{o.OrderNo}\n设备：{o.DeviceCode} {o.DeviceName}\n" +
                   $"状态：{OrderStatusCode.ToChinese(o.Status)}   优先级：{PriorityCode.ToChinese(o.Priority)}\n" +
                   $"报修人：{o.ReporterName}   工程师：{o.EngineerName ?? "-"}\n" +
                   $"报修时间：{o.CreatedAt:yyyy-MM-dd HH:mm}\n" +
                   $"接单：{o.StartTime:yyyy-MM-dd HH:mm}   完工：{o.FinishTime:yyyy-MM-dd HH:mm}\n" +
                   $"故障描述：{o.FaultDesc}\n识别结果：{o.Recognized ?? "-"}\n维修总结：{o.RepairSummary ?? "-"}",
            Font = new Font("微软雅黑", 9F)
        };
        Control photoHost = o.PhotoPath != null && File.Exists(o.PhotoPath)
            ? new PictureBox { Image = Image.FromFile(o.PhotoPath), Location = new Point(500, 16), Size = new Size(190, 150), SizeMode = PictureBoxSizeMode.Zoom }
            : new UILabel { Text = "无照片", Location = new Point(500, 60), Size = new Size(190, 30), TextAlign = ContentAlignment.MiddleCenter };

        var grid = Ui.Grid(16, 240, 680, 200);
        var t = new System.Data.DataTable();
        t.Columns.Add("时间", typeof(string));
        t.Columns.Add("动作", typeof(string));
        t.Columns.Add("操作人", typeof(string));
        t.Columns.Add("备注", typeof(string));
        foreach (var r in records)
            t.Rows.Add(r.CreatedAt.ToString("MM-dd HH:mm:ss"), WorkOrderRecord.ToChinese(r.Action), r.Operator, r.Note);
        grid.DataSource = t;

        var close = Ui.Btn("关闭", 300, 446 % 470 - 28, 100, 30, (_, _) => Close());
        close.Location = new Point(300, 440);

        Controls.AddRange(new Control[] { info, photoHost, grid, close });
    }
}
