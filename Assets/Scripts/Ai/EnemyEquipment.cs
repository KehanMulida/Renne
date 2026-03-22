using UnityEngine;

/// <summary>
/// 敌人装备组件
/// 职责：
/// 1. 管理武器槽和当前弹药量
/// 2. 提供射击方法（含散布角度计算）
/// 3. CombatExecutor 调用 Shoot() 执行攻击
/// </summary>
public class EnemyEquipment : MonoBehaviour
{
    [Header("装备槽")]
    [Tooltip("当前装备的武器（留空则使用 EnemyConfig 基础攻击）")]
    [SerializeField] private WeaponData weapon;

    [Header("射击设置")]
    [Tooltip("子弹从哪个位置发出（通常是枪口或头部位置）")]
    [SerializeField] private Transform muzzlePoint;

    [Tooltip("可以命中的 Layer")]
    [SerializeField] private LayerMask hitLayer;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    // 运行时弹药量（从 weapon.MaxBullet 初始化）
    private int currentAmmo = 0;

    // ============ 公开属性 ============

    public WeaponData Weapon => weapon;
    public bool HasWeapon => weapon != null;
    public int CurrentAmmo => currentAmmo;
    public bool HasAmmo => !weapon.IshasBullet || currentAmmo > 0;

    /// <summary>
    /// 有效攻击范围 = 武器射程 和 视野范围 取较小值
    /// 无武器时近战1格
    /// 确保敌人不会在看不见的地方攻击
    /// </summary>
    public float GetAttackRange(EnemyConfig config)
    {
        if (!HasWeapon) return 1f;
        return Mathf.Min(weapon.AttackRange, config.visionRange);
    }

    // ============ 初始化 ============

    void Start()
    {
        InitAmmo();
    }

    private void InitAmmo()
    {
        if (HasWeapon && weapon.IshasBullet)
        {
            currentAmmo = weapon.MaxBullet;
            Log($"Loaded {currentAmmo} rounds of {weapon.Name}");
        }
    }

    // ============ 射击 ============

    /// <summary>
    /// 执行射击
    /// 计算散布偏移 → 生成子弹 → 扣除弹药
    /// 返回是否成功射击
    /// </summary>
    public bool Shoot(Transform target, EnemyConfig config)
    {
        if (!HasWeapon)
        {
            Log("No weapon equipped");
            return false;
        }

        if (!HasAmmo)
        {
            Log("Out of ammo!");
            return false;
        }

        // 射击起点
        Vector3 origin = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + Vector3.up * 1.5f;

        // 基础方向（朝向目标）
        Vector3 baseDirection = (target.position - origin).normalized;

        // 计算散布偏移
        float spreadAngle = weapon.CalculateSpreadAngle();

        // 在水平面和垂直面各施加随机偏移
        Vector3 spreadDirection = Quaternion.Euler(
            spreadAngle,                            // 垂直偏移
            spreadAngle * Random.Range(-1f, 1f),    // 水平随机偏移
            0
        ) * baseDirection;

        // 计算伤害（含暴击）
        int damage = weapon.CalculateDamage();

        Log($"Shooting at {target.name} | spread: {spreadAngle:F1}° | damage: {damage}");

        // 发射子弹
        BulletProjectile.Fire(
            weapon,
            origin,
            spreadDirection,
            damage,
            gameObject,
            hitLayer
        );

        // 扣除弹药
        if (weapon.IshasBullet)
        {
            currentAmmo--;
            Log($"Ammo remaining: {currentAmmo}/{weapon.MaxBullet}");
        }

        return true;
    }

    /// <summary>
    /// 装弹（补满弹药）
    /// </summary>
    public void Reload()
    {
        if (!HasWeapon || !weapon.IshasBullet) return;
        currentAmmo = weapon.MaxBullet;
        Log($"Reloaded: {currentAmmo}/{weapon.MaxBullet}");
    }

    // ============ 运行时换武器 ============

    public void EquipWeapon(WeaponData newWeapon)
    {
        weapon = newWeapon;
        InitAmmo();
        Log($"Equipped: {(newWeapon != null ? newWeapon.Name : "None")}");
    }

    public void UnequipWeapon()
    {
        weapon = null;
        currentAmmo = 0;
        Log("Weapon unequipped");
    }

    // ============ 无武器时的基础攻击伤害 ============

    public int GetBaseDamage(EnemyConfig config) => config.attackDamage;

    // ============ 调试 ============

    private void Log(string msg)
    {
        if (enableDebugLog)
            Debug.Log($"[EnemyEquipment:{gameObject.name}] {msg}");
    }
}