using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 回合阵营枚举
/// 定义游戏中的不同阵营
/// </summary>
public enum TurnFaction
{
    Player,    // 玩家阵营
    Enemy,     // 敌人阵营
    Neutral    // 中立阵营（可选，如NPC）
}

/// <summary>
/// 回合状态枚举
/// 定义回合的不同阶段
/// </summary>
public enum TurnPhase
{
    TurnStart,      // 回合开始阶段
    Action,         // 行动阶段（移动、攻击等）
    TurnEnd,        // 回合结束阶段
    WaitingForInput // 等待输入（玩家回合时）
}

/// <summary>
/// 回合数据类
/// 存储当前回合的信息
/// 注意：这里的turnNumber是全局回合数，每个阵营有自己的独立计数
/// </summary>
public class TurnData
{
    public int globalTurnNumber;      // 全局回合数（从1开始，所有阵营共享）
    public TurnFaction currentFaction; // 当前行动的阵营
    public TurnPhase currentPhase;    // 当前阶段
    public float turnStartTime;       // 回合开始的时间戳

    public TurnData(int number, TurnFaction faction)
    {
        globalTurnNumber = number;
        currentFaction = faction;
        currentPhase = TurnPhase.TurnStart;
        turnStartTime = Time.time;
    }
}

/// <summary>
/// 回合系统 - 核心回合管理（开放世界版本）
/// 职责：
/// 1. 管理回合流转（玩家回合 ↔ 敌人回合，持续进行）
/// 2. 管理回合阶段（开始 → 行动 → 结束）
/// 3. 为每个阵营单独计数回合数
/// 4. 通过事件通知其他系统回合变化
/// 设计理念：
/// - 不存在"战斗开始/结束"的概念
/// - 游戏持续运行，回合永不停止（除非手动暂停）
/// - 每个阵营独立计数回合数（用于事件计算等）
/// 特点：
/// - 单例模式，全局唯一
/// - 事件驱动，低耦合
/// - 只负责回合逻辑，不包含具体的游戏规则
/// </summary>
public class TurnSystem : MonoBehaviour
{
    public static TurnSystem Instance { get; private set; }

    // ============ 配置参数 ============
    
    [Header("回合配置")]
    [SerializeField] private TurnFaction startingFaction = TurnFaction.Player;  // 起始阵营
    [SerializeField] private bool autoStartOnAwake = true;                      // 游戏启动时自动开始回合系统
    
    [Header("回合顺序")]
    [SerializeField] private List<TurnFaction> turnOrder = new List<TurnFaction> 
    { 
        TurnFaction.Player, 
        TurnFaction.Enemy 
    };  // 回合顺序列表

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;  // 是否启用调试日志

    // ============ 运行时状态 ============
    
    private TurnData currentTurn;           // 当前回合数据
    private int currentFactionIndex = 0;    // 当前阵营在turnOrder中的索引
    private bool isSystemActive = false;    // 回合系统是否激活
    private bool isProcessingTurn = false;  // 是否正在处理回合切换

    // 每个阵营的独立回合计数器
    private Dictionary<TurnFaction, int> factionTurnCounters;

    // ============ 公开属性（只读）============
    
    /// <summary>当前回合数据</summary>
    public TurnData CurrentTurn => currentTurn;
    
    /// <summary>当前是哪个阵营的回合</summary>
    public TurnFaction CurrentFaction => currentTurn?.currentFaction ?? TurnFaction.Player;
    
    /// <summary>当前回合阶段</summary>
    public TurnPhase CurrentPhase => currentTurn?.currentPhase ?? TurnPhase.TurnStart;
    
    /// <summary>全局回合数（所有阵营共享）</summary>
    public int GlobalTurnNumber => currentTurn?.globalTurnNumber ?? 0;
    
    /// <summary>回合系统是否激活</summary>
    public bool IsSystemActive => isSystemActive;
    
    /// <summary>是否正在处理回合切换</summary>
    public bool IsProcessingTurn => isProcessingTurn;

    // ============ 事件系统 ============
    
    /// <summary>系统启动事件：回合系统开始运行时触发</summary>
    public event System.Action OnSystemStart;
    
    /// <summary>系统停止事件：回合系统停止时触发</summary>
    public event System.Action OnSystemStop;
    
    /// <summary>回合开始事件：新回合开始时触发</summary>
    public event System.Action<TurnData> OnTurnStart;
    
    /// <summary>回合结束事件：回合结束时触发</summary>
    public event System.Action<TurnData> OnTurnEnd;
    
    /// <summary>阵营变更事件：切换到新阵营时触发（参数：阵营, 该阵营的回合数）</summary>
    public event System.Action<TurnFaction, int> OnFactionChanged;
    
    /// <summary>阶段变更事件：回合阶段改变时触发</summary>
    public event System.Action<TurnPhase> OnPhaseChanged;

    // ============ 初始化 ============

    void Awake()
    {
        // 单例模式初始化
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 初始化阵营回合计数器
            factionTurnCounters = new Dictionary<TurnFaction, int>();
            foreach (TurnFaction faction in System.Enum.GetValues(typeof(TurnFaction)))
            {
                factionTurnCounters[faction] = 0;
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 验证回合顺序
        if (turnOrder == null || turnOrder.Count == 0)
        {
            Debug.LogError("Turn order is empty! Adding default order.");
            turnOrder = new List<TurnFaction> { TurnFaction.Player, TurnFaction.Enemy };
        }

        if (autoStartOnAwake)
        {
            StartSystem();
        }
    }

    // ============ 公开接口 - 系统控制 ============

    /// <summary>
    /// 启动回合系统
    /// </summary>
    public void StartSystem()
    {
        if (isSystemActive)
        {
            Debug.LogWarning("Turn system is already active!");
            return;
        }

        isSystemActive = true;
        currentFactionIndex = turnOrder.IndexOf(startingFaction);
        if (currentFactionIndex < 0) currentFactionIndex = 0;

        DebugLog("=== Turn System Started (Open World Mode) ===");
        OnSystemStart?.Invoke();

        // 开始第一个回合
        StartNewTurn();
    }

    /// <summary>
    /// 停止回合系统
    /// </summary>
    public void StopSystem()
    {
        if (!isSystemActive)
        {
            Debug.LogWarning("Turn system is not active!");
            return;
        }

        isSystemActive = false;
        DebugLog("=== Turn System Stopped ===");
        OnSystemStop?.Invoke();
    }

    /// <summary>
    /// 结束当前回合，切换到下一个阵营
    /// </summary>
    public void EndCurrentTurn()
    {
        if (!isSystemActive)
        {
            Debug.LogWarning("Cannot end turn: System is not active!");
            return;
        }

        if (isProcessingTurn)
        {
            Debug.LogWarning("Already processing turn change!");
            return;
        }

        isProcessingTurn = true;

        // 回合结束阶段
        SetPhase(TurnPhase.TurnEnd);
        DebugLog($"--- Turn End: Global#{currentTurn.globalTurnNumber}, {currentTurn.currentFaction}#{GetFactionTurnNumber(currentTurn.currentFaction)} ---");
        OnTurnEnd?.Invoke(currentTurn);

        // 切换到下一个阵营
        SwitchToNextFaction();

        isProcessingTurn = false;
    }

    /// <summary>
    /// 强制切换到指定阵营的回合
    /// </summary>
    public void ForceSwitchToFaction(TurnFaction faction)
    {
        if (!isSystemActive)
        {
            Debug.LogWarning("Cannot switch faction: System is not active!");
            return;
        }

        int index = turnOrder.IndexOf(faction);
        if (index < 0)
        {
            Debug.LogError($"Faction {faction} not found in turn order!");
            return;
        }

        currentFactionIndex = index;
        StartNewTurn();
    }

    // ============ 公开接口 - 查询 ============

    /// <summary>
    /// 检查是否是指定阵营的回合
    /// </summary>
    public bool IsCurrentFaction(TurnFaction faction)
    {
        return isSystemActive && CurrentFaction == faction;
    }

    /// <summary>
    /// 检查是否在行动阶段
    /// </summary>
    public bool IsActionPhase()
    {
        return isSystemActive && CurrentPhase == TurnPhase.Action;
    }

    /// <summary>
    /// 获取下一个将要行动的阵营
    /// </summary>
    public TurnFaction GetNextFaction()
    {
        int nextIndex = (currentFactionIndex + 1) % turnOrder.Count;
        return turnOrder[nextIndex];
    }

    /// <summary>
    /// 获取指定阵营已经经过的回合数
    /// </summary>
    public int GetFactionTurnNumber(TurnFaction faction)
    {
        return factionTurnCounters.ContainsKey(faction) ? factionTurnCounters[faction] : 0;
    }

    /// <summary>
    /// 获取所有阵营的回合计数
    /// </summary>
    public Dictionary<TurnFaction, int> GetAllFactionTurnNumbers()
    {
        return new Dictionary<TurnFaction, int>(factionTurnCounters);
    }

    // ============ 私有方法 - 回合管理 ============

    /// <summary>
    /// 开始新回合
    /// </summary>
    private void StartNewTurn()
    {
        TurnFaction faction = turnOrder[currentFactionIndex];
        
        // 该阵营的回合计数+1
        factionTurnCounters[faction]++;
        
        // 计算全局回合数
        int globalTurnNumber = currentTurn == null ? 1 : currentTurn.globalTurnNumber;
        if (currentFactionIndex == 0 && currentTurn != null)
        {
            globalTurnNumber++;
        }

        // 创建新回合数据
        currentTurn = new TurnData(globalTurnNumber, faction);

        int factionTurnNum = factionTurnCounters[faction];
        DebugLog($"=== Turn Start: Global#{globalTurnNumber}, {faction}#{factionTurnNum} ===");

        // 触发阵营变更事件
        OnFactionChanged?.Invoke(faction, factionTurnNum);

        // 进入回合开始阶段
        SetPhase(TurnPhase.TurnStart);
        OnTurnStart?.Invoke(currentTurn);

        // 根据阵营类型决定下一步
        if (faction == TurnFaction.Player)
        {
            SetPhase(TurnPhase.WaitingForInput);
        }
        else
        {
            SetPhase(TurnPhase.Action);
        }
    }

    /// <summary>
    /// 切换到下一个阵营
    /// </summary>
    private void SwitchToNextFaction()
    {
        currentFactionIndex = (currentFactionIndex + 1) % turnOrder.Count;
        StartNewTurn();
    }

    /// <summary>
    /// 设置回合阶段
    /// </summary>
    private void SetPhase(TurnPhase phase)
    {
        if (currentTurn != null)
        {
            currentTurn.currentPhase = phase;
            DebugLog($"Phase: {phase}");
            OnPhaseChanged?.Invoke(phase);
        }
    }

    /// <summary>
    /// 调试日志输出
    /// </summary>
    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[TurnSystem] {message}");
        }
    }

    // ============ 调试功能 ============

    void OnGUI()
    {
        if (!enableDebugLog || !isSystemActive) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 14;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        string info = $"[Turn System - Open World]\n" +
                     $"Global Turn: {GlobalTurnNumber}\n" +
                     $"Current: {CurrentFaction} #{GetFactionTurnNumber(CurrentFaction)}\n" +
                     $"Phase: {CurrentPhase}\n" +
                     $"Next: {GetNextFaction()}\n" +
                     $"---\n" +
                     $"Player Turns: {GetFactionTurnNumber(TurnFaction.Player)}\n" +
                     $"Enemy Turns: {GetFactionTurnNumber(TurnFaction.Enemy)}";

        GUI.Box(new Rect(10, 10, 250, 160), info, style);
    }
}