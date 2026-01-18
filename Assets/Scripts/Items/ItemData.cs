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
/// 物品基础配置数据
/// 所有物品的共享基类
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
    public int UseCost = 1;             // 使用回合（0=即可使用，1=消耗1回合）
    public string EffectType = "";      // 效果类型（如"HealHP"）
    public int EffectValue = 0;         // 效果数值

    [Header("音效和特效")]
    public string Sound = "";           // 使用音效名称
    public string Animation = "";       // 使用动画名称
    public string VFX = "";             // 视觉特效名称

    [Header("视觉")]
    public Sprite icon;                 // 物品图标
    public GameObject Prefab;           // 3D模型预制体

    [Header("属性")]
    public float weight = 1f;           // 重量
    public int value = 10;              // 价值
    public int maxStack = 1;            // 堆叠上限

    /// <summary>
    /// 获取稀有度颜色
    /// </summary>
    public Color GetRarityColor()
    {
        switch (rarity)
        {
            case ItemRarity.Common: return Color.white;
            case ItemRarity.Rare: return new Color(0.3f, 0.6f, 1f); // 蓝色
            case ItemRarity.Legendary: return new Color(1f, 0.8f, 0f); // 金色
            default: return Color.white;
        }
    }

    /// <summary>
    /// 虚方法：子类可以重写
    /// </summary>
    public virtual string GetDetailedInfo()
    {
        return $"{Name}\n{description}";
    }
}

/// <summary>
/// 武器配置数据（扩展自ItemData）
/// </summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "SRPG/Item Data/Weapon", order = 11)]
public class WeaponData : ItemData
{
    [Header("武器专属属性")]
    public string WeaponType = "Gun";       // 武器种类（近战、射击）
    public int Damage = 20;                 // 伤害
    public int AttackRange = 5;             // 攻击距离
    public bool IshasBullet = true;         // 是否有子弹
    public int MaxBullet = 7;               // 最大子弹量
    public int NoiceLevel = 2;              // 声音播报度
    public int Accuracy = 70;               // 武器精准度

    [Header("子弹预制体")]
    public GameObject BulletPrefab;         // 子弹预制体

    public WeaponData()
    {
        Type = ItemType.Weapon;
        UseCost = 1;  // 攻击消耗1回合
    }

    public override string GetDetailedInfo()
    {
        string info = base.GetDetailedInfo();
        info += $"\n\n<b>武器属性</b>";
        info += $"\n类型: {WeaponType}";
        info += $"\n伤害: {Damage}";
        info += $"\n射程: {AttackRange}";
        info += $"\n精准度: {Accuracy}%";
        
        if (IshasBullet)
        {
            info += $"\n弹药: {MaxBullet}";
        }
        
        info += $"\n声音: {NoiceLevel}";
        
        return info;
    }
}

/// <summary>
/// 消耗品配置数据（扩展自ItemData）
/// </summary>
[CreateAssetMenu(fileName = "ConsumableData", menuName = "SRPG/Item Data/Consumable", order = 12)]
public class ConsumableData : ItemData
{
    [Header("消耗品专属属性")]
    public int healAmount = 0;          // 恢复生命
    public int staminaAmount = 0;       // 恢复体力
    public int sanityAmount = 0;        // 恢复精神

    public ConsumableData()
    {
        Type = ItemType.Consumable;
        UseCost = 0;  // 使用不消耗回合
        maxStack = 10;
    }

    public override string GetDetailedInfo()
    {
        string info = base.GetDetailedInfo();
        info += $"\n\n<b>效果</b>";
        
        if (healAmount > 0)
            info += $"\n恢复生命: +{healAmount}";
        if (staminaAmount > 0)
            info += $"\n恢复体力: +{staminaAmount}";
        if (sanityAmount > 0)
            info += $"\n恢复精神: +{sanityAmount}";
        
        return info;
    }
}