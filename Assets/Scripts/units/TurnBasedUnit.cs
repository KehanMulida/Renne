using UnityEngine;
using System.Collections;

/// <summary>
/// 回合制单位组件
/// 职责：回合逻辑（是否轮到我、AP 消耗、事件触发）
/// AP 数值从 PlayerConfig 或 EnemyConfig 的 GetCurrentAP() 读取
/// 不持有任何 AP 数值，Config 负责正常/战斗模式的切换
/// </summary>
[RequireComponent(typeof(UnitMovement))]
public class TurnBasedUnit : MonoBehaviour
{
    // ============ 配置 ============

    [Header("单位属性")]
    [SerializeField] private TurnFaction faction = TurnFaction.Player;

    [Header("可选引用")]
    [SerializeField] private GameObject selectionIndicator;

    // ============ 运行时状态 ============

    private int remainingActionPoints = 0;
    // 本回合已消耗的 AP 累计。剩余 AP 恒满足 remaining = Clamp(GetMaxAP() - _apSpentThisTurn)，
    // 使 AP 上限中途改变（下蹲/站起、战斗模式切换）时能可升可降地正确重算（见 RefreshAP）。
    private int _apSpentThisTurn = 0;
    private bool isMyTurn = false;

    // 组件缓存
    private UnitMovement      unitMovement;
    private ITurnControllable _controller;
    private IDamageable       _damageable;

    // 战斗模式状态
    private bool isInCombatMode = false;
    public bool IsInCombatMode => isInCombatMode;

    // 反应窗口（QTE）状态
    // 敌人射击前开放，玩家行动一次或超时后关闭
    private bool isReactionWindowOpen = false;
    public bool IsReactionWindowOpen => isReactionWindowOpen;

    private Coroutine _reactionTimeoutCoroutine;

    // 反应窗口事件（PlayerInputController / UI 监听）
    public event System.Action           OnReactionWindowOpened;
    public event System.Action           OnReactionWindowClosed;
    /// <summary>每帧广播 QTE 剩余秒数（UI 用于显示倒计时进度条）</summary>
    public event System.Action<float>    OnReactionWindowTick;

    // ============ 公开属性 ============

    public TurnFaction Faction => faction;
    public bool IsMyTurn => isMyTurn;
    public int RemainingActionPoints => remainingActionPoints;
    public bool CanAct => isMyTurn && remainingActionPoints > 0;
    public bool HasActedThisTurn => remainingActionPoints <= 0;
    public bool HasEnoughMovementPoints(int required) => remainingActionPoints >= required;

    // ============ 事件 ============

    public event System.Action OnMyTurnStart;
    public event System.Action OnMyTurnEnd;
    public event System.Action OnActionStart;
    public event System.Action OnActionEnd;

    /// <summary>
    /// 本单位消耗 AP 时触发，参数为本次实际消耗量
    /// MissionManager 订阅此事件以追踪 APConsumed 条件
    /// </summary>
    public event System.Action<int> OnAPConsumed;

    /// <summary>
    /// 玩家单位在战斗模式下 AP 归零时触发。
    /// PlayerInputController 订阅此事件以结束回合，
    /// 避免在 ConsumeAP 内部直接调用 TurnSystem。
    /// </summary>
    public event System.Action OnAPExhausted;

    // ============ 初始化 ============

    void Awake()
    {
        unitMovement = GetComponent<UnitMovement>();
        _controller  = GetComponent<ITurnControllable>();
        _damageable  = GetComponent<IDamageable>();

        if (unitMovement == null)
            Debug.LogError($"[{gameObject.name}] TurnBasedUnit requires UnitMovement!");
    }

    void Start()
    {
        // 向 CombatModeManager 注册并订阅事件
        if (CombatModeManager.Instance != null)
        {
            CombatModeManager.Instance.RegisterUnit(this);
            CombatModeManager.Instance.OnEnterCombatMode += OnEnterCombatMode;
            CombatModeManager.Instance.OnExitCombatMode  += OnExitCombatMode;

            // OpenReactionWindow 现在由 CombatModeManager.NotifyAttackLaunched 直接调用（含 qteDuration）
            // 不再通过 OnEnemyAttackLaunched 事件订阅，避免双重触发
        }

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnStart    += HandleTurnStart;
            TurnSystem.Instance.OnTurnEnd      += HandleTurnEnd;
            TurnSystem.Instance.OnFactionChanged += HandleFactionChanged;

            if (TurnSystem.Instance.IsCurrentFaction(faction))
            {
                isMyTurn = true;
                ResetTurnAP();

                if (faction == TurnFaction.Player && selectionIndicator != null)
                    selectionIndicator.SetActive(true);

                OnMyTurnStart?.Invoke();
            }
        }
        else
        {
            Debug.LogError($"[{gameObject.name}] TurnSystem not found!");
        }
    }

    void OnDestroy()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnStart      -= HandleTurnStart;
            TurnSystem.Instance.OnTurnEnd        -= HandleTurnEnd;
            TurnSystem.Instance.OnFactionChanged -= HandleFactionChanged;
        }

        if (unitMovement != null)
            unitMovement.OnMoveComplete -= HandleMoveComplete;

        if (CombatModeManager.Instance != null)
        {
            CombatModeManager.Instance.OnEnterCombatMode -= OnEnterCombatMode;
            CombatModeManager.Instance.OnExitCombatMode  -= OnExitCombatMode;

            // OpenReactionWindow 已改为直接调用，不再通过事件订阅
        }
    }

    // ============ Config AP 读取 ============

    /// <summary>
    /// 从 Config 获取当前模式的 AP 上限
    /// Config 内部管理正常/战斗模式的切换
    /// </summary>
    private int GetMaxAP()
    {
        if (_controller != null)
            return _controller.GetCurrentAP();

        Debug.LogWarning($"[{gameObject.name}] No ITurnControllable found, defaulting AP to 1");
        return 1;
    }

    // ============ 回合事件处理 ============

    private void HandleTurnStart(TurnData turnData)
    {
        if (turnData.currentFaction != faction) return;

        // 敌人已死亡（尸体状态），自动跳过回合
        if (faction == TurnFaction.Enemy && _damageable != null && !_damageable.IsAlive)
        {
            Debug.Log($"[{gameObject.name}] Dead, skipping turn");
            if (TurnSystem.Instance != null)
                TurnSystem.Instance.EndCurrentTurn();
            return;
        }

        isMyTurn = true;
        ResetTurnAP();

        // 玩家回合开始时强制关闭 QTE 窗口
        // 避免敌人回合的 QTE 残留导致玩家输入走 QTE 路径（2格限制/跳过AP检查）
        if (faction == TurnFaction.Player)
            CloseReactionWindow();

        if (faction == TurnFaction.Player && selectionIndicator != null)
            selectionIndicator.SetActive(true);

        Debug.Log($"[{gameObject.name}] Turn start | AP:{remainingActionPoints}");
        OnMyTurnStart?.Invoke();
    }

    private void HandleTurnEnd(TurnData turnData)
    {
        if (turnData.currentFaction != faction) return;

        isMyTurn = false;

        if (selectionIndicator != null)
            selectionIndicator.SetActive(false);

        Debug.Log($"[{gameObject.name}] Turn end");
        OnMyTurnEnd?.Invoke();
    }

    private void HandleFactionChanged(TurnFaction newFaction, int turnNumber)
    {
        bool wasMyTurn = isMyTurn;
        isMyTurn = (newFaction == faction);

        if (!wasMyTurn && isMyTurn)
        {
            // 敌人已死亡，自动跳过
            if (faction == TurnFaction.Enemy && _damageable != null && !_damageable.IsAlive)
            {
                Debug.Log($"[{gameObject.name}] Dead, skipping turn via FactionChanged");
                isMyTurn = false;
                if (TurnSystem.Instance != null)
                    TurnSystem.Instance.EndCurrentTurn();
                return;
            }

            ResetTurnAP();

            // 玩家回合开始时强制关闭 QTE 窗口（同 HandleTurnStart）
            if (faction == TurnFaction.Player)
                CloseReactionWindow();

            if (faction == TurnFaction.Player && selectionIndicator != null)
                selectionIndicator.SetActive(true);

            Debug.Log($"[{gameObject.name}] Turn start via FactionChanged | AP:{remainingActionPoints}");
            OnMyTurnStart?.Invoke();
        }
        else if (wasMyTurn && !isMyTurn)
        {
            if (selectionIndicator != null)
                selectionIndicator.SetActive(false);

            OnMyTurnEnd?.Invoke();
            // QTE 不在此触发——敌人回合刚开始时敌人还没行动
            // QTE 在 MoveExecutor / BulletProjectile.Fire 里触发（敌人真正行动时）
        }
    }

    private void HandleMoveComplete()
    {
        // Debug.Log($"[{gameObject.name}] Move complete | AP remaining:{remainingActionPoints}");
    }

    // ============ 反应窗口 ============

    /// <summary>
    /// 敌人攻击前调用，开放 QTE 反应窗口。
    /// 窗口持续 duration 秒（EnemyConfig.qteWindowDuration），玩家可移动或交互来躲避。
    /// 没有 AP 时无法反应，窗口不会开放。
    /// </summary>
    public void OpenReactionWindow(Vector3 bulletDirection, float duration = 3f)
    {
        // QTE 移动是负节奏的免费行动（IsMyTurn=false 时移动不消耗 AP）
        // 不检查 remainingActionPoints，让玩家始终能在敌人回合前抢先移动一格
        isReactionWindowOpen = true;
        Debug.Log($"[{gameObject.name}] QTE opened | AP:{remainingActionPoints} | duration:{duration}s");
        OnReactionWindowOpened?.Invoke();

        // 启动自动超时：如果玩家在 duration 秒内没有行动，窗口自动关闭
        if (_reactionTimeoutCoroutine != null)
            StopCoroutine(_reactionTimeoutCoroutine);
        _reactionTimeoutCoroutine = StartCoroutine(ReactionWindowTimeoutRoutine(duration));
    }

    private IEnumerator ReactionWindowTimeoutRoutine(float duration)
    {
        float remaining = duration;
        while (remaining > 0f && isReactionWindowOpen)
        {
            remaining -= Time.deltaTime;
            OnReactionWindowTick?.Invoke(Mathf.Max(0f, remaining));
            yield return null;
        }
        CloseReactionWindow();
    }

    /// <summary>
    /// 玩家行动（移动/交互）后手动关闭反应窗口，或超时后自动关闭。
    /// 由 PlayerInputController 在移动/交互完成后调用。
    /// </summary>
    public void CloseReactionWindow()
    {
        if (!isReactionWindowOpen) return;

        isReactionWindowOpen = false;

        if (_reactionTimeoutCoroutine != null)
        {
            StopCoroutine(_reactionTimeoutCoroutine);
            _reactionTimeoutCoroutine = null;
        }

        Debug.Log($"[{gameObject.name}] QTE closed");
        OnReactionWindowClosed?.Invoke();
    }

    private void OnEnterCombatMode()
    {
        isInCombatMode = true;
        Debug.Log($"[{gameObject.name}] Entered combat mode");
    }

    private void OnExitCombatMode()
    {
        isInCombatMode = false;
        Debug.Log($"[{gameObject.name}] Exited combat mode");
    }

    /// <summary>
    /// 战斗模式切换时立即刷新当前回合剩余 AP。
    /// 现统一走 RefreshAP（按 上限−已用 重算），进入战斗时收紧、退出战斗时按已用返还。
    /// 由 CombatModeManager 在 Enter/ExitCombatMode 更新完所有 Config 后调用。
    /// </summary>
    public void RefreshCombatAP() => RefreshAP();

    /// <summary>
    /// 按最新 AP 上限重算本回合剩余 AP：remaining = Clamp(GetMaxAP() - 本回合已用, 0, GetMaxAP())。
    /// 可升可降——用于下蹲/站起、战斗模式切换等中途改变 AP 上限的场景。
    /// 例：站立上限5、蹲下走3格后站起 → 5-3=2。因扣除“已用”，无法靠反复蹲/站刷出额外步数。
    /// 仅在本单位回合内生效；非当前回合的单位在其回合开始时自动读取新上限。
    /// </summary>
    public void RefreshAP()
    {
        if (!isMyTurn) return;
        int newMax = GetMaxAP();
        remainingActionPoints = Mathf.Clamp(newMax - _apSpentThisTurn, 0, newMax);
    }

    // ============ AP 消耗接口 ============

    /// <summary>
    /// 消耗指定数量 AP
    /// 仅对玩家单位：战斗模式下 AP 归零后自动结束回合，触发快速切换。
    /// 敌人单位不走此路径——EnemyAIController.EndTurn() 会在所有 Enemy 完成行动后
    /// 统一调用 TurnSystem.EndCurrentTurn()，确保不会在其他 Enemy 的移动协程
    /// 尚未结束时提前切换回合（导致 AI 在玩家回合继续移动的视觉 Bug）。
    /// </summary>
    public void ConsumeAP(int points = 1)
    {
        // 计算实际消耗量（不超过当前剩余），用于 OnAPConsumed 事件上报精确值
        int actual = Mathf.Min(points, remainingActionPoints);
        remainingActionPoints = Mathf.Max(0, remainingActionPoints - points);
        _apSpentThisTurn += actual; // 累计已用，供 RefreshAP 在上限变化时按“上限−已用”重算
        if (actual > 0) OnAPConsumed?.Invoke(actual);
        // Debug.Log($"[{gameObject.name}] ConsumeAP:{points} | Remaining:{remainingActionPoints} | CombatMode:{isInCombatMode}");

        // 战斗模式下 AP 耗尽时通知外部（仅玩家）。
        // 由 PlayerInputController 订阅 OnAPExhausted 来结束回合，
        // 避免在此直接耦合 TurnSystem。
        // Enemy 单位由 EnemyAIController.EndTurn() → CombatModeManager 统一处理。
        if (remainingActionPoints <= 0 && isMyTurn && isInCombatMode
            && faction == TurnFaction.Player)
        {
            Debug.Log($"[{gameObject.name}] Combat mode: AP exhausted");
            OnAPExhausted?.Invoke();
        }
    }

    /// <summary>消耗所有剩余 AP</summary>
    public void ConsumeAllAP()
    {
        ConsumeAP(remainingActionPoints);
    }

    /// <summary>直接设置剩余 AP（buff/debuff 用）</summary>
    public void SetAP(int value)
    {
        int max = GetMaxAP();
        remainingActionPoints = Mathf.Clamp(value, 0, max);
        _apSpentThisTurn = max - remainingActionPoints; // 维持 remaining = max - spent 不变式
    }

    /// <summary>增加 AP（肾上腺素等道具用，不超过当回合上限）</summary>
    public void AddAP(int points)
    {
        int before = remainingActionPoints;
        remainingActionPoints = Mathf.Min(remainingActionPoints + points, GetMaxAP());
        // 反映到已用累计（负向），使 RefreshAP 重算时保留这部分加成
        _apSpentThisTurn = Mathf.Max(0, _apSpentThisTurn - (remainingActionPoints - before));
    }

    // ============ 行动接口 ============

    public bool StartAction()
    {
        if (!CanAct) return false;
        OnActionStart?.Invoke();
        return true;
    }

    public void EndAction()
    {
        if (!isMyTurn) return;
        OnActionEnd?.Invoke();
    }

    public void SkipAction()
    {
        _apSpentThisTurn += remainingActionPoints; // 剩余全部计为已用，维持不变式
        remainingActionPoints = 0;
    }

    public void ResetActionState() => ResetTurnAP();

    /// <summary>回合开始（或强制重置）：剩余 AP 置为上限，已用清零。</summary>
    private void ResetTurnAP()
    {
        remainingActionPoints = GetMaxAP();
        _apSpentThisTurn = 0;
    }

    // ============ 兼容旧接口 ============

    [System.Obsolete("Use ConsumeAP(int points) instead")]
    public void ConsumeActionPoint(int points = 1) => ConsumeAP(points);
}