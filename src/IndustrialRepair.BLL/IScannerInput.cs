using System;
using System.Collections.Generic;

namespace IndustrialRepair.BLL;

/// <summary>
/// 扫码输入源契约（规划：用 IScannerInput 隔离串口设备）。
/// 消费方（扫码页）只认本接口：真实串口扫码枪、无硬件模拟器、
/// 图片扫码、手动录入最终都走同一条「编码 → 配件查询」流程。
/// 事件在线程池线程触发，订阅方需自行封送回 UI 线程。
/// </summary>
public interface IScannerInput : IDisposable
{
    /// <summary>收到一帧原始数据；fromSim=true 表示来自模拟/非硬件源。</summary>
    event Action<string, bool>? FrameReceived;

    /// <summary>当前是否处于工作状态。</summary>
    bool IsActive { get; }

    void Start();
    void Stop();
}
