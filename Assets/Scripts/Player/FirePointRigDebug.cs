using UnityEngine;

/// <summary>
/// 【调试/占位】给玩家装一把可见的占位枪，并把枪口(FirePoint)接到 EquipmentManager，
/// 用来在没有美术资源时校准廖枪/射击的“枪口高度”和“朝向”。
///
/// 机制配合：EquipmentManager.UpdateFirePointRotation 每帧按 firePoint 的 localPosition
/// 水平半径与高度，把它绕玩家 Y 轴转到鼠标方向。所以这里把 firePoint 放到
/// (0, muzzleHeight, muzzleRadius)，运行时它就会随瞄准方向绕玩家旋转，枪管跟着指向鼠标。
///
/// 用法：挂到玩家（与 EquipmentManager 同一物体）→ 进 Play 自动生成 → 在 Inspector 调
/// muzzleHeight / muzzleRadius 观察高度与出膛位置。正式美术接入后删除本组件即可。
/// </summary>
[RequireComponent(typeof(EquipmentManager))]
public class FirePointRigDebug : MonoBehaviour
{
    [Header("枪口位置（相对玩家）")]
    [Tooltip("枪口高度（米，相对玩家原点）")]
    public float muzzleHeight = 1.2f;
    [Tooltip("枪口离玩家中心的水平距离（米）")]
    public float muzzleRadius = 0.4f;

    [Header("占位枪外形")]
    [Tooltip("枪管长度（米）")]
    public float barrelLength = 0.6f;
    [Tooltip("枪管粗细（米）")]
    public float barrelThickness = 0.08f;
    [Tooltip("是否生成可见枪管网格（关掉则只建空的 firePoint 节点）")]
    public bool showModel = true;

    [Header("行为")]
    [Tooltip("若 EquipmentManager 已手动指定了 firePoint，则不覆盖、不生成")]
    public bool respectExisting = true;

    private void Start()
    {
        var eq = GetComponent<EquipmentManager>();
        if (eq == null) return;

        if (respectExisting && eq.FirePoint != null)
        {
            Debug.Log("[FirePointRigDebug] 已存在 firePoint，跳过占位枪生成。");
            return;
        }

        // firePoint 根节点：放到 (0, 高, 半径)，运行时由 UpdateFirePointRotation 绕玩家旋转
        var fp = new GameObject("FirePoint_Debug").transform;
        fp.SetParent(transform, false);
        fp.localPosition = new Vector3(0f, muzzleHeight, muzzleRadius);
        fp.localRotation = Quaternion.identity;

        if (showModel)
        {
            // 枪管：细长方块，沿 firePoint 前方(+Z)伸出，用来肉眼判断朝向
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrel.name = "Barrel_Debug";
            var col = barrel.GetComponent<Collider>();
            if (col != null) Destroy(col); // 不参与物理，避免挡子弹/寻路

            barrel.transform.SetParent(fp, false);
            barrel.transform.localScale    = new Vector3(barrelThickness, barrelThickness, barrelLength);
            barrel.transform.localPosition = new Vector3(0f, 0f, barrelLength * 0.5f);
        }

        eq.SetFirePoint(fp);
        Debug.Log($"[FirePointRigDebug] 占位枪已接入 firePoint（高 {muzzleHeight}m, 半径 {muzzleRadius}m, 管长 {barrelLength}m）。");
    }
}
