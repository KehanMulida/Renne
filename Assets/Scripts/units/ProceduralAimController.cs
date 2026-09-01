using UnityEngine;

/// <summary>
/// 角色程序化动画（分层驱动）：
///   · Upper Body Layer：基于 AI 瞄准方向旋转 root 骨骼做上半身瞄准（±40/转身/切 Center-left-right）。
///   · Base Layer：腿部运动——移动(追击/调查)=Run、站立=Stand。
/// 两层各司其职、互不干扰；合并在一个组件里以复用 Animator/层解析/CrossFade。
///
/// 规则（yaw = 瞄准方向相对身体 forward 的偏航角）：
///   - |yaw| ≤ maxRootYaw(默认40°)：身体不动，旋转 root 骨骼精确瞄准；
///     按 yaw 正负在 Center / Aim_left / Aim_right 动画状态间切换。
///   - |yaw| > maxRootYaw：旋转 object 自身（转身）把目标带回 ±40 内，root 到达上限。
///
/// 关键：root 骨骼的程序化旋转在 LateUpdate（Animator 写完骨骼之后）叠加，否则会被动画覆盖。
/// 子弹命中由 EnemyEquipment 直接朝目标点计算，本控制器只负责视觉瞄准。
///
/// 用法：挂在敌人根物体上（与 UnitMovement/AI 同物体）。AI 每帧想瞄准时调 AimAt(worldPos)；
/// 停止调用 aimHoldTime 秒后自动回到 Relax。CombatExecutor 已接入。
/// </summary>
[DisallowMultipleComponent]
public class ProceduralAimController : MonoBehaviour
{
    [Header("引用（留空自动解析）")]
    [Tooltip("角色 Animator；留空自动取子物体上的 Animator")]
    public Animator animator;
    [Tooltip("要旋转的 root 骨骼；留空则在模型层级里按名字查找")]
    public Transform rootBone;
    [Tooltip("root 骨骼名字（自动查找用）")]
    public string rootBoneName = "Beiye_root";

    [Header("瞄准角度")]
    [Tooltip("root 骨骼最大偏航角；超出则转身")]
    public float maxRootYaw = 40f;
    [Tooltip("死区：|yaw| 小于此值用 Center 状态")]
    public float centerDeadzone = 8f;
    [Tooltip("超过 maxRootYaw 时身体转向的最大速度（度/秒）")]
    public float bodyTurnSpeed = 360f;
    [Tooltip("身体转身平滑时间（越大越缓、越自然；0=瞬时）")]
    public float turnSmoothTime = 0.2f;
    [Tooltip("上半身 root 瞄准平滑时间（小幅迟滞让瞄准更自然）")]
    public float rootSmoothTime = 0.08f;

    [Header("动画状态名（瞄准状态所在层会自动检测）")]
    public string centerState = "Beiye_root|Rifle_Aim_Cente";
    public string leftState   = "Beiye_root|Rifle_Aim_left";
    public string rightState  = "Beiye_root|Rifle_Aim_right";
    [Tooltip("不瞄准时的上半身状态。留空=用 centerState（中立瞄准姿态）；有 relax/idle 状态就填它")]
    public string idleState = "";
    [Tooltip("瞄准状态所在层名（默认 Upper Body Layer；留空则自动用含 centerState 的层）")]
    public string aimLayerName = "Upper Body Layer";
    [Tooltip("状态切换淡入时间（秒）")]
    public float stateCrossfade = 0.15f;

    [Header("腿部运动 (Base Layer)")]
    [Tooltip("是否驱动 Base Layer 腿部动画（站立/巡逻走/追击跑）")]
    public bool driveLocomotion = true;
    [Tooltip("站立（不移动）时的状态")]
    public string standState = "Beiye_root|Stand";
    [Tooltip("巡逻/调查（移动但没看见玩家）时的状态")]
    public string walkState  = "Beiye_root|Walk";
    [Tooltip("追击（移动且看见玩家）时的状态")]
    public string runState   = "Beiye_root|Run";
    [Tooltip("腿部所在层名（默认 Base Layer；留空则自动用含 walkState 的层）")]
    public string baseLayerName = "Base Layer";

    [Header("行为")]
    [Tooltip("停止调用 AimAt 超过此秒数后回到 idle/Cente")]
    public float aimHoldTime = 0.4f;
    [Tooltip("打印调试日志（排查状态名/层不匹配）")]
    public bool enableDebugLog = false;

    public bool IsAiming { get; private set; }

    private Vector3 _aimTarget;
    private float _lastAimTime = -999f;
    private string _currentState;
    private int _aimLayer = -1;
    private int _baseLayer = -1;
    private string _currentBaseState;
    private float _bodyYawVel;   // 身体转身平滑速度（SmoothDampAngle 用）
    private float _rootYaw;      // 平滑后的 root 偏航
    private float _rootYawVel;   // root 偏航平滑速度
    private UnitMovement _movement;

    void Awake()
    {
        _movement = GetComponent<UnitMovement>();

        // 挂错位置自检：本组件必须挂在敌人【根物体】（有 UnitMovement/EnemyAIController）上，
        // 不能挂在 Beiye_root 骨骼上——否则 transform 变成骨骼、身体朝向/转身全错，
        // 且 EnemyAIController/CombatExecutor/SoundPerception 用 GetComponent 找不到它、AimAt 永不触发。
        if (_movement == null && GetComponent<EnemyAIController>() == null)
            Debug.LogError($"[ProceduralAimController] 挂错位置了！应挂在敌人根物体（有 UnitMovement/EnemyAIController），" +
                           $"当前挂在 '{name}'。请移到敌人根物体，并把 Root Bone 设为 Beiye_root 骨骼。", this);
    }

    /// <summary>AI 调用：瞄准世界坐标点（每帧持续调用以保持瞄准）。</summary>
    public void AimAt(Vector3 worldPos)
    {
        _aimTarget   = worldPos;
        _lastAimTime = Time.time;
        IsAiming     = true;
    }

    /// <summary>停止瞄准，回到 Relax。</summary>
    public void StopAiming() => IsAiming = false;

    void LateUpdate()
    {
        if (!ResolveRefs()) return;

        // 先更新 IsAiming（超时收枪），供腿部判断"是否看见玩家"
        if (IsAiming && Time.time - _lastAimTime > aimHoldTime)
            IsAiming = false;

        // 腿部（Base Layer）：站立=Stand；移动时——看见玩家(IsAiming)=Run(追击)，否则=Walk(巡逻/调查丢失视野)
        if (driveLocomotion && _movement != null)
        {
            string loco = !_movement.IsMoving ? standState
                        : (IsAiming ? runState : walkState);
            CrossFadeState(_baseLayer, loco, ref _currentBaseState);
        }

        if (!IsAiming)
        {
            // 收枪：平滑量归零，下次瞄准从头缓动
            _bodyYawVel = 0f; _rootYaw = 0f; _rootYawVel = 0f;
            CrossFadeState(_aimLayer, string.IsNullOrEmpty(idleState) ? centerState : idleState, ref _currentState);
            return;
        }

        // 目标相对身体的水平方向
        Vector3 flatDir = _aimTarget - transform.position;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;

        float yaw = Vector3.SignedAngle(transform.forward, flatDir, Vector3.up);

        // 超出 root 范围：平滑转身把目标带回范围内（SmoothDampAngle：起步加速、临近减速，自然）。
        // 移动中不转身——身体朝向交给 UnitMovement（面向行进方向），此时只用 root 尽力瞄。
        bool moving = _movement != null && _movement.IsMoving;
        if (Mathf.Abs(yaw) > maxRootYaw && !moving)
        {
            float curY = transform.eulerAngles.y;
            float tgtY = Quaternion.LookRotation(flatDir, Vector3.up).eulerAngles.y;
            float newY = Mathf.SmoothDampAngle(curY, tgtY, ref _bodyYawVel, turnSmoothTime, bodyTurnSpeed);
            Vector3 e = transform.eulerAngles; e.y = newY; transform.eulerAngles = e;

            // 转身后重算 yaw，root 用剩余角度
            flatDir = _aimTarget - transform.position;
            flatDir.y = 0f;
            yaw = Vector3.SignedAngle(transform.forward, flatDir, Vector3.up);
        }

        float clamped = Mathf.Clamp(yaw, -maxRootYaw, maxRootYaw);

        // 上半身瞄准也做小幅平滑（跟随迟滞，更像真人）
        _rootYaw = Mathf.SmoothDampAngle(_rootYaw, clamped, ref _rootYawVel, rootSmoothTime);

        // 选动画状态（用平滑后的角度）：死区内 Center，否则按正负 left/right
        string state = Mathf.Abs(_rootYaw) < centerDeadzone ? centerState
                     : (_rootYaw < 0f ? leftState : rightState);
        CrossFadeState(_aimLayer, state, ref _currentState);

        // 叠加 root 骨骼偏航（世界 up 轴，避免 rig 局部轴朝向问题）
        if (rootBone != null)
            rootBone.rotation = Quaternion.AngleAxis(_rootYaw, Vector3.up) * rootBone.rotation;
    }

    // 通用状态切换：在指定层 CrossFade 到 state（用 tracker 去重）。瞄准层和腿部层共用。
    private void CrossFadeState(int layer, string state, ref string tracker)
    {
        if (string.IsNullOrEmpty(state) || state == tracker || animator == null || layer < 0) return;
        if (!animator.HasState(layer, Animator.StringToHash(state)))
        {
            if (enableDebugLog)
                Debug.LogWarning($"[Anim] 层 {layer} 没有状态 '{state}'，CrossFade 会静默失败。检查状态名/层是否正确。", this);
            return;
        }
        animator.CrossFade(state, stateCrossfade, layer);
        tracker = state;
        if (enableDebugLog) Debug.Log($"[Anim] CrossFade → '{state}' (layer {layer})", this);
    }

    private bool ResolveRefs()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null) return false;

        if (rootBone == null && !string.IsNullOrEmpty(rootBoneName))
            rootBone = FindDeepChild(animator.transform, rootBoneName);

        // 解析瞄准状态所在层：优先按层名，否则找含 centerState 的层
        if (_aimLayer < 0)
        {
            if (!string.IsNullOrEmpty(aimLayerName))
                for (int i = 0; i < animator.layerCount; i++)
                    if (animator.GetLayerName(i) == aimLayerName) { _aimLayer = i; break; }

            if (_aimLayer < 0)
            {
                int h = Animator.StringToHash(centerState);
                for (int i = 0; i < animator.layerCount; i++)
                    if (animator.HasState(i, h)) { _aimLayer = i; break; }
            }

            if (_aimLayer < 0) _aimLayer = 0;
            // 保证瞄准层权重为 1（上半身始终有动画；瞄准/收枪只切状态、不动权重）
            if (_aimLayer > 0) animator.SetLayerWeight(_aimLayer, 1f);
            if (enableDebugLog)
                Debug.Log($"[Anim] 瞄准层 = {_aimLayer} ({animator.GetLayerName(_aimLayer)})", this);
        }

        // 解析腿部层（Base Layer）：优先按层名，否则找含 walkState 的层
        if (_baseLayer < 0)
        {
            if (!string.IsNullOrEmpty(baseLayerName))
                for (int i = 0; i < animator.layerCount; i++)
                    if (animator.GetLayerName(i) == baseLayerName) { _baseLayer = i; break; }

            if (_baseLayer < 0)
            {
                int h = Animator.StringToHash(walkState);
                for (int i = 0; i < animator.layerCount; i++)
                    if (animator.HasState(i, h)) { _baseLayer = i; break; }
            }

            if (_baseLayer < 0) _baseLayer = 0;
            if (enableDebugLog)
                Debug.Log($"[Anim] 腿部层 = {_baseLayer} ({animator.GetLayerName(_baseLayer)})", this);
        }

        return true;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root.name == childName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepChild(root.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }
}
