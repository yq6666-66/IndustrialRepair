using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>PM 调度编辑对话框（FR1：设备下拉 + 间隔预设 + Time/Meter + 编辑回填）。</summary>
internal class FPMEdit : UIForm
{
    private readonly MaintenanceSchedule? _existing;
    private readonly List<Device> _devices;
    private readonly UIComboBox _cmbDevice;
    private readonly UITextBox _txtTitle = Ui.Txt("如：季度轴承润滑", 100, 58, 260);
    private readonly UIComboBox _cmbType = Ui.Combo(100, 96, 100);
    private readonly UIComboBox _cmbPreset = Ui.Combo(240, 96, 120);
    private readonly UITextBox _txtInterval = Ui.Txt("90", 100, 134, 80);
    private readonly UILabel _lblUnit = new() { AutoSize = true, Location = new Point(188, 138), Text = "天" };
    private readonly UIComboBox _cmbPriority = Ui.Combo(100, 172, 120);
    private readonly UITextBox _txtDesc = new()
    { Location = new Point(100, 210), Size = new Size(260, 60), Multiline = true, Watermark = "描述（选填）" };
    private readonly UICheckBox _chkActive = new() { Text = "启用", Location = new Point(98, 282), Size = new Size(60, 26), Checked = true };

    public MaintenanceSchedule? Result { get; private set; }

    public FPMEdit(MaintenanceSchedule? existing)
    {
        _existing = existing;
        _devices = new BasicRepository().ListDevices().ToList();

        Text = existing == null ? "新增 PM 调度" : "编辑 PM 调度";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(390, 360);
        StartPosition = FormStartPosition.CenterParent;

        _cmbDevice = Ui.Combo(100, 20, 260);
        foreach (var d in _devices) _cmbDevice.Items.Add($"{d.DeviceCode} {d.DeviceName}");
        if (_cmbDevice.Items.Count > 0) _cmbDevice.SelectedIndex = 0;

        // FR5：间隔预设
        _cmbPreset.Items.AddRange(new object[] { "—— 预设 ——", "每周(7天)", "每月(30天)", "季度(90天)", "半年(180天)", "年度(365天)" });
        _cmbPreset.SelectedIndex = 0;
        _cmbPreset.SelectedIndexChanged += (_, _) =>
        {
            if (int.TryParse(_cmbPreset.Text?.Split('(')[1]?.TrimEnd(')'), out var days)) _txtInterval.Text = days.ToString();
        };

        _cmbType.Items.AddRange(new object[] { "Time", "Meter" });
        _cmbType.SelectedIndex = 0;
        _cmbType.SelectedIndexChanged += (_, _) => _lblUnit.Text = _cmbType.Text == "Meter" ? "小时" : "天";

        _cmbPriority.Items.AddRange(new object[] { "一般", "紧急", "严重" });
        _cmbPriority.SelectedIndex = 0;

        // 编辑回填
        if (existing != null)
        {
            var idx = _devices.FindIndex(d => d.DeviceId == existing.DeviceId);
            if (idx >= 0) _cmbDevice.SelectedIndex = idx;
            _txtTitle.Text = existing.Title;
            _cmbType.Text = existing.IntervalType;
            _txtInterval.Text = (existing.IntervalDays ?? (int)(existing.IntervalHours ?? 30)).ToString();
            _cmbPriority.Text = existing.TemplatePriority switch { "Urgent" => "紧急", "Critical" => "严重", _ => "一般" };
            _txtDesc.Text = existing.TemplateDesc ?? "";
            _chkActive.Checked = existing.IsActive;
        }

        var ok = Ui.Btn("保存", 100, 318, 110, 32, OnSave);
        var back = Ui.Btn("取消", 220, 318, 110, 32, (_, _) => Close());
        Controls.AddRange(new Control[]
        {
            Ui.Lbl("设备：", 10, 20, 85), _cmbDevice,
            Ui.Lbl("任务名：", 10, 58, 85), _txtTitle,
            Ui.Lbl("触发：", 10, 96, 85), _cmbType, _cmbPreset,
            Ui.Lbl("间隔：", 10, 134, 85), _txtInterval, _lblUnit,
            Ui.Lbl("优先级：", 10, 172, 85), _cmbPriority,
            Ui.Lbl("描述：", 10, 214, 85), _txtDesc,
            Ui.Lbl("状态：", 10, 284, 85), _chkActive, ok, back
        });
    }

    private void OnSave(object? s, EventArgs e)
    {
        if (_cmbDevice.SelectedIndex < 0 || _cmbDevice.SelectedIndex >= _devices.Count) { Ui.Error("请选择设备。"); return; }
        if (string.IsNullOrWhiteSpace(_txtTitle.Text)) { Ui.Error("请填写任务名。"); return; }
        if (!int.TryParse(_txtInterval.Text, out var interval) || interval < 1) { Ui.Error("间隔须为正整数。"); return; }

        var dev = _devices[_cmbDevice.SelectedIndex];
        var priority = _cmbPriority.Text switch { "紧急" => PriorityCode.Urgent, "严重" => PriorityCode.Critical, _ => PriorityCode.Normal };
        Result = new MaintenanceSchedule(
            _existing?.ScheduleId ?? 0, dev.DeviceId, dev.DeviceName,
            _txtTitle.Text.Trim(), _cmbType.Text,
            _cmbType.Text == "Time" ? interval : (int?)null,
            _cmbType.Text == "Meter" ? interval : (decimal?)null,
            _existing?.LastExecutedAt, null, _chkActive.Checked,
            string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim(), priority);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>供应商编辑对话框（FR16）。</summary>
internal class FVendorEdit : UIForm
{
    private readonly Vendor? _existing;
    private readonly UITextBox _txtName = Ui.Txt("供应商名称", 100, 20, 220);
    private readonly UITextBox _txtContact = Ui.Txt("联系人（选填）", 100, 58, 220);
    private readonly UITextBox _txtPhone = Ui.Txt("电话（选填）", 100, 96, 220);
    private readonly UITextBox _txtEmail = Ui.Txt("邮箱（选填）", 100, 134, 220);
    private readonly UITextBox _txtAddr = Ui.Txt("地址（选填）", 100, 172, 220);
    private readonly UICheckBox _chkActive = new() { Text = "启用", Location = new Point(98, 214), Size = new Size(60, 26), Checked = true };

    public Vendor? Result { get; private set; }

    public FVendorEdit(Vendor? existing)
    {
        _existing = existing;
        Text = existing == null ? "新增供应商" : "编辑供应商";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(350, 290);
        StartPosition = FormStartPosition.CenterParent;

        if (existing != null)
        {
            _txtName.Text = existing.VendorName;
            _txtContact.Text = existing.Contact ?? "";
            _txtPhone.Text = existing.Phone ?? "";
            _txtEmail.Text = existing.Email ?? "";
            _txtAddr.Text = existing.Address ?? "";
            _chkActive.Checked = existing.Status;
        }

        var ok = Ui.Btn("保存", 100, 250, 110, 32, OnSave);
        var back = Ui.Btn("取消", 220, 250, 110, 32, (_, _) => Close());
        Controls.AddRange(new Control[]
        {
            Ui.Lbl("名称：", 10, 20, 85), _txtName,
            Ui.Lbl("联系人：", 10, 58, 85), _txtContact,
            Ui.Lbl("电话：", 10, 96, 85), _txtPhone,
            Ui.Lbl("邮箱：", 10, 134, 85), _txtEmail,
            Ui.Lbl("地址：", 10, 172, 85), _txtAddr,
            Ui.Lbl("状态：", 10, 216, 85), _chkActive, ok, back
        });
    }

    private void OnSave(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { Ui.Error("请填写供应商名称。"); return; }
        Result = new Vendor(_existing?.VendorId ?? 0, _txtName.Text.Trim(),
            string.IsNullOrWhiteSpace(_txtContact.Text) ? null : _txtContact.Text.Trim(),
            string.IsNullOrWhiteSpace(_txtPhone.Text) ? null : _txtPhone.Text.Trim(),
            string.IsNullOrWhiteSpace(_txtEmail.Text) ? null : _txtEmail.Text.Trim(),
            string.IsNullOrWhiteSpace(_txtAddr.Text) ? null : _txtAddr.Text.Trim(),
            _chkActive.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
