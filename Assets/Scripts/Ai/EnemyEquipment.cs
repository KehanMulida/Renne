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
    /// <summary>
    /// 有效攻击范围 = min(武器射程, 敌人视野)
    /// 视野是开枪前提，武器射程是物理上限，两者取较小值。
    /// 无武器时退回到近战范围。
    /// </summary>
    public float GetAttackRange(EnemyConfig config)
    {
        if (!HasWeapon) return config != null ? config.attackRange : 1f;
        return Mathf.Min(weapon.AttackRange, config != null ? config.visionRange : weapon.AttackRange);
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
    /// 按武器 FireMode 执行完整射击序列（协程）
    /// CombatExecutor 用 yield return 调用，可正确处理点射间隔
    /// </summary>
    public System.Collections.IEnumerator ShootCoroutine(Transform target, EnemyConfig config)
    {
        if (!HasWeapon || !HasAmmo) yield break;

        Vector3 origin = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + Vector3.up * 1.0f + transform.forward * 0.5f;

        float qteDur = config != null ? config.qteWindowDuration : 0f;

        switch (weapon.fireMode)
        {
            case FireMode.SemiAuto:
                FirePellets(origin, target, 1, false, qteDur);
                break;

            case FireMode.Burst:
                for (int i = 0; i < weapon.burstCount; i++)
                {
                    if (!HasAmmo) break;
                    FirePellets(origin, target, 1, false, i == 0 ? qteDur : 0f);
                    if (i < weapon.burstCount - 1)
                        yield return new WaitForSeconds(weapon.fireInterval);
                }
                break;

            case FireMode.FullAuto:
                // AI 全自动：每次行动连发 burstCount 发（不做帧级别持续射击）
                for (int i = 0; i < weapon.burstCount; i++)
                {
                    if (!HasAmmo) break;
                    FirePellets(origin, target, 1, false, i == 0 ? qteDur : 0f);
                    if (i < weapon.burstCount - 1)
                        yield return new WaitForSeconds(weapon.fireInterval);
                }
                break;

            case FireMode.Shotgun:
                FirePellets(origin, target, weapon.pelletsPerShot, true, qteDur);
                break;
        }
    }

    // 发射 count 颗弹丸，扣 1 发弹药
    private void FirePellets(Vector3 origin, Transform target, int count, bool isPellet, float qteDur)
    {
        Vector3 baseDir = (target.position - origin).normalized;

        for (int i = 0; i < count; i++)
        {
            float spreadH = weapon.CalculateSpreadAngle();
            float spreadV = weapon.CalculateSpreadAngle();
            if (isPellet)
            {
                spreadH += Random.Range(-weapon.pelletSpreadAngle, weapon.pelletSpreadAngle);
                spreadV += Random.Range(-weapon.pelletSpreadAngle, weapon.pelletSpreadAngle);
            }
            Vector3 dir = Quaternion.Euler(spreadV, spreadH * Random.Range(-1f, 1f), 0) * baseDir;
            int dmg = weapon.CalculateDamage();
            BulletProjectile.Fire(weapon, origin, dir, dmg, gameObject, hitLayer,
                qteDuration: i == 0 ? qteDur : 0f); // QTE 只在第一颗触发
        }

        if (weapon.IshasBullet)
        {
            currentAmmo--;
            Log($"射击 [{weapon.fireMode}] x{count}  弹药: {currentAmmo}/{weapon.MaxBullet}");
        }
    }

    /// <summary>向后兼容：SemiAuto 单发（CombatExecutor 已改用 ShootCoroutine）</summary>
    public bool Shoot(Transform target, EnemyConfig config)
    {
        if (!HasWeapon || !HasAmmo) return false;
        Vector3 origin = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + Vector3.up * 1.0f + transform.forward * 0.5f;
        FirePellets(origin, target, 1, false, config != null ? config.qteWindowDuration : 0f);
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

    /// <summary>
    /// 从场景 WorldItem 拾取武器（AI 行为节点调用）
    /// 要求 worldItem 的 ItemData 是 WeaponData，否则返回 false
    /// </summary>
    public bool TryPickupWeaponFromScene(WorldItem worldItem)
    {
        if (worldItem == null || worldItem.IsPickedUp) return false;

        WeaponData wd = worldItem.ItemData as WeaponData;
        if (wd == null) return false;

        if (!worldItem.Pickup(gameObject)) return false;

        EquipWeapon(wd);
        Log($"从场景拾取武器: {wd.Name}");
        return true;
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