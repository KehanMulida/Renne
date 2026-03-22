using UnityEngine;
using System;

/// <summary>
/// 跨回合蓄力动作
/// 用于消耗 AP 超过单回合上限的动作（装填大炮、重武器等）
/// 使用方法：
///   ChargeAction charge = new ChargeAction("装填大炮", requiredAP: 4);
///   每回合调用 charge.Contribute(availableAP) 累积 AP
///   charge.IsComplete 为 true 时触发动作
/// </summary>
public class ChargeAction
{
    // ============ 配置 ============

    /// <summary>动作名称（用于 debug）</summary>
    public string ActionName { get; private set; }

    /// <summary>完成所需总 AP</summary>
    public int RequiredAP { get; private set; }

    // ============ 运行时状态 ============

    /// <summary>已累积的 AP</summary>
    public int AccumulatedAP { get; private set; }

    /// <summary>还需要多少 AP 才能完成</summary>
    public int RemainingRequired => Mathf.Max(0, RequiredAP - AccumulatedAP);

    /// <summary>是否已完成（累积 AP 达到要求）</summary>
    public bool IsComplete => AccumulatedAP >= RequiredAP;

    /// <summary>是否正在蓄力中（已开始但未完成）</summary>
    public bool IsCharging => AccumulatedAP > 0 && !IsComplete;

    /// <summary>蓄力进度（0~1）</summary>
    public float Progress => RequiredAP > 0 ? (float)AccumulatedAP / RequiredAP : 0f;

    // ============ 事件 ============

    /// <summary>每次成功累积 AP 时触发（参数：本次贡献量，总累积量）</summary>
    public event Action<int, int> OnContributed;

    /// <summary>蓄力完成时触发</summary>
    public event Action OnCompleted;

    /// <summary>蓄力被取消时触发</summary>
    public event Action OnCancelled;

    // ============ 构造 ============

    public ChargeAction(string actionName, int requiredAP)
    {
        ActionName   = actionName;
        RequiredAP   = Mathf.Max(1, requiredAP);
        AccumulatedAP = 0;
    }

    // ============ 核心方法 ============

    /// <summary>
    /// 本回合贡献 AP 到蓄力池
    /// 返回实际消耗的 AP（不会超过 RemainingRequired）
    /// </summary>
    public int Contribute(int availableAP)
    {
        if (IsComplete)
        {
            Debug.LogWarning($"[ChargeAction:{ActionName}] Already complete");
            return 0;
        }

        if (availableAP <= 0) return 0;

        // 实际消耗不超过剩余需求
        int contribution = Mathf.Min(availableAP, RemainingRequired);
        AccumulatedAP += contribution;

        Debug.Log($"[ChargeAction:{ActionName}] Contributed {contribution} AP | " +
                  $"Progress: {AccumulatedAP}/{RequiredAP}");

        OnContributed?.Invoke(contribution, AccumulatedAP);

        if (IsComplete)
        {
            Debug.Log($"[ChargeAction:{ActionName}] Charge complete!");
            OnCompleted?.Invoke();
        }

        return contribution;
    }

    /// <summary>
    /// 取消蓄力，重置状态
    /// 已累积的 AP 不退还（设计上蓄力被打断就浪费）
    /// </summary>
    public void Cancel()
    {
        if (AccumulatedAP <= 0) return;

        Debug.Log($"[ChargeAction:{ActionName}] Cancelled | Lost {AccumulatedAP} AP");
        AccumulatedAP = 0;
        OnCancelled?.Invoke();
    }

    /// <summary>重置到初始状态（动作完成后可复用）</summary>
    public void Reset()
    {
        AccumulatedAP = 0;
    }

    public override string ToString() =>
        $"ChargeAction[{ActionName}] {AccumulatedAP}/{RequiredAP} AP";
}
