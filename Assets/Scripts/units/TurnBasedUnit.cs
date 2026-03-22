using UnityEngine;

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
    private bool isMyTurn = false;

    // 组件缓存
    private UnitMovement unitMovement;
    private PlayerController playerController;
    private EnemyAIController enemyController;

    // 战斗模式状态
    private bool isInCombatMode = false;
    public bool IsInCombatMode => isInCombatMode;

    // 反应窗口状态
    // 敌人射击时开放，玩家移动一次后关闭
    private bool isReactionWindowOpen = false;
    public bool IsReactionWindowOpen => isReactionWindowOpen;

    // 反应窗口事件（PlayerInputController 监听）
    public event System.Action OnReactionWindowOpened;
    public event System.Action OnReactionWindowClosed;

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

    // ============ 初始化 ============

    void Awake()
    {
        unitMovement     = GetComponent<UnitMovement>();
        playerController = GetComponent<PlayerController>();
        enemyController  = GetComponent<EnemyAIController>();

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

            // 只有玩家单位监听敌人攻击，开放反应窗口
            if (faction == TurnFaction.Player)
                CombatModeManager.Instance.OnEnemyAttackLaunched += OpenReactionWindow;
        }

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnStart    += HandleTurnStart;
            TurnSystem.Instance.OnTurnEnd      += HandleTurnEnd;
            TurnSystem.Instance.OnFactionChanged += HandleFactionChanged;

            if (TurnSystem.Instance.IsCurrentFaction(faction))
            {
                isMyTurn = true;
                remainingActionPoints = GetMaxAP();

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

            if (faction == TurnFaction.Player)
                CombatModeManager.Instance.OnEnemyAttackLaunched -= OpenReactionWindow;
        }
    }

    // ============ Config AP 读取 ============

    /// <summary>
    /// 从 Config 获取当前模式的 AP 上限
    /// Config 内部管理正常/战斗模式的切换
    /// </summary>
    private int GetMaxAP()
    {
        if (playerController != null && playerController.Config != null)
            return playerController.Config.GetCurrentAP();

        if (enemyController != null && enemyController.config != null)
            return enemyController.config.GetCurrentAP();

        Debug.LogWarning($"[{gameObject.name}] No config found, defaulting AP to 1");
        return 1;
    }

    // ============ 回合事件处理 ============

    private void HandleTurnStart(TurnData turnData)
    {
        if (turnData.currentFaction != faction) return;

        isMyTurn = true;
        remainingActionPoints = GetMaxAP();

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
            remainingActionPoints = GetMaxAP();

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
        }
    }

    private void HandleMoveComplete()
    {
        Debug.Log($"[{gameObject.name}] Move complete | AP remaining:{remainingActionPoints}");
    }

    // ============ 反应窗口 ============

    /// <summary>
    /// 敌人射击时调用，开放玩家反应移动窗口
    /// 即使不是玩家回合也可以移动一次
    /// </summary>
    private void OpenReactionWindow(Vector3 bulletDirection)
    {
        // 没有 AP 就无法反应
        if (remainingActionPoints <= 0)
        {
            Debug.Log($"[{gameObject.name}] No AP to react");
            return;
        }

        isReactionWindowOpen = true;
        Debug.Log($"[{gameObject.name}] Reaction window opened | AP:{remainingActionPoints}");
        OnReactionWindowOpened?.Invoke();
    }

    /// <summary>
    /// 玩家移动一次后关闭反应窗口
    /// 由 PlayerInputController 在移动完成后调用
    /// </summary>
    public void CloseReactionWindow()
    {
        if (!isReactionWindowOpen) return;

        isReactionWindowOpen = false;
        Debug.Log($"[{gameObject.name}] Reaction window closed");
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

    // ============ AP 消耗接口 ============

    /// <summary>
    /// 消耗指定数量 AP
    /// 战斗模式下 AP 归零后自动结束回合，触发快速切换
    /// </summary>
    public void ConsumeAP(int points = 1)
    {
        remainingActionPoints = Mathf.Max(0, remainingActionPoints - points);
        Debug.Log($"[{gameObject.name}] ConsumeAP:{points} | Remaining:{remainingActionPoints} | CombatMode:{isInCombatMode}");

        // 战斗模式下 AP 耗尽立即结束回合
        if (isInCombatMode && remainingActionPoints <= 0 && isMyTurn)
        {
            Debug.Log($"[{gameObject.name}] Combat mode: AP exhausted, auto ending turn");
            if (TurnSystem.Instance != null)
                TurnSystem.Instance.EndCurrentTurn();
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
        remainingActionPoints = Mathf.Clamp(value, 0, GetMaxAP());
        Debug.Log($"[{gameObject.name}] SetAP:{remainingActionPoints}");
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
        remainingActionPoints = 0;
    }

    public void ResetActionState()
    {
        remainingActionPoints = GetMaxAP();
    }

    // ============ 兼容旧接口 ============

    [System.Obsolete("Use ConsumeAP(int points) instead")]
    public void ConsumeActionPoint(int points = 1) => ConsumeAP(points);
}