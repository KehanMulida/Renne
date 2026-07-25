using UnityEngine;

/// <summary>
/// 可被 AI 视觉感知的目标接口。
/// 暴露检测点世界坐标，下蹲时返回更低的位置，
/// 让 VisionPerception 的射线终点随姿态变化，无需知道 PlayerController 的具体类型。
/// </summary>
public interface IDetectable
{
    /// <summary>AI 视线射线的目标点（站立时约 1m 高，下蹲时约 0.4m 高）</summary>
    Vector3 DetectionPosition { get; }
}
