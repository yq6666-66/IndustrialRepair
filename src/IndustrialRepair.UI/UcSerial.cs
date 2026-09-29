using System;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 串口·扫码（物联网设备集成）：真实串口收发 + 无硬件扫码模拟器。
/// 协议帧：PART:配件编码（扫码枪常见前缀格式）。
/// </summary>
internal class UcSerial : UserControl, IRefreshable
{
    private readonly SerialPortService _serial = new();
    private readonly PartService _parts = new();
    private readonly UIComboBox _cmbPort = Ui.Combo(90, 12, 140);
    private readonly UIComboBox _cmbBaud = Ui.Combo(250, 12, 100);
    private readonly UIButton _btnOpen = Ui.Btn("打开串口", 370, 10, 100, 32);
    private readonly UIButton _btnSim = Ui.Btn("启动扫码模拟器", 490, 10, 140, 32);
    private readonly UITextBox _log = new()
    {
        Location = new Point(10, 100), Size = new Size(700, 560), Multiline = true,
        ReadOnly = true, Font = new Font("Consolas", 9F), Watermark = "接收帧日志…"
    };
    private readonly UILabel _lblPart = new()
    {
        Location = new Point(730, 100), Size = new Size(440, 300), AutoSize = false,
        BackColor = Color.FromArgb(245, 248, 252),
        Font = new Font("微软雅黑", 10F), Text = "扫码后将显示配件信息\n\n协议帧示例：PART:P-ZC-001"
    };

    public UcSerial()
    {
        _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "115200" });
        _cmbBaud.SelectedIndex = 0;

        _btnOpen.Click += OnOpen;
        _btnSim.Click += OnSim;
        var btnSend = Ui.Btn("发送测试帧", 650, 10, 100, 32, (_, _) =>
        {
            if (!_serial.IsOpen) { Ui.Error("串口未打开。"); return; }
            try { _serial.Send("PING"); AppendLog(">> PING"); }
            catch (Exception ex) { Ui.Error(ex.Message); }
        });
        var btnScanImg = Ui.Btn("从图片扫码", 760, 10, 100, 32, OnScanImage);

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("串口：", 10, 14, 76), _cmbPort,
            Ui.Lbl("波特率：", 200, 14, 50) , _cmbBaud,
            _btnOpen, _btnSim, btnSend, btnScanImg, _log, _lblPart
        });
        _serial.FrameReceived += OnFrame;
        Disposed += (_, _) => _serial.Dispose();
    }

    public void RefreshData()
    {
        _cmbPort.Items.Clear();
        var ports = _serial.AvailablePorts;
        foreach (var p in ports) _cmbPort.Items.Add(p);
        if (_cmbPort.Items.Count == 0)
        {
            _cmbPort.Items.Add("COM1");
            AppendLog("未检测到物理串口（可使用扫码模拟器演示）。");
        }
        _cmbPort.SelectedIndex = 0;
    }

    private void OnOpen(object? s, EventArgs e)
    {
        try
        {
            if (_serial.IsOpen)
            {
                _serial.Close();
                _btnOpen.Text = "打开串口";
                AppendLog("串口已关闭。");
            }
            else
            {
                _serial.Open(_cmbPort.Text, int.Parse(_cmbBaud.Text));
                _btnOpen.Text = "关闭串口";
                AppendLog($"已打开 {_cmbPort.Text} @ {_cmbBaud.Text}，等待扫码帧…");
            }
        }
        catch (Exception ex) { Ui.Error("串口操作失败：" + ex.Message); }
    }

    private void OnSim(object? s, EventArgs e)
    {
        if (_serial.Simulating)
        {
            _serial.StopSimulator();
            _btnSim.Text = "启动扫码模拟器";
            AppendLog("扫码模拟器已停止。");
        }
        else
        {
            _serial.StartSimulator();
            _btnSim.Text = "停止扫码模拟器";
            AppendLog("扫码模拟器已启动：每 4 秒模拟一帧配件条码。");
        }
    }

    /// <summary>从图片扫码（开源库 ZXing.Net 解码照片中的二维码/条码）</summary>
    private void OnScanImage(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp", Title = "选择含二维码的照片（可用配件二维码标签测试）" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        try
        {
            var text = BarcodeUtil.Decode(ofd.FileName);
            if (text == null) { Ui.Error("未在图片中识别到二维码/条码。提示：可先在配件库生成二维码标签测试。"); return; }
            OnFrame(text, false);
        }
        catch (Exception ex) { Ui.Error(ex.Message); }
    }

    /// <summary>串口/模拟器数据帧回调（可能来自线程池，封送到 UI 线程）</summary>
    private void OnFrame(string frame, bool fromSim)
    {
        if (InvokeRequired) { BeginInvoke(() => OnFrame(frame, fromSim)); return; }
        AppendLog($"{(fromSim ? "[模拟] " : "")}<< {frame}");
        var code = SerialPortService.ParsePartCode(frame);
        if (code == null) return;

        var hit = _parts.Search(code).FirstOrDefault();
        _lblPart.Text = hit == null
            ? $"扫码：{code}\n未在配件库中找到该编码。"
            : $"扫码识别配件：\n\n{hit.PartName}\n编码：{hit.PartCode}\n类别：{hit.CategoryName}\n规格：{hit.Spec}\n" +
              $"单价：{hit.Price:F2} 元 / {hit.Unit}\n当前库存：{hit.StockQty}（下限 {hit.MinStock}）";
    }

    private void AppendLog(string line) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
}
