using UnityEngine;

[CreateAssetMenu(fileName = "EnemyConfig", menuName = "AI/Enemy Config")]
public class EnemyConfig : ScriptableObject
{
    // ============ 感知参数 ============

    [Header("视野感知")]
    [Tooltip("视野距离（格子数）")]
    public float visionRange = 15f;

    [Tooltip("视野角度（度）")]
    public float visionAngle = 90f;

    [Tooltip("是否可以跨楼层看到玩家")]
    public bool canSeeAcrossFloors = false;

    [Header("听觉感知")]
    [Tooltip("听觉范围（格子数）")]
    public float hearingRange = 10f;

    [Tooltip("是否可以跨楼层听到声音")]
    public bool canHearAcrossFloors = false;

    // ============ 移动参数 ============

    [Header("移动")]
    [Tooltip("巡逻移动速度")]
    public float moveSpeed = 3f;

    [Tooltip("正常模式每回合可移动格子数")]
    public int moveRange = 5;

    [Tooltip("战斗模式每回合可移动格子数")]
    public int combatMoveRange = 2;

    [Tooltip("追击移动速度")]
    public float chaseSpeed = 5f;

    [Tooltip("旋转速度")]
    public float rotationSpeed = 5f;

    // ============ AP 状态（运行时，不序列化）============

    [System.NonSerialized]
    private bool isInCombatMode = false;

    [System.NonSerialized]
    private float _missionAPMultiplier = 1f;

    /// <summary>
    /// 获取当前模式的 AP 值（已应用任务倍率）
    /// TurnBasedUnit 回合开始时调用此方法
    /// </summary>
    public int GetCurrentAP()
    {
        int base_ = isInCombatMode ? combatMoveRange : moveRange;
        return Mathf.Max(1, Mathf.RoundToInt(base_ * _missionAPMultiplier));
    }

    /// <summary>
    /// 设置任务 AP 倍率（由 EnemyAIController.SetMissionContext 调用）
    /// </summary>
    public void SetMissionAPMultiplier(float multiplier)
    {
        _missionAPMultiplier = Mathf.Max(0f, multiplier);
        Debug.Log($"[EnemyConfig] APMultiplier={_missionAPMultiplier} | EffectiveAP={GetCurrentAP()}");
    }

    /// <summary>
    /// 切换战斗模式（由 CombatModeManager 调用）
    /// </summary>
    public void SetCombatMode(bool inCombat)
    {
        isInCombatMode = inCombat;
        Debug.Log($"[EnemyConfig] CombatMode={inCombat} | AP={GetCurrentAP()}");
    }

    /// <summary>当前是否在战斗模式</summary>
    public bool IsInCombatMode => isInCombatMode;

    [Header("巡逻")]
    [Tooltip("巡逻目标最小距离（格子数），避免原地来回走")]
    public int patrolMinDistance = 3;

    [Tooltip("巡逻目标最大距离（格子数）")]
    public int patrolMaxDistance = 6;

    [Tooltip("巡逻目标选取最大尝试次数")]
    public int patrolMaxAttempts = 20;

    // ============ 战斗参数 ============

    [Header("攻击")]
    [Tooltip("攻击冷却时间（秒）")]
    public float attackCooldown = 1f;

    [Tooltip("无武器时的空手伤害（近战1格）")]
    public int attackDamage = 10;

    // ============ 生命值 & 状态 ============

    [Header("生命值")]
    public int maxHp = 100;
    public int maxSanity = 100;

    [Tooltip("低血量阈值（低于此比例视为低血量状态，0=不触发）")]
    [Range(0f, 1f)]
    public float lowHpThreshold = 0.3f;

    // ============ 状态切换参数 ============

    [Header("搜索行为")]
    [Tooltip("失去视野后继续搜索的回合数")]
    public int searchConfidenceMax = 3;

    [Tooltip("听到声音后调查的回合数")]
    public int soundInvestigateRounds = 2;

    [Header("状态行为")]
    [Tooltip("调查停留时长（秒）")]
    public float investigationDuration = 5f;

    [Tooltip("回合开始延迟（秒），模拟思考时间")]
    public float turnStartDelay = 0.5f;

    [Tooltip("每次行动后的间隔（秒）")]
    public float actionInterval = 0.3f;

    [Tooltip("敌人行动时给玩家的 QTE 反应窗口时长（秒）\n" +
             "0 = 此敌人不触发 QTE\n" +
             "移动/攻击时均会触发，玩家可在此期间移动2格或使用道具")]
    [Range(0f, 5f)]
    public float qteWindowDuration = 2f;

    // ============ 行为树 ============

    [Header("行为树")]
    public TextAsset behaviorTreeAsset;

    // ============ 运行时读取接口 ============
    // 其他系统通过这些属性读取，方便之后统一修改或加 buff/debuff

    /// <summary>实际视野范围（可被状态修改）</summary>
    public float EffectiveVisionRange => visionRange;

    /// <summary>实际听觉范围（可被状态修改）</summary>
    public float EffectiveHearingRange => hearingRange;

    /// <summary>实际移动格数（可被状态修改）</summary>
    public int EffectiveMoveRange => moveRange;

    /// <summary>实际攻击伤害（可被状态修改）</summary>
    public int EffectiveAttackDamage => attackDamage;

    /// <summary>是否处于低血量状态</summary>
    public bool IsLowHp(int currentHp) =>
        lowHpThreshold > 0 && currentHp <= maxHp * lowHpThreshold;
}