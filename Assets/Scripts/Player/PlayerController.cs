using UnityEngine;
using System.Collections;

/// <summary>
/// 玩家控制器
/// 职责：
/// 1. 管理玩家的运行时数据
/// 2. 从PlayerConfig读取配置
/// 3. 提供属性查询接口
/// 4. 处理属性变化（生命值、体力等）
/// 特点：
/// - 使用ScriptableObject配置，数据与逻辑分离
/// - 运行时创建配置副本，不修改原始配置
/// - 提供事件通知属性变化
/// </summary>
public class PlayerController : MonoBehaviour, IDamageable
{
      public bool IsAlive => CurrentHp > 0;

    [Header("配置数据")]
    [SerializeField] private PlayerConfig configTemplate;  // 配置模板
    
    [Header("运行时数据（只读）")]
    [SerializeField] private PlayerConfig runtimeConfig;   // 运行时配置副本
    
    // ============ 公开属性（只读）============
    
    public int ID => runtimeConfig?.ID ?? 0;
    public string Name => runtimeConfig?.Name ?? "Unknown";
    public int MaxHp => runtimeConfig?.MaxHp ?? 100;
    public int CurrentHp => runtimeConfig?.CurrentHp ?? 100;
    public int MaxStamina => runtimeConfig?.MaxStamina ?? 100;
    public int CurrentStamina => runtimeConfig?.CurrentStamina ?? 100;
    public int MoveRange => runtimeConfig?.MoveRange ?? 5;
    public int CombatMoveRange => runtimeConfig?.CombatMoveRange ?? 2;
    public float TurnStartDelay => runtimeConfig?.TurnStartDelay ?? 0.5f;
    public float ActionInterval => runtimeConfig?.ActionInterval ?? 0.3f;
    public float CombatTurnStartDelay => runtimeConfig?.CombatTurnStartDelay ?? 0.1f;
    public float CombatActionInterval => runtimeConfig?.CombatActionInterval ?? 0.1f;
    public float Reaction => runtimeConfig?.Reaction ?? 1f;
    public int MaxSanity => runtimeConfig?.MaxSanity ?? 100;
    public int CurrentSanity => runtimeConfig?.CurrentSanity ?? 100;
    public float BaseNoiceLevel => runtimeConfig?.BaseNoiceLevel ?? 2f;
    public int BaseVisibility => runtimeConfig?.BaseVisibility ?? 5;

    /// <summary>运行时配置引用，供 TurnBasedUnit 等外部系统读取</summary>
    public PlayerConfig Config => runtimeConfig;

    // ============ 事件系统 ============
    
    /// <summary>生命值变化事件</summary>
    public event System.Action<int, int> OnHpChanged;  // 参数：(当前值, 最大值)
    
    /// <summary>体力值变化事件</summary>
    public event System.Action<int, int> OnStaminaChanged;
    
    /// <summary>精神值变化事件</summary>
    public event System.Action<int, int> OnSanityChanged;
    
    /// <summary>死亡事件</summary>
    public event System.Action OnDeath;

    // ============ 初始化 ============

    void Awake()
    {
        InitializeConfig();
    }

    void Start()
    {
        // 应用配置到其他组件
        ApplyConfigToComponents();
    }

    /// <summary>
    /// 初始化配置
    /// </summary>
    private void InitializeConfig()
    {
        if (configTemplate == null)
        {
            Debug.LogError($"[PlayerController] {gameObject.name} has no config template!");
            return;
        }

        // 创建运行时副本，避免修改原始配置
        runtimeConfig = configTemplate.CreateRuntimeCopy();
        runtimeConfig.ResetToDefault();

        Debug.Log($"[PlayerController] Initialized {Name} (ID:{ID})");
    }

    /// <summary>
    /// 应用PlayerConfig的信息配置到其他组件
    /// </summary>
    private void ApplyConfigToComponents()
    {
        if (runtimeConfig == null) return;

        // 应用到UnitMovement
        UnitMovement movement = GetComponent<UnitMovement>();
        if (movement != null)
        {
            movement.SetMoveRange(runtimeConfig.MoveRange);
            movement.SetMoveSpeed(runtimeConfig.MoveSpeed);
            Debug.Log($"[PlayerController] Applied to UnitMovement - Range: {runtimeConfig.MoveRange}, Speed: {runtimeConfig.MoveSpeed}");
        }

        // 应用到SoundEmitter
        SoundEmitter emitter = GetComponent<SoundEmitter>();
        if (emitter != null)
        {
            emitter.SetMovementSoundRadius(runtimeConfig.BaseNoiceLevel);
            Debug.Log($"[PlayerController] Applied to SoundEmitter - NoiceLevel: {runtimeConfig.BaseNoiceLevel}");
        }

        // 应用到SoundListener
        SoundListener listener = GetComponent<SoundListener>();
        if (listener != null)
        {
            listener.SetHearingRange(runtimeConfig.HearingRange);
            Debug.Log($"[PlayerController] Applied to SoundListener - HearingRange: {runtimeConfig.HearingRange}");
        }

        Debug.Log($"[PlayerController] All configurations applied to components");
    }

    // ============ 生命值管理 ============

    /// <summary>
    /// 受到伤害
    /// </summary>
    public void TakeDamage(int damage, GameObject attacker = null)
    {
        if (runtimeConfig == null) return;

        int oldHp = runtimeConfig.CurrentHp;
        runtimeConfig.CurrentHp = Mathf.Max(0, runtimeConfig.CurrentHp - damage);

        Debug.Log($"[PlayerController] {Name} took {damage} damage. HP: {oldHp} -> {runtimeConfig.CurrentHp}");

        OnHpChanged?.Invoke(runtimeConfig.CurrentHp, runtimeConfig.MaxHp);

        if (runtimeConfig.CurrentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 直接扣除生命值（可以传入负数表示恢复）
    /// </summary>
    public void ModifyHp(int amount)
    {
        if (amount < 0)
        {
            TakeDamage(-amount);
        }
        else
        {
            Heal(amount);
        }
    }

    /// <summary>
    /// 恢复生命值
    /// </summary>
    public void Heal(int amount)
    {
        if (runtimeConfig == null) return;

        int oldHp = runtimeConfig.CurrentHp;
        runtimeConfig.CurrentHp = Mathf.Min(runtimeConfig.MaxHp, runtimeConfig.CurrentHp + amount);

        Debug.Log($"[PlayerController] {Name} healed {amount}. HP: {oldHp} -> {runtimeConfig.CurrentHp}");

        OnHpChanged?.Invoke(runtimeConfig.CurrentHp, runtimeConfig.MaxHp);
    }

    /// <summary>
    /// 设置生命值（直接设置，不触发过多事件）
    /// </summary>
    public void SetHp(int value)
    {
        if (runtimeConfig == null) return;

        runtimeConfig.CurrentHp = Mathf.Clamp(value, 0, runtimeConfig.MaxHp);
        OnHpChanged?.Invoke(runtimeConfig.CurrentHp, runtimeConfig.MaxHp);

        if (runtimeConfig.CurrentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 死亡处理
    /// </summary>
    private void Die()
    {
        Debug.Log($"[PlayerController] {Name} died!");
        OnDeath?.Invoke();
        
        // 这里可以添加死亡逻辑
        // 例如：播放死亡动画，禁用移动等
    }

    // ============ 体力管理 ============

    /// <summary>
    /// 消耗体力
    /// </summary>
    public bool ConsumeStamina(int amount)
    {
        if (runtimeConfig == null) return false;

        if (runtimeConfig.CurrentStamina < amount)
        {
            Debug.Log($"[PlayerController] Not enough stamina! Need: {amount}, Have: {runtimeConfig.CurrentStamina}");
            return false;
        }

        int oldStamina = runtimeConfig.CurrentStamina;
        runtimeConfig.CurrentStamina -= amount;

        Debug.Log($"[PlayerController] Consumed {amount} stamina. Stamina: {oldStamina} -> {runtimeConfig.CurrentStamina}");

        OnStaminaChanged?.Invoke(runtimeConfig.CurrentStamina, runtimeConfig.MaxStamina);
        return true;
    }

    /// <summary>
    /// 直接扣除体力（强制扣除，即使不够也会扣到0）
    /// </summary>
    public void ReduceStamina(int amount)
    {
        if (runtimeConfig == null) return;

        int oldStamina = runtimeConfig.CurrentStamina;
        runtimeConfig.CurrentStamina = Mathf.Max(0, runtimeConfig.CurrentStamina - amount);

        Debug.Log($"[PlayerController] Reduced {amount} stamina. Stamina: {oldStamina} -> {runtimeConfig.CurrentStamina}");

        OnStaminaChanged?.Invoke(runtimeConfig.CurrentStamina, runtimeConfig.MaxStamina);
    }

    /// <summary>
    /// 恢复体力
    /// </summary>
    public void RestoreStamina(int amount)
    {
        if (runtimeConfig == null) return;

        int oldStamina = runtimeConfig.CurrentStamina;
        runtimeConfig.CurrentStamina = Mathf.Min(runtimeConfig.MaxStamina, runtimeConfig.CurrentStamina + amount);

        OnStaminaChanged?.Invoke(runtimeConfig.CurrentStamina, runtimeConfig.MaxStamina);
    }

    /// <summary>
    /// 设置体力值（直接设置）
    /// </summary>
    public void SetStamina(int value)
    {
        if (runtimeConfig == null) return;

        runtimeConfig.CurrentStamina = Mathf.Clamp(value, 0, runtimeConfig.MaxStamina);
        OnStaminaChanged?.Invoke(runtimeConfig.CurrentStamina, runtimeConfig.MaxStamina);
    }

    /// <summary>
    /// 每回合恢复体力
    /// </summary>
    public void RegenerateStamina()
    {
        if (runtimeConfig == null) return;
        RestoreStamina(runtimeConfig.StaminaRegenPerTurn);
    }

    // ============ 精神值管理 ============

    /// <summary>
    /// 降低精神值（扣除）
    /// </summary>
    public void ReduceSanity(int amount)
    {
        if (runtimeConfig == null) return;

        int oldSanity = runtimeConfig.CurrentSanity;
        runtimeConfig.CurrentSanity = Mathf.Max(0, runtimeConfig.CurrentSanity - amount);

        Debug.Log($"[PlayerController] Sanity reduced by {amount}. Sanity: {oldSanity} -> {runtimeConfig.CurrentSanity}");

        OnSanityChanged?.Invoke(runtimeConfig.CurrentSanity, runtimeConfig.MaxSanity);
    }

    /// <summary>
    /// 恢复精神值
    /// </summary>
    public void RestoreSanity(int amount)
    {
        if (runtimeConfig == null) return;

        runtimeConfig.CurrentSanity = Mathf.Min(runtimeConfig.MaxSanity, runtimeConfig.CurrentSanity + amount);

        OnSanityChanged?.Invoke(runtimeConfig.CurrentSanity, runtimeConfig.MaxSanity);
    }

    /// <summary>
    /// 设置精神值（直接设置）
    /// </summary>
    public void SetSanity(int value)
    {
        if (runtimeConfig == null) return;

        runtimeConfig.CurrentSanity = Mathf.Clamp(value, 0, runtimeConfig.MaxSanity);
        OnSanityChanged?.Invoke(runtimeConfig.CurrentSanity, runtimeConfig.MaxSanity);
    }

    // ============ 查询接口 ============

    /// <summary>
    /// 检查是否有足够体力
    /// </summary>
    public bool HasEnoughStamina(int required)
    {
        return runtimeConfig != null && runtimeConfig.CurrentStamina >= required;
    }

    /// <summary>
    /// 检查是否存活
    /// </summary>
    public bool isAlive()
    {
        return runtimeConfig != null && runtimeConfig.CurrentHp > 0;
    }

    /// <summary>
    /// 获取生命值百分比
    /// </summary>
    public float GetHpPercentage()
    {
        if (runtimeConfig == null || runtimeConfig.MaxHp == 0) return 0f;
        return (float)runtimeConfig.CurrentHp / runtimeConfig.MaxHp;
    }

    /// <summary>
    /// 获取体力百分比
    /// </summary>
    public float GetStaminaPercentage()
    {
        if (runtimeConfig == null || runtimeConfig.MaxStamina == 0) return 0f;
        return (float)runtimeConfig.CurrentStamina / runtimeConfig.MaxStamina;
    }

    /// <summary>
    /// 获取精神值百分比
    /// </summary>
    public float GetSanityPercentage()
    {
        if (runtimeConfig == null || runtimeConfig.MaxSanity == 0) return 0f;
        return (float)runtimeConfig.CurrentSanity / runtimeConfig.MaxSanity;
    }

    /// <summary>
    /// 获取配置的只读引用
    /// </summary>
    public PlayerConfig GetConfig()
    {
        return runtimeConfig;
    }

    // ============ 调试可视化 ============

    void OnGUI()
    {
        if (runtimeConfig == null) return;

        // 在角色头顶显示状态信息
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3.5f);
        
        if (screenPos.z > 0)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 12;
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;

            // 显示生命值
            Color hpColor = GetHpPercentage() > 0.5f ? Color.green : 
                           GetHpPercentage() > 0.2f ? Color.yellow : Color.red;
            style.normal.textColor = hpColor;
            
            string info = $"{Name}\n" +
                         $"HP: {CurrentHp}/{MaxHp}\n" +
                         $"SP: {CurrentStamina}/{MaxStamina}";

            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y - 40, 100, 60), 
                     info, style);
        }
    }
}