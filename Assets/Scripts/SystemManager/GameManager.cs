using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 游戏阶段数据类
/// 对应 game_phases.json 中的单个阶段配置
/// </summary>
[System.Serializable]
public class GamePhaseData
{
    public string id;                   // 阶段唯一标识，如 "Phase_01"
    public string displayName;          // 策划用可读名称
    public string missionPool;          // 该阶段对应的任务池资源名
    public float progressThreshold;    // PlanProgress 到达此值时推进到下一阶段
    public bool isEnding;              // 是否为终态（结局阶段）
    public GamePhaseOnEnter onEnter;   // 进入阶段时执行的指令
    public List<GamePhaseTransition> transitions; // 条件转移规则列表
}

/// <summary>
/// 进入阶段时的指令集
/// </summary>
[System.Serializable]
public class GamePhaseOnEnter
{
    public string dialogue;    // 对话键值，传给 DialogueManager（待实现）
    public string music;       // 背景音乐键值（待实现）
    public string cutscene;   // 过场动画键值（待实现）
}

/// <summary>
/// 阶段转移规则
/// 从上到下匹配，第一个满足条件的 trigger 触发跳转
/// 与 BTRunner Selector 逻辑相同
/// </summary>
[System.Serializable]
public class GamePhaseTransition
{
    public string to;          // 目标阶段 ID
    public string trigger;     // 触发条件（missionSuccess / missionFailed / planProgress100）
    public float value;        // 部分 trigger 的附加参数（如回合上限数值）
}

/// <summary>
/// JSON 根结构
/// </summary>
[System.Serializable]
public class GamePhaseConfig
{
    public string initialPhase;             // 初始阶段 ID
    public List<GamePhaseData> phases;      // 所有阶段数据列表
}

/// <summary>
/// GameManager — 游戏阶段状态机
/// 职责：
/// 1. 加载并持有所有阶段配置（game_phases.json）
/// 2. 维护当前所处阶段
/// 3. 监听触发条件（missionSuccess / missionFailed / planProgress）
/// 4. 执行阶段转移并广播 OnPhaseChanged 事件
/// 5. 进入新阶段时执行 onEnter 指令（通知 MissionManager、触发对话等）
/// 
/// 设计原则：
/// - 单例模式，全局唯一，DontDestroyOnLoad
/// - 阶段定义完全外置于 JSON，代码不硬编码任何剧情逻辑
/// - 事件驱动，低耦合，其他系统订阅 OnPhaseChanged 响应阶段变化
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ============ Inspector 配置 ============

    [Header("阶段配置文件")]
    [SerializeField] private TextAsset phasesJsonFile;  // 在 Inspector 中拖入 game_phases.json

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showDebugGUI = true;

    // ============ 运行时状态 ============

    private GamePhaseConfig config;                         // 解析后的完整配置
    private Dictionary<string, GamePhaseData> phaseDict;   // 阶段 ID → 数据，O(1) 查找
    private GamePhaseData currentPhase;                     // 当前阶段数据
    private bool isInitialized = false;                     // 是否完成初始化

    // ============ 公开属性（只读）============

    /// <summary>当前阶段 ID</summary>
    public string CurrentPhaseId => currentPhase?.id ?? "None";

    /// <summary>当前阶段可读名称</summary>
    public string CurrentPhaseName => currentPhase?.displayName ?? "未初始化";

    /// <summary>当前阶段是否为结局终态</summary>
    public bool IsEnding => currentPhase?.isEnding ?? false;

    /// <summary>GameManager 是否完成初始化</summary>
    public bool IsInitialized => isInitialized;

    // ============ 事件系统 ============

    /// <summary>
    /// 阶段切换事件
    /// 参数：(旧阶段 ID, 新阶段数据)
    /// MissionManager、UI、相机等系统订阅此事件响应阶段变化
    /// </summary>
    public event Action<string, GamePhaseData> OnPhaseChanged;

    /// <summary>
    /// 系统初始化完成事件
    /// 其他系统在此之后才能安全查询 GameManager
    /// </summary>
    public event Action OnInitialized;

    // ============ Unity 生命周期 ============

    void Awake()
    {
        // 单例初始化
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeFromJson();
    }

    // ============ 初始化 ============

    /// <summary>
    /// 从 Inspector 绑定的 JSON 文件解析阶段配置
    /// </summary>
    private void InitializeFromJson()
    {
        if (phasesJsonFile == null)
        {
            Debug.LogError("[GameManager] phasesJsonFile 未绑定！请在 Inspector 中拖入 game_phases.json");
            return;
        }

        // 解析 JSON
        config = JsonUtility.FromJson<GamePhaseConfig>(phasesJsonFile.text);

        if (config == null || config.phases == null || config.phases.Count == 0)
        {
            Debug.LogError("[GameManager] JSON 解析失败或 phases 列表为空！");
            return;
        }

        // 建立 ID → 数据字典，方便 O(1) 查找
        phaseDict = new Dictionary<string, GamePhaseData>();
        foreach (var phase in config.phases)
        {
            if (phaseDict.ContainsKey(phase.id))
            {
                Debug.LogWarning($"[GameManager] 发现重复阶段 ID：{phase.id}，后者将覆盖前者");
            }
            phaseDict[phase.id] = phase;
        }

        DebugLog($"阶段配置加载完成，共 {config.phases.Count} 个阶段，初始阶段：{config.initialPhase}");

        // 进入初始阶段
        isInitialized = true;
        OnInitialized?.Invoke();

        TransitionTo(config.initialPhase);
    }

    // ============ 核心接口 — 阶段控制 ============

    /// <summary>
    /// 切换到指定阶段
    /// 由内部条件判断调用，也可由外部系统强制调用（演出/调试用）
    /// </summary>
    public void TransitionTo(string phaseId)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("[GameManager] 尚未初始化，无法切换阶段");
            return;
        }

        if (!phaseDict.TryGetValue(phaseId, out GamePhaseData targetPhase))
        {
            Debug.LogError($"[GameManager] 找不到阶段 ID：{phaseId}，请检查 JSON 配置");
            return;
        }

        // 当前已是终态，不再接受转移
        if (currentPhase != null && currentPhase.isEnding)
        {
            DebugLog($"当前阶段 [{currentPhase.id}] 为终态（结局），不再接受阶段转移");
            return;
        }

        string oldPhaseId = currentPhase?.id ?? "None";
        currentPhase = targetPhase;

        DebugLog($"阶段切换：{oldPhaseId} → {currentPhase.id}（{currentPhase.displayName}）");

        // 广播阶段变化事件，MissionManager 等系统在此响应
        OnPhaseChanged?.Invoke(oldPhaseId, currentPhase);

        // 执行 onEnter 指令
        ExecuteOnEnter(currentPhase.onEnter);
    }

    /// <summary>
    /// 接收触发条件，检查当前阶段的 transitions 列表
    /// 从上到下匹配，第一个满足的 trigger 执行跳转
    /// </summary>
    /// <param name="trigger">触发条件字符串，如 "missionSuccess" / "missionFailed" / "planProgress100"</param>
    public void ReceiveTrigger(string trigger)
    {
        if (currentPhase == null || currentPhase.isEnding)
        {
            DebugLog($"收到 trigger [{trigger}]，但当前无有效阶段或已是终态，忽略");
            return;
        }

        if (currentPhase.transitions == null || currentPhase.transitions.Count == 0)
        {
            DebugLog($"当前阶段 [{currentPhase.id}] 无转移规则，忽略 trigger [{trigger}]");
            return;
        }

        // 从上到下匹配，第一个命中的规则触发跳转
        foreach (var transition in currentPhase.transitions)
        {
            if (transition.trigger == trigger)
            {
                DebugLog($"trigger [{trigger}] 命中转移规则 → {transition.to}");
                TransitionTo(transition.to);
                return;
            }
        }

        DebugLog($"trigger [{trigger}] 在阶段 [{currentPhase.id}] 中无匹配规则");
    }

    // ============ 便捷接口 — 常用触发条件 ============

    /// <summary>
    /// 通知 GameManager：当前任务成功
    /// 由 MissionManager 在任务结算后调用
    /// </summary>
    public void NotifyMissionSuccess()
    {
        ReceiveTrigger("missionSuccess");
    }

    /// <summary>
    /// 通知 GameManager：当前任务失败
    /// 由 MissionManager 在任务结算后调用
    /// </summary>
    public void NotifyMissionFailed()
    {
        ReceiveTrigger("missionFailed");
    }

    /// <summary>
    /// 通知 GameManager：PlanProgress 已到达 100%
    /// 由 MissionManager 在进度更新时调用
    /// </summary>
    public void NotifyPlanComplete()
    {
        ReceiveTrigger("planProgress100");
    }

    /// <summary>
    /// 通知 GameManager：当前任务池所有任务均已结算（含串行后续任务）
    /// 由 MissionManager.CheckAllPoolMissionsSettled() 在任务链全部完成后调用。
    /// 相比 missionSuccess（每个子任务完成都触发），此触发器只在整条链结束时触发一次，
    /// 适用于需要"全部任务做完才进入下阶段"的场景。
    /// </summary>
    public void NotifyAllMissionsSettled()
    {
        ReceiveTrigger("allMissionsSettled");
    }

    /// <summary>
    /// 通知 GameManager：玩家触发了逃脱结局
    /// 由逃生门交互触发
    /// </summary>
    public void NotifyPlayerEscaped()
    {
        ReceiveTrigger("playerEscaped");
    }

    /// <summary>
    /// 通知 GameManager：PlanProgress 达到当前阶段的 progressThreshold
    /// 由 MissionManager.AddProgress 在进度首次越过阈值时调用（每阶段只触发一次）
    /// </summary>
    public void NotifyProgressThreshold()
    {
        ReceiveTrigger("planProgressThreshold");
    }

    /// <summary>
    /// 通知 GameManager：玩家完成了清场（所有敌人被击败）
    /// 由 EnemyAIController 在最后一个敌人死亡时调用
    /// </summary>
    public void NotifyAllEnemiesDefeated()
    {
        ReceiveTrigger("allEnemiesDefeated");
    }

    // ============ 查询接口 ============

    /// <summary>
    /// 获取当前阶段的任务池名称，供 MissionManager 加载
    /// </summary>
    public string GetCurrentMissionPool()
    {
        return currentPhase?.missionPool ?? "";
    }

    /// <summary>
    /// 获取当前阶段数据（只读）
    /// </summary>
    public GamePhaseData GetCurrentPhaseData()
    {
        return currentPhase;
    }

    /// <summary>
    /// 检查指定阶段 ID 是否存在于配置中
    /// </summary>
    public bool PhaseExists(string phaseId)
    {
        return phaseDict != null && phaseDict.ContainsKey(phaseId);
    }

    // ============ 私有方法 ============

    /// <summary>
    /// 执行进入阶段时的指令集（onEnter）
    /// 目前仅打印日志，后续接入 DialogueManager / AudioManager 时在此扩展
    /// </summary>
    private void ExecuteOnEnter(GamePhaseOnEnter onEnter)
    {
        if (onEnter == null) return;

        if (!string.IsNullOrEmpty(onEnter.dialogue))
        {
            DebugLog($"[onEnter] 触发对话：{onEnter.dialogue}（DialogueManager 待接入）");
            // TODO: DialogueManager.Instance.Play(onEnter.dialogue, context);
        }

        if (!string.IsNullOrEmpty(onEnter.music))
        {
            DebugLog($"[onEnter] 切换音乐：{onEnter.music}（AudioManager 待接入）");
            // TODO: AudioManager.Instance.PlayMusic(onEnter.music);
        }

        if (!string.IsNullOrEmpty(onEnter.cutscene))
        {
            DebugLog($"[onEnter] 触发过场：{onEnter.cutscene}（CutsceneManager 待接入）");
            // TODO: CutsceneManager.Instance.Play(onEnter.cutscene);
        }
    }

    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[GameManager] {message}");
        }
    }

    // ============ 调试 GUI ============

    void OnGUI()
    {
        if (!showDebugGUI || !isInitialized) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 13;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        string info = $"[GameManager]\n" +
                      $"阶段 ID：{CurrentPhaseId}\n" +
                      $"阶段名：{CurrentPhaseName}\n" +
                      $"是否终态：{IsEnding}\n" +
                      $"任务池：{GetCurrentMissionPool()}\n" +
                      $"---\n" +
                      $"手动触发（仅调试用）：\n" +
                      $"[G] missionSuccess\n" +
                      $"[H] missionFailed";

        float boxWidth = 260;
        float boxHeight = 180;
        GUI.Box(new Rect(Screen.width - boxWidth - 10, 10, boxWidth, boxHeight), info, style);
    }

    // 调试快捷键
    void Update()
    {
        if (!enableDebugLog) return;

        if (Input.GetKeyDown(KeyCode.G))
        {
            DebugLog("【调试】手动触发 missionSuccess");
            NotifyMissionSuccess();
        }
        if (Input.GetKeyDown(KeyCode.H))
        {
            DebugLog("【调试】手动触发 missionFailed");
            NotifyMissionFailed();
        }
    }
}