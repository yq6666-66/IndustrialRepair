using System;
using System.IO.Ports;
using System.Timers;

namespace IndustrialRepair.BLL;

/// <summary>
/// 真实串口扫码输入（IScannerInput 的硬件实现）：打开指定串口，
/// 收到以换行结束的文本帧后转发。设备未到课时可用 SimulatedScannerInput 演示。
/// </summary>
public sealed class SerialScannerInput : IScannerInput
{
    private readonly SerialPortService _port = new();
    private readonly string _portName;
    private readonly int _baud;

    public SerialScannerInput(string portName, int baudRate = 9600)
    {
        _portName = portName;
        _baud = baudRate;
    }

    public event Action<string, bool>? FrameReceived
    {
        add => _port.FrameReceived += value;
        remove => _port.FrameReceived -= value;
    }

    public bool IsActive => _port.IsOpen;

    public void Start() => _port.Open(_portName, _baud);

    public void Stop() => _port.Close();

    /// <summary>向串口写一帧文本（联调扫码枪回环时用）。</summary>
    public void Send(string text) => _port.Send(text);

    public void Dispose() => _port.Dispose();
}

/// <summary>
/// 无硬件扫码模拟输入（IScannerInput 的演示实现）：定时产生配件条码帧。
/// 与串口实现共用同一接口与解析流程，答辩时可现场切换。
/// </summary>
public sealed class SimulatedScannerInput : IScannerInput
{
    private static readonly string[] SimCodes =
    {
        "PART:P-ZC-001", "PART:P-MF-001", "PART:P-DQ-001", "PART:P-CD-001",
        "PART:P-JG-001", "PART:P-RH-001", "PART:P-ZC-002", "PART:P-QT-001"
    };

    private readonly System.Timers.Timer? _timer = new(4000) { AutoReset = true };
    private readonly Random _rand = new();

    public event Action<string, bool>? FrameReceived;

    public bool IsActive => _timer!.Enabled;

    public SimulatedScannerInput()
    {
        _timer!.Elapsed += (_, _) =>
            FrameReceived?.Invoke(SimCodes[_rand.Next(SimCodes.Length)], true);
    }

    public void Start() => _timer!.Start();

    public void Stop() => _timer!.Stop();

    public void Dispose()
    {
        _timer!.Stop();
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
