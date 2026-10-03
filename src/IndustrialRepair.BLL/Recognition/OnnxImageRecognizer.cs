using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using IndustrialRepair.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IndustrialRepair.BLL;

/// <summary>ONNX 识别后端配置（appsettings.json 的 "Onnx" 节）。</summary>
public sealed record OnnxOptions
{
    public string ModelPath { get; init; } = "";
    public string LabelsPath { get; init; } = "";
    public int InputSize { get; init; } = 224;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath) &&
        !string.IsNullOrWhiteSpace(LabelsPath) && File.Exists(LabelsPath) &&
        InputSize >= 32 && InputSize <= 640;

    public static OnnxOptions? LoadFromConfig()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.db.json"),
        };
        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("Onnx", out var node) || node.ValueKind != JsonValueKind.Object)
                    continue;
                var o = new OnnxOptions
                {
                    ModelPath = node.TryGetProperty("ModelPath", out var m) ? m.GetString() ?? "" : "",
                    LabelsPath = node.TryGetProperty("LabelsPath", out var l) ? l.GetString() ?? "" : "",
                    InputSize = node.TryGetProperty("InputSize", out var s) && s.TryGetInt32(out var size) ? size : 224
                };
                return o.IsValid ? o : null;
            }
            catch { /* 配置损坏按未配置处理 */ }
        }
        return null;
    }
}

/// <summary>
/// ONNX 图像分类后端（NR8，开源库 Microsoft.ML.OnnxRuntime，MIT）——为课程模型预留的接入位：
/// 配置 "Onnx" 节（模型/标签文件就绪）即自动启用；未配置或加载失败自动回退特征库引擎，UI/流程零改动。
/// 约定：单输入 float NCHW 模型（1,3,H,W），输出 logits/softmax（1,N），
/// 标签文件每行 "类别名<TAB>配件编码"（编码可缺省→类别名原样输出）。
/// </summary>
public sealed class OnnxImageRecognizer
{
    private readonly OnnxOptions _options;
    private readonly InferenceSession? _session;
    private readonly string _inputName;
    private readonly (string Label, string? PartCode)[] _labels;

    public bool Enabled => _session != null;
    public static string EngineMode => OnnxImageRecognizerFactory.Create().Enabled ? "ONNX模型" : "特征库匹配";

    internal OnnxImageRecognizer(OnnxOptions options, InferenceSession? session, string inputName,
                                 (string, string?)[] labels)
    {
        _options = options; _session = session; _inputName = inputName; _labels = labels;
    }

    /// <summary>对照片推理，返回按分数降序的候选（不超过 topN；无有效映射时为空表）。</summary>
    public List<RecognizeResult> Recognize(string imagePath, int topN = 3)
    {
        if (_session == null) throw new InvalidOperationException("ONNX 后端未启用。");
        using var src = Image.FromFile(imagePath);
        var tensor = Preprocess(src, _options.InputSize);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
        using var results = _session.Run(inputs);
        var scores = results.First().AsTensor<float>().ToArray();

        return scores.Select((s, i) => (Score: s, Idx: i))
                     .Where(x => x.Idx < _labels.Length)
                     .OrderByDescending(x => x.Score)
                     .Take(topN)
                     .Select(x => new RecognizeResult
                     {
                         PartId = 0,   // 编码→PartId 由 RecognitionEngine 特征库回填（模型只给编码）
                         PartCode = _labels[x.Idx].PartCode ?? _labels[x.Idx].Label,
                         PartName = _labels[x.Idx].Label,
                         Score = (int)Math.Clamp(x.Score * 100, 1, 99),
                         Source = "Onnx",
                         Reason = $"ONNX 推理：类别 {_labels[x.Idx].Label}（{x.Score:P1}）"
                     })
                     .ToList();
    }

    /// <summary>预处理（纯函数可单测）：等比缩放填充到 size×size，RGB/255 归一化，NCHW。</summary>
    public static DenseTensor<float> Preprocess(Image image, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min((float)size / image.Width, (float)size / image.Height);
            var w = (int)(image.Width * scale); var h = (int)(image.Height * scale);
            g.Clear(Color.FromArgb(114, 114, 114));   // YOLO 风格灰底 letterbox
            g.DrawImage(image, (size - w) / 2, (size - h) / 2, w, h);
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
        var data = bmp.LockBits(new Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                var ptr = (byte*)data.Scan0;
                for (int y = 0; y < size; y++)
                {
                    var row = ptr + y * data.Stride;
                    for (int x = 0; x < size; x++)
                    {
                        var b = row[x * 3]; var g = row[x * 3 + 1]; var r = row[x * 3 + 2];
                        tensor[0, 0, y, x] = r / 255f;
                        tensor[0, 1, y, x] = g / 255f;
                        tensor[0, 2, y, x] = b / 255f;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return tensor;
    }

    /// <summary>标签映射（纯函数可单测）：每行 "类别名[Tab]配件编码"（编码可缺省）。</summary>
    public static (string Label, string? PartCode)[] ParseLabels(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim())
             .Where(l => l.Length > 0 && !l.StartsWith('#'))
             .Select(l =>
             {
                 var idx = l.IndexOf('\t');
                 return idx < 0 ? (l, (string?)null) : (l[..idx].Trim(), l[(idx + 1)..].Trim());
             })
             .ToArray();
}

/// <summary>工厂：配置就绪建会话（失败记日志降级）；未配置返回禁用实例。</summary>
public static class OnnxImageRecognizerFactory
{
    private static OnnxImageRecognizer? _cached;

    public static OnnxImageRecognizer Create()
    {
        if (_cached != null) return _cached;
        var o = OnnxOptions.LoadFromConfig();
        if (o == null) { _cached = new OnnxImageRecognizer(new OnnxOptions(), null, "", Array.Empty<(string, string?)>()); return _cached; }
        try
        {
            using var probe = new InferenceSession(o.ModelPath);
            var inputName = probe.InputMetadata.Keys.First();
            var labels = OnnxImageRecognizer.ParseLabels(File.ReadAllLines(o.LabelsPath));
            if (labels.Length == 0) throw new InvalidDataException("标签文件为空。");
            _cached = new OnnxImageRecognizer(o, new InferenceSession(o.ModelPath), inputName, labels);
            FileLog.Write($"[NR8] ONNX 后端已启用：{Path.GetFileName(o.ModelPath)}，类别 {labels.Length} 个，输入 {o.InputSize}px");
        }
        catch (Exception ex)
        {
            FileLog.Write($"[NR8] ONNX 后端启用失败（回退特征库）：{ex.Message}");
            _cached = new OnnxImageRecognizer(new OnnxOptions(), null, "", Array.Empty<(string, string?)>());
        }
        return _cached;
    }

    /// <summary>测试/配置变更后重置缓存。</summary>
    public static void Reset() => _cached = null;
}
