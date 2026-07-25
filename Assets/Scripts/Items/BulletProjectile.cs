using UnityEngine;

/// <summary>
/// 通用子弹组件（玩家和敌人共用）
/// 职责：
/// 1. 直线飞行
/// 2. Linecast 碰撞检测（不穿透）
/// 3. 命中后造成伤害
/// 4. 特效接入点（飞行特效、命中特效）
/// 使用方法：
///   通过 BulletProjectile.Fire() 静态工厂生成
///   不要直接放在场景里
/// 美术接入点：
///   weaponData.BulletPrefab    → 子弹飞行模型/特效
///   weaponData.HitVFXPrefab    → 命中特效
///   weaponData.MuzzleVFXPrefab → 枪口特效
/// </summary>
public class BulletProjectile : MonoBehaviour
{
    private WeaponData weaponData;
    private GameObject shooter;
    private Vector3 direction;
    private int damage;

    private float distanceTravelled = 0f;
    private Vector3 lastPosition;
    private bool hasHit = false;

    // ============ 静态工厂 ============

    /// <summary>
    /// 生成并发射子弹
    /// </summary>
    /// <param name="weaponData">武器配置（含速度、距离、特效）</param>
    /// <param name="origin">发射起点（枪口位置）</param>
    /// <param name="targetDirection">射击方向（已经含散布偏移）</param>
    /// <param name="damage">伤害值（由外部计算暴击后传入）</param>
    /// <param name="shooter">射击者（用于排除自伤）</param>
    /// <param name="hitLayer">可以命中的 Layer</param>
    public static BulletProjectile Fire(
        WeaponData weaponData,
        Vector3 origin,
        Vector3 targetDirection,
        int damage,
        GameObject shooter,
        LayerMask hitLayer)
    {
        GameObject go;

        if (weaponData.BulletPrefab != null)
        {
            go = Instantiate(weaponData.BulletPrefab, origin, Quaternion.LookRotation(targetDirection));
        }
        else
        {
            // 无 Prefab 时创建调试用胶囊体
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(targetDirection);
            go.transform.localScale = new Vector3(0.05f, 0.05f, 0.3f);
            go.GetComponent<Renderer>().material.color = Color.yellow;
            Destroy(go.GetComponent<Collider>());
        }

        go.name = "[Bullet]";

        // 枪口特效
        if (weaponData.MuzzleVFXPrefab != null)
            Instantiate(weaponData.MuzzleVFXPrefab, origin, Quaternion.LookRotation(targetDirection));

        BulletProjectile bullet = go.AddComponent<BulletProjectile>();
        bullet.weaponData   = weaponData;
        bullet.shooter      = shooter;
        bullet.direction    = targetDirection.normalized;
        bullet.damage       = damage;
        bullet.lastPosition = origin;
        bullet.hitLayer     = hitLayer;

        return bullet;
    }

    // ============ 飞行 ============

    private LayerMask hitLayer;

    void Update()
    {
        if (hasHit) return;

        // weaponData 还未初始化（Fire() 还没调用），等下一帧
        if (weaponData == null) return;

        float moveDistance = weaponData.BulletSpeed * Time.deltaTime;
        Vector3 newPosition = transform.position + direction * moveDistance;

        // Linecast 从上一帧到当前帧检测碰撞
        if (Physics.Linecast(lastPosition, newPosition, out RaycastHit hit, hitLayer))
        {
            OnHit(hit);
            return;
        }

        lastPosition = newPosition;
        transform.position = newPosition;
        distanceTravelled += moveDistance;

        // 超出最大距离，消失
        if (distanceTravelled >= weaponData.BulletMaxDistance)
        {
            Destroy(gameObject);
        }
    }

    // ============ 命中处理 ============

    private void OnHit(RaycastHit hit)
    {
        hasHit = true;

        // 命中特效（美术接入点）
        if (weaponData.HitVFXPrefab != null)
            Instantiate(weaponData.HitVFXPrefab, hit.point,
                Quaternion.LookRotation(hit.normal));

        // 排除射击者自伤
        if (hit.collider.gameObject == shooter ||
            hit.collider.transform.IsChildOf(shooter.transform))
        {
            Destroy(gameObject);
            return;
        }

        // 造成伤害
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
            damageable.TakeDamage(damage, shooter);
            Debug.Log($"[Bullet] Hit {hit.collider.transform.root.name} for {damage} dmg");
        }

        Destroy(gameObject);
    }
}