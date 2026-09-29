using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Timers;

namespace IndustrialRepair.BLL;

/// <summary>
/// 串口通信封装（任务书：物联网设备集成/串口通信）。
/// 支持真实串口（扫码枪/工位终端）与内置模拟器（无硬件时演示）。
/// 协议约定：收到以换行结束的文本帧，如 "PART:P-ZC-001"。
/// </summary>
public class SerialPortService : IDisposable
{
    private SerialPort? _port;
    private System.Timers.Timer? _simTimer;
    private static readonly string[] SimCodes =
    {
        "PART:P-ZC-001", "PART:P-MF-001", "PART:P-DQ-001", "PART:P-CD-001",
        "PART:P-JG-001", "PART:P-RH-001", "PART:P-ZC-002", "PART:P-QT-001"
    };
    private readonly Random _rand = new();

    /// <summary>收到一帧数据（UI 线程外触发，注意回调内封送）</summary>
    public event Action<string, bool>? FrameReceived;

    public string[] AvailablePorts => SerialPort.GetPortNames();
    public bool IsOpen => _port?.IsOpen == true;
    public bool Simulating => _simTimer != null;

    public void Open(string portName, int baudRate = 9600)
    {
        Close();
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 500,
            NewLine = "\r\n"
        };
        _port.DataReceived += (_, e) =>
        {
            if (e.EventType != SerialData.Chars) return;
            try
            {
                var data = _port.ReadExisting();
                foreach (var frame in data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    FrameReceived?.Invoke(frame, false);
            }
            catch { /* 读取竞争时忽略本帧 */ }
        };
        _port.Open();
    }

    public void Close()
    {
        if (_port != null)
        {
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.Dispose();
            _port = null;
        }
    }

    public void Send(string text)
    {
        if (!IsOpen) throw new InvalidOperationException("串口未打开。");
        _port!.WriteLine(text);
    }

    /// <summary>启动扫码模拟器：每 4 秒生成一个配件条码帧</summary>
    public void StartSimulator()
    {
        StopSimulator();
        _simTimer = new System.Timers.Timer(4000) { AutoReset = true };
        _simTimer.Elapsed += (_, _) =>
        {
            var code = SimCodes[_rand.Next(SimCodes.Length)];
            FrameReceived?.Invoke(code, true);
        };
        _simTimer.Start();
    }

    public void StopSimulator()
    {
        _simTimer?.Stop();
        _simTimer?.Dispose();
        _simTimer = null;
    }

    /// <summary>解析扫码帧：PART:编码 → 配件编码</summary>
    public static string? ParsePartCode(string frame)
    {
        frame = frame.Trim();
        if (frame.StartsWith("PART:", StringComparison.OrdinalIgnoreCase))
            return frame[5..].Trim();
        return null;
    }

    public void Dispose()
    {
        StopSimulator();
        Close();
        GC.SuppressFinalize(this);
    }
}
