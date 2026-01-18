using UnityEngine;

/// <summary>
/// 玩家配置数据（ScriptableObject）
/// 职责：
/// 1. 存储玩家的所有配置数据
/// 2. 可以在Inspector中编辑
/// 3. 可以创建多个配置文件（不同难度、角色等）
/// 特点：
/// - 数据与逻辑分离
/// - 易于调整平衡性
/// - 可序列化保存
/// 使用方法：
/// 1. Project右键 > Create > SRPG > Player Config
/// 2. 配置各项数值
/// 3. 拖入到PlayerController或UnitMovement
/// </summary>
[CreateAssetMenu(fileName = "PlayerConfig", menuName = "SRPG/Player Config", order = 1)]
public class PlayerConfig : ScriptableObject
{
    [Header("基础信息")]
    [Tooltip("角色唯一ID")]
    public int ID = 1001;
    
    [Tooltip("角色名称")]
    public string Name = "玩家";

    [Header("生命值系统")]
    [Tooltip("最大生命值")]
    [Range(1, 500)]
    public int MaxHp = 100;
    
    [Tooltip("当前生命值（运行时会被覆盖）")]
    [Range(0, 500)]
    public int CurrentHp = 100;

    [Header("体力系统")]
    [Tooltip("最大体力值")]
    [Range(1, 500)]
    public int MaxStamina = 100;
    
    [Tooltip("当前体力值（运行时会被覆盖）")]
    [Range(0, 500)]
    public int CurrentStamina = 100;
    
    [Tooltip("每回合恢复的体力")]
    [Range(0, 100)]
    public int StaminaRegenPerTurn = 10;

    [Header("移动能力")]
    [Tooltip("每回合可移动的格子数")]
    [Range(1, 20)]
    public int MoveRange = 5;
    
    [Tooltip("移动速度（格子/秒）")]
    [Range(0.1f, 20f)]
    public float MoveSpeed = 5f;
    
    [Tooltip("每次移动消耗的体力")]
    [Range(0, 50)]
    public int MoveStaminaCost = 5;

    [Header("战斗属性")]
    [Tooltip("反应速度（影响先手、闪避等）")]
    [Range(0f, 10f)]
    public float Reaction = 1.0f;
    
    [Tooltip("攻击力")]
    [Range(1, 200)]
    public int AttackPower = 10;
    
    [Tooltip("防御力")]
    [Range(0, 200)]
    public int Defense = 5;

    [Header("感知能力")]
    [Tooltip("最大精神值（用于技能等）")]
    [Range(1, 500)]
    public int MaxSanity = 100;
    
    [Tooltip("当前精神值（运行时会被覆盖）")]
    [Range(0, 500)]
    public int CurrentSanity = 100;

    [Header("声音感知")]
    [Tooltip("基础声音传播半径（米）")]
    [Range(0f, 20f)]
    public float BaseNoiceLevel = 2f;
    
    [Tooltip("听觉范围（米）")]
    [Range(0f, 50f)]
    public float HearingRange = 10f;

    [Header("视野")]
    [Tooltip("基础可见距离（格子数）")]
    [Range(1, 30)]
    public int BaseVisibility = 5;
    
    [Tooltip("视野角度（度）")]
    [Range(0f, 360f)]
    public float ViewAngle = 120f;

    [Header("预制体")]
    [Tooltip("玩家预制体引用")]
    public GameObject Prefab;

    // ============ 运行时数据（不在Inspector显示）============
    
    [System.NonSerialized]
    public bool isDirty = false;  // 数据是否被修改

    /// <summary>
    /// 重置为初始值
    /// </summary>
    public void ResetToDefault()
    {
        CurrentHp = MaxHp;
        CurrentStamina = MaxStamina;
        CurrentSanity = MaxSanity;
        isDirty = false;
    }

    /// <summary>
    /// 验证数据合法性
    /// </summary>
    public void ValidateData()
    {
        CurrentHp = Mathf.Clamp(CurrentHp, 0, MaxHp);
        CurrentStamina = Mathf.Clamp(CurrentStamina, 0, MaxStamina);
        CurrentSanity = Mathf.Clamp(CurrentSanity, 0, MaxSanity);
        
        if (MoveRange <= 0) MoveRange = 1;
        if (MoveSpeed <= 0) MoveSpeed = 1f;
    }

    /// <summary>
    /// 创建运行时副本
    /// 用途：避免修改原始配置文件
    /// </summary>
    public PlayerConfig CreateRuntimeCopy()
    {
        PlayerConfig copy = Instantiate(this);
        copy.ResetToDefault();
        return copy;
    }

    void OnValidate()
    {
        // Inspector修改时自动验证
        ValidateData();
    }
}