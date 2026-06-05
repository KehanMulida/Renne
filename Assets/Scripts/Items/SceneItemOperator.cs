using UnityEngine;

/// <summary>
/// 场景物品运行组件基类（挂在与 SceneItemInstance 相同的 GameObject 上）
///
/// 职责划分：
///   SceneItemInstance — 负责开关判定、交互、格子阻挡（不关心物品在"做什么"）
///   SceneItemOperator — 负责物品运行时的具体行为（发电、钻掘、警报等）
///
/// 使用方式：
///   继承此类，重写 OnStartRunning / OnStopRunning / OnRunningUpdate / OnRunningTurnTick
///   例：GeneratorOperator : SceneItemOperator
///
/// 触发来源：
///   SceneItemInstance.OnInteracted → 检测 IsOpen 变化 → 触发对应生命周期
/// </summary>
[RequireComponent(typeof(SceneItemInstance))]
public abstract class SceneItemOperator : MonoBehaviour
{
    // ── 运行状态 ──────────────────────────────────────────────────────

    /// <summary>当前是否正在运行（等同于 SceneItemInstance.IsOpen）</summary>
    public bool IsRunning { get; private set; }

    // ── 组件缓存 ─────────────────────────────────────────────────────

    protected SceneItemInstance Instance { get; private set; }

    // ── 事件（供外部系统订阅，无需直接访问 SceneItemInstance）──────────

    /// <summary>物品开始运行时触发</summary>
    public event System.Action<SceneItemOperator> OnStarted;

    /// <summary>物品停止运行时触发</summary>
    public event System.Action<SceneItemOperator> OnStopped;

    // ══════════════════════════════════════════════════════════════════
    // 生命周期
    // ══════════════════════════════════════════════════════════════════

    protected virtual void Awake()
    {
        Instance = GetComponent<SceneItemInstance>();
    }

    protected virtual void Start()
    {
        if (Instance == null) return;

        Instance.OnInteracted += HandleInteracted;
        Instance.OnDestroyed  += HandleDestroyed;

        // 若物品初始就是开启状态（toggleConfig.startOpen = true），立即运行
        if (Instance.IsOpen)
            StartRunning();

        // 订阅回合系统（turn-based 效果用）
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnStart += HandleTurnStart;
    }

    protected virtual void OnDestroy()
    {
        if (Instance != null)
        {
            Instance.OnInteracted -= HandleInteracted;
            Instance.OnDestroyed  -= HandleDestroyed;
        }

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnStart -= HandleTurnStart;
    }

    protected virtual void Update()
    {
        if (IsRunning)
            OnRunningUpdate();
    }

    // ══════════════════════════════════════════════════════════════════
    // 事件处理（由 SceneItemInstance 驱动，不需要子类关心）
    // ══════════════════════════════════════════════════════════════════

    private void HandleInteracted(SceneItemInstance item)
    {
        // Instance.IsOpen 在 ExecuteToggle 之后已经是新状态
        bool shouldRun = item.IsOpen;

        if (shouldRun && !IsRunning)
            StartRunning();
        else if (!shouldRun && IsRunning)
            StopRunning();
    }

    private void HandleDestroyed(SceneItemInstance item)
    {
        if (IsRunning) StopRunning();
    }

    private void HandleTurnStart(TurnData turnData)
    {
        if (IsRunning)
            OnRunningTurnTick(turnData);
    }

    // ── 内部状态切换 ─────────────────────────────────────────────────

    private void StartRunning()
    {
        IsRunning = true;
        Debug.Log($"[{GetType().Name}:{name}] 开始运行");
        OnStartRunning();
        OnStarted?.Invoke(this);
    }

    private void StopRunning()
    {
        IsRunning = false;
        Debug.Log($"[{GetType().Name}:{name}] 停止运行");
        OnStopRunning();
        OnStopped?.Invoke(this);
    }

    // ══════════════════════════════════════════════════════════════════
    // 子类重写的生命周期（只需关心"运行时做什么"）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>开始运行时调用一次（播放音效、启动特效、通知其他系统等）</summary>
    protected virtual void OnStartRunning() { }

    /// <summary>停止运行时调用一次（停止音效、关闭特效等）</summary>
    protected virtual void OnStopRunning() { }

    /// <summary>运行中每帧调用（持续特效、实时状态更新等）</summary>
    protected virtual void OnRunningUpdate() { }

    /// <summary>运行中每个回合触发一次（资源消耗、噪音生成、MissionManager 通知等）</summary>
    protected virtual void OnRunningTurnTick(TurnData turnData) { }
}
