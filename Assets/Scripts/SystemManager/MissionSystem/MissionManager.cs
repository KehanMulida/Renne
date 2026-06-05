using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MissionManager — 任务系统核心管理器
///
/// 职责：
/// 1. 订阅 GameManager.OnPhaseChanged，切换任务池
/// 2. 按优先级和前置条件激活 MissionPool 里的任务
/// 3. 每回合结束后检查激活任务的成功/失败条件
/// 4. 任务结算后累积 PlanProgress，通知 GameManager
/// 5. 向参与 Enemy 推送 MissionContext
///
/// 依赖：
/// - GameManager（阶段切换事件）
/// - TurnSystem（回合结束事件）
/// - ConditionEvaluator（条件求值）
/// - ZoneManager（区域查询）
/// - StoryManager（StoryFlag 查询）
/// - EnemyAIController（推送 MissionContext）
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    // ══════════════════════════════════════════════════════
    // Inspector 配置
    // ══════════════════════════════════════════════════════

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private bool showDebugGUI  = true;

    // ══════════════════════════════════════════════════════
    // 运行时状态
    // ══════════════════════════════════════════════════════

    private MissionPool currentPool;                           // 当前阶段的任务池
    private List<MissionRuntimeState> activeMissions = new(); // 当前激活的任务列表
    private float planProgress = 0f;                          // 敌人计划进度（0-100）

    // 本阶段内已结算（成功或失败）的任务 ID
    // 用途：① 防止已结算任务重复激活  ② CheckAllPoolMissionsSettled 判断整池是否完成
    private readonly HashSet<string> _settledThisPhase = new HashSet<string>();

    // 当前阶段的 progressThreshold 触发器是否已触发（每阶段只触发一次）
    private bool _progressThresholdFired = false;

    // APConsumed 订阅表：missionId → 订阅列表（unit + handler 配对，用于精确取消订阅）
    private readonly Dictionary<string, List<(TurnBasedUnit unit, System.Action<int> handler)>>
        _apSubscriptions = new Dictionary<string, List<(TurnBasedUnit, System.Action<int>)>>();

    // ── 公开属性 ─────────────────────────────────────────

    public float PlanProgress => planProgress;
    public IReadOnlyList<MissionRuntimeState> ActiveMissions => activeMissions;

    // ══════════════════════════════════════════════════════
    // 事件
    // ══════════════════════════════════════════════════════

    public event System.Action<MissionData, bool> OnMissionSettled; // (任务数据, 是否成功)
    public event System.Action<float>             OnProgressChanged; // 新进度值

    // ══════════════════════════════════════════════════════
    // 初始化
    // ══════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += OnPhaseChanged;

            // GameManager.Awake() 已触发初始阶段，MissionManager.Start() 会错过
            // 延迟一帧补初始化，确保所有 Enemy 的 Start() 都已执行完
            StartCoroutine(LateInitialize());
        }
        else
        {
            Debug.LogError("[MissionManager] GameManager 未找到！");
        }

        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnEnd += OnTurnEnd;
        else
            Debug.LogWarning("[MissionManager] TurnSystem 未找到");
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged -= OnPhaseChanged;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnEnd -= OnTurnEnd;
    }

    // ══════════════════════════════════════════════════════
    // 阶段切换响应
    // ══════════════════════════════════════════════════════

    private System.Collections.IEnumerator LateInitialize()
    {
        yield return null; // 等一帧，让所有 Enemy 的 Start() 执行完
        var currentPhase = GameManager.Instance?.GetCurrentPhaseData();
        if (currentPhase != null)
        {
            DebugLog($"补充初始化：当前阶段 [{currentPhase.id}]");
            OnPhaseChanged("None", currentPhase);
        }
    }

    private void OnPhaseChanged(string oldPhaseId, GamePhaseData newPhase)
    {
        DebugLog($"阶段切换：{oldPhaseId} → {newPhase.id}，加载任务池：{newPhase.missionPool}");

        // 阶段切换时重置已结算记录和阈值触发标记
        _settledThisPhase.Clear();
        _progressThresholdFired = false;

        // 非持久性任务：从所有 Enemy 队列中移除，并从激活列表清除
        // 持久性任务（persistAcrossPhases=true）保留在 Enemy 队列和 activeMissions 中
        var toRemove = activeMissions.Where(s => !s.data.persistAcrossPhases).ToList();
        foreach (var state in toRemove)
        {
            UnsubscribeAPEvents(state.data.missionId);
            PopMissionFromAllEnemies(state.data.missionId);
            activeMissions.Remove(state);
        }
        DebugLog($"Phase 清理：移除 {toRemove.Count} 个非持久任务，保留 {activeMissions.Count} 个持久任务");

        // 加载新任务池
        if (string.IsNullOrEmpty(newPhase.missionPool))
        {
            DebugLog($"阶段 [{newPhase.id}] 无任务池（结局阶段）");
            currentPool = null;
            return;
        }

        currentPool = Resources.Load<MissionPool>(newPhase.missionPool);
        if (currentPool == null)
        {
            Debug.LogError($"[MissionManager] 找不到 MissionPool 资产：{newPhase.missionPool}\n" +
                           $"请确认文件放在 Resources/ 目录下且文件名一致");
            return;
        }

        DebugLog($"任务池 [{currentPool.name}] 加载完成，共 {currentPool.missions?.Count ?? 0} 个任务");

        // 任务池为空也不崩溃，只是没有任务激活
        if (currentPool.missions == null || currentPool.missions.Count == 0)
        {
            DebugLog($"任务池 [{currentPool.name}] 没有配置任何任务");
            return;
        }

        // 激活满足前置条件的任务
        ActivateEligibleMissions();
    }

    // ══════════════════════════════════════════════════════
    // 任务激活
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 检查任务池，激活所有满足前置条件的任务（按 priority 从高到低处理）
    ///
    /// 串行控制（allowParallel=false）：
    ///   以"参与 Enemy"为粒度检查，而非全局检查。
    ///   若某任务的参与 Enemy 中，有任意一个正在执行更高优先级的任务
    ///   → 本任务推迟激活，避免"未来任务提前完成"。
    ///   不同 Enemy 的任务互不干扰：E01 忙于 P5 任务，不阻止 E03 激活 P3 任务。
    ///
    /// allowParallel=true（并行）：
    ///   只要前置条件满足就立即激活，不检查 Enemy 是否有其他任务在跑。
    /// </summary>
    private void ActivateEligibleMissions()
    {
        if (currentPool == null) return;

        var sorted = currentPool.GetSortedByPriority(); // 从高到低排列

        foreach (var entry in sorted)
        {
            if (entry.missionData == null) continue;

            // 已激活或已结算的任务不重复处理
            if (activeMissions.Any(m => m.data.missionId == entry.missionData.missionId))
                continue;
            if (_settledThisPhase.Contains(entry.missionData.missionId))
                continue;

            // ── 串行控制（仅对 allowParallel=false 的任务生效）────────────────
            // 检查：本任务的参与 Enemy 中，是否有人正在跑更高优先级的任务？
            // 有 → 等对方做完再激活（保证任务按优先级顺序执行，不跳过上一个任务）
            // 这里只比较共用 Enemy，与本任务无关的 Enemy 的任务状态不影响判断
            if (!entry.allowParallel && IsBlockedByHigherPriorityTask(entry))
            {
                DebugLog($"任务 [{entry.missionData.missionId}] P{entry.priority} " +
                         $"等待参与 Enemy 完成更高优先级任务（串行）");
                continue;
            }

            // 检查前置条件（AND 逻辑）
            if (!ConditionEvaluator.EvaluateAll(entry.preconditions))
            {
                DebugLog($"任务 [{entry.missionData.missionId}] 前置条件未满足，跳过");
                continue;
            }

            ActivateMission(entry);
        }
    }

    /// <summary>
    /// 判断候选任务是否被"共用 Enemy 上更高优先级的激活任务"阻塞。
    ///
    /// 逻辑：
    ///   遍历当前所有激活任务，找到 priority > candidate.priority 的任务；
    ///   若该高优先级任务的参与 Enemy 与候选任务有交集（或任一方为全员推送）→ 阻塞。
    ///
    /// 只被 allowParallel=false 的任务调用。
    /// </summary>
    private bool IsBlockedByHigherPriorityTask(MissionEntry candidate)
    {
        bool candidateIsGlobal = candidate.missionData.assignedEnemies == null ||
                                 candidate.missionData.assignedEnemies.Count == 0;

        foreach (var active in activeMissions)
        {
            if (!active.isActive) continue;
            if (active.priority <= candidate.priority) continue; // 只关心更高优先级

            bool activeIsGlobal = active.data.assignedEnemies == null ||
                                  active.data.assignedEnemies.Count == 0;

            // 任一方为"全员任务" → 必然覆盖所有 Enemy → 有交集
            if (candidateIsGlobal || activeIsGlobal)
                return true;

            // 两者都指定了 Enemy → 检查是否有同一个 Enemy
            foreach (var id in candidate.missionData.assignedEnemies)
                if (active.data.assignedEnemies.Contains(id)) return true;
        }
        return false;
    }

    private void ActivateMission(MissionEntry entry)
    {
        var state = new MissionRuntimeState(entry.missionData, entry.priority);
        // exclusive = allowParallel=false，记录在 state 中供日志和重复激活时使用
        state.exclusive = !entry.allowParallel;
        activeMissions.Add(state);

        DebugLog($"激活任务：[{entry.missionData.missionId}] {entry.missionData.displayName}" +
                 $"{(state.exclusive ? " [串行]" : " [并行]")}");

        // 播放开始演出
        if (!string.IsNullOrEmpty(entry.missionData.dialogueOnStart))
            DebugLog($"[onStart] 触发演出：{entry.missionData.dialogueOnStart}（DialogueManager 待接入）");

        // 向参与 Enemy 推送 MissionContext
        PushContextToEnemies(entry.missionData, entry.priority, state.exclusive);

        // 订阅 assignedEnemies 的 AP 消耗事件（用于 APConsumed 条件计数）
        SubscribeAPEvents(entry.missionData, state);

        // 设置 StoryFlag（任务已开始）
        if (StoryManager.Instance != null)
            StoryManager.Instance.SetFlag($"{entry.missionData.missionId}_started");
    }

    // ══════════════════════════════════════════════════════
    // 回合结束 — 条件检查
    // ══════════════════════════════════════════════════════

    private void OnTurnEnd(TurnData turnData)
    {
        // 只在敌人回合结束后检查（敌人行动完才结算）
        if (turnData.currentFaction != TurnFaction.Enemy) return;

        // ── 提前收集所有 Enemy，避免多个方法重复调用 FindObjectsOfType ──
        var allEnemies = FindObjectsOfType<EnemyAIController>();

        // ── 更新回合计数 ──────────────────────────────────────────────────
        // 有 targetZoneId 的任务：只有参与 Enemy 已抵达目标区域才开始计数
        //   → turnCount 含义变为"在目标区域内驻守的回合数"，从抵达那一刻起算
        // 无 targetZoneId 的任务：从任务激活起计数（原有行为）
        foreach (var state in activeMissions)
        {
            if (!state.isActive) continue;

            if (!string.IsNullOrEmpty(state.data.targetZoneId))
            {
                if (IsAnyAssignedEnemyInTargetZone(state, allEnemies))
                    state.turnCount++;
            }
            else
            {
                state.turnCount++;
            }
        }

        // 更新区域驻留计数器（供 TurnsInZone 条件使用）
        UpdateZoneTurnCounters(allEnemies);

        CheckAllMissions();

        // 检查是否有新任务可以激活（前置条件可能刚被满足）
        ActivateEligibleMissions();

        // 检查整个任务池是否全部结算（包括串行后续任务）
        // 必须在 ActivateEligibleMissions 之后调用，确保新激活的任务已进入 activeMissions
        CheckAllPoolMissionsSettled();
    }

    /// <summary>
    /// 检查指定任务的参与 Enemy 中，是否至少有一个当前在 targetZoneId 内。
    /// 用于 TurnCount 的"抵达后才开始计数"判断。
    /// </summary>
    private bool IsAnyAssignedEnemyInTargetZone(
        MissionRuntimeState state, EnemyAIController[] allEnemies)
    {
        if (ZoneManager.Instance == null) return false;

        bool pushAll = state.data.assignedEnemies == null ||
                       state.data.assignedEnemies.Count == 0;

        foreach (var enemy in allEnemies)
        {
            if (!enemy.IsAlive) continue;
            if (!pushAll && !state.data.assignedEnemies.Contains(enemy.EnemyId)) continue;

            var mov = enemy.GetComponent<UnitMovement>();
            if (mov == null) continue;

            if (ZoneManager.Instance.IsUnitInZone(
                    state.data.targetZoneId, mov.CurrentGridPosition, mov.CurrentFloor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 每敌人回合末调用：为所有激活任务更新区域驻留计数器。
    ///
    /// 逻辑：扫描每个激活任务的成功/失败条件中引用了 TurnsInZone 的 zoneId，
    /// 若该任务的参与 Enemy 中至少有一个当前在该区域内，则对应计数 +1。
    /// 结果写入 MissionRuntimeState.turnsInZone，供 ConditionEvaluator 读取。
    /// </summary>
    private void UpdateZoneTurnCounters(EnemyAIController[] allEnemies = null)
    {
        if (activeMissions.Count == 0) return;

        if (allEnemies == null)
            allEnemies = FindObjectsOfType<EnemyAIController>();

        foreach (var state in activeMissions)
        {
            if (!state.isActive) continue;

            // 收集该任务所有条件中引用了 TurnsInZone 的 zoneId
            var zonesToTrack = new System.Collections.Generic.HashSet<string>();
            CollectTurnsInZoneIds(state.data.successConditions, zonesToTrack);
            CollectTurnsInZoneIds(state.data.failConditions,    zonesToTrack);
            if (zonesToTrack.Count == 0) continue;

            bool pushAll = state.data.assignedEnemies == null ||
                           state.data.assignedEnemies.Count == 0;

            foreach (var zoneId in zonesToTrack)
            {
                bool anyInZone = false;
                foreach (var enemy in allEnemies)
                {
                    if (!enemy.IsAlive) continue;
                    if (!pushAll && !state.data.assignedEnemies.Contains(enemy.EnemyId))
                        continue;

                    var mov = enemy.GetComponent<UnitMovement>();
                    if (mov == null) continue;

                    if (ZoneManager.Instance != null &&
                        ZoneManager.Instance.IsUnitInZone(
                            zoneId, mov.CurrentGridPosition, mov.CurrentFloor))
                    {
                        anyInZone = true;
                        break;
                    }
                }

                if (anyInZone)
                {
                    if (!state.turnsInZone.ContainsKey(zoneId))
                        state.turnsInZone[zoneId] = 0;
                    state.turnsInZone[zoneId]++;
                    DebugLog($"任务 [{state.data.missionId}] 区域 [{zoneId}] 驻留 " +
                             $"+1 = {state.turnsInZone[zoneId]} 回合");
                }
            }
        }
    }

    private static void CollectTurnsInZoneIds(
        List<MissionCondition> conditions,
        System.Collections.Generic.HashSet<string> result)
    {
        if (conditions == null) return;
        foreach (var c in conditions)
            if (c.check == ConditionCheck.TurnsInZone && !string.IsNullOrEmpty(c.zoneId))
                result.Add(c.zoneId);
    }

    /// <summary>
    /// 检查当前任务池里所有任务是否已全部结算。
    /// 满足条件时向 GameManager 发送 "allMissionsSettled" 触发器，
    /// 用于替代 "missionSuccess"，避免中途某个子任务完成就提前切换阶段。
    ///
    /// 判断逻辑：
    ///   activeMissions 为空（当前没有进行中的任务）
    ///   AND 任务池里每个条目要么已结算，要么前置条件永久不满足（跳过不计）
    /// </summary>
    private void CheckAllPoolMissionsSettled()
    {
        if (currentPool == null) return;
        if (activeMissions.Count > 0) return; // 仍有任务在执行，还没做完

        foreach (var entry in currentPool.missions)
        {
            if (entry.missionData == null) continue;

            // 已结算：跳过
            if (_settledThisPhase.Contains(entry.missionData.missionId)) continue;

            // 未结算：检查它是否仍然可以被激活
            // 如果前置条件满足（含空条件 = 无要求），说明它还是"待执行"状态，池子未完成
            bool precondsMet = entry.preconditions == null
                               || entry.preconditions.Count == 0
                               || ConditionEvaluator.EvaluateAll(entry.preconditions);
            if (precondsMet)
            {
                DebugLog($"任务池未完成：[{entry.missionData.missionId}] 尚未结算");
                return; // 还有任务没做完，不触发
            }
            // 前置条件不满足且未结算 → 视为永久跳过，不阻塞完成判断
        }

        DebugLog("所有可执行任务已结算 → allMissionsSettled");
        GameManager.Instance?.NotifyAllMissionsSettled();
    }

    private void CheckAllMissions()
    {
        // 先收集需要结算的任务，避免在遍历中修改列表导致索引越界
        var toRemove = new List<(MissionRuntimeState state, bool success)>();

        foreach (var state in activeMissions)
        {
            if (!state.isActive) continue;

            // 先检查失败条件（逻辑由 failLogic 决定）
            bool failMet = state.data.failLogic == ConditionLogic.AND
                ? ConditionEvaluator.EvaluateAll(state.data.failConditions, state)
                : ConditionEvaluator.EvaluateAny(state.data.failConditions, state);

            if (failMet)
            {
                // 打印触发失败的条件，方便调试
                if (enableDebugLog && state.data.failConditions != null)
                    foreach (var fc in state.data.failConditions)
                        if (ConditionEvaluator.Evaluate(fc, state))
                            DebugLog($"  ↳ 触发失败条件：target={fc.target} check={fc.check} " +
                                     $"targetId={fc.targetId} zoneId={fc.zoneId} val={fc.numericValue}");
                toRemove.Add((state, false));
                continue;
            }

            // 再检查成功条件
            bool hasSuccessConditions = state.data.successConditions != null &&
                                        state.data.successConditions.Count > 0;
            if (!hasSuccessConditions) continue;

            bool successMet = state.data.successLogic == ConditionLogic.OR
                ? ConditionEvaluator.EvaluateAny(state.data.successConditions, state)
                : ConditionEvaluator.EvaluateAll(state.data.successConditions, state);

            if (successMet)
            {
                // 打印触发成功的条件，方便调试
                if (enableDebugLog && state.data.successConditions != null)
                    foreach (var sc in state.data.successConditions)
                        if (ConditionEvaluator.Evaluate(sc, state))
                            DebugLog($"  ↳ 触发成功条件：target={sc.target} check={sc.check} " +
                                     $"targetId={sc.targetId} zoneId={sc.zoneId} val={sc.numericValue} turnCount={state.turnCount}");

                state.repeatsDone++;
                DebugLog($"任务 [{state.data.missionId}] 成功 ({state.repeatsDone}/{state.data.repeatCount})");

                if (state.IsCompleted)
                    toRemove.Add((state, true));
                else
                {
                    PushContextToEnemies(state.data, state.priority, state.exclusive, state.repeatsDone);
                    DebugLog($"任务 [{state.data.missionId}] 需要继续完成 " +
                             $"({state.repeatsDone}/{state.data.repeatCount})");
                }
            }
        }

        // 统一结算和移除
        foreach (var (state, success) in toRemove)
        {
            SettleMission(state, success);
            activeMissions.Remove(state);
        }
    }

    // ══════════════════════════════════════════════════════
    // 任务结算
    // ══════════════════════════════════════════════════════

    private void SettleMission(MissionRuntimeState state, bool success)
    {
        state.isActive = false;
        _settledThisPhase.Add(state.data.missionId); // 记录结算，用于串行激活判断
        float delta = success ? state.data.progressOnSuccess : state.data.progressOnFail;

        DebugLog($"任务 [{state.data.missionId}] {(success ? "成功" : "失败")}，" +
                 $"PlanProgress +{delta} ({planProgress} → {planProgress + delta})");

        // 累积进度
        AddProgress(delta);

        // 设置 StoryFlag
        if (StoryManager.Instance != null)
        {
            string flag = success
                ? $"{state.data.missionId}_success"
                : $"{state.data.missionId}_failed";
            StoryManager.Instance.SetFlag(flag);
        }

        // 播放结算演出
        string dialogue = success ? state.data.dialogueOnSuccess : state.data.dialogueOnFail;
        if (!string.IsNullOrEmpty(dialogue))
            DebugLog($"[onSettle] 触发演出：{dialogue}（DialogueManager 待接入）");

        // 通知外部
        OnMissionSettled?.Invoke(state.data, success);

        // 通知 GameManager（单个任务结算信号，用于 missionSuccess/missionFailed 触发器）
        // 注意：阶段切换推荐使用 allMissionsSettled（整池全部完成后触发），
        // 而非 missionSuccess/missionFailed（每个子任务结算都触发，容易导致提前切换）。
        // 仅在阶段 JSON 的 transitions 里明确配置了这两个触发器时才有实际作用。
        if (success) GameManager.Instance?.NotifyMissionSuccess();
        else         GameManager.Instance?.NotifyMissionFailed();

        // 取消 AP 事件订阅（必须在 Pop 之前，防止 GC 泄漏）
        UnsubscribeAPEvents(state.data.missionId);

        // 任务结算完成后，将此任务从所有参与 Enemy 的队列中移除
        // 放在最后，确保上方所有结算逻辑执行完毕再切换 Enemy 行为
        PopMissionFromAllEnemies(state.data.missionId);
    }

    // ══════════════════════════════════════════════════════
    // PlanProgress 管理
    // ══════════════════════════════════════════════════════

    private void AddProgress(float delta)
    {
        if (delta <= 0f) return;

        planProgress = Mathf.Clamp(planProgress + delta, 0f, 100f);
        OnProgressChanged?.Invoke(planProgress);

        // 检查当前阶段的 progressThreshold（每阶段只触发一次，且阈值 > 0 才生效）
        if (!_progressThresholdFired && GameManager.Instance != null)
        {
            float threshold = GameManager.Instance.GetCurrentPhaseData()?.progressThreshold ?? 0f;
            if (threshold > 0f && planProgress >= threshold)
            {
                _progressThresholdFired = true;
                DebugLog($"PlanProgress 达到阶段阈值 {threshold}，触发 planProgressThreshold");
                GameManager.Instance.NotifyProgressThreshold();
            }
        }

        if (planProgress >= 100f)
        {
            DebugLog("PlanProgress 达到 100%，计划完成！");
            GameManager.Instance?.NotifyPlanComplete();
        }
    }

    // ══════════════════════════════════════════════════════
    // MissionContext 推送
    // ══════════════════════════════════════════════════════

    private void PushContextToEnemies(
        MissionData data, int priority, bool exclusive = false, int repeatsDone = 0)
    {
        var allEnemies = FindObjectsOfType<EnemyAIController>();

        // 如果 assignedEnemies 为空，推给所有 Enemy
        bool pushAll = data.assignedEnemies == null || data.assignedEnemies.Count == 0;

        foreach (var enemy in allEnemies)
        {
            if (!pushAll && !data.assignedEnemies.Contains(enemy.EnemyId))
                continue;

            var ctx = new MissionContext(data, enemy.EnemyId, priority);
            ctx.repeatsDone = repeatsDone;
            enemy.PushMissionContext(ctx, exclusive);

            DebugLog($"推送 MissionContext [{data.missionId}] P{priority}" +
                     $"{(exclusive ? " [独占]" : "")} → {enemy.EnemyId}");
        }
    }

    /// <summary>从所有 Enemy 的任务队列中移除指定任务</summary>
    private void PopMissionFromAllEnemies(string missionId)
    {
        foreach (var enemy in FindObjectsOfType<EnemyAIController>())
            if (enemy != null) enemy.PopMissionContext(missionId);
    }

    // ══════════════════════════════════════════════════════
    // APConsumed 事件订阅管理
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 订阅任务 assignedEnemies 的 OnAPConsumed 事件
    /// 在 ActivateMission 时调用，累加到对应 MissionRuntimeState.totalAPConsumed
    /// assignedEnemies 为空时订阅所有场上 Enemy
    /// </summary>
    private void SubscribeAPEvents(MissionData data, MissionRuntimeState state)
    {
        bool pushAll = data.assignedEnemies == null || data.assignedEnemies.Count == 0;
        var subs = new List<(TurnBasedUnit unit, System.Action<int> handler)>();

        foreach (var enemy in FindObjectsOfType<EnemyAIController>())
        {
            if (!pushAll && !data.assignedEnemies.Contains(enemy.EnemyId)) continue;

            var turnUnit = enemy.GetComponent<TurnBasedUnit>();
            if (turnUnit == null) continue;

            // 闭包捕获 state，直接累加（不需要再查字典）
            var capturedState = state;
            System.Action<int> handler = cost => capturedState.totalAPConsumed += cost;
            turnUnit.OnAPConsumed += handler;
            subs.Add((turnUnit, handler));
        }

        _apSubscriptions[data.missionId] = subs;
        DebugLog($"APConsumed 订阅：任务 [{data.missionId}] 共订阅 {subs.Count} 个 Enemy");
    }

    /// <summary>
    /// 取消订阅指定任务的 OnAPConsumed 事件，防止 GC 泄漏
    /// 在 SettleMission 和 OnPhaseChanged 清理任务时调用
    /// </summary>
    private void UnsubscribeAPEvents(string missionId)
    {
        if (!_apSubscriptions.TryGetValue(missionId, out var subs)) return;

        foreach (var (unit, handler) in subs)
            if (unit != null) unit.OnAPConsumed -= handler;

        _apSubscriptions.Remove(missionId);
    }

    // ══════════════════════════════════════════════════════
    // 公开接口（供调试控制台调用）
    // ══════════════════════════════════════════════════════

    /// <summary>强制设置 PlanProgress（调试用）</summary>
    public void DEBUG_SetProgress(float value)
    {
        planProgress = Mathf.Clamp(value, 0f, 100f);
        OnProgressChanged?.Invoke(planProgress);
        DebugLog($"[调试] PlanProgress 强制设为 {planProgress}");
    }

    /// <summary>强制结算某个任务（调试用）</summary>
    public void DEBUG_SettleMission(string missionId, bool success)
    {
        var state = activeMissions.FirstOrDefault(m => m.data.missionId == missionId);
        if (state == null)
        {
            Debug.LogWarning($"[MissionManager] 找不到激活的任务 [{missionId}]");
            return;
        }
        SettleMission(state, success);
        activeMissions.Remove(state);
    }

    /// <summary>强制激活任务池里某个任务（调试用）</summary>
    public void DEBUG_ActivateMission(string missionId)
    {
        if (currentPool == null) { Debug.LogWarning("[MissionManager] 当前无任务池"); return; }
        var entry = currentPool.FindById(missionId);
        if (entry == null) { Debug.LogWarning($"[MissionManager] 任务池里找不到 [{missionId}]"); return; }
        ActivateMission(entry);
    }

    // ══════════════════════════════════════════════════════
    // 调试
    // ══════════════════════════════════════════════════════

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[MissionManager] {msg}");
    }

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        float w = 280f;
        float x = Screen.width - w - 10;
        float y = 300f;

        string info = $"[MissionManager]\n" +
                      $"PlanProgress：{planProgress:F1} / 100\n" +
                      $"当前任务池：{currentPool?.name ?? "无"}\n" +
                      $"激活任务数：{activeMissions.Count}\n";

        foreach (var s in activeMissions)
            info += $"  [{s.data.missionId}] {s.repeatsDone}/{s.data.repeatCount}\n";

        GUI.Box(new Rect(x, y, w, Mathf.Max(80, 60 + activeMissions.Count * 20)), info, style);
    }
}