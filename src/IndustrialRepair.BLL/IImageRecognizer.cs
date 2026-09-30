using System.Collections.Generic;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 拍照识别器契约（规划：用 IImageRecognizer 隔离课程模型）。
/// 当前实现：RecognitionEngine V2（ZXing 解码 + OpenCV 视觉 + 特征库融合）；
/// 课程提供的识别模型到位后，实现本接口替换接入，UI 与工单流程零改动。
/// </summary>
public interface IImageRecognizer
{
    /// <summary>识别照片，返回按置信度降序的候选（Top N）；无法识别时返回含"未匹配"占位的单元素列表。</summary>
    List<RecognizeResult> Recognize(string imagePath);
}
