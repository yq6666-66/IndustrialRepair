using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using OpenCvSharp;

namespace IndustrialRepair.BLL;

/// <summary>
/// 拍照识别引擎 V2 —— 三级流水线（开源库：ZXing.Net 解码 + OpenCvSharp 视觉分析）：
/// ① ZXing：照片含二维码/条码（PART:编码）→ 直接命中，置信 100%；
/// ② OpenCV：CLAHE 增强 → 中心 ROI 均值色 → 最大轮廓圆度(4πA/P²)判形状、轮廓面积占比判尺寸；
/// ③ 特征库融合：与 part.FeatureJson（颜色/形状/尺寸）加权最近邻，输出 Top3 与可解释理由。
/// OpenCV 运行异常时自动降级为纯托管旧规则引擎，保证演示稳定。
/// 实现 IImageRecognizer 契约：课程识别模型到位后按同一接口替换接入。
/// </summary>
public class RecognitionEngine : IImageRecognizer
{
    private class PartFeature
    {
        public int PartId;
        public string Code = "";
        public string Name = "";
        public int R, G, B;
        public string Shape = "";
        public string Size = "";
    }

    /// <summary>图像视觉特征（OpenCV 或降级引擎提取）</summary>
    private record CvFeatures(double R, double G, double B, double Circularity, double AreaRatio, bool FromOpenCv);

    private readonly PartRepository _parts = new();
    private List<PartFeature>? _library;

    private List<PartFeature> Library
    {
        get
        {
            if (_library != null) return _library;
            _library = new List<PartFeature>();
            foreach (var p in _parts.Search(null))
            {
                if (string.IsNullOrWhiteSpace(p.FeatureJson)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(p.FeatureJson);
                    var root = doc.RootElement;
                    var f = new PartFeature { PartId = p.PartId, Code = p.PartCode, Name = p.PartName };
                    if (root.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.Array)
                    {
                        var arr = color.EnumerateArray().ToArray();
                        f.R = arr[0].GetInt32(); f.G = arr[1].GetInt32(); f.B = arr[2].GetInt32();
                    }
                    if (root.TryGetProperty("shape", out var shape)) f.Shape = shape.GetString() ?? "";
                    if (root.TryGetProperty("size", out var size)) f.Size = size.GetString() ?? "";
                    _library.Add(f);
                }
                catch { /* 特征格式异常的配件跳过 */ }
            }
            return _library;
        }
    }

    /// <summary>接口实现入口：生产识别（含二维码直达路径）。</summary>
    public List<RecognizeResult> Recognize(string imagePath) => Recognize(imagePath, includeBarcode: true);

    /// <summary>
    /// 识别照片，返回 Top3 候选（二维码命中始终置顶）。
    /// includeBarcode=false 供外观评测使用——照片内的条码/二维码不得作为分类答案。
    /// </summary>
    public List<RecognizeResult> Recognize(string imagePath, bool includeBarcode)
    {
        var results = new List<RecognizeResult>();

        // ① ZXing 二维码/条码直达
        if (includeBarcode)
        {
            try
            {
                var text = BarcodeUtil.Decode(imagePath);
                var code = BarcodeUtil.ExtractPartCode(text);
                if (code != null)
                {
                    var hit = _parts.Search(code).FirstOrDefault(p =>
                        string.Equals(p.PartCode, code, StringComparison.OrdinalIgnoreCase));
                    results.Add(hit == null
                        ? new RecognizeResult { Score = 90, PartCode = code, PartName = $"未登记编码 {code}", Source = RecognizeSource.Barcode, Reason = $"ZXing 解码：{text}（配件库无此编码）" }
                        : new RecognizeResult { PartId = hit.PartId, PartCode = hit.PartCode, PartName = hit.PartName, Score = 100, Source = RecognizeSource.Barcode, Reason = $"ZXing 二维码解码命中：{text}" });
                }
            }
            catch { /* 解码失败继续视觉路径 */ }
        }

        // ② 视觉路径：NR8——ONNX 后端配置就绪则优先推理（编码→特征库回填 PartId），
        //    否则特征库加权最近邻；两条路径异常均降级托管规则
        var onnx = OnnxImageRecognizerFactory.Create();
        if (onnx.Enabled)
        {
            try
            {
                foreach (var cand in onnx.Recognize(imagePath))
                {
                    var hit = _parts.Search(cand.PartCode).FirstOrDefault(p =>
                        string.Equals(p.PartCode, cand.PartCode, StringComparison.OrdinalIgnoreCase));
                    if (hit == null) continue;   // 模型类别不在配件库：丢弃（评测可见 Fallback）
                    results.Add(new RecognizeResult
                    {
                        PartId = hit.PartId, PartCode = hit.PartCode, PartName = hit.PartName,
                        Score = cand.Score, Source = cand.Source, Reason = cand.Reason
                    });
                }
                return Normalize(results);
            }
            catch (Exception ex) { FileLog.Write("ONNX 推理降级特征库：" + ex.Message); }
        }

        var feat = ExtractWithOpenCv(imagePath) ?? ExtractLegacy(imagePath);
        if (feat != null)
        {
            // ③ 特征库加权最近邻
            string shapeGuess = feat.Circularity switch
            {
                >= 0.93 => "circle",
                >= 0.83 => "hex",
                >= 0.45 => "rect",
                _ => "rect"
            };
            string sizeGuess = feat.AreaRatio switch { > 0.30 => "large", > 0.10 => "medium", _ => "small" };

            foreach (var p in Library)
            {
                double dist = Math.Sqrt(Math.Pow(feat.R - p.R, 2) + Math.Pow(feat.G - p.G, 2) + Math.Pow(feat.B - p.B, 2));
                double colorScore = Math.Max(0, 100 - dist / 441 * 180);
                double shapeScore = string.IsNullOrEmpty(p.Shape) ? 60 : (p.Shape == shapeGuess ? 100 : 45);
                double sizeScore = string.IsNullOrEmpty(p.Size) ? 60 : (p.Size == sizeGuess ? 100 : 45);
                int total = (int)Math.Round(colorScore * 0.6 + shapeScore * 0.25 + sizeScore * 0.15);
                results.Add(new RecognizeResult
                {
                    PartId = p.PartId, PartCode = p.Code, PartName = p.Name,
                    Score = Math.Clamp(total, 1, 98),
                    Source = feat.FromOpenCv ? RecognizeSource.OpenCv : RecognizeSource.Legacy,
                    Reason = (feat.FromOpenCv ? "[OpenCV] " : "[规则] ") +
                             $"主色({feat.R:F0},{feat.G:F0},{feat.B:F0})↔库({p.R},{p.G},{p.B}) 距离{dist:F0}；" +
                             $"圆度{feat.Circularity:F2}→{shapeGuess}；轮廓占比{feat.AreaRatio:P0}→{sizeGuess}"
                });
            }
        }

        var top = Normalize(results);
        if (top.Count == 0)
            top.Add(new RecognizeResult { Score = 0, PartName = "未匹配", Source = RecognizeSource.Unmatched, Reason = "特征库为空或照片质量不足" });
        return top;
    }

    /// <summary>
    /// 候选规整（生产与评测共用）：过滤无 PartId 的占位项 →
    /// 按 PartCode 去重（同件保分高者）→ 分数降序、同分按编码升序（稳定）→ 取前 N。
    /// </summary>
    public static List<RecognizeResult> Normalize(IEnumerable<RecognizeResult> candidates, int take = 3) =>
        candidates.Where(c => c.PartId > 0)
                  .GroupBy(c => c.PartCode, StringComparer.OrdinalIgnoreCase)
                  .Select(g => g.OrderByDescending(c => c.Score).First())
                  .OrderByDescending(c => c.Score)
                  .ThenBy(c => c.PartCode, StringComparer.OrdinalIgnoreCase)
                  .Take(take)
                  .ToList();

    /// <summary>OpenCV 路径：中心 ROI 均值色 + CLAHE/Canny 最大轮廓圆度与占比</summary>
    private static CvFeatures? ExtractWithOpenCv(string path)
    {
        try
        {
            using var src = Cv2.ImRead(path, ImreadModes.Color);
            if (src.Empty()) return null;
            var roi = new Rect(src.Width / 10, src.Height / 10, Math.Max(1, src.Width * 8 / 10), Math.Max(1, src.Height * 8 / 10));
            using var cropped = new Mat(src, roi);

            var mean = Cv2.Mean(cropped);                       // BGR 顺序
            using var gray = new Mat();
            Cv2.CvtColor(cropped, gray, ColorConversionCodes.BGR2GRAY);
            using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);
            using var edges = new Mat();
            Cv2.Canny(enhanced, edges, 50, 150);
            Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double circ = 0, areaRatio = 0;
            if (contours.Length > 0)
            {
                var largest = contours.OrderByDescending(c => Cv2.ContourArea(c, false)).First();
                double a = Cv2.ContourArea(largest, false);
                double p = Cv2.ArcLength(largest, true);
                if (p > 1)
                {
                    circ = 4 * Math.PI * a / (p * p);
                    areaRatio = a / (cropped.Rows * (double)cropped.Cols);
                }
            }
            return new CvFeatures(mean.Val2, mean.Val1, mean.Val0, circ, areaRatio, true);
        }
        catch (Exception ex)
        {
            FileLog.Write("OpenCV 分析降级：" + ex.Message);
            return null;
        }
    }

    /// <summary>降级路径（纯托管，不依赖 OpenCV 原生库）</summary>
    private static CvFeatures? ExtractLegacy(string path)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            int w = bmp.Width, h = bmp.Height;
            int x0 = w / 5, y0 = h / 5, x1 = w - x0, y1 = h - y0;
            long sr = 0, sg = 0, sb = 0, cnt = 0;
            for (int y = y0; y < y1; y += 3)
                for (int x = x0; x < x1; x += 3)
                {
                    var c = bmp.GetPixel(x, y);
                    sr += c.R; sg += c.G; sb += c.B; cnt++;
                }
            if (cnt == 0) return null;
            return new CvFeatures((double)sr / cnt, (double)sg / cnt, (double)sb / cnt, 0, 0, false);
        }
        catch { return null; }
    }
}
