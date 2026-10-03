using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace IndustrialRepair.BLL;

/// <summary>遥测数据帧</summary>
public class TelemetryFrame
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = "";
    public double Temp { get; set; }
    public double Vibration { get; set; }
    public DateTime Ts { get; set; } = DateTime.Now;
    public bool Alarm => Temp > TempLimit || Vibration > VibrationLimit;
    public const double TempLimit = 75.0;
    public const double VibrationLimit = 8.0;
}

/// <summary>
/// MQTT 物联网网关（开源库 MQTTnet 4.x，.NET 基金会，~4k★，MIT）：
/// 1) 应用内嵌 MqttServer(localhost:1883)，无需外部 Broker；
/// 2) 设备遥测模拟器发布 factory/{deviceId}/telemetry JSON（温度/振动随机游走）；
/// 3) 订阅回调上抛 UI；超阈值（温度>75℃ 或 振动>8.0）经冷却窗自动生成“IoT自动报修”工单。
/// </summary>
public class MqttGateway : IDisposable
{
    private MqttServer? _server;
    private IMqttClient? _client;
    private System.Threading.Timer? _simTimer;
    private readonly Random _rand = new();
    private readonly BasicRepository _basic = new();
    private readonly WorkOrderRepository _orders = new();
    private readonly ConcurrentDictionary<int, (double temp, double vib)> _state = new();
    private readonly ConcurrentDictionary<int, DateTime> _alarmCooldown = new();
    private List<Device> _devices = new();

    /// <summary>收到遥测帧（可能来自 MQTT 线程，UI 需自行封送）</summary>
    public event Action<TelemetryFrame>? TelemetryReceived;
    /// <summary>IOT 自动报修工单已创建（工单号）</summary>
    public event Action<string, TelemetryFrame>? AutoOrderCreated;
    /// <summary>网关状态文本</summary>
    public event Action<string>? StatusChanged;

    public bool Running { get; private set; }
    public int Port { get; } = 18835;   // 避开常用 1883，减少端口冲突

    /// <summary>
    /// 启动网关。withSimulator=false 不启动随机遥测模拟器——
    /// 测试/联调时配合 PublishTelemetry 用确定性输入验证越限报修与冷却。
    /// </summary>
    public async Task StartAsync(bool withSimulator = true)
    {
        if (Running) return;
        var factory = new MqttFactory();

        // 注意：WithDefaultEndpoint() 负责启用默认 TCP 端点，WithDefaultEndpointPort 仅改端口
        _server = factory.CreateMqttServer(
            new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(Port).Build());
        await _server.StartAsync();
        await Task.Delay(300);   // 等待监听就绪

        _client = factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();
            OnFrame(topic, payload);
            return Task.CompletedTask;
        };
        await _client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", Port).Build());
        await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic("factory/+/telemetry")).Build());

        _devices = _basic.ListDevices();
        _state.Clear();
        foreach (var d in _devices)
            _state[d.DeviceId] = (55 + _rand.NextDouble() * 12, 3.5 + _rand.NextDouble() * 2.5);

        if (withSimulator)
            _simTimer = new System.Threading.Timer(_ => PublishAll(), null, 0, 3000);
        Running = true;
        StatusChanged?.Invoke(withSimulator
            ? $"MQTT 网关运行中：内嵌 Broker 127.0.0.1:{Port}，设备 {_devices.Count} 台，3 秒/帧"
            : $"MQTT 网关运行中：内嵌 Broker 127.0.0.1:{Port}，设备 {_devices.Count} 台（模拟器关闭）");
    }

    /// <summary>发布一帧指定遥测（确定性输入：测试越限报修/冷却联调用）</summary>
    public void PublishTelemetry(TelemetryFrame frame)
    {
        if (_client?.IsConnected != true)
            throw new InvalidOperationException("MQTT 网关未启动。");
        var payload = JsonSerializer.Serialize(frame);
        _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"factory/{frame.DeviceId}/telemetry")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build()).Wait(3000);
    }

    public async Task StopAsync()
    {
        _simTimer?.Dispose(); _simTimer = null;
        if (_client?.IsConnected == true) await _client.DisconnectAsync();
        _client?.Dispose(); _client = null;
        if (_server != null) { await _server.StopAsync(); _server.Dispose(); _server = null; }
        Running = false;
        StatusChanged?.Invoke("MQTT 网关已停止。");
    }

    /// <summary>发布一帧全部设备遥测（随机游走，10% 概率冲高触发告警）</summary>
    private void PublishAll()
    {
        if (_client?.IsConnected != true) return;
        foreach (var d in _devices)
        {
            var s = _state.GetOrAdd(d.DeviceId, _ => (60.0, 4.0));
            double spike = _rand.NextDouble() < 0.10 ? 18 + _rand.NextDouble() * 12 : 0;
            double temp = Math.Max(30, Math.Min(105, s.temp + (_rand.NextDouble() - 0.45) * 4 + spike));
            double vib = Math.Max(0.5, Math.Min(14, s.vib + (_rand.NextDouble() - 0.45) * 0.9 + spike / 12));
            _state[d.DeviceId] = (temp, vib);
            var frame = new TelemetryFrame { DeviceId = d.DeviceId, DeviceName = d.DeviceName, Temp = Math.Round(temp, 1), Vibration = Math.Round(vib, 2) };
            var payload = JsonSerializer.Serialize(frame);
            _client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic($"factory/{d.DeviceId}/telemetry")
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build()).Wait();
        }
    }

    private void OnFrame(string topic, string payload)
    {
        TelemetryFrame? frame;
        try { frame = JsonSerializer.Deserialize<TelemetryFrame>(payload); }
        catch { return; }
        if (frame == null) return;
        if (frame.DeviceId == 0 && topic.Split('/') is { Length: > 1 } seg && int.TryParse(seg[1], out var devId))
            frame.DeviceId = devId;

        TelemetryReceived?.Invoke(frame);
        if (frame.Alarm) TryAutoReport(frame);
    }

    /// <summary>超阈值自动报修（同设备 10 分钟冷却，防重复建单）</summary>
    private void TryAutoReport(TelemetryFrame f)
    {
        var last = _alarmCooldown.GetOrAdd(f.DeviceId, DateTime.MinValue);
        if ((DateTime.Now - last).TotalMinutes < 10) return;
        _alarmCooldown[f.DeviceId] = DateTime.Now;
        try
        {
            const int adminUserId = 1;   // 系统管理员作为 IoT 报修发起人
            var desc = $"[IoT自动报修] {f.DeviceName} 遥测越限：温度 {f.Temp}℃（限 {TelemetryFrame.TempLimit}℃）、" +
                       $"振动 {f.Vibration}mm/s（限 {TelemetryFrame.VibrationLimit}），时间 {f.Ts:HH:mm:ss}";
            var orderNo = _orders.Create(f.DeviceId, adminUserId, desc, "机械",
                f.Temp > TelemetryFrame.TempLimit ? PriorityCode.Critical : PriorityCode.Urgent,
                null, "MQTT 遥测阈值触发（物联网设备集成）");
            FileLog.Write($"MQTT 自动报修：{orderNo} {desc}");
            AutoOrderCreated?.Invoke(orderNo, f);
            NotificationCenter.Post(AppNotification.Alert("MQTT 自动报修",
                $"{orderNo}：{f.DeviceName} 温度{f.Temp}℃ 振动{f.Vibration}mm/s 越限"));
        }
        catch (Exception ex)
        {
            FileLog.Write("MQTT 自动报修失败：" + ex.Message);
        }
    }

    public void Dispose()
    {
        try { StopAsync().Wait(2000); } catch { }
        GC.SuppressFinalize(this);
    }
}

/// <summary>滚动文件日志（全局异常与关键事件留痕）</summary>
public static class FileLog
{
    private static readonly object Lock = new();
    public static string LogDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "logs");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var file = Path.Combine(LogDir, $"app_{DateTime.Now:yyyyMMdd}.log");
            lock (Lock)
                File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* 日志失败不再抛出 */ }
    }

    public static void Write(Exception ex, string context = "")
    {
        Write($"{context} 异常：{ex.GetType().Name}: {ex.Message}\r\n   堆栈：{ex.StackTrace}");
    }
}
