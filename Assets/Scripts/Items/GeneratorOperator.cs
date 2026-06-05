using UnityEngine;

/// <summary>
/// 发电机运行组件示例
///
/// 配合 SceneItemInstance（isToggleable = true）使用：
///   开启 → 开始供电，播放运转特效/音效，向 SoundManager 广播机械噪音
///   关闭 → 停止供电，停止特效/音效
///   每回合 → 消耗燃料（可选），广播噪音
///
/// 挂在与 SceneItemInstance 相同的 GameObject 上即可自动响应开关。
/// </summary>
public class GeneratorOperator : SceneItemOperator
{
    [Header("供电配置")]
    [Tooltip("此发电机供电的区域 ID（对应 ZoneMarker.zoneId）\n" +
             "运行时通知 MissionManager / 其他系统此区域已通电")]
    public string poweredZoneId = "";

    [Header("噪音")]
    [Tooltip("运行时每回合广播的噪音等级（0=无声，5=极响）")]
    [Range(0, 5)]
    public int runningNoiseLevel = 3;

    [Header("特效 / 音效")]
    [Tooltip("运行时循环特效（粒子系统，自动控制播放/停止）")]
    public ParticleSystem runningVFX;

    [Tooltip("运行时音效 key（接入 SoundManager 或 AudioSource）")]
    public string runningSoundKey = "";

    // ── 生命周期 ─────────────────────────────────────────────────────

    protected override void OnStartRunning()
    {
        if (runningVFX != null) runningVFX.Play();

        // 通知任务系统此物体已激活
        if (!string.IsNullOrEmpty(Instance.Data?.sceneObjectId))
            WorldItemRegistry.Record(Instance.Data.sceneObjectId,
                WorldItemState.Completed, "Player");

        Debug.Log($"[Generator:{name}] 开始供电 → Zone:{poweredZoneId}");
    }

    protected override void OnStopRunning()
    {
        if (runningVFX != null) runningVFX.Stop();

        if (!string.IsNullOrEmpty(Instance.Data?.sceneObjectId))
            WorldItemRegistry.Record(Instance.Data.sceneObjectId,
                WorldItemState.Interrupted, "Player");

        Debug.Log($"[Generator:{name}] 停止供电");
    }

    protected override void OnRunningTurnTick(TurnData turnData)
    {
        // 每回合广播机械运转噪音（供 EnemyPerception 感知）
        if (runningNoiseLevel > 0 && SoundManager.Instance != null)
        {
            SoundManager.Instance.BroadcastSound(new SoundEvent(
                transform.position,
                runningNoiseLevel * 2f, // 噪音等级转化为半径（米）
                gameObject,
                SoundType.Environmental,
                runningNoiseLevel / 5f
            ));
        }
    }
}
