using UnityEngine;

/// <summary>
/// 钻机运行组件示例
///
/// 挂在与 SceneItemInstance（isToggleable = true）相同的 GameObject 上。
/// 开启 → 开始钻掘进度计时，每回合推进 progressPerTurn
/// 关闭 → 暂停进度
/// 进度满 → 触发 OnDrillComplete 事件（任务系统可订阅）
/// </summary>
public class DrillOperator : SceneItemOperator
{
    [Header("钻掘配置")]
    [Tooltip("完成所需总回合数")]
    [Range(1, 50)]
    public int totalTurnsRequired = 10;

    [Tooltip("每运行回合推进的进度（通常为 1）")]
    [Range(1, 5)]
    public int progressPerTurn = 1;

    [Header("噪音")]
    [Range(0, 5)]
    public int runningNoiseLevel = 4;

    [Header("特效")]
    public ParticleSystem drillingVFX;

    // ── 运行时状态 ────────────────────────────────────────────────────

    private int currentProgress = 0;
    public int CurrentProgress => currentProgress;
    public float ProgressRatio => totalTurnsRequired > 0
        ? (float)currentProgress / totalTurnsRequired : 0f;
    public bool IsComplete => currentProgress >= totalTurnsRequired;

    /// <summary>钻掘完成时触发</summary>
    public event System.Action<DrillOperator> OnDrillComplete;

    // ── 生命周期 ─────────────────────────────────────────────────────

    protected override void OnStartRunning()
    {
        if (drillingVFX != null) drillingVFX.Play();
        Debug.Log($"[Drill:{name}] 开始钻掘 | 进度:{currentProgress}/{totalTurnsRequired}");
    }

    protected override void OnStopRunning()
    {
        if (drillingVFX != null) drillingVFX.Stop();
        Debug.Log($"[Drill:{name}] 暂停钻掘 | 进度:{currentProgress}/{totalTurnsRequired}");
    }

    protected override void OnRunningTurnTick(TurnData turnData)
    {
        if (IsComplete) return;

        // 推进进度
        currentProgress = Mathf.Min(currentProgress + progressPerTurn, totalTurnsRequired);
        Debug.Log($"[Drill:{name}] 进度 {currentProgress}/{totalTurnsRequired}");

        // 广播噪音（统一走 SoundEmitter.Emit）
        if (runningNoiseLevel > 0)
        {
            SoundEmitter.Emit(transform.position, SoundType.Environmental,
                runningNoiseLevel * 2f, gameObject, runningNoiseLevel / 5f);
        }

        // 完成
        if (IsComplete)
        {
            Debug.Log($"[Drill:{name}] 钻掘完成！");
            OnDrillComplete?.Invoke(this);

            // 通知任务系统
            if (!string.IsNullOrEmpty(Instance.Data?.sceneObjectId))
                WorldItemRegistry.Record(Instance.Data.sceneObjectId,
                    WorldItemState.Completed, "Enemy");
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 进度可视化
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2f,
            $"Drill: {currentProgress}/{totalTurnsRequired}  ({ProgressRatio:P0})");
    }
#endif
}
