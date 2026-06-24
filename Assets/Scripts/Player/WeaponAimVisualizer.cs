using UnityEngine;

/// <summary>
/// 武器瞄准线可视化（LineRenderer，游戏内可见）
/// 仿 Project Zomboid 风格：
///   - 中心瞄准线从玩家延伸到最大射程
///   - 两侧散布边线展示散布扇形
///   - 方向平行于地面（水平），弹道从玩家射出而非在鼠标处截断
///   - 弹药耗尽时变红提示
/// 挂载到玩家 GameObject，与 EquipmentManager 同节点。
/// </summary>
public class WeaponAimVisualizer : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private EquipmentManager equipmentManager;

    [Header("线条配置")]
    [Tooltip("留空则自动用 Sprites/Default，建议指定 Particles/Unlit 材质以支持透明混色")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float centerLineWidth = 0.03f;
    [SerializeField] private float spreadLineWidth  = 0.015f;
    [SerializeField] private Color colorCenter      = new Color(1.00f, 0.95f, 0.70f, 0.90f);
    [SerializeField] private Color colorSpread      = new Color(1.00f, 0.45f, 0.10f, 0.45f);
    [SerializeField] private Color colorNoAmmo      = new Color(1.00f, 0.15f, 0.15f, 0.85f);
    [SerializeField] private Color colorSpreadNoAmmo= new Color(1.00f, 0.15f, 0.15f, 0.35f);

    [Header("障碍物检测")]
    [Tooltip("瞄准线碰到这些 Layer 的物体时停止（通常是墙、掩体等）")]
    [SerializeField] private LayerMask obstacleLayer;

    // originHeight 已由 EquipmentManager.FireOrigin 统一管理，此处保留作回退备用
    [HideInInspector] public float originHeight = 1.2f;

    private LineRenderer _center;
    private LineRenderer _spreadLeft;
    private LineRenderer _spreadRight;

    void Awake()
    {
        if (equipmentManager == null)
            equipmentManager = GetComponent<EquipmentManager>();

        _center      = CreateLine("AimCenter",      centerLineWidth, colorCenter);
        _spreadLeft  = CreateLine("AimSpreadLeft",  spreadLineWidth,  colorSpread);
        _spreadRight = CreateLine("AimSpreadRight", spreadLineWidth,  colorSpread);

        SetVisible(false);
    }

    void Update()
    {
        if (equipmentManager == null) { SetVisible(false); return; }

        var slot = equipmentManager.CurrentSlot;
        if (!equipmentManager.IsAiming || slot == null || slot.itemData?.Type != ItemType.Weapon)
        {
            SetVisible(false);
            return;
        }

        WeaponData weapon = slot.itemData as WeaponData;
        if (weapon == null) { SetVisible(false); return; }

        bool noAmmo = weapon.IshasBullet && slot.quantity <= 0;

        // 起点：与 EquipmentManager.FireOrigin 保持一致（firePoint 或回退高度）
        Vector3 origin = equipmentManager.FireOrigin;

        // 方向：origin → 地面命中点（与 Straight throwable 逻辑一致），水平化后延伸到最大射程
        Vector3 toHit = equipmentManager.AimTargetPos - origin;
        toHit.y = 0f;
        if (toHit.sqrMagnitude < 0.001f) { SetVisible(false); return; }
        Vector3 dir = toHit.normalized;

        float range       = weapon.BulletMaxDistance;
        float halfAngle   = (1f - Mathf.Clamp01(weapon.Accuracy / 100f)) * weapon.MaxSpreadAngle;

        if (weapon.fireMode == FireMode.Shotgun)
            halfAngle += weapon.pelletSpreadAngle;

        halfAngle = Mathf.Max(halfAngle, 0.5f);

        Vector3 dirLeft  = Quaternion.Euler(0f, -halfAngle, 0f) * dir;
        Vector3 dirRight = Quaternion.Euler(0f,  halfAngle, 0f) * dir;

        // 各方向独立 Raycast，碰到障碍物截断到命中点
        float rangeCenter = CalcRayRange(origin, dir,      range);
        float rangeLeft   = CalcRayRange(origin, dirLeft,  range);
        float rangeRight  = CalcRayRange(origin, dirRight, range);

        Vector3 endCenter = origin + dir      * rangeCenter;
        Vector3 endLeft   = origin + dirLeft  * rangeLeft;
        Vector3 endRight  = origin + dirRight * rangeRight;

        // 保持线条高度与起点一致（水平面）
        endCenter.y = endLeft.y = endRight.y = origin.y;

        SetLine(_center,      origin, endCenter);
        SetLine(_spreadLeft,  origin, endLeft);
        SetLine(_spreadRight, origin, endRight);

        // 颜色：弹药耗尽变红
        Color cc = noAmmo ? colorNoAmmo      : colorCenter;
        Color sc = noAmmo ? colorSpreadNoAmmo : colorSpread;
        ApplyColor(_center,      cc);
        ApplyColor(_spreadLeft,  sc);
        ApplyColor(_spreadRight, sc);

        SetVisible(true);
    }

    // 沿 dir 方向 Raycast，命中障碍物时返回命中距离，否则返回 maxRange
    private float CalcRayRange(Vector3 origin, Vector3 dir, float maxRange)
    {
        if (obstacleLayer == 0) return maxRange; // 未配置 Layer 时不做检测
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRange, obstacleLayer))
            return hit.distance;
        return maxRange;
    }

    // ── 工具方法 ────────────────────────────────────────────────────────

    private LineRenderer CreateLine(string goName, float width, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, worldPositionStays: false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = width;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = lineMaterial != null
            ? lineMaterial
            : new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        return lr;
    }

    private void SetLine(LineRenderer lr, Vector3 start, Vector3 end)
    {
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
    }

    private void ApplyColor(LineRenderer lr, Color c)
    {
        lr.startColor = lr.endColor = c;
    }

    private void SetVisible(bool visible)
    {
        if (_center      != null) _center.gameObject.SetActive(visible);
        if (_spreadLeft  != null) _spreadLeft.gameObject.SetActive(visible);
        if (_spreadRight != null) _spreadRight.gameObject.SetActive(visible);
    }
}
