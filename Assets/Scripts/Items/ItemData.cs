using UnityEngine;

/// <summary>
/// 物品类型枚举
/// </summary>
public enum ItemType
{
    Consumable,     // 消耗品
    Weapon,         // 武器
    Equipment,      // 装备
    KeyItem,        // 关键道具
    Material,   // 材料
    SceneItem       // 场景物品
}

/// <summary>
/// 物品稀有度（简化为3级）
/// </summary>
public enum ItemRarity
{
    Common,         // 普通（白色）
    Rare,           // 稀有（蓝色）
    Legendary       // 传说（金色）
}

/// <summary>
/// 投掷物飞行轨迹类型
/// </summary>
public enum FlightMode
{
    Arc,        // 抛物线（酒瓶、石头等）
    Straight,   // 直线（飞刀、标枪等）
}

/// <summary>
/// 投掷配置
/// 作为 ItemData 的可选内嵌数据
/// 任何物品只要填写此配置就可以投掷，留空表示不可投掷
/// </summary>
[System.Serializable]
public class ThrowableConfig
{
    [Header("飞行配置")]
    [Tooltip("Arc=抛物线  Straight=直线")]
    public FlightMode flightMode = FlightMode.Arc;

    [Tooltip("最大投掷距离（格子数）")]
    [Range(1, 20)]
    public int throwRange = 6;

    [Tooltip("飞行速度（格子/秒）")]
    [Range(1f, 30f)]
    public float flightSpeed = 10f;

    [Tooltip("抛物线高度（仅 FlightMode.Arc 有效）")]
    [Range(0f, 5f)]
    public float arcHeight = 2f;

    [Header("伤害配置")]
    [Tooltip("直接命中伤害")]
    [Range(0, 200)]
    public int directDamage = 20;

    [Tooltip("溅射范围（格子数，0=无范围伤害）")]
    [Range(0, 5)]
    public int splashRadius = 0;

    [Tooltip("溅射伤害（splashRadius > 0 时有效）")]
    [Range(0, 100)]
    public int splashDamage = 0;

    [Tooltip("是否可以伤害投掷者自身")]
    public bool canDamageSelf = false;

    [Header("碰撞配置")]
    [Tooltip("可以命中的 Layer（通常是 Enemy）")]
    public LayerMask hitLayer;

    [Header("落地行为")]
    [Tooltip("命中或落地后是否损坏\n损坏：生成碎片，不可重新拾取\n不损坏：可以配置是否重新生成为 WorldItem")]
    public bool breakOnImpact = true;

    [Tooltip("未损坏时，落地后是否重新生成为可拾取的 WorldItem\n仅 breakOnImpact = false 时有效")]
    public bool canPickupAfterThrow = false;

    [Tooltip("命中特效 Prefab（可选）")]
    public GameObject impactVFXPrefab;

    [Tooltip("损坏后的碎片 Prefab（breakOnImpact = true 时可选）")]
    public GameObject debrisPrefab;

    [Header("声音")]
    public string throwSound = "";
    public string impactSound = "";

    // ============ 便捷查询 ============

    /// <summary>是否有范围伤害</summary>
    public bool HasSplash => splashRadius > 0 && splashDamage > 0;

    /// <summary>是否是抛物线模式</summary>
    public bool IsArc => flightMode == FlightMode.Arc;

    /// <summary>落地后可以被重新拾取</summary>
    public bool IsPickupable => !breakOnImpact && canPickupAfterThrow;
}

/// <summary>
/// 物品基础配置数据
/// 所有物品的共享基类
/// ThrowableConfig 作为可选字段内嵌，填写后该物品即可投掷
/// </summary>
[CreateAssetMenu(fileName = "ItemData", menuName = "SRPG/Item Data/Base Item", order = 10)]
public class ItemData : ScriptableObject
{
    [Header("基础信息")]
    public int ID = 2001;
    public string Name = "物品";

    [TextArea(2, 4)]
    public string description = "物品描述";

    [Header("分类")]
    public ItemType Type = ItemType.Consumable;
    public ItemRarity rarity = ItemRarity.Common;

    [Header("使用")]
    public int UseCost = 1;
    public string EffectType = "";
    public int EffectValue = 0;

    [Header("音效和特效")]
    public string Sound = "";
    public string Animation = "";
    public string VFX = "";

    [Header("视觉")]
    public Sprite icon;
    public GameObject Prefab;

    [Header("属性")]
    public float weight = 1f;
    public int value = 10;
    public int maxStack = 1;

    [Header("投掷配置")]
    [Tooltip("勾选后该物品可以投掷")]
    public bool isThrowable = false;

    [Tooltip("投掷相关参数（仅 isThrowable = true 时生效）")]
    public ThrowableConfig throwableConfig = new ThrowableConfig();

    // ============ 便捷查询 ============

    /// <summary>该物品是否可以投掷</summary>
    public bool IsThrowable => isThrowable;

    /// <summary>安全获取投掷配置（确保 isThrowable 已勾选）</summary>
    public ThrowableConfig ThrowCfg => isThrowable ? throwableConfig : null;

    // ============ 工具方法 ============

    public Color GetRarityColor()
    {
        switch (rarity)
        {
            case ItemRarity.Common:    return Color.white;
            case ItemRarity.Rare:      return new Color(0.3f, 0.6f, 1f);
            case ItemRarity.Legendary: return new Color(1f, 0.8f, 0f);
            default:                   return Color.white;
        }
    }

    public virtual string GetDetailedInfo()
    {
        string info = $"{Name}\n{description}";

        if (IsThrowable && ThrowCfg != null)
        {
            info += $"\n\n<b>投掷属性</b>";
            info += $"\n飞行模式: {ThrowCfg.flightMode}";
            info += $"\n射程: {ThrowCfg.throwRange} 格";
            info += $"\n直接伤害: {ThrowCfg.directDamage}";

            if (ThrowCfg.HasSplash)
                info += $"\n溅射: {ThrowCfg.splashRadius} 格 / {ThrowCfg.splashDamage} 伤害";

            if (ThrowCfg.IsPickupable)
                info += "\n（落地后可重新拾取）";
            else if (ThrowCfg.breakOnImpact)
                info += "\n（落地损坏）";
        }

        return info;
    }
}

/// <summary>
/// 武器配置数据
/// </summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "SRPG/Item Data/Weapon", order = 11)]
public class WeaponData : ItemData
{
    [Header("武器基础属性")]
    public string WeaponType = "Gun";
    public int Damage = 20;
    public int AttackRange = 5;
    public int NoiceLevel = 2;

    [Header("弹药")]
    [Tooltip("是否使用弹药")]
    public bool IshasBullet = true;

    [Tooltip("最大弹药量")]
    public int MaxBullet = 7;

    [Header("射击精度")]
    [Tooltip("精准度（0~100）\n100=完全准确无偏移\n0=最大角度偏移")]
    [Range(0, 100)]
    public int Accuracy = 70;

    [Tooltip("最大散布角度（度）\nAccuracy=0时偏移此角度，Accuracy=100时无偏移")]
    [Range(0f, 45f)]
    public float MaxSpreadAngle = 20f;

    [Header("暴击")]
    [Tooltip("暴击率（0~100）")]
    [Range(0, 100)]
    public int CriticalChance = 10;

    [Tooltip("暴击伤害倍率")]
    public float CriticalMultiplier = 2f;

    [Header("子弹配置")]
    [Tooltip("子弹飞行速度（世界单位/秒）")]
    public float BulletSpeed = 30f;

    [Tooltip("子弹最大飞行距离（世界单位）")]
    public float BulletMaxDistance = 50f;

    [Tooltip("子弹 Prefab（挂有 BulletProjectile 组件）")]
    public GameObject BulletPrefab;

    [Tooltip("命中特效 Prefab（可选）")]
    public GameObject HitVFXPrefab;

    [Tooltip("枪口特效 Prefab（可选）")]
    public GameObject MuzzleVFXPrefab;

    public WeaponData()
    {
        Type = ItemType.Weapon;
        UseCost = 1;
    }

    /// <summary>
    /// 根据 Accuracy 计算本次射击的角度偏移
    /// Accuracy=100 → 偏移0度  Accuracy=0 → 偏移MaxSpreadAngle度
    /// </summary>
    public float CalculateSpreadAngle()
    {
        float inaccuracy = 1f - Accuracy / 100f;
        float spread = inaccuracy * MaxSpreadAngle;
        return UnityEngine.Random.Range(-spread, spread);
    }

    /// <summary>根据暴击率判断是否暴击</summary>
    public bool RollCritical() =>
        UnityEngine.Random.Range(0, 100) < CriticalChance;

    /// <summary>计算实际伤害（含暴击，命中由子弹物理判定）</summary>
    public int CalculateDamage() =>
        RollCritical() ? Mathf.RoundToInt(Damage * CriticalMultiplier) : Damage;

    public override string GetDetailedInfo()
    {
        string info = base.GetDetailedInfo();
        info += $"\n\n<b>武器属性</b>";
        info += $"\n类型: {WeaponType}";
        info += $"\n伤害: {Damage}";
        info += $"\n射程: {AttackRange}";
        info += $"\n精准度: {Accuracy}% (最大散布 ±{MaxSpreadAngle}°)";
        info += $"\n暴击: {CriticalChance}% x{CriticalMultiplier}";
        if (IshasBullet) info += $"\n弹药: {MaxBullet}";
        info += $"\n声音: {NoiceLevel}";
        return info;
    }
}

/// <summary>
/// 消耗品配置数据
/// 可同时填写 throwableConfig 变成可投掷消耗品
/// 例：酒瓶 - 直接使用回血，投掷造成伤害并落地损坏
/// </summary>
[CreateAssetMenu(fileName = "ConsumableData", menuName = "SRPG/Item Data/Consumable", order = 12)]
public class ConsumableData : ItemData
{
    [Header("消耗品专属属性")]
    public int healAmount = 0;
    public int staminaAmount = 0;
    public int sanityAmount = 0;

    [Header("伤害效果（近战使用时）")]
    [Tooltip("近战使用时对目标造成的伤害（0=无近战伤害）")]
    public int meleeDamage = 0;

    [Tooltip("近战伤害范围（格子数，0=只打相邻格）")]
    [Range(0, 3)]
    public int meleeRange = 0;

    [Tooltip("可以命中的 Layer")]
    public LayerMask meleeLayer;

    public ConsumableData()
    {
        Type = ItemType.Consumable;
        UseCost = 0;
        maxStack = 10;
    }

    public override string GetDetailedInfo()
    {
        string info = base.GetDetailedInfo();

        if (healAmount > 0 || staminaAmount > 0 || sanityAmount > 0)
        {
            info += $"\n\n<b>直接使用效果</b>";
            if (healAmount > 0)    info += $"\n恢复生命: +{healAmount}";
            if (staminaAmount > 0) info += $"\n恢复体力: +{staminaAmount}";
            if (sanityAmount > 0)  info += $"\n恢复精神: +{sanityAmount}";
        }

        if (meleeDamage > 0)
        {
            info += $"\n\n<b>近战效果</b>";
            info += $"\n近战伤害: {meleeDamage}";
            if (meleeRange > 0)
                info += $"\n范围: {meleeRange} 格";
        }

        return info;
    }
}

// ══════════════════════════════════════════════════════════════════════
// SceneItemData 相关枚举
// ══════════════════════════════════════════════════════════════════════

/// <summary>谁可以与此物体发起交互</summary>
public enum SceneInteractableBy
{
    PlayerOnly, // 仅玩家（Enemy AI 忽略）
    EnemyOnly,  // 仅 Enemy AI（玩家无提示）
    Both,       // 双方都可以
    None,       // 纯背景，无交互
}

/// <summary>碎片/倒下后的阻挡模式</summary>
public enum DebrisBlockMode
{
    NoBlock,       // 无阻挡（地面装饰）
    BlockMovement, // 阻挡移动（可作掩体）
}

// ══════════════════════════════════════════════════════════════════════
// 行为配置块（每个布尔开关对应一个配置块）
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 门旋转动画的铰链位置
/// 决定门绕哪侧边展开，由 SceneItemInstance 根据 Collider/gridWidth 自动换算世界坐标
/// </summary>
public enum HingeSide
{
    Left,    // 左铰链：门向右展开（openAngle 为正时向右摆）
    Right,   // 右铰链：门向左展开（openAngle 为负时向左摆）
    Center,  // 以物体中心旋转（非门物体或旋转木门等特殊用法）
}

/// <summary>
/// 开关配置（isToggleable = true）
/// 适用于：门、容器盖、开关、保险箱
/// </summary>
[System.Serializable]
public class ToggleConfig
{
    [Tooltip("初始是否处于「开」状态")]
    public bool startOpen = false;

    [Tooltip("开/关各消耗的 AP 数")]
    [Range(0, 3)]
    public int apCostToToggle = 1;

    [Tooltip("切换到「开」时的噪音（0=无声，5=极响）")]
    [Range(0, 5)]
    public int openNoiseLevel = 1;

    [Tooltip("切换到「关」时的噪音")]
    [Range(0, 5)]
    public int closeNoiseLevel = 0;

    [Tooltip("「开」状态时是否仍然阻挡移动\n门打开后通常不阻挡，容器盖打开后可能仍占格")]
    public bool openStateBlocksMovement = false;

    [Tooltip("「开」状态时是否仍然阻挡视线")]
    public bool openStateBlocksVision = false;

    [Tooltip("「开」状态时是否仍然阻挡子弹")]
    public bool openStateBlocksBullets = false;

    [Tooltip("开启动画 Trigger 名")]
    public string openAnimTrigger = "Open";

    [Tooltip("关闭动画 Trigger 名")]
    public string closeAnimTrigger = "Close";

    [Tooltip("开启音效 key")]
    public string openSound = "";

    [Tooltip("关闭音效 key")]
    public string closeSound = "";

    [Tooltip("关门时夹到单位造成的伤害（0=不造成伤害）\n门关上时若有单位在门格，将被推开并受到此伤害")]
    [Range(0, 30)]
    public int slamDamage = 0;

    [Header("旋转动画（门专用）")]
    [Tooltip("铰链位置\n" +
             "Left   = 左铰链，门向右展开\n" +
             "Right  = 右铰链，门向左展开\n" +
             "Center = 以物体中心旋转（旋转门 / 非门物体）\n\n" +
             "半宽由 BoxCollider.size.x / 2 自动读取，无 Collider 时用 gridWidth × cellSize / 2")]
    public HingeSide hingeSide = HingeSide.Left;

    [Tooltip("开门旋转角度（度）\n" +
             "  正值 = 顺时针（俯视），负值 = 逆时针\n" +
             "  典型值：90 或 -90\n" +
             "  0   = 不播旋转动画，改用 Animator Trigger")]
    [Range(-180f, 180f)]
    public float openAngle = 0f;

    [Tooltip("开/关动画时长（秒）")]
    [Range(0.1f, 1f)]
    public float swingDuration = 0.28f;
}

/// <summary>
/// 锁配置（isLockable = true）
/// 通常配合 isToggleable 使用（锁住的门/容器）
/// </summary>
[System.Serializable]
public class LockConfig
{
    [Tooltip("初始是否上锁")]
    public bool startLocked = false;

    [Tooltip("开锁所需的 ItemData ID（0 = 不需要道具，通过其他方式解锁）")]
    public int requiredKeyItemId = 0;

    [Tooltip("是否可以撬锁（不需要钥匙，但消耗更多 AP）")]
    public bool canPickLock = false;

    [Tooltip("撬锁消耗 AP")]
    [Range(1, 5)]
    public int pickLockApCost = 3;

    [Tooltip("撬锁噪音等级")]
    [Range(0, 5)]
    public int pickLockNoiseLevel = 1;
}

/// <summary>
/// 推动配置（isMovable = true）
/// 适用于：木箱、轻型家具、可移动的掩体
/// </summary>
[System.Serializable]
public class PushConfig
{
    [Tooltip("推动一格消耗的 AP 数")]
    [Range(1, 4)]
    public int apCostPerPush = 1;

    [Tooltip("单次可连续推动的最大格数（1 = 只能推一格）")]
    [Range(1, 5)]
    public int maxPushDistance = 1;

    [Tooltip("推动时的噪音（0=无声，5=极响）")]
    [Range(0, 5)]
    public int pushNoiseLevel = 2;

    [Tooltip("推动后物体是否额外提供掩体\n（原本不是掩体但翻倒后能挡子弹）")]
    public bool grantsCoverAfterPush = false;

    [Tooltip("推动音效 key")]
    public string pushSound = "";
}

/// <summary>
/// 允许推倒的方向（世界坐标，Flags 多选）
/// 用于限制物体只能朝特定方向倒：
///   书架贴着南墙 → 只允许 Forward（向北）或 Right | Left（左右）
///   花盆居中摆放 → All（四方向都行）
/// </summary>
[System.Flags]
public enum ToppleDirectionFlags
{
    None    = 0,
    Right   = 1 << 0,  // +X
    Left    = 1 << 1,  // -X
    Forward = 1 << 2,  // +Z
    Back    = 1 << 3,  // -Z
    All     = Right | Left | Forward | Back,
}

/// <summary>
/// 推倒配置（isToppleable = true）
/// 推倒是一次性永久状态变化：物体以底部边缘为轴旋转 90° 倒下，阻挡属性随之改变
/// 适用于：书架、花盆、立灯
/// </summary>
[System.Serializable]
public class ToppleConfig
{
    [Tooltip("推倒消耗的 AP 数")]
    [Range(1, 4)]
    public int apCostToTopple = 2;

    [Tooltip("推倒时的噪音")]
    [Range(0, 5)]
    public int toppleNoiseLevel = 3;

    [Tooltip("允许推倒的方向（多选）\n" +
             "书架贴南墙 → 只勾 Forward / Right / Left\n" +
             "居中摆放 → 保持 All")]
    public ToppleDirectionFlags allowedDirections = ToppleDirectionFlags.All;

    [Tooltip("倒下后是否阻挡移动")]
    public bool toppledBlocksMovement = true;

    [Tooltip("倒下后是否阻挡视线")]
    public bool toppledBlocksVision = false;

    [Tooltip("倒下后是否阻挡子弹（作为新掩体）")]
    public bool toppledBlocksBullets = false;

    [Tooltip("倒下后是否提供掩体")]
    public bool toppledProvidesCover = false;

    [Tooltip("倒下后掩体等级（toppledProvidesCover = true 时生效）")]
    [Range(0, 3)]
    public int toppledCoverLevel = 1;

    [Tooltip("推倒音效 key")]
    public string toppleSound = "";

    [Tooltip("推倒砸到单位时造成的伤害（0=不造成伤害）\n落下时占用格内的单位将被推开并受到此伤害")]
    [Range(0, 50)]
    public int knockbackDamage = 10;

    [Tooltip("推开的格子数（通常为 1）")]
    [Range(1, 3)]
    public int knockbackDistance = 1;
}

/// <summary>
/// 掩体配置（providesCover = true）
/// 适用于：沙袋、木桶、厚重家具、翻倒的桌子
/// </summary>
[System.Serializable]
public class CoverConfig
{
    [Tooltip("掩体防护等级（1~3）\n1=轻掩体（减少 20% 命中）  2=半掩体（50%）  3=全掩体（90%）")]
    [Range(1, 3)]
    public int coverLevel = 1;

    [Tooltip("掩体有效的朝向角（0°=北，顺时针，-1=全方向）")]
    [Range(-1, 360)]
    public int coverDirectionDeg = -1;

    [Tooltip("掩体防弹耐久（被子弹命中后减少，0=不受损）")]
    [Range(0, 200)]
    public int coverDurability = 0;
}

/// <summary>
/// 可破坏配置（isDestroyable = true）
/// 适用于：花盆、玻璃、轻型家具、油桶
/// </summary>
[System.Serializable]
public class DestroyConfig
{
    [Tooltip("物体最大 HP")]
    [Range(1, 500)]
    public int maxHp = 20;

    [Tooltip("破坏后生成的碎片 Prefab（留空则直接消失）")]
    public GameObject debrisPrefab;

    [Tooltip("碎片的阻挡模式")]
    public DebrisBlockMode debrisBlockMode = DebrisBlockMode.NoBlock;

    [Tooltip("碎片存在回合数（0 = 永久，-1 = 立即消失）")]
    public int debrisDuration = 3;

    [Tooltip("破坏特效 Prefab（粒子、爆炸等）")]
    public GameObject destroyVFXPrefab;

    [Tooltip("破坏音效 key")]
    public string destroySound = "";
}

/// <summary>
/// 容器配置（isContainer = true）
/// 适用于：木箱、保险箱、柜子
/// 容器被「打开」(isToggleable) 或「破坏」(isDestroyable) 后可以拾取内容物
/// </summary>
[System.Serializable]
public class ContainerConfig
{
    [Tooltip("容器内固定战利品的 ItemData ID 列表\n（与 ItemData.ID 对应，由 SceneItemInstance 在打开/破坏时生成）")]
    public int[] fixedLootIds = new int[0];

    [Tooltip("随机掉落表 ID（0 = 不使用随机表，仅用固定战利品）\n预留接口，对接 LootTableManager")]
    public int lootTableId = 0;

    [Tooltip("容器是否只能被打开一次（true = 拿空后变空容器）")]
    public bool singleUse = true;
}

/// <summary>
/// 爆炸配置（isExplosive = true）
/// 适用于：油桶、灭火器、燃气罐
/// 可以被射击/破坏触发，也可以主动引爆
/// </summary>
[System.Serializable]
public class ExplosiveConfig
{
    [Tooltip("爆炸伤害")]
    [Range(0, 300)]
    public int explosionDamage = 50;

    [Tooltip("爆炸范围（格数）")]
    [Range(1, 8)]
    public int explosionRadius = 2;

    [Tooltip("HP 归零时自动引爆")]
    public bool explodeOnDestroy = true;

    [Tooltip("是否可以被玩家主动引爆（消耗 1 AP）")]
    public bool canManuallyTrigger = false;

    [Tooltip("是否可以连锁引爆附近其他爆炸物")]
    public bool chainExplode = true;

    [Tooltip("爆炸特效 Prefab")]
    public GameObject explosionVFXPrefab;

    [Tooltip("爆炸音效 key")]
    public string explosionSound = "";

    [Tooltip("爆炸后是否留下碎片/残骸（使用 DestroyConfig.debrisPrefab）")]
    public bool leaveDebris = true;
}

// ══════════════════════════════════════════════════════════════════════
// SceneItemData
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// 场景物品配置数据（ScriptableObject）
///
/// 不可被背包拾取的大型场景交互物：门、推箱子、椅子、书架、油桶、花盆等
///
/// 设计原则：
///   每个布尔开关对应一套独立行为，同一物体可同时勾选多个。
///   例如：木箱 = isMovable + isContainer + isDestroyable
///         金属门 = isToggleable + isLockable + isDestroyable
///         书架   = isToppleable + isDestroyable + blocksVision
///         油桶   = isMovable + isDestroyable + isExplosive
///
/// 运行时由 SceneItemInstance 组件读取此配置来挂载和执行对应行为。
/// </summary>
[CreateAssetMenu(fileName = "SceneItemData", menuName = "SRPG/Item Data/Scene Item", order = 13)]
public class SceneItemData : ItemData
{
    // ── 网格 / 阻挡 ───────────────────────────────────────────────────

    [Header("网格占用（所有场景物体必填）")]
    [Tooltip("站立时 X 方向占用格子数（宽）\n推倒时成为侧向延伸的格数")]
    [Min(1)] public int gridWidth = 1;

    [Tooltip("站立时 Z 方向占用格子数（深）")]
    [Min(1)] public int gridDepth = 1;

    [Tooltip("物体高度（格子数）\n推倒时成为倒向延伸的格数\n" +
             "示例：书架 gridWidth=1, gridDepth=1, gridHeight=2\n" +
             "  → 向东倒后占 2×1 格（东延伸 2 格）")]
    [Min(1)] public int gridHeight = 2;

    [Header("阻挡")]
    [Tooltip("阻挡单位移动（默认开启，Prop 类可关闭）")]
    public bool blocksMovement = true;

    [Tooltip("阻挡 Enemy 视线")]
    public bool blocksVision = false;

    [Tooltip("阻挡子弹穿透（可作掩体）")]
    public bool blocksBullets = true;

    [Tooltip("阻挡声音传播（遮挡噪音感知）")]
    public bool blocksSound = false;

    // ── 交互通用 ─────────────────────────────────────────────────────

    [Header("交互通用")]
    [Tooltip("谁可以与此物体交互\nPlayerOnly / EnemyOnly / Both / None")]
    public SceneInteractableBy interactableBy = SceneInteractableBy.PlayerOnly;

    [Tooltip("交互所需距离（格数，1 = 只能相邻格）")]
    [Range(1, 3)]
    public int interactionRange = 1;

    // ── 功能开关 + 配置块（勾选开关后配置块自动展开）────────────────────
    // 每个 Header 区块：布尔开关 + 对应 Config
    // Config 使用 [ConditionalHide] 在开关为 false 时完全隐藏

    [Header("━ 开关  ━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可开关（门、容器盖、机关、开关）\n切换后阻挡属性由 ToggleConfig 定义")]
    public bool isToggleable = false;
    [ConditionalHide("isToggleable")]
    public ToggleConfig toggleConfig = new ToggleConfig();

    [Header("━ 锁  ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可上锁/解锁（配合 isToggleable 使用）\n需要钥匙道具或撬锁才能操作")]
    public bool isLockable = false;
    [ConditionalHide("isLockable")]
    public LockConfig lockConfig = new LockConfig();

    [Header("━ 推动  ━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可被推动到相邻格（箱子、轻型家具）")]
    public bool isMovable = false;
    [ConditionalHide("isMovable")]
    public PushConfig pushConfig = new PushConfig();

    [Header("━ 推倒  ━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可被推倒（一次性永久状态，阻挡属性随之变化）\n适用于书架、花盆、立灯")]
    public bool isToppleable = false;
    [ConditionalHide("isToppleable")]
    public ToppleConfig toppleConfig = new ToppleConfig();

    [Header("━ 掩体  ━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("此物体提供掩体保护（沙袋、翻倒桌等）")]
    public bool providesCover = false;
    [ConditionalHide("providesCover")]
    public CoverConfig coverConfig = new CoverConfig();

    [Header("━ 可破坏  ━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可被攻击或主动摧毁（花盆、玻璃、木板）")]
    public bool isDestroyable = false;
    [ConditionalHide("isDestroyable")]
    public DestroyConfig destroyConfig = new DestroyConfig();

    [Header("━ 容器  ━━━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("内含可拾取物品（木箱、保险箱、柜子）\n打开或破坏后可获取战利品")]
    public bool isContainer = false;
    [ConditionalHide("isContainer")]
    public ContainerConfig containerConfig = new ContainerConfig();

    [Header("━ 爆炸物  ━━━━━━━━━━━━━━━━━━━━━━")]
    [Tooltip("可爆炸产生 AoE 伤害（油桶、灭火器、燃气罐）\n通常配合 isDestroyable 使用")]
    public bool isExplosive = false;
    [ConditionalHide("isExplosive")]
    public ExplosiveConfig explosiveConfig = new ExplosiveConfig();

    // ── Animator / VFX ────────────────────────────────────────────────

    [Header("动画 / 特效")]
    [Tooltip("Animator Controller（含开关、推倒、破坏等动画片段）")]
    public RuntimeAnimatorController animatorController;

    [Tooltip("状态切换通用特效 Prefab（在物体位置播放）")]
    public GameObject stateChangeVFXPrefab;

    // ── 剧情 / 任务 ───────────────────────────────────────────────────

    [Header("剧情 / 任务")]
    [Tooltip("场景唯一 ID，对应 WorldItem.objectId\n供 MissionCondition.StoryObjectState 查找\n普通家具留空")]
    public string sceneObjectId = "";

    [Tooltip("交互后是否写入 StoryFlag（触发剧情用）")]
    public bool writesStoryFlag = false;

    [Tooltip("写入的 StoryFlag key（writesStoryFlag = true 时填写）")]
    public string storyFlagKey = "";

    // ── 便捷查询 ─────────────────────────────────────────────────────

    /// <summary>此物体是否有任何可执行的交互</summary>
    public bool HasAnyInteraction => interactableBy != SceneInteractableBy.None
                                      && (isToggleable || isMovable || isToppleable
                                          || isDestroyable || isContainer || isExplosive);

    /// <summary>玩家可以操作</summary>
    public bool PlayerCanInteract => HasAnyInteraction
                                      && interactableBy != SceneInteractableBy.EnemyOnly;

    /// <summary>Enemy AI 可以操作</summary>
    public bool EnemyCanInteract  => HasAnyInteraction
                                      && interactableBy != SceneInteractableBy.PlayerOnly;

    // 安全获取各配置块（未启用时返回 null，调用方可做 null 检查）
    public ToggleConfig    ToggleCfg    => isToggleable  ? toggleConfig    : null;
    public LockConfig      LockCfg      => isLockable    ? lockConfig      : null;
    public PushConfig      PushCfg      => isMovable     ? pushConfig      : null;
    public ToppleConfig    ToppleCfg    => isToppleable  ? toppleConfig    : null;
    public CoverConfig     CoverCfg     => providesCover ? coverConfig     : null;
    public DestroyConfig   DestroyCfg   => isDestroyable ? destroyConfig   : null;
    public ContainerConfig ContainerCfg => isContainer   ? containerConfig : null;
    public ExplosiveConfig ExplosiveCfg => isExplosive   ? explosiveConfig : null;

    // ── 构造 ─────────────────────────────────────────────────────────

    public SceneItemData()
    {
        Type        = ItemType.SceneItem;
        UseCost     = 0;
        isThrowable = false;
        maxStack    = 1;
    }

    // ── 编辑器验证 ────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 网格尺寸最小值保障（旧 .asset 反序列化时 int 默认为 0，强制纠正）
        if (gridWidth  < 1) gridWidth  = 1;
        if (gridDepth  < 1) gridDepth  = 1;
        if (gridHeight < 1) gridHeight = 2; // 高度默认 2 格（普通家具/书架）

        // isLockable 必须配合 isToggleable 使用
        if (isLockable && !isToggleable)
            UnityEngine.Debug.LogWarning($"[SceneItemData] {name}：isLockable=true 但 isToggleable=false，锁没有意义");

        // isExplosive 通常配合 isDestroyable 使用
        if (isExplosive && !isDestroyable)
            UnityEngine.Debug.LogWarning($"[SceneItemData] {name}：isExplosive=true 但 isDestroyable=false，" +
                                          "爆炸物通常可被破坏触发，请确认是否需要勾选 isDestroyable");

        // sceneObjectId 不允许含空格
        if (!string.IsNullOrEmpty(sceneObjectId) && sceneObjectId.Contains(" "))
            UnityEngine.Debug.LogWarning($"[SceneItemData] {name}：sceneObjectId 不能含空格");
    }
#endif

    public override string GetDetailedInfo()
    {
        string info = $"{Name}\n{description}";
        info += $"\n网格: 1×1";
        if (blocksMovement) info += " ■移动";
        if (blocksVision)   info += " ■视线";
        if (blocksBullets)  info += " ■子弹";
        if (blocksSound)    info += " ■声音";

        if (isToggleable)  info += $"\n[开关] AP={toggleConfig.apCostToToggle}  噪音={toggleConfig.openNoiseLevel}";
        if (isLockable)    info += $"  🔒{(lockConfig.canPickLock ? "(可撬)" : "")}";
        if (isMovable)     info += $"\n[推动] AP×{pushConfig.apCostPerPush}  最远{pushConfig.maxPushDistance}格";
        if (isToppleable)  info += $"\n[推倒] AP={toppleConfig.apCostToTopple}  噪音={toppleConfig.toppleNoiseLevel}";
        if (providesCover) info += $"\n[掩体] 等级{coverConfig.coverLevel}";
        if (isDestroyable) info += $"\n[耐久] HP={destroyConfig.maxHp}";
        if (isContainer)   info += $"\n[容器] {containerConfig.fixedLootIds.Length} 件战利品";
        if (isExplosive)   info += $"\n[爆炸] 伤害={explosiveConfig.explosionDamage}  范围={explosiveConfig.explosionRadius}格";

        return info;
    }
}