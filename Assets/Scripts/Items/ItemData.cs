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
    Material        // 材料
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