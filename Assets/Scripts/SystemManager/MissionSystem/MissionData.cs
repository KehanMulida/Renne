using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MissionData — 单个任务的完整数据定义（ScriptableObject）
/// 右键 Create > Mission > Mission Data 创建
///
/// 设计原则：
/// - 只存配置数据，不存运行时状态（运行时状态在 MissionRuntimeState 里）
/// - assignedEnemies 定义谁执行任务，MissionManager 据此推送 MissionContext
/// - targetZoneId 定义任务目标区域，写入 Enemy Blackboard 供 AI 导航使用
/// </summary>
[CreateAssetMenu(fileName = "MissionData", menuName = "Mission/Mission Data")]
public class MissionData : ScriptableObject
{
    // ══════════════════════════════════════════════════════
    // 基础信息
    // ══════════════════════════════════════════════════════

    [Header("基础信息")]

    [Tooltip("任务唯一标识，不含空格，如 escort_vip_01")]
    public string missionId;

    [Tooltip("策划用可读名称")]
    public string displayName;

    [TextArea(2, 4)]
    [Tooltip("任务描述，仅供策划参考")]
    public string description;

    // ══════════════════════════════════════════════════════
    // AI 行为配置
    // ══════════════════════════════════════════════════════

    [Header("AI 行为模式")]

    [Tooltip("决定 AI 走哪个行为树分支（未配置 defaultActionWeights 时自动生成权重）\n\n" +
             "Patrol       → 在 targetZoneId 区域内持续巡逻  ← 巡逻任务选这个\n" +
             "Defensive    → 守住 targetZoneId 区域，低机动\n" +
             "Mobile       → 向 targetZoneId 推进（护送/运输/占领）\n" +
             "Investigative→ 搜索调查 targetZoneId\n" +
             "Aggressive   → 冲向 targetZoneId（无 zone 则随机游走）\n" +
             "Stealth      → 低噪声潜行巡逻")]
    public MissionBehaviourProfile behaviourProfile = MissionBehaviourProfile.Aggressive;

    [Tooltip("遭遇玩家时允许进入的战斗模式\n具体战术由战斗决策层（HP × 友军）动态选择")]
    public CombatResponse combatResponse = CombatResponse.Chase;

    // ══════════════════════════════════════════════════════
    // 参与 Enemy 和目标区域
    // ══════════════════════════════════════════════════════

    [Header("参与 Enemy")]

    [Tooltip("参与此任务的 Enemy 编号列表（如 E01、E03）\n" +
             "MissionManager 只向这些 Enemy 推送 MissionContext\n" +
             "留空则向场景内所有 Enemy 推送")]
    public List<string> assignedEnemies;

    [Header("目标区域")]

    [Tooltip("任务的目标区域 ID，对应 ZoneMarker.zoneId\n" +
             "写入 Enemy Blackboard 的 targetZoneId 字段，供 AI 导航使用\n" +
             "留空表示任务无固定目标区域（如巡逻任务自行选择巡逻点）")]
    public string targetZoneId;

    [Tooltip("ObjectiveGuard / ObjectiveDestroy 任务必填\n" +
             "对应 WorldItem.objectId（Inspector 里的 Object Id 字段）\n" +
             "例如 generator_b1 / relay_switch_02\n" +
             "写入 Enemy Blackboard 的 targetObjectId，供 ObjectiveInteractExecutor 使用\n" +
             "普通任务留空")]
    public string targetObjectId = "";

    // ══════════════════════════════════════════════════════
    // 进度贡献
    // ══════════════════════════════════════════════════════

    [Header("进度贡献")]

    [Tooltip("任务成功时 PlanProgress 增加量（关键任务设置更高值）")]
    public float progressOnSuccess = 10f;

    [Tooltip("任务失败时 PlanProgress 增加量（0 = 失败完全阻止进度贡献）")]
    public float progressOnFail = 0f;

    [Tooltip("需要成功几次才算达成任务目标（1 = 一次即可）")]
    [Min(1)]
    public int repeatCount = 1;

    // ══════════════════════════════════════════════════════
    // 成功 / 失败条件
    // ══════════════════════════════════════════════════════

    [Header("成功条件（AND 逻辑，全部满足才成功）")]
    [Tooltip("例：\n护送任务 → UnitInZone(VIP, floor3_safeRoom)\n" +
             "消灭任务 → AllAssignedDefeated（配合 assignedEnemies 指定目标）\n" +
             "封锁任务 → 回合数达到要求（由 MissionManager 计时）")]
    public List<MissionCondition> successConditions;

    [Tooltip("成功条件的逻辑运算方式\nAND = 全部满足才成功\nOR = 任一满足即成功")]
    public ConditionLogic successLogic = ConditionLogic.AND;

    [Header("失败条件")]
    [Tooltip("例：\n护送任务 → IsDefeated(VIP)\n" +
             "消灭任务 → AssignedEnemyDefeated(E03)（执行者被玩家击败）\n" +
             "封锁任务 → PlayerInZone(封锁区域)")]
    public List<MissionCondition> failConditions;

    [Tooltip("失败条件的逻辑运算方式\nOR = 任一满足即失败（默认）\nAND = 全部同时满足才失败")]
    public ConditionLogic failLogic = ConditionLogic.OR;

    // ══════════════════════════════════════════════════════
    // AI 行为权重
    // ══════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════
    // 编队配置
    // ══════════════════════════════════════════════════════

    [Header("编队配置")]

    [Tooltip("跟随目标 Enemy ID（留空 = 独立行动）\n" +
             "填写后该 Enemy 会保持在 Leader 附近 formationDistance 格以内")]
    public string leaderId = "";

    [Tooltip("编队跟随距离（格数，Manhattan 距离）\n" +
             "跟随者与 Leader 的最大允许距离，超出则向 Leader 靠近\n" +
             "已在距离内则原地不动（或执行区域巡逻）\n" +
             "默认 2，设为 1 则紧贴 Leader\n" +
             "moveInGroup=true 时此值被自动覆盖为 1")]
    [Min(1)]
    public int formationDistance = 2;

    [Tooltip("是否并排同步行动\n" +
             "true  = 跟随者始终保持在 Leader 紧邻格（并排/贴身护卫），\n" +
             "        formationDistance 自动强制为 1，GetFormationCells 只返回正交相邻格\n" +
             "false = 跟随者可在 formationDistance 范围内自由游走（默认，编队巡逻）")]
    public bool moveInGroup = false;

    // ══════════════════════════════════════════════════════
    // 持久性
    // ══════════════════════════════════════════════════════

    [Header("持久性")]

    [Tooltip("true = GamePhase 切换时保留此任务继续执行\n" +
             "false = 切换 Phase 时自动从所有 Enemy 队列中清除（默认）")]
    public bool persistAcrossPhases = false;

    // ══════════════════════════════════════════════════════
    // 状态覆盖
    // ══════════════════════════════════════════════════════

    [Header("状态覆盖")]

    [Tooltip("AP 倍率：1 = 不变，>1 增加行动力，<1 减少\n" +
             "任务结束后推送的新 Context 默认为 1，自动还原")]
    [Min(0f)]
    public float apMultiplier = 1f;

    [Tooltip("移动速度倍率：1 = 不变，>1 加速，<1 减速\n" +
             "作用于 UnitMovement.moveSpeed")]
    [Min(0f)]
    public float speedMultiplier = 1f;

    [Header("行为权重 — 默认（无专属配置的 Enemy 使用）")]
    [Tooltip("写入 Enemy Blackboard 的权重，BTRunner 读取后决定走哪个行为分支")]
    public List<ActionWeight> defaultActionWeights;

    [Header("行为权重 — Enemy 专属（按编号覆盖默认权重）")]
    [Tooltip("没有专属配置的 Enemy 自动降级使用 defaultActionWeights")]
    public List<EnemyActionProfile> enemyActionProfiles;

    // ══════════════════════════════════════════════════════
    // 演出配置
    // ══════════════════════════════════════════════════════

    [Header("演出（对应 Resources/Dialogues/ 下的文件路径）")]

    [Tooltip("任务开始时触发的演出键值（留空则不触发）")]
    public string dialogueOnStart;

    [Tooltip("任务成功时触发的演出键值")]
    public string dialogueOnSuccess;

    [Tooltip("任务失败时触发的演出键值")]
    public string dialogueOnFail;

    // ══════════════════════════════════════════════════════
    // 编辑器验证
    // ══════════════════════════════════════════════════════

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!string.IsNullOrEmpty(missionId) && missionId.Contains(" "))
            Debug.LogWarning($"[MissionData] {name}：missionId 不能含空格，请使用下划线");

        if (progressOnFail > progressOnSuccess)
            Debug.LogWarning($"[MissionData] {name}：progressOnFail ({progressOnFail}) " +
                             $"大于 progressOnSuccess ({progressOnSuccess})，请确认是否符合设计意图");

        // assignedEnemies 格式检查
        if (assignedEnemies != null)
        {
            foreach (var id in assignedEnemies)
            {
                if (!string.IsNullOrEmpty(id) && id.Contains(" "))
                    Debug.LogWarning($"[MissionData] {name}：assignedEnemies 中的 ID [{id}] 含空格");
            }
        }
    }
#endif
}