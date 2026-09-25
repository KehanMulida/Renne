using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相机遮挡淡出：把挡在相机与玩家之间的物体临时变半透明，离开后恢复。
///
/// ── 相比旧版修掉的问题 ────────────────────────────────────────────────
/// 1. **矮墙永远不透明**（你遇到的现象）：旧版只朝玩家身上**一个点**（胸口）发射线。
///    俯视相机是**从上往下斜着**打过来的，射线会直接从矮墙（厕所隔板等）**上方掠过**，
///    于是矮墙永远进不了遮挡列表。现在**沿玩家身高采样多个点**（脚/腰/头）——
///    朝脚下那条射线才会被矮墙挡住。
///    （另：旧版那个 `onlyCheckAbovePlayer` 用的是遮挡物 pivot 高度，判据本身也是错的
///     ——墙 pivot 在地面会被整个跳过。现已改为用命中点 `hit.point.y`，且默认关闭。）
/// 2. **URP 下根本没变透明**：旧版用 Built-in RP 的 `_ALPHABLEND_ON` + `_Color`，
///    URP/Lit 不认。现在统一走 `MaterialFadeUtil`（`_Surface` / `_SURFACE_TYPE_TRANSPARENT`
///    / `_BaseColor`）。
/// 3. **材质泄漏**：旧版 `renderer.materials`（复数）每次访问都会克隆一份，且从不 Destroy，
///    每轮"遮挡→恢复"泄漏约 2N 个 Material。现在用 `sharedMaterials` 读原始、
///    实例只在进入遮挡时建一次、恢复时 Destroy。
/// 4. **每帧重赋 materials**：很贵且打断 SRP Batcher。现在每帧只改 alpha。
/// 5. **可能永远不恢复**：旧版 `Mathf.Lerp` + `Mathf.Approximately(a,1)` 几乎判不成立，
///    物体会永久留在字典里。现在用 `MoveTowards`，能精确到达。
/// 6. **闪烁**：旧版每帧 `Random.insideUnitSphere` 随机偏移射线，同一遮挡物时有时无。
///    现在用固定采样图案；`RaycastAll` 也换成 `RaycastNonAlloc`（不再每帧分配）。
///
/// ⚠ 注意：`FloorVisibilityController` 也会改 Renderer 的材质透明度。若两者作用在
///    同一个 Renderer 上会互相覆盖——它那边仍是旧写法，建议后续也迁到 MaterialFadeUtil。
/// </summary>
public class CameraOcclusionHandler : MonoBehaviour
{
    [Header("目标")]
    [SerializeField] private Transform player;
    [SerializeField] private Camera    targetCamera;

    [Header("遮挡检测")]
    [SerializeField] private LayerMask occlusionLayers;

    [Tooltip("★ 关键参数：沿玩家身高采样几个点（脚→头均匀分布）。\n" +
             "只朝一个点发射线时，俯视相机的射线会从矮墙上方掠过，矮墙永远不会变透明。\n" +
             "至少 2~3 个点才能让朝脚下的射线打到厕所隔板这类矮墙。")]
    [Range(1, 6)]
    [SerializeField] private int sampleHeights = 3;

    [Tooltip("玩家身高（采样点分布在 脚+footOffset ~ 身高 之间）")]
    [SerializeField] private float playerHeight = 1.7f;
    [SerializeField] private float footOffset   = 0.15f;

    [Tooltip("横向再各加一条射线（相机平面左右），用于覆盖较宽的遮挡物。0=关闭")]
    [SerializeField] private float lateralSpread = 0.4f;

    [Tooltip("★ 只淡出【命中点离玩家】这么近的遮挡物（米）。<=0 = 不限制。\n" +
             "作用：远处那些属于别的房间的墙不会跟着一起透明，避免看到不该看的区域。\n" +
             "注意：淡出是按 Renderer 整体生效的——若整片墙体是同一个模型，仍会整体透明，\n" +
             "需要美术侧把长墙按房间/段拆成独立对象，这个限制才真正起效。")]
    [SerializeField] private float maxOccluderDistance = 4f;

    [Header("透明度")]
    [Range(0f, 1f)]
    [SerializeField] private float targetAlpha = 0.3f;
    [SerializeField] private float fadeSpeed = 6f;

    [Header("过滤")]
    [Tooltip("忽略命中点低于玩家脚下的物体（地板等）。判据是【命中点】高度，不是物体 pivot。\n" +
             "默认关闭：开了会把朝脚下那条射线打到的矮墙也滤掉，矮墙就又不透明了。")]
    [SerializeField] private bool  skipBelowFeet = false;
    [SerializeField] private float footEpsilon = 0.05f;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;

    // ── 运行时 ──────────────────────────────────────────────────────────
    private class FadeEntry
    {
        public Material[] originalShared;   // 原始共享材质（不是克隆）
        public Material[] fadeInstances;    // 我们建的透明实例，恢复时要 Destroy
        public float      alpha;
    }

    private readonly Dictionary<Renderer, FadeEntry> _faded = new Dictionary<Renderer, FadeEntry>();
    private readonly HashSet<Renderer> _currentOccluders     = new HashSet<Renderer>();
    private readonly List<Renderer>    _toRemove             = new List<Renderer>();
    private readonly RaycastHit[]      _hitBuf               = new RaycastHit[16];

    /// <summary>缓存"该 Renderer 是否允许淡出"，避免每帧对每个命中做 GetComponentInParent</summary>
    private readonly Dictionary<Renderer, bool> _fadeAllowed = new Dictionary<Renderer, bool>();

    /// <summary>挂了 NoOcclusionFade 的物体（含父物体）永不透明</summary>
    private bool AllowFade(Renderer r)
    {
        if (_fadeAllowed.TryGetValue(r, out bool ok)) return ok;
        ok = r.GetComponentInParent<NoOcclusionFade>() == null;
        _fadeAllowed[r] = ok;
        return ok;
    }

    // 采样是固定图案（沿玩家身高均匀分布 + 可选左右各一条），
    // 不用随机偏移——随机会让同一个遮挡物时有时无，透明度抖动。

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (player == null)
        {
            foreach (var u in FindObjectsOfType<UnitMovement>())
            {
                if (u.GetComponent<TurnBasedUnit>()?.Faction == TurnFaction.Player)
                {
                    player = u.transform;
                    break;
                }
            }
        }
    }

    void Start()
    {
        if (player == null)       Debug.LogError("[CameraOcclusion] 未指定玩家", this);
        if (targetCamera == null) Debug.LogError("[CameraOcclusion] 未找到相机", this);
    }

    void LateUpdate()
    {
        if (player == null || targetCamera == null) return;

        _currentOccluders.Clear();
        DetectOcclusion();
        UpdateFades();
    }

    // ── 检测 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 本帧「被遮挡的采样射线比例」0~1。供相机做让位用（相机不必自己再打一遍射线）。
    /// 注意：这里统计的是**任何**挡住视线的命中，不受 maxOccluderDistance 过滤影响——
    /// 远处的墙虽然不该被弄透明，但它确实挡住了玩家，相机仍应该让位。
    /// </summary>
    public float OcclusionAmount { get; private set; }

    private int _raysCast, _raysBlocked;

    private void DetectOcclusion()
    {
        Transform camT   = targetCamera.transform;
        Vector3   camPos = camT.position;

        _raysCast = _raysBlocked = 0;

        int hs = Mathf.Max(1, sampleHeights);
        for (int i = 0; i < hs; i++)
        {
            // ★ 沿玩家身高分布采样点：朝【脚下】那条射线才会被矮墙挡住
            float t = hs == 1 ? 0.5f : i / (float)(hs - 1);
            Vector3 aim = player.position + Vector3.up * Mathf.Lerp(footOffset, playerHeight, t);

            CastAt(camPos, aim);
            if (lateralSpread > 0.001f)
            {
                CastAt(camPos + camT.right * lateralSpread, aim);
                CastAt(camPos - camT.right * lateralSpread, aim);
            }
        }

        OcclusionAmount = _raysCast > 0 ? _raysBlocked / (float)_raysCast : 0f;
    }

    private void CastAt(Vector3 origin, Vector3 aim)
    {
        Vector3 delta = aim - origin;
        float   dist  = delta.magnitude;
        if (dist <= 0.001f) return;

        _raysCast++;
        bool blocked = false;

        float footY = player.position.y + footEpsilon;

        int hits = Physics.RaycastNonAlloc(
            origin, delta / dist, _hitBuf, dist, occlusionLayers, QueryTriggerInteraction.Ignore);

        for (int k = 0; k < hits; k++)
        {
            ref RaycastHit hit = ref _hitBuf[k];

            // 用【命中点】高度过滤，不是物体 pivot（墙 pivot 在地面，按 pivot 判会整个跳过）
            if (skipBelowFeet && hit.point.y <= footY) continue;

            Renderer r = hit.collider.GetComponent<Renderer>();
            if (r == null) r = hit.collider.GetComponentInParent<Renderer>();
            if (r == null) continue;

            if (r.transform == player || r.transform.IsChildOf(player)) continue;  // 别把玩家自己弄透明

            // 这条射线确实被挡住了 → 计入遮挡程度（相机让位据此判断）。
            // 注意要在下面各种"不淡出"过滤【之前】记：
            // 墙可以拒绝变透明，但它确实挡住了玩家——此时应该由**相机**让路。
            blocked = true;

            // ★ 挂了 NoOcclusionFade 的墙永不淡出（它的职责就是挡住视线、不泄漏隔壁房间）
            if (!AllowFade(r)) continue;

            // 只淡出玩家身边的遮挡物：别把远处属于其它房间的墙也弄透明
            if (maxOccluderDistance > 0f &&
                (hit.point - player.position).sqrMagnitude > maxOccluderDistance * maxOccluderDistance)
                continue;

            if (_currentOccluders.Add(r) && !_faded.ContainsKey(r))
            {
                _faded[r] = BeginFade(r);
                if (enableDebugLog)
                    Debug.Log($"[CameraOcclusion] 新遮挡物：{r.gameObject.name}", r);
            }
        }

        if (blocked) _raysBlocked++;
    }

    // ── 淡入淡出 ────────────────────────────────────────────────────────

    private void UpdateFades()
    {
        _toRemove.Clear();

        foreach (var kv in _faded)
        {
            Renderer  r = kv.Key;
            FadeEntry e = kv.Value;

            if (r == null) { _toRemove.Add(r); continue; }   // Renderer 已被销毁

            bool  occluding = _currentOccluders.Contains(r);
            float goal      = occluding ? targetAlpha : 1f;

            // MoveTowards 而非 Lerp：能精确到达 1，恢复判定才会真正触发
            e.alpha = Mathf.MoveTowards(e.alpha, goal, fadeSpeed * Time.deltaTime);

            // 每帧只改 alpha，不重新赋 renderer.materials
            for (int i = 0; i < e.fadeInstances.Length; i++)
                MaterialFadeUtil.SetAlpha(e.fadeInstances[i], e.alpha);

            if (!occluding && e.alpha >= 1f)
            {
                EndFade(r, e);
                _toRemove.Add(r);
            }
        }

        for (int i = 0; i < _toRemove.Count; i++) _faded.Remove(_toRemove[i]);
    }

    /// <summary>进入遮挡：用 sharedMaterials 建一次透明实例并赋给 Renderer</summary>
    private FadeEntry BeginFade(Renderer r)
    {
        Material[] shared = r.sharedMaterials;          // 读 shared 不会克隆
        var inst = new Material[shared.Length];

        for (int i = 0; i < shared.Length; i++)
        {
            if (shared[i] == null) continue;
            inst[i] = new Material(shared[i]);
            MaterialFadeUtil.SetTransparent(inst[i]);
            MaterialFadeUtil.SetAlpha(inst[i], 1f);
        }

        r.materials = inst;                              // 只赋这一次

        return new FadeEntry { originalShared = shared, fadeInstances = inst, alpha = 1f };
    }

    /// <summary>恢复：换回原始共享材质，并销毁我们建的实例（否则泄漏）</summary>
    private void EndFade(Renderer r, FadeEntry e)
    {
        if (r != null) r.sharedMaterials = e.originalShared;

        for (int i = 0; i < e.fadeInstances.Length; i++)
            if (e.fadeInstances[i] != null) Destroy(e.fadeInstances[i]);
    }

    void OnDestroy()
    {
        foreach (var kv in _faded) EndFade(kv.Key, kv.Value);
        _faded.Clear();
    }

    void OnDrawGizmosSelected()
    {
        if (player == null || targetCamera == null) return;

        Gizmos.color = Color.cyan;
        Transform camT = targetCamera.transform;

        int hs = Mathf.Max(1, sampleHeights);
        for (int i = 0; i < hs; i++)
        {
            float t = hs == 1 ? 0.5f : i / (float)(hs - 1);
            Vector3 aim = player.position + Vector3.up * Mathf.Lerp(footOffset, playerHeight, t);

            Gizmos.DrawLine(camT.position, aim);
            if (lateralSpread > 0.001f)
            {
                Gizmos.DrawLine(camT.position + camT.right * lateralSpread, aim);
                Gizmos.DrawLine(camT.position - camT.right * lateralSpread, aim);
            }
        }
    }
}
