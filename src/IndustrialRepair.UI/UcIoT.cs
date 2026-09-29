using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// IoT · MQTT 遥测（开源库 MQTTnet 4.x，.NET 基金会）：
/// 应用内嵌 Broker + 设备遥测模拟发布 + 订阅上屏；温度/振动超阈值自动生成报修工单。
/// 对应任务书「物联网设备集成」的进阶演示（与串口扫码页互补）。
/// </summary>
internal class UcIoT : UserControl, IRefreshable
{
    private readonly MqttGateway _gw = new();
    private readonly UIButton _btnStart;
    private readonly UILabel _lblStatus = new()
    {
        Location = new Point(10, 12), Size = new Size(660, 24), AutoSize = false,
        Font = new Font("微软雅黑", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 122, 204),
        Text = "MQTT 网关未启动。"
    };
    private readonly UILineChart _chart = new() { Dock = DockStyle.Fill };
    private readonly UIDataGridView _gridHealth = Ui.Grid(0, 0, 100, 100);
    private readonly UITextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9F)
    };
    private readonly Dictionary<int, Queue<double>> _tempSeries = new();
    private readonly Dictionary<int, (double temp, double vib, DateTime ts, bool alarm)> _health = new();
    private readonly List<Device> _devices = new();
    private int _selectedDevice;

    public UcIoT()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 50 };
        _btnStart = Ui.Btn("启动网关", 700, 10, 100, 32);
        _btnStart.Click += OnToggle;
        var tip = new UILabel
        {
            Text = "内嵌 Broker 127.0.0.1:18835 · 主题 factory/{设备}/telemetry · 阈值 75℃ / 8.0mm/s",
            Location = new Point(10, 34), Size = new Size(900, 18), AutoSize = false,
            ForeColor = Color.Gray, Font = new Font("微软雅黑", 8.5F)
        };
        top.Controls.AddRange(new Control[] { _lblStatus, _btnStart, tip });

        var chartHost = new UIPanel { Dock = DockStyle.Top, Height = 260, Text = "" };
        var chartHead = new UILabel
        {
            Dock = DockStyle.Top, Height = 26, Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            Text = "  实时温度曲线（点击下表设备切换）", TextAlign = ContentAlignment.MiddleLeft
        };
        _chart.Dock = DockStyle.Fill;
        chartHost.Controls.Add(_chart);
        chartHost.Controls.Add(chartHead);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200
        };
        var gridHost = new Panel { Dock = DockStyle.Fill };
        _gridHealth.Dock = DockStyle.Fill;
        _gridHealth.Click += (_, _) =>
        {
            if (int.TryParse(_gridHealth.CurrentRow?.Cells["DeviceId"]?.Value?.ToString(), out var id))
            { _selectedDevice = id; RedrawChart(); }
        };
        gridHost.Controls.Add(_gridHealth);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        logHost.Controls.Add(_log);

        split.Panel1.Controls.Add(gridHost);
        split.Panel2.Controls.Add(logHost);

        Controls.Add(split);
        Controls.Add(chartHost);
        Controls.Add(top);

        // 事件订阅（回调线程 → 封送 UI）
        _gw.StatusChanged += s => SafeRun(() => _lblStatus.Text = s);
        _gw.TelemetryReceived += f => SafeRun(() => OnTelemetry(f));
        _gw.AutoOrderCreated += (no, f) => SafeRun(() =>
            AppendLog($"*** 自动报修已创建 {no}：{f.DeviceName} 温度{f.Temp}℃ 振动{f.Vibration}mm/s 越限 ***"));
        Disposed += (_, _) => _gw.Dispose();
    }

    private void SafeRun(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
        else a();
    }

    public void RefreshData()
    {
        try
        {
            _devices.Clear();
            _devices.AddRange(new WorkOrderService().Devices());
            if (_selectedDevice == 0 && _devices.Count > 0) _selectedDevice = _devices[0].DeviceId;
            foreach (var d in _devices)
                if (!_health.ContainsKey(d.DeviceId))
                    _health[d.DeviceId] = (0, 0, DateTime.MinValue, false);
            RenderHealth();
        }
        catch (Exception ex) { Ui.Error(DAL.DbHelper.FriendlyError(ex)); }
    }

    private async void OnToggle(object? s, EventArgs e)
    {
        try
        {
            _btnStart.Enabled = false;
            if (_gw.Running)
            {
                await _gw.StopAsync();
                _btnStart.Text = "启动网关";
            }
            else
            {
                await _gw.StartAsync();
                _btnStart.Text = "停止网关";
            }
        }
        catch (Exception ex) { Ui.Error("MQTT 网关操作失败：" + ex.Message); }
        finally { _btnStart.Enabled = true; }
    }

    private void OnTelemetry(TelemetryFrame f)
    {
        _health[f.DeviceId] = (f.Temp, f.Vibration, f.Ts, f.Alarm);
        var q = _tempSeries.TryGetValue(f.DeviceId, out var series) ? series : _tempSeries[f.DeviceId] = new Queue<double>();
        q.Enqueue(f.Temp);
        while (q.Count > 30) q.Dequeue();

        if (f.Alarm) AppendLog($"[告警] {f.DeviceName} 温度 {f.Temp}℃ / 振动 {f.Vibration}mm/s 越限（冷却 10 分钟内不重复建单）");
        else AppendLog($"factory/{f.DeviceId}/telemetry  {f.DeviceName}  温度 {f.Temp}℃  振动 {f.Vibration}mm/s");

        RenderHealth();
        if (f.DeviceId == _selectedDevice) RedrawChart();
    }

    private void RenderHealth()
    {
        var t = new System.Data.DataTable();
        t.Columns.Add("DeviceId", typeof(int));
        t.Columns.Add("设备", typeof(string));
        t.Columns.Add("温度℃", typeof(double));
        t.Columns.Add("振动mm/s", typeof(double));
        t.Columns.Add("状态", typeof(string));
        t.Columns.Add("更新时间", typeof(string));
        foreach (var d in _devices)
        {
            if (!_health.TryGetValue(d.DeviceId, out var h)) continue;
            t.Rows.Add(d.DeviceId, d.DeviceName, h.temp, h.vib,
                h.ts == DateTime.MinValue ? "-" : h.alarm ? "越限告警" : "正常",
                h.ts == DateTime.MinValue ? "-" : h.ts.ToString("HH:mm:ss"));
        }
        _gridHealth.DataSource = t;
        // 告警行标红
        foreach (DataGridViewRow row in _gridHealth.Rows)
            if (row.Cells["状态"].Value?.ToString() == "越限告警")
                row.DefaultCellStyle.ForeColor = Color.Firebrick;
    }

    private void RedrawChart()
    {
        if (!_tempSeries.TryGetValue(_selectedDevice, out var q) || q.Count == 0) return;
        try
        {
            var option = new UILineOption();
            var series = option.AddSeries("温度℃", false);
            int i = 0;
            foreach (var v in q) series.Add($"T{++i}", v);
            _chart.SetOption(option);
            _chart.Refresh();
        }
        catch { /* 图表刷新异常不影响主流程 */ }
    }

    private void AppendLog(string line) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
}
