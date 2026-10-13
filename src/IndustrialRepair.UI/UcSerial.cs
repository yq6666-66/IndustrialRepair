using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 串口·扫码（物联网设备集成）：真实串口（SerialScannerInput）与无硬件模拟器
/// （SimulatedScannerInput）实现同一 IScannerInput 契约，本页只面向接口消费；
/// 手动录入编码走同一条「编码 → 配件查询」流程（规划：设备未到时用同一接口测试）。
/// 协议帧：PART:配件编码（扫码枪常见前缀格式）。
/// </summary>
internal class UcSerial : UserControl, IRefreshable
{
    private readonly PartService _parts = new();
    private IScannerInput? _active;              // 当前扫码输入源（接口类型，切换即换实现）
    private SerialScannerInput? _serialInput;    // 保留引用：测试帧发送仅串口源支持
    // 第一行坐标按「标签→控件→标签→控件→按钮」顺序排布，避免标签压住前一个下拉框
    private readonly UIComboBox _cmbPort = Ui.Combo(78, 12, 140);
    private readonly UIComboBox _cmbBaud = Ui.Combo(306, 12, 100);
    private readonly UIButton _btnOpen = Ui.Btn("打开串口", 414, 10, 100, 32);
    private readonly UIButton _btnSim = Ui.Btn("启动扫码模拟器", 522, 10, 140, 32);
    private readonly UITextBox _txtManual = Ui.Txt("直接输入配件编码，如 P-ZC-001", 102, 56, 220);
    private readonly UITextBox _log = new()
    {
        Location = new Point(10, 100), Size = new Size(700, 560), Multiline = true,
        ReadOnly = true, Font = new Font("Consolas", 9F), Watermark = "接收帧日志…",
        StyleCustomMode = true, FillColor = Theme.Current.InputBg, ForeColor = Theme.Current.TextPrimary,
        RectColor = Theme.Current.Border, WatermarkColor = Theme.Current.TextSecondary,
        // 只读态走独立颜色：不设则边框回落成库默认亮色
        FillReadOnlyColor = Theme.Current.InputBg, ForeReadOnlyColor = Theme.Current.TextPrimary,
        RectReadOnlyColor = Theme.Current.Border
    };
    private readonly UILabel _lblPart = new()
    {
        Location = new Point(730, 100), Size = new Size(440, 300), AutoSize = false,
        BackColor = Theme.Current.CardBg, ForeColor = Theme.Current.TextPrimary,
        Font = new Font("微软雅黑", 10F), Text = "扫码后将显示配件信息\n\n协议帧示例：PART:P-ZC-001"
    };

    public UcSerial()
    {
        _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "115200" });
        _cmbBaud.SelectedIndex = 0;

        _btnOpen.Click += OnOpen;
        _btnSim.Click += OnSim;
        var btnManual = Ui.Btn("查询", 330, 54, 70, 28, (_, _) =>
        {
            var code = _txtManual.Text.Trim();
            if (code.Length == 0) { Ui.Error("请输入配件编码。"); return; }
            AppendLog("[手动录入] " + code);
            LookupPart(code);                       // 与扫码帧同一条查询流程
        });
        var btnSend = Ui.Btn("发送测试帧", 670, 10, 100, 32, (_, _) =>
        {
            if (_serialInput is not { IsActive: true }) { Ui.Error("请先打开串口。"); return; }
            try { _serialInput.Send("PING"); AppendLog(">> PING"); }
            catch (Exception ex) { Ui.Error(ex.Message); }
        });
        var btnScanImg = Ui.Btn("从图片扫码", 778, 10, 100, 32, OnScanImage);

        Controls.AddRange(new Control[]
        {
            Ui.Lbl("串口：", 10, 14, 64), _cmbPort,
            Ui.Lbl("波特率：", 226, 14, 76), _cmbBaud,
            _btnOpen, _btnSim, btnSend, btnScanImg,
            Ui.Lbl("手动录入：", 10, 58, 88), _txtManual, btnManual,
            _log, _lblPart
        });
        Disposed += (_, _) => StopSource();
    }

    public void RefreshData()
    {
        _cmbPort.Items.Clear();
        var ports = SerialPort.GetPortNames();
        foreach (var p in ports) _cmbPort.Items.Add(p);
        if (_cmbPort.Items.Count == 0)
        {
            _cmbPort.Items.Add("COM1");
            AppendLog("未检测到物理串口（可使用扫码模拟器或手动录入演示）。");
        }
        _cmbPort.SelectedIndex = 0;
    }

    private void OnOpen(object? s, EventArgs e)
    {
        try
        {
            if (_serialInput is { IsActive: true })
            {
                StopSource();
                _btnOpen.Text = "打开串口";
                AppendLog("串口已关闭。");
                return;
            }
            StopSource();
            _serialInput = new SerialScannerInput(_cmbPort.Text, int.Parse(_cmbBaud.Text));
            StartSource(_serialInput);
            _btnOpen.Text = "关闭串口";
            AppendLog($"已打开 {_cmbPort.Text} @ {_cmbBaud.Text}，等待扫码帧…");
        }
        catch (Exception ex)
        {
            StopSource();
            _btnOpen.Text = "打开串口";
            Ui.Error("串口操作失败：" + ex.Message);
        }
    }

    private void OnSim(object? s, EventArgs e)
    {
        if (_active is SimulatedScannerInput)
        {
            StopSource();
            _btnSim.Text = "启动扫码模拟器";
            AppendLog("扫码模拟器已停止。");
            return;
        }
        StopSource();
        StartSource(new SimulatedScannerInput());
        _btnSim.Text = "停止扫码模拟器";
        AppendLog("扫码模拟器已启动：每 4 秒模拟一帧配件条码。");
    }

    /// <summary>切换到新输入源：旧源先解绑停止，再订阅并启动新源（同一接口、同一消费流程）。</summary>
    private void StartSource(IScannerInput source)
    {
        _active = source;
        _active.FrameReceived += OnFrame;
        _active.Start();
    }

    private void StopSource()
    {
        if (_active == null) return;
        _active.FrameReceived -= OnFrame;
        _active.Stop();
        _active.Dispose();
        if (!ReferenceEquals(_active, _serialInput)) _btnSim.Text = "启动扫码模拟器";
        else _serialInput = null;
        _btnOpen.Text = "打开串口";
        _active = null;
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

    /// <summary>输入源数据帧回调（可能来自线程池，封送到 UI 线程）</summary>
    private void OnFrame(string frame, bool fromSim)
    {
        if (InvokeRequired) { BeginInvoke(() => OnFrame(frame, fromSim)); return; }
        AppendLog($"{(fromSim ? "[模拟] " : "")}<< {frame}");
        var code = SerialPortService.ParsePartCode(frame);
        if (code == null) return;
        LookupPart(code);
    }

    /// <summary>编码 → 配件查询：串口 / 模拟器 / 图片 / 手动录入共用此流程</summary>
    private void LookupPart(string code)
    {
        var hit = _parts.Search(code).FirstOrDefault();
        _lblPart.Text = hit == null
            ? $"扫码：{code}\n未在配件库中找到该编码。"
            : $"扫码识别配件：\n\n{hit.PartName}\n编码：{hit.PartCode}\n类别：{hit.CategoryName}\n规格：{hit.Spec}\n" +
              $"单价：{hit.Price:F2} 元 / {hit.Unit}\n当前库存：{hit.StockQty}（下限 {hit.MinStock}）";
    }

    private void AppendLog(string line) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
}
