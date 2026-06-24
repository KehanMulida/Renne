using UnityEngine;

public enum ItemType
{
    Consumable,
    Weapon,
    Equipment,
    KeyItem,
    Material,
    SceneItem
}

public enum ItemRarity
{
    Common,
    Rare,
    Legendary
}

public enum FlightMode
{
    Arc,
    Straight,
}

public enum FireMode
{
    SemiAuto,  // 半自动：每次 LMB 射一次（手枪、狙击）
    Burst,     // 点射：每次 LMB 连发 burstCount 发（突击步枪）
    FullAuto,  // 全自动：按住 LMB 持续射击，受 fireInterval 控制射速（机枪）
    Shotgun,   // 散弹：每次 LMB 同时发射 pelletsPerShot 颗弹丸
}

/// <summary>物品可注册的主动使用功能（用于径向菜单条目生成）</summary>
public enum ItemFunction
{
    Melee,   // 近战：以物品为武器攻击相邻格
    Throw,   // 投掷：弧线/直线抛出
    Consume, // 使用/食用：即时消耗触发效果
    Shoot,   // 射击：武器开枪（消耗弹药）
}

// ── 投掷配置 ────────────────────────────────────────────────────────────────

[System.Serializable]
public class ThrowableConfig
{
    [Header("飞行配置")]
    public FlightMode flightMode = FlightMode.Arc;
    [Range(1, 20)] public int throwRange = 6;
    [Range(1f, 30f)] public float flightSpeed = 10f;
    [Range(0f, 5f)] public float arcHeight = 2f;

    [Header("伤害配置")]
    [Range(0, 200)] public int directDamage = 20;
    [Range(0, 5)] public int splashRadius = 0;
    [Range(0, 100)] public int splashDamage = 0;
    public bool canDamageSelf = false;

    [Header("碰撞配置")]
    public LayerMask hitLayer;

    [Header("落地行为")]
    public bool breakOnImpact = true;
    public bool canPickupAfterThrow = false;
    public GameObject impactVFXPrefab;
    public GameObject debrisPrefab;

    [Header("声音")]
    public string throwSound = "";
    public string impactSound = "";

    [Header("烟雾效果（烟雾罐）")]
    public bool isSmokeGrenade = false;
    [Tooltip("烟雾最少持续回合数")]
    [Range(1, 10)] public int smokeMinTurns = 2;
    [Tooltip("烟雾最多持续回合数（实际回合数在此区间随机）")]
    [Range(1, 10)] public int smokeMaxTurns = 4;
    [Tooltip("烟雾半径（格）")]
    [Range(1, 8)] public int smokeRadius = 3;

    [Header("声音诱饵（诱饵投掷物）")]
    public bool isDecoy = false;
    [Tooltip("诱饵噪音等级（越高越容易被 AI 听到）")]
    [Range(1, 5)] public int decoyNoiseLevel = 4;
    [Tooltip("诱饵持续发声时长（秒）")]
    [Range(1f, 30f)] public float decoyDuration = 5f;
    [Tooltip("诱饵发声间隔（秒）")]
    [Range(0.5f, 5f)] public float decoyInterval = 1.5f;

    public bool HasSplash    => splashRadius > 0 && splashDamage > 0;
    public bool IsArc        => flightMode == FlightMode.Arc;
    public bool IsPickupable => !breakOnImpact && canPickupAfterThrow;
}

// ── SceneItem 枚举 & 配置块 ─────────────────────────────────────────────────

public enum SceneInteractableBy { PlayerOnly, EnemyOnly, Both, None }
public enum DebrisBlockMode     { NoBlock, BlockMovement }
public enum HingeSide           { Left, Right, Center }

[System.Flags]
public enum ToppleDirectionFlags
{
    None    = 0,
    Right   = 1 << 0,
    Left    = 1 << 1,
    Forward = 1 << 2,
    Back    = 1 << 3,
    All     = Right | Left | Forward | Back,
}

[System.Serializable]
public class ToggleConfig
{
    [Header("开关")]
    public bool startOpen = false;
    [Range(0, 3)] public int apCostToToggle = 1;
    [Range(0, 5)] public int openNoiseLevel = 1;
    [Range(0, 5)] public int closeNoiseLevel = 0;
    public bool openStateBlocksMovement = false;
    public bool openStateBlocksVision = false;
    public bool openStateBlocksBullets = false;
    public string openAnimTrigger = "Open";
    public string closeAnimTrigger = "Close";
    public string openSound = "";
    public string closeSound = "";
    [Range(0, 30)] public int slamDamage = 0;

    [Header("旋转动画（门专用）")]
    public HingeSide hingeSide = HingeSide.Left;
    [Range(-180f, 180f)] public float openAngle = 0f;
    [Range(0.1f, 1f)] public float swingDuration = 0.28f;
}

[System.Serializable]
public class LockConfig
{
    public bool startLocked = false;
    public int requiredKeyItemId = 0;
    public bool canPickLock = false;
    [Range(1, 5)] public int pickLockApCost = 3;
    [Range(0, 5)] public int pickLockNoiseLevel = 1;
}

[System.Serializable]
public class PushConfig
{
    [Range(1, 4)] public int apCostPerPush = 1;
    [Range(1, 5)] public int maxPushDistance = 1;
    [Range(0, 5)] public int pushNoiseLevel = 2;
    public bool grantsCoverAfterPush = false;
    public string pushSound = "";
}

[System.Serializable]
public class ToppleConfig
{
    [Range(1, 4)] public int apCostToTopple = 2;
    [Range(0, 5)] public int toppleNoiseLevel = 3;
    public ToppleDirectionFlags allowedDirections = ToppleDirectionFlags.All;
    public bool toppledBlocksMovement = true;
    public bool toppledBlocksVision = false;
    public bool toppledBlocksBullets = false;
    public bool toppledProvidesCover = false;
    [Range(0, 3)] public int toppledCoverLevel = 1;
    public string toppleSound = "";
    [Range(0, 50)] public int knockbackDamage = 10;
    [Range(1, 3)] public int knockbackDistance = 1;
}

[System.Serializable]
public class CoverConfig
{
    [Range(1, 3)] public int coverLevel = 1;
    [Range(-1, 360)] public int coverDirectionDeg = -1;
    [Range(0, 200)] public int coverDurability = 0;
}

[System.Serializable]
public class DestroyConfig
{
    [Range(1, 500)] public int maxHp = 20;
    public GameObject debrisPrefab;
    public DebrisBlockMode debrisBlockMode = DebrisBlockMode.NoBlock;
    public int debrisDuration = 3;
    public GameObject destroyVFXPrefab;
    public string destroySound = "";
}

[System.Serializable]
public class ContainerConfig
{
    public int[] fixedLootIds = new int[0];
    public int lootTableId = 0;
    public bool singleUse = true;
}

[System.Serializable]
public class ExplosiveConfig
{
    [Range(0, 300)] public int explosionDamage = 50;
    [Range(1, 8)] public int explosionRadius = 2;
    public bool explodeOnDestroy = true;
    public bool canManuallyTrigger = false;
    public bool chainExplode = true;
    public GameObject explosionVFXPrefab;
    public string explosionSound = "";
    public bool leaveDebris = true;
}

// ── 乘坐位移配置 ────────────────────────────────────────────────────────────

[System.Serializable]
public class RideConfig
{
    [Header("滑行参数")]
    [Range(1, 15)] public int   maxSlideCells  = 4;   // 一次最多滑行格数
    [Range(0, 3)]  public int   apCostToBoard  = 0;   // 上车消耗 AP
    [Range(0, 3)]  public int   apCostPerCell  = 1;   // 每格消耗 AP
    [Range(1f, 20f)] public float slideSpeed   = 6f;  // 动画速度（格/秒）
    public bool allowDiagonal = false;                 // 允许斜向滑行（8方向）
    [Range(0, 5)] public int slideNoiseLevel = 2;      // 滑行产生的噪音等级

    [Header("骑手偏移（相对于载具中心）")]
    public Vector3 riderOffset = new Vector3(0f, 0.2f, 0f);

    [Header("动画 Trigger（留空跳过）")]
    public string boardAnimTrigger    = "";
    public string slideAnimTrigger    = "";
    public string dismountAnimTrigger = "";
}

// ── ItemData（统一物品基类）─────────────────────────────────────────────────

/// <summary>
/// 所有可背包物品的统一数据类。
/// 在 Inspector 里切换 Type，对应的配置块自动显示/隐藏。
/// SceneItem 因不进入背包而保留语义上的独立，但字段合并在同一 SO 里。
/// </summary>
[CreateAssetMenu(fileName = "ItemData", menuName = "SRPG/Item Data/Item", order = 10)]
public class ItemData : ScriptableObject
{
    // ── 基础信息（所有类型共用）────────────────────────────────────────────

    [Header("基础信息")]
    public int    ID          = 2001;
    public string Name        = "物品";
    [TextArea(2, 4)]
    public string description = "物品描述";
    public Sprite Icon; // UGUI 物品格子图标（可空，显示时用默认占位图）

    [Header("分类")]
    public ItemType   Type   = ItemType.Consumable;
    public ItemRarity rarity = ItemRarity.Common;

    [Header("使用")]
    public int    UseCost    = 1;
    public string EffectType = "";
    public int    EffectValue = 0;

    [Header("音效 / 特效")]
    public string Sound     = "";
    public string Animation = "";
    public string VFX       = "";

    [Header("视觉")]
    public Sprite     icon;
    public GameObject Prefab;

    [Header("属性")]
    public float weight   = 1f;
    public int   value    = 10;
    public int   maxStack = 1;

    [Header("投掷配置")]
    public bool isThrowable = false;
    [ConditionalHide("isThrowable")]
    public ThrowableConfig throwableConfig = new ThrowableConfig();

    // ── Type 镜像 bool（供 ConditionalHide 使用，不在 Inspector 显示）────────

    [HideInInspector] [SerializeField] private bool _isConsumable;
    [HideInInspector] [SerializeField] private bool _isWeapon;
    [HideInInspector] [SerializeField] private bool _isSceneItem;
    [HideInInspector] [SerializeField] private bool _isEquipment;

    // ── 消耗品字段（Type == Consumable 时显示）──────────────────────────────

    [ConditionalHide("_isConsumable")] public int       healAmount    = 0;
    [ConditionalHide("_isConsumable")] public int       staminaAmount = 0;
    [ConditionalHide("_isConsumable")] public int       sanityAmount  = 0;
    [ConditionalHide("_isConsumable")] public int       damageAmount  = 0; // 使用后扣除 HP
    [ConditionalHide("_isConsumable")] public int       sanityDrain   = 0; // 使用后扣除精神值
    [ConditionalHide("_isConsumable")] public int       meleeDamage   = 0;
    [ConditionalHide("_isConsumable")] public int       meleeRange    = 0;
    [ConditionalHide("_isConsumable")] public LayerMask meleeLayer;
    [Tooltip("使用后临时增加 AP（当前回合立即生效）")]
    [ConditionalHide("_isConsumable")] public int       apBonus       = 0;

    // ── 装备字段（Type == Equipment 时显示）──────────────────────────────────

    [Header("  防具参数")]
    [Tooltip("伤害减少百分比（0~50）")]
    [ConditionalHide("_isEquipment")] [Range(0, 50)] public int damageReduction = 20;
    [Tooltip("耐久度（-1 = 无限）")]
    [ConditionalHide("_isEquipment")] public int armorDurability = 50;

    // ── 武器字段（Type == Weapon 时显示）────────────────────────────────────

    [ConditionalHide("_isWeapon")] public string   WeaponType         = "Gun";
    [ConditionalHide("_isWeapon")] public FireMode fireMode           = FireMode.SemiAuto;

    [Header("  弹药")]
    [ConditionalHide("_isWeapon")] public bool     IshasBullet        = true;
    [ConditionalHide("_isWeapon")] public int      MaxBullet          = 7;
    [ConditionalHide("_isWeapon")] public int      ReloadApCost       = 1;

    [Header("  射击参数")]
    [ConditionalHide("_isWeapon")] public int      Damage             = 20;
    [ConditionalHide("_isWeapon")] public int      AttackRange        = 5;
    [ConditionalHide("_isWeapon")] public int      Accuracy           = 70;
    [ConditionalHide("_isWeapon")] public float    MaxSpreadAngle     = 20f;
    [ConditionalHide("_isWeapon")] public int      CriticalChance     = 10;
    [ConditionalHide("_isWeapon")] public float    CriticalMultiplier = 2f;

    [Header("  点射 / 散弹")]
    [Tooltip("Burst：每次扣动扳机连发数；SemiAuto/FullAuto 填 1")]
    [ConditionalHide("_isWeapon")] public int      burstCount         = 1;
    [Tooltip("Shotgun：每次射击同时飞出的弹丸数")]
    [ConditionalHide("_isWeapon")] public int      pelletsPerShot     = 1;
    [Tooltip("散弹弹丸的额外散布角度（叠加在 MaxSpreadAngle 之上）")]
    [ConditionalHide("_isWeapon")] public float    pelletSpreadAngle  = 10f;
    [Tooltip("全自动 / 点射的射击间隔（秒），决定射速；SemiAuto 填 0")]
    [ConditionalHide("_isWeapon")] public float    fireInterval       = 0.15f;

    [Header("  噪音 & 弹道")]
    [ConditionalHide("_isWeapon")] public int      NoiceLevel         = 2;
    [ConditionalHide("_isWeapon")] public float    BulletSpeed        = 30f;
    [ConditionalHide("_isWeapon")] public float    BulletMaxDistance  = 50f;
    [ConditionalHide("_isWeapon")] public LayerMask weaponHitLayer;

    [Header("  特效 & 预制体")]
    [ConditionalHide("_isWeapon")] public GameObject BulletPrefab;
    [ConditionalHide("_isWeapon")] public GameObject HitVFXPrefab;
    [ConditionalHide("_isWeapon")] public GameObject MuzzleVFXPrefab;

    // ── 场景物品字段（Type == SceneItem 时显示）─────────────────────────────

    [ConditionalHide("_isSceneItem")] [Min(1)] public int gridWidth  = 1;
    [ConditionalHide("_isSceneItem")] [Min(1)] public int gridDepth  = 1;
    [ConditionalHide("_isSceneItem")] [Min(1)] public int gridHeight = 2;

    [ConditionalHide("_isSceneItem")] public bool blocksMovement = true;
    [ConditionalHide("_isSceneItem")] public bool blocksVision   = false;
    [ConditionalHide("_isSceneItem")] public bool blocksBullets  = true;
    [ConditionalHide("_isSceneItem")] public bool blocksSound    = false;

    [ConditionalHide("_isSceneItem")] public SceneInteractableBy interactableBy  = SceneInteractableBy.PlayerOnly;
    [ConditionalHide("_isSceneItem")] [Range(1, 3)] public int   interactionRange = 1;

    [ConditionalHide("_isSceneItem")]                    public bool         isToggleable = false;
    [ConditionalHide("_isSceneItem", "isToggleable")]    public ToggleConfig toggleConfig = new ToggleConfig();

    [ConditionalHide("_isSceneItem")]                    public bool       isLockable  = false;
    [ConditionalHide("_isSceneItem", "isLockable")]      public LockConfig lockConfig  = new LockConfig();

    [ConditionalHide("_isSceneItem")]                    public bool       isMovable   = false;
    [ConditionalHide("_isSceneItem", "isMovable")]       public PushConfig pushConfig  = new PushConfig();

    [ConditionalHide("_isSceneItem")]                    public bool         isToppleable = false;
    [ConditionalHide("_isSceneItem", "isToppleable")]    public ToppleConfig toppleConfig = new ToppleConfig();

    [ConditionalHide("_isSceneItem")]                    public bool        providesCover = false;
    [ConditionalHide("_isSceneItem", "providesCover")]   public CoverConfig coverConfig   = new CoverConfig();

    [ConditionalHide("_isSceneItem")]                    public bool          isDestroyable = false;
    [ConditionalHide("_isSceneItem", "isDestroyable")]   public DestroyConfig destroyConfig = new DestroyConfig();

    [ConditionalHide("_isSceneItem")]                    public bool             isContainer     = false;
    [ConditionalHide("_isSceneItem", "isContainer")]     public ContainerConfig  containerConfig = new ContainerConfig();

    [ConditionalHide("_isSceneItem")]                    public bool             isExplosive     = false;
    [ConditionalHide("_isSceneItem", "isExplosive")]     public ExplosiveConfig  explosiveConfig = new ExplosiveConfig();

    [ConditionalHide("_isSceneItem")]                    public bool       isRideable  = false;
    [ConditionalHide("_isSceneItem", "isRideable")]      public RideConfig rideConfig  = new RideConfig();

    [ConditionalHide("_isSceneItem")] public RuntimeAnimatorController animatorController;
    [ConditionalHide("_isSceneItem")] public GameObject stateChangeVFXPrefab;

    [ConditionalHide("_isSceneItem")] public string sceneObjectId   = "";
    [ConditionalHide("_isSceneItem")] public bool   writesStoryFlag = false;
    [ConditionalHide("_isSceneItem")] public string storyFlagKey    = "";

    // ── 物品功能枚举与注册 ────────────────────────────────────────────────

    /// <summary>
    /// 返回此物品当前支持的所有主动功能（用于 R 键径向菜单）。
    /// 顺序即菜单排列顺序。
    /// </summary>
    public System.Collections.Generic.List<ItemFunction> GetAvailableFunctions()
    {
        var list = new System.Collections.Generic.List<ItemFunction>();

        if (Type == ItemType.Weapon)
        {
            list.Add(ItemFunction.Shoot);
            return list;
        }

        if (Type == ItemType.Equipment)
        {
            list.Add(ItemFunction.Consume); // "穿上"防具
            return list;
        }

        if (Type != ItemType.Consumable) return list;
        if (meleeDamage > 0) list.Add(ItemFunction.Melee);
        if (isThrowable)     list.Add(ItemFunction.Throw);
        list.Add(ItemFunction.Consume);
        return list;
    }

    // ── 便捷查询 ──────────────────────────────────────────────────────────

    public bool IsThrowable => isThrowable;
    public ThrowableConfig ThrowCfg => isThrowable ? throwableConfig : null;

    // SceneItem convenience properties
    public bool HasAnyInteraction => interactableBy != SceneInteractableBy.None
                                      && (isToggleable || isMovable || isToppleable
                                          || isDestroyable || isContainer || isExplosive || isRideable);
    public bool PlayerCanInteract => HasAnyInteraction && interactableBy != SceneInteractableBy.EnemyOnly;
    public bool EnemyCanInteract  => HasAnyInteraction && interactableBy != SceneInteractableBy.PlayerOnly;

    public ToggleConfig    ToggleCfg    => isToggleable  ? toggleConfig    : null;
    public LockConfig      LockCfg      => isLockable    ? lockConfig      : null;
    public PushConfig      PushCfg      => isMovable     ? pushConfig      : null;
    public ToppleConfig    ToppleCfg    => isToppleable  ? toppleConfig    : null;
    public CoverConfig     CoverCfg     => providesCover ? coverConfig     : null;
    public DestroyConfig   DestroyCfg   => isDestroyable ? destroyConfig   : null;
    public ContainerConfig ContainerCfg => isContainer   ? containerConfig : null;
    public ExplosiveConfig ExplosiveCfg => isExplosive   ? explosiveConfig : null;
    public RideConfig      RideCfg      => isRideable    ? rideConfig      : null;

    // ── 武器方法 ──────────────────────────────────────────────────────────

    public float CalculateSpreadAngle()
    {
        float inaccuracy = 1f - Accuracy / 100f;
        return UnityEngine.Random.Range(-inaccuracy * MaxSpreadAngle, inaccuracy * MaxSpreadAngle);
    }

    public bool RollCritical() => UnityEngine.Random.Range(0, 100) < CriticalChance;

    public int CalculateDamage() =>
        RollCritical() ? Mathf.RoundToInt(Damage * CriticalMultiplier) : Damage;

    // ── GetDetailedInfo ───────────────────────────────────────────────────

    public virtual string GetDetailedInfo()
    {
        string info = $"{Name}\n{description}";

        switch (Type)
        {
            case ItemType.Consumable:
                if (healAmount > 0 || staminaAmount > 0 || sanityAmount > 0)
                {
                    info += "\n\n<b>直接使用效果</b>";
                    if (healAmount > 0)    info += $"\n恢复生命: +{healAmount}";
                    if (staminaAmount > 0) info += $"\n恢复体力: +{staminaAmount}";
                    if (sanityAmount > 0)  info += $"\n恢复精神: +{sanityAmount}";
                }
                if (meleeDamage > 0)
                {
                    info += "\n\n<b>近战效果</b>";
                    info += $"\n近战伤害: {meleeDamage}";
                    if (meleeRange > 0) info += $"\n范围: {meleeRange} 格";
                }
                break;

            case ItemType.Equipment:
                info += "\n\n<b>防具属性</b>";
                info += $"\n伤害减免: {damageReduction}%";
                info += armorDurability < 0 ? "\n耐久: 无限" : $"\n耐久: {armorDurability}";
                break;

            case ItemType.Weapon:
                info += "\n\n<b>武器属性</b>";
                info += $"\n类型: {WeaponType}";
                info += $"\n伤害: {Damage}";
                info += $"\n射程: {AttackRange}";
                info += $"\n精准度: {Accuracy}% (散布 ±{MaxSpreadAngle}°)";
                info += $"\n暴击: {CriticalChance}% x{CriticalMultiplier}";
                if (IshasBullet) info += $"\n弹药: {MaxBullet}";
                info += $"\n声音: {NoiceLevel}";
                break;

            case ItemType.SceneItem:
                info += $"\n网格: {gridWidth}x{gridDepth}";
                if (blocksMovement) info += " ■移动";
                if (blocksVision)   info += " ■视线";
                if (blocksBullets)  info += " ■子弹";
                if (blocksSound)    info += " ■声音";
                if (isToggleable)  info += $"\n[开关] AP={toggleConfig.apCostToToggle}";
                if (isLockable)    info += isToggleable ? " [上锁]" : "\n[上锁]";
                if (isMovable)     info += $"\n[推动] AP×{pushConfig.apCostPerPush}";
                if (isToppleable)  info += $"\n[推倒] AP={toppleConfig.apCostToTopple}";
                if (providesCover) info += $"\n[掩体] 等级{coverConfig.coverLevel}";
                if (isDestroyable) info += $"\n[耐久] HP={destroyConfig.maxHp}";
                if (isContainer)   info += $"\n[容器] {containerConfig.fixedLootIds.Length} 件";
                if (isExplosive)   info += $"\n[爆炸] 伤害={explosiveConfig.explosionDamage}";
                if (isRideable)    info += $"\n[乘坐] 花费{UseCost}AP 最多{rideConfig.maxSlideCells}格";
                break;
        }

        if (IsThrowable && ThrowCfg != null)
        {
            info += "\n\n<b>投掷属性</b>";
            info += $"\n射程: {ThrowCfg.throwRange} 格  直接伤害: {ThrowCfg.directDamage}";
            if (ThrowCfg.HasSplash)
                info += $"\n溅射: {ThrowCfg.splashRadius} 格 / {ThrowCfg.splashDamage}";
        }

        return info;
    }

    // ── 颜色 ──────────────────────────────────────────────────────────────

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

    // ── 编辑器 OnValidate（更新隐藏 bool 驱动 ConditionalHide）────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        _isConsumable = Type == ItemType.Consumable;
        _isWeapon     = Type == ItemType.Weapon;
        _isSceneItem  = Type == ItemType.SceneItem;
        _isEquipment  = Type == ItemType.Equipment;

        // 值域 clamp（替代 [Range] 与 [ConditionalHide] 的 PropertyDrawer 冲突）
        Accuracy          = Mathf.Clamp(Accuracy, 0, 100);
        MaxSpreadAngle    = Mathf.Clamp(MaxSpreadAngle, 0f, 45f);
        CriticalChance    = Mathf.Clamp(CriticalChance, 0, 100);
        burstCount        = Mathf.Max(1, burstCount);
        pelletsPerShot    = Mathf.Max(1, pelletsPerShot);
        pelletSpreadAngle = Mathf.Clamp(pelletSpreadAngle, 0f, 45f);
        fireInterval      = Mathf.Max(0f, fireInterval);
        meleeRange     = Mathf.Clamp(meleeRange, 0, 3);
        AttackRange    = Mathf.Max(AttackRange, 0);
        Damage         = Mathf.Max(Damage, 0);
        healAmount      = Mathf.Max(healAmount, 0);
        staminaAmount   = Mathf.Max(staminaAmount, 0);
        sanityAmount    = Mathf.Max(sanityAmount, 0);
        meleeDamage     = Mathf.Max(meleeDamage, 0);
        apBonus         = Mathf.Max(apBonus, 0);
        damageReduction = Mathf.Clamp(damageReduction, 0, 50);
        if (armorDurability < -1) armorDurability = -1;

        if (gridWidth  < 1) gridWidth  = 1;
        if (gridDepth  < 1) gridDepth  = 1;
        if (gridHeight < 1) gridHeight = 2;

        if (isLockable && !isToggleable)
            Debug.LogWarning($"[ItemData] {name}: isLockable=true 但 isToggleable=false");
        if (throwableConfig.smokeMinTurns > throwableConfig.smokeMaxTurns)
            throwableConfig.smokeMaxTurns = throwableConfig.smokeMinTurns;
        if (isExplosive && !isDestroyable)
            Debug.LogWarning($"[ItemData] {name}: isExplosive=true 但 isDestroyable=false");
        if (!string.IsNullOrEmpty(sceneObjectId) && sceneObjectId.Contains(" "))
            Debug.LogWarning($"[ItemData] {name}: sceneObjectId 不能含空格");
    }
#endif
}

// ── 向后兼容空子类（现有 .asset 文件不需要重配置）──────────────────────────

/// <summary>消耗品 — 构造函数设 Type，字段全在 ItemData</summary>
[CreateAssetMenu(fileName = "ConsumableData", menuName = "SRPG/Item Data/Consumable", order = 12)]
public class ConsumableData : ItemData
{
    public ConsumableData() { Type = ItemType.Consumable; UseCost = 0; maxStack = 10; }
}

/// <summary>武器 — 构造函数设 Type，字段全在 ItemData</summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "SRPG/Item Data/Weapon", order = 11)]
public class WeaponData : ItemData
{
    public WeaponData() { Type = ItemType.Weapon; UseCost = 1; }
}

/// <summary>场景物品 — 构造函数设 Type，字段全在 ItemData</summary>
[CreateAssetMenu(fileName = "SceneItemData", menuName = "SRPG/Item Data/Scene Item", order = 13)]
public class SceneItemData : ItemData
{
    public SceneItemData() { Type = ItemType.SceneItem; UseCost = 0; isThrowable = false; maxStack = 1; }
}
