using System;
using System.IO.Ports;

namespace IndustrialRepair.BLL;

/// <summary>
/// 串口通信硬件层（任务书：串口通信）。只负责真实串口的收发；
/// 无硬件演示请用 SimulatedScannerInput，两者同实现 IScannerInput 契约。
/// 协议约定：收到以换行结束的文本帧，如 "PART:P-ZC-001"。
/// </summary>
public class SerialPortService : IDisposable
{
    private SerialPort? _port;

    /// <summary>收到一帧数据（UI 线程外触发，注意回调内封送）</summary>
    public event Action<string, bool>? FrameReceived;

    public string[] AvailablePorts => SerialPort.GetPortNames();
    public bool IsOpen => _port?.IsOpen == true;

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

    /// <summary>解析扫码帧：PART:编码 → 配件编码（非编码帧返回 null）</summary>
    public static string? ParsePartCode(string frame)
    {
        frame = frame.Trim();
        if (frame.StartsWith("PART:", StringComparison.OrdinalIgnoreCase))
            return frame[5..].Trim();
        return null;
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}
