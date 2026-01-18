using UnityEngine;

/// <summary>
/// 回合制单位组件
/// 职责：
/// 1. 为单位添加回合制属性（阵营、行动状态等）
/// 2. 监听回合系统事件，自动管理单位状态
/// 3. 提供行动接口（开始行动、结束行动）
/// 特点：
/// - 与UnitMovement解耦，通过事件通信
/// - 自动订阅和取消订阅回合系统事件
/// - 可以独立存在，不依赖移动系统
/// </summary>
[RequireComponent(typeof(UnitMovement))]
public class TurnBasedUnit : MonoBehaviour
{
    // ============ 配置参数 ============
    
    [Header("单位属性")]
    [SerializeField] private TurnFaction faction = TurnFaction.Player;  // 单位所属阵营
    [SerializeField] private int actionPointsPerTurn = 5;               // 每回合行动点数（改为移动点数）
    [SerializeField] private bool useMovementPoints = true;             // 是否使用移动点数系统

    [Header("可选引用")]
    [SerializeField] private GameObject selectionIndicator;  // 选中指示器（可选）

    // ============ 运行时状态 ============
    
    private UnitMovement unitMovement;       // UnitMovement组件引用
    private bool hasActedThisTurn = false;   // 本回合是否已行动（已废弃，改用移动点数）
    private int remainingActionPoints = 0;   // 剩余行动点数（现在是剩余移动点数）
    private bool isMyTurn = false;           // 是否是该单位的回合

    // ============ 公开属性（只读）============
    
    /// <summary>单位阵营</summary>
    public TurnFaction Faction => faction;
    
    /// <summary>是否是该单位的回合</summary>
    public bool IsMyTurn => isMyTurn;
    
    /// <summary>本回合是否已行动（兼容旧版，实际使用移动点数）</summary>
    public bool HasActedThisTurn => remainingActionPoints <= 0;
    
    /// <summary>剩余行动点数（移动点数）</summary>
    public int RemainingActionPoints => remainingActionPoints;
    
    /// <summary>是否可以行动（还有移动点数）</summary>
    public bool CanAct => isMyTurn && remainingActionPoints > 0;

    // ============ 事件系统 ============
    
    /// <summary>单位回合开始事件：轮到该单位的阵营时触发</summary>
    public event System.Action OnMyTurnStart;
    
    /// <summary>单位回合结束事件：该单位的阵营回合结束时触发</summary>
    public event System.Action OnMyTurnEnd;
    
    /// <summary>单位行动开始事件：单位开始执行行动时触发</summary>
    public event System.Action OnActionStart;
    
    /// <summary>单位行动结束事件：单位完成行动时触发</summary>
    public event System.Action OnActionEnd;

    // ============ 初始化 ============

    void Awake()
    {
        unitMovement = GetComponent<UnitMovement>();
        
        if (unitMovement == null)
        {
            Debug.LogError($"TurnBasedUnit on {gameObject.name} requires UnitMovement component!");
        }
    }

    void Start()
    {
        Debug.Log($"[{gameObject.name}] TurnBasedUnit.Start() begins");
        
        // 订阅回合系统事件
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnStart += HandleTurnStart;
            TurnSystem.Instance.OnTurnEnd += HandleTurnEnd;
            TurnSystem.Instance.OnFactionChanged += HandleFactionChanged;
            
            Debug.Log($"[{gameObject.name}] Subscribed to TurnSystem events");
            
            // 立即检查当前回合状态
            if (TurnSystem.Instance.IsCurrentFaction(faction))
            {
                Debug.Log($"[{gameObject.name}] System already started, initializing for my turn");
                isMyTurn = true;
                hasActedThisTurn = false;
                
                // 初始化移动点数
                InitializeMovementPoints();
                
                if (faction == TurnFaction.Player && selectionIndicator != null)
                {
                    selectionIndicator.SetActive(true);
                }
                
                Debug.Log($"[{gameObject.name}] Invoking OnMyTurnStart event");
                OnMyTurnStart?.Invoke();
            }
            else
            {
                Debug.Log($"[{gameObject.name}] Not my turn, current faction: {TurnSystem.Instance.CurrentFaction}");
            }
        }
        else
        {
            Debug.LogError("TurnSystem not found! TurnBasedUnit requires TurnSystem in scene.");
        }

        // 订阅移动完成事件（自动结束行动）
        if (unitMovement != null)
        {
            unitMovement.OnMoveComplete += HandleMoveComplete;
            Debug.Log($"[{gameObject.name}] Subscribed to UnitMovement events");
        }

        // 隐藏选中指示器（如果不是自己的回合）
        if (selectionIndicator != null && !isMyTurn)
        {
            selectionIndicator.SetActive(false);
        }
        
        Debug.Log($"[{gameObject.name}] TurnBasedUnit.Start() complete - Faction: {faction}, IsMyTurn: {isMyTurn}, RemainingPoints: {remainingActionPoints}");
    }

    /// <summary>
    /// 初始化移动点数（独立方法，便于调试）
    /// </summary>
    private void InitializeMovementPoints()
    {
        if (useMovementPoints)
        {
            PlayerController playerCtrl = GetComponent<PlayerController>();
            if (playerCtrl != null)
            {
                remainingActionPoints = playerCtrl.MoveRange;
                Debug.Log($"[{gameObject.name}] ✓ Movement points from PlayerConfig: {remainingActionPoints}");
            }
            else
            {
                remainingActionPoints = actionPointsPerTurn;
                Debug.Log($"[{gameObject.name}] ⚠ Movement points from default (no PlayerController): {remainingActionPoints}");
            }
        }
        else
        {
            remainingActionPoints = actionPointsPerTurn;
            Debug.Log($"[{gameObject.name}] Movement points from actionPointsPerTurn: {remainingActionPoints}");
        }
    }

    void OnDestroy()
    {
        // 取消订阅，防止内存泄漏
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnStart -= HandleTurnStart;
            TurnSystem.Instance.OnTurnEnd -= HandleTurnEnd;
            TurnSystem.Instance.OnFactionChanged -= HandleFactionChanged;
        }

        if (unitMovement != null)
        {
            unitMovement.OnMoveComplete -= HandleMoveComplete;
        }
    }

    // ============ 回合系统事件处理 ============

    /// <summary>
    /// 处理回合开始
    /// </summary>
    private void HandleTurnStart(TurnData turnData)
    {
        Debug.Log($"[{gameObject.name}] HandleTurnStart called - TurnData faction: {turnData.currentFaction}, My faction: {faction}");
        
        if (turnData.currentFaction == faction)
        {
            isMyTurn = true;
            hasActedThisTurn = false;
            
            // 刷新移动点数
            InitializeMovementPoints();

            if (faction == TurnFaction.Player && selectionIndicator != null)
            {
                selectionIndicator.SetActive(true);
            }

            Debug.Log($"[{gameObject.name}] My turn started! Movement points: {remainingActionPoints}");
            OnMyTurnStart?.Invoke();
        }
    }

    /// <summary>
    /// 处理回合结束
    /// </summary>
    private void HandleTurnEnd(TurnData turnData)
    {
        if (turnData.currentFaction == faction)
        {
            isMyTurn = false;

            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(false);
            }

            Debug.Log($"[{gameObject.name}] My turn ended!");
            OnMyTurnEnd?.Invoke();
        }
    }

    /// <summary>
    /// 处理阵营变更
    /// </summary>
    private void HandleFactionChanged(TurnFaction newFaction, int turnNumber)
    {
        bool wasMyTurn = isMyTurn;
        isMyTurn = (newFaction == faction);
        
        Debug.Log($"[{gameObject.name}] Faction changed to {newFaction} - IsMyTurn: {isMyTurn}");
        
        // 如果从不是我的回合变成我的回合，触发OnMyTurnStart
        if (!wasMyTurn && isMyTurn)
        {
            hasActedThisTurn = false;
            remainingActionPoints = actionPointsPerTurn;
            
            if (faction == TurnFaction.Player && selectionIndicator != null)
            {
                selectionIndicator.SetActive(true);
            }
            
            Debug.Log($"[{gameObject.name}] My turn started via FactionChanged!");
            OnMyTurnStart?.Invoke();
        }
    }

    /// <summary>
    /// 处理移动完成
    /// </summary>
    private void HandleMoveComplete()
    {
        if (isMyTurn && useMovementPoints)
        {
            // 移动点数系统：移动1格消耗1点
            // 实际消耗在UnitMovement中计算路径长度
        }
    }

    // ============ 公开接口 - 行动管理 ============

    /// <summary>
    /// 开始行动
    /// </summary>
    public bool StartAction()
    {
        if (!CanAct)
        {
            Debug.LogWarning($"[{gameObject.name}] Cannot start action!");
            return false;
        }

        Debug.Log($"[{gameObject.name}] Action started!");
        OnActionStart?.Invoke();
        return true;
    }

    /// <summary>
    /// 结束行动
    /// </summary>
    public void EndAction()
    {
        if (!isMyTurn) return;

        hasActedThisTurn = true;
        Debug.Log($"[{gameObject.name}] Action ended!");
        OnActionEnd?.Invoke();
    }

    /// <summary>
    /// 消耗行动点
    /// </summary>
    public void ConsumeActionPoint(int points = 1)
    {
        remainingActionPoints = Mathf.Max(0, remainingActionPoints - points);
        Debug.Log($"[{gameObject.name}] Consumed {points} movement point(s). Remaining: {remainingActionPoints}");

        if (remainingActionPoints <= 0)
        {
            hasActedThisTurn = true;
            Debug.Log($"[{gameObject.name}] No movement points left");
        }
    }

    /// <summary>
    /// 检查是否有足够的移动点数
    /// </summary>
    public bool HasEnoughMovementPoints(int required)
    {
        return remainingActionPoints >= required;
    }

    /// <summary>
    /// 重置行动状态
    /// </summary>
    public void ResetActionState()
    {
        hasActedThisTurn = false;
        remainingActionPoints = actionPointsPerTurn;
        Debug.Log($"[{gameObject.name}] Action state reset!");
    }

    /// <summary>
    /// 跳过行动
    /// </summary>
    public void SkipAction()
    {
        if (!isMyTurn) return;

        hasActedThisTurn = true;
        remainingActionPoints = 0;
        Debug.Log($"[{gameObject.name}] Action skipped!");
        OnActionEnd?.Invoke();
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        if (isMyTurn)
        {
            Gizmos.color = hasActedThisTurn ? Color.gray : Color.green;
        }
        else
        {
            Gizmos.color = new Color(1, 1, 1, 0.3f);
        }

        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.3f);

        Color factionColor = faction == TurnFaction.Player ? Color.blue : Color.red;
        Gizmos.color = factionColor;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.5f, 0.2f);
    }

    void OnGUI()
    {
        if (!Application.isPlaying || !isMyTurn) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.8f);
        if (screenPos.z > 0)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 16;
            style.normal.textColor = hasActedThisTurn ? Color.gray : Color.yellow;
            style.alignment = TextAnchor.MiddleCenter;

            GUI.Label(new Rect(screenPos.x - 25, Screen.height - screenPos.y - 10, 50, 20), 
                      $"AP:{remainingActionPoints}", style);
        }
    }
}