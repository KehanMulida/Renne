using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 投掷物飞行体
/// 职责：
/// 1. 按抛物线或直线轨迹飞行
/// 2. 飞行中检测碰撞，命中敌人触发伤害
/// 3. 落地后根据配置决定：损坏/落地弹跳/重新生成为可拾取物
/// 美术接入点：
/// - itemData.Prefab          飞行时的模型
/// - config.impactVFXPrefab   命中/落地特效
/// - config.debrisPrefab      损坏后的碎片模型
/// - LandingBounce 协程       物品未损坏时的落地弹跳动画（代码驱动，不依赖 Animator）
/// </summary>
public class ThrowableProjectile : MonoBehaviour
{
    private ItemData itemData;
    private ThrowableConfig config;
    private Vector3 startPos;
    private Vector3 targetPos;
    private GameObject thrower;

    private float flightProgress = 0f;
    private float flightDuration;
    private bool hasLanded = false;

    private Vector3 lastPosition;
    private const float minSafeDistance = 1.0f;

    // 飞行中旋转效果（美术感）
    // 直线模式：朝飞行方向旋转（飞刀效果）
    // 抛物线模式：绕自身轴翻滚（投掷效果）
    private Vector3 tumbleAxis;

    // ============ 静态工厂 ============

    public static ThrowableProjectile Launch(
        ItemData itemData,
        ThrowableConfig config,
        Vector3 startPos,
        Vector3 targetPos,
        GameObject thrower)
    {
        GameObject go;

        if (itemData.Prefab != null)
        {
            // 使用美术提供的 Prefab
            go = Instantiate(itemData.Prefab, startPos, Quaternion.identity);

            // Prefab 上可能有 Collider，移除以避免物理干扰（碰撞检测由代码控制）
            foreach (var col in go.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }
        else
        {
            // 无 Prefab 时创建调试用的默认球体（上线前应替换为真实美术资源）
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = startPos;
            go.transform.localScale = Vector3.one * 0.25f;
            go.GetComponent<Renderer>().material.color = Color.white;
            Destroy(go.GetComponent<Collider>());
        }

        go.name = $"[Projectile] {itemData.Name}";

        ThrowableProjectile proj = go.AddComponent<ThrowableProjectile>();
        proj.itemData       = itemData;
        proj.config         = config;
        proj.startPos       = startPos;
        proj.targetPos      = targetPos;
        proj.thrower        = thrower;
        proj.lastPosition   = startPos;
        proj.flightDuration = Vector3.Distance(startPos, targetPos) / config.flightSpeed;

        // 随机翻滚轴（让每次投掷的旋转看起来不一样）
        proj.tumbleAxis = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized;

        return proj;
    }

    // ============ 飞行 ============

    void Update()
    {
        if (hasLanded) return;

        flightProgress += Time.deltaTime / flightDuration;
        flightProgress = Mathf.Clamp01(flightProgress);

        Vector3 newPosition = CalcArcPos(flightProgress);

        // 碰撞检测（离投掷者足够远后才开始）
        float distFromThrower = thrower != null
            ? Vector3.Distance(newPosition, thrower.transform.position)
            : float.MaxValue;

        if (distFromThrower > minSafeDistance && lastPosition != Vector3.zero)
        {
            if (Physics.Linecast(lastPosition, newPosition, out RaycastHit hit, config.hitLayer))
            {
                OnHitTarget(hit.collider, hit.point);
                return;
            }
        }

        lastPosition = newPosition;
        transform.position = newPosition;

        // ---- 飞行旋转动画 ----
        if (config.IsArc)
        {
            // 抛物线模式：绕随机轴翻滚，速度和飞行速度成正比
            transform.Rotate(tumbleAxis, config.flightSpeed * 120f * Time.deltaTime, Space.World);
        }
        else
        {
            // 直线模式：朝飞行方向对齐（飞刀/标枪扎入效果）
            Vector3 dir = (targetPos - startPos).normalized;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        if (flightProgress >= 1f)
            OnLand(targetPos);
    }

    private Vector3 CalcArcPos(float t)
    {
        Vector3 linear = Vector3.Lerp(startPos, targetPos, t);
        if (config.IsArc)
            return linear + Vector3.up * (Mathf.Sin(t * Mathf.PI) * config.arcHeight);
        return linear;
    }

    // ============ 命中处理 ============

    private void OnHitTarget(Collider target, Vector3 hitPoint)
    {
        if (hasLanded) return;
        hasLanded = true;

        Debug.Log($"[Projectile:{itemData.Name}] Hit {target.gameObject.name}");

        // 直接命中伤害
        IDamageable directHit = target.GetComponentInParent<IDamageable>();
        if (directHit != null) ApplyDamage(directHit, config.directDamage);

        // 范围伤害时排除已被直接命中的目标，避免双重伤害
        if (config.HasSplash) ApplySplashDamage(hitPoint, directHit);

        OnImpact(hitPoint);
    }

    private void OnLand(Vector3 landPos)
    {
        if (hasLanded) return;
        hasLanded = true;

        Debug.Log($"[Projectile:{itemData.Name}] Landed at {landPos}");

        if (config.HasSplash) ApplySplashDamage(landPos, null);

        OnImpact(landPos);
    }

    private void ApplyDamage(IDamageable damageable, int damage)
    {
        if (damageable == null) return;

        // 排除自伤
        if (!config.canDamageSelf)
        {
            MonoBehaviour mb = damageable as MonoBehaviour;
            if (mb != null && mb.gameObject == thrower) return;
        }

        damageable.TakeDamage(damage);
        Debug.Log($"[Projectile] Dealt {damage} dmg to {(damageable as MonoBehaviour)?.gameObject.name}");
    }

    private void ApplySplashDamage(Vector3 center, IDamageable exclude)
    {
        float radius = config.splashRadius * (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);
        Collider[] hits = Physics.OverlapSphere(center, radius, config.hitLayer);

        // IDamageable 去重，同一敌人多个 Collider 只算一次
        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

        // 把直接命中的目标加进去，避免被范围伤害再打一次
        if (exclude != null) alreadyHit.Add(exclude);

        foreach (var col in hits)
        {
            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;
            if (alreadyHit.Contains(damageable)) continue;
            alreadyHit.Add(damageable);

            ApplyDamage(damageable, config.splashDamage);
        }
    }

    // ============ 落地/命中后处理 ============

    private void OnImpact(Vector3 pos)
    {
        // 命中特效（美术接入点：config.impactVFXPrefab）
        if (config.impactVFXPrefab != null)
            Instantiate(config.impactVFXPrefab, pos, Quaternion.identity);

        if (config.breakOnImpact)
        {
            // 损坏：生成碎片后直接销毁（美术接入点：config.debrisPrefab）
            if (config.debrisPrefab != null)
                Instantiate(config.debrisPrefab, pos, Quaternion.identity);

            Debug.Log($"[Projectile:{itemData.Name}] Broken on impact");
            Destroy(gameObject);
        }
        else
        {
            // 未损坏：播放落地弹跳动画，动画结束后决定是否生成 WorldItem
            StartCoroutine(LandingBounceCoroutine(pos));
        }
    }

    /// <summary>
    /// 落地弹跳动画（代码驱动，不依赖 Animator）
    /// 模拟物体落地后小幅弹跳然后静止的物理效果
    /// 如果美术有 Animator，可以在这里触发对应的 trigger 替换代码动画
    /// </summary>
    private IEnumerator LandingBounceCoroutine(Vector3 landPos)
    {
        // ---- 美术接入点 ----
        // 如果 Prefab 上有 Animator，在这里触发落地动画：
        // Animator anim = GetComponentInChildren<Animator>();
        // if (anim != null) anim.SetTrigger("OnLand");
        // yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);

        // 代码驱动的弹跳动画（无 Animator 时的 fallback）
        transform.position = landPos;

        // 弹跳参数
        float bounceHeight   = 0.15f;   // 第一次弹跳高度
        float bounceDuration = 0.12f;   // 单次弹跳时长
        int   bounceCount    = 2;       // 弹跳次数
        float dampening      = 0.45f;   // 每次弹跳高度衰减比例

        // 同时做一个小幅度的随机水平滑动，模拟物体落地后滚开
        Vector3 slideDir = new Vector3(
            Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)
        ).normalized;
        float slideDistance = Random.Range(0.1f, 0.3f);
        Vector3 finalPos = landPos + slideDir * slideDistance;

        for (int i = 0; i < bounceCount; i++)
        {
            float currentBounceHeight = bounceHeight * Mathf.Pow(dampening, i);
            Vector3 bounceStart = transform.position;

            // 上升
            float elapsed = 0f;
            while (elapsed < bounceDuration * 0.5f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (bounceDuration * 0.5f);
                float yOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * currentBounceHeight;

                // 水平方向同时向最终位置滑动
                Vector3 horizontalPos = Vector3.Lerp(bounceStart, finalPos,
                    (float)i / bounceCount + t / bounceCount);

                transform.position = new Vector3(
                    horizontalPos.x,
                    landPos.y + yOffset,
                    horizontalPos.z
                );

                // 弹跳时也带一点旋转
                transform.Rotate(tumbleAxis, 180f * Time.deltaTime, Space.World);
                yield return null;
            }

            // 下落
            elapsed = 0f;
            while (elapsed < bounceDuration * 0.5f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (bounceDuration * 0.5f);
                float yOffset = Mathf.Cos(t * Mathf.PI * 0.5f) * currentBounceHeight;

                Vector3 horizontalPos = Vector3.Lerp(bounceStart, finalPos,
                    (float)i / bounceCount + (0.5f + t * 0.5f) / bounceCount);

                transform.position = new Vector3(
                    horizontalPos.x,
                    landPos.y + yOffset,
                    horizontalPos.z
                );

                transform.Rotate(tumbleAxis, 180f * Time.deltaTime, Space.World);
                yield return null;
            }
        }

        // 最终静止位置
        transform.position = finalPos;

        // 静止后生成 WorldItem 或直接销毁
        if (config.canPickupAfterThrow)
        {
            // 在静止位置生成可拾取物品
            WorldItem.CreateWorldItem(itemData, finalPos, quantity: 1);
            Debug.Log($"[Projectile:{itemData.Name}] Spawned as WorldItem at {finalPos}");
        }
        else
        {
            Debug.Log($"[Projectile:{itemData.Name}] Landed, not pickupable");
        }

        Destroy(gameObject);
    }

    // ============ 调试 ============

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || hasLanded || config == null) return;

        // 飞行轨迹（Arc=黄色，Straight=青色）
        Gizmos.color = config.IsArc ? Color.yellow : Color.cyan;
        Vector3 prev = startPos;
        for (int i = 1; i <= 20; i++)
        {
            float t = i / 20f;
            Gizmos.DrawLine(prev, CalcArcPos(t));
            prev = CalcArcPos(t);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, 0.15f);

        if (config.HasSplash && GridManager.Instance != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(targetPos, config.splashRadius * GridManager.Instance.CellSize);
        }
    }
}