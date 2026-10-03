using System.Drawing;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>NR8：ONNX 后端纯函数（标签映射 / 预处理张量 / 配置门控）。</summary>
public class OnnxRecognizerTests
{
    [Fact]
    public void 标签映射支持制表符编码与注释()
    {
        var labels = OnnxImageRecognizer.ParseLabels(new[]
        {
            "# 注释",
            "深沟球轴承\tP-ZC-001",
            "骨架油封\tP-MF-001",
            "未知类别",          // 无编码 → null
            "  ",                // 空行
        });
        Assert.Equal(3, labels.Length);
        Assert.Equal("P-ZC-001", labels[0].PartCode);
        Assert.Null(labels[2].PartCode);
    }

    [Fact]
    public void 预处理张量为NCHW且归一化()
    {
        using var img = new Bitmap(64, 48);   // 非 1:1 → letterbox 缩放
        using (var g = Graphics.FromImage(img)) g.Clear(Color.Red);

        var tensor = OnnxImageRecognizer.Preprocess(img, 32);
        Assert.Equal(1 * 3 * 32 * 32, tensor.Length);
        // 纯红图：R 通道接近 1，G/B 接近 0（中心像素，避免 letterbox 灰边）
        Assert.True(tensor[0, 0, 16, 16] > 0.9f, $"R={tensor[0, 0, 16, 16]}");
        Assert.True(tensor[0, 1, 16, 16] < 0.1f, $"G={tensor[0, 1, 16, 16]}");
        Assert.True(tensor[0, 2, 16, 16] < 0.1f, $"B={tensor[0, 2, 16, 16]}");
    }

    [Fact]
    public void 未配置时引擎模式为特征库且工厂复用实例()
    {
        OnnxImageRecognizerFactory.Reset();
        var a = OnnxImageRecognizerFactory.Create();
        var b = OnnxImageRecognizerFactory.Create();
        Assert.False(a.Enabled);
        Assert.Same(a, b);                                  // 缓存复用（不重复探测文件）
        Assert.Contains("特征库", OnnxImageRecognizer.EngineMode);
        OnnxImageRecognizerFactory.Reset();
    }

    [Fact]
    public void 配置对象缺文件视为无效()
    {
        var o = new OnnxOptions { ModelPath = "not-exist.onnx", LabelsPath = "not-exist.txt" };
        Assert.False(o.IsValid);
    }
}
