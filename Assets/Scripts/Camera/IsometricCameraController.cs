using UnityEngine;

/// <summary>
/// 2.5D俯视角相机控制器
/// 职责：
/// 1. 提供2.5D俯视角视角
/// 2. 跟随目标单位
/// 3. 支持缩放和手动平移
/// 特点：
/// - 独立于游戏逻辑，可以单独使用
/// - 提供多种控制模式（跟随/手动）
/// - 适配SRPG的俯视角需求
/// 使用场景：
/// - 战棋游戏
/// - 策略游戏
/// - 需要俯视角的2.5D游戏
/// </summary>
public class IsometricCameraController : MonoBehaviour
{
    // ============ 配置参数 ============
    
    [Header("相机设置")]
    [SerializeField] private Transform target;                      // 跟随的目标（通常是玩家单位）
    [SerializeField] private Vector3 offset = new Vector3(0, 10, -8);  // 相机相对目标的偏移
    [SerializeField] private float angleX = 45f;                     // 俯视角度（推荐30-60度）
    [SerializeField] private float angleY = 45f;   
    [Header("跟随设置")]
    [SerializeField] private bool followTarget = true;              // 是否跟随目标
    [SerializeField] private float followSpeed = 5f;                // 跟随速度
    [SerializeField] private float followThreshold = 0.1f;          // 跟随阈值（小于此距离不移动）

    [Header("缩放设置")]
    [SerializeField] private float zoomSpeed = 2f;                  // 缩放速度
    [SerializeField] private float minZoom = 5f;                    // 最小缩放距离
    [SerializeField] private float maxZoom = 20f;                   // 最大缩放距离
    [SerializeField] private bool allowZoom = true;                 // 是否允许缩放

    [Header("平移设置")]
    [SerializeField] private float panSpeed = 10f;                  // 平移速度
    [SerializeField] private bool allowPan = true;                  // 是否允许手动平移
    [SerializeField] private KeyCode panUpKey = KeyCode.W;          // 向上平移键
    [SerializeField] private KeyCode panDownKey = KeyCode.S;        // 向下平移键
    [SerializeField] private KeyCode panLeftKey = KeyCode.A;        // 向左平移键
    [SerializeField] private KeyCode panRightKey = KeyCode.D;       // 向右平移键

    // ============ 运行时数据 ============
    
    private Vector3 targetPosition;  // 相机的目标位置
    private Camera cam;              // Camera组件引用

    void Start()
    {
        cam = GetComponent<Camera>();
        
        // 设置相机俯视角度
        // Euler角：(X=俯视角度, Y=方向, Z=倾斜)
        transform.rotation = Quaternion.Euler(angleX, angleY, 0);

        // 初始化目标位置
        if (target != null)
        {
            targetPosition = target.position + offset;
        }
        else
        {
            targetPosition = transform.position;
        }

        transform.position = targetPosition;
    }

    void LateUpdate()
    {
        // LateUpdate确保在所有Update后执行，避免相机抖动
        HandleZoom();
        HandlePan();
        HandleFollow();
    }

    // ============ 相机控制逻辑 ============
    
    /// <summary>
    /// 处理跟随目标
    /// 使用平滑插值避免相机运动过于生硬
    /// </summary>
    private void HandleFollow()
    {
        if (!followTarget || target == null)
            return;

        // 计算理想位置（offset 会因遮挡让位而临时变陡，见 GetEffectiveOffset）
        Vector3 desiredPosition = target.position + GetEffectiveOffset();
        
        // 只有距离超过阈值才移动（避免微小抖动）
        if (Vector3.Distance(targetPosition, desiredPosition) > followThreshold)
        {
            // 使用Lerp进行平滑插值
            targetPosition = Vector3.Lerp(targetPosition, desiredPosition, followSpeed * Time.deltaTime);
            transform.position = targetPosition;
        }
    }

    // ============ 遮挡让位 ============

    [Header("遮挡让位（被墙挡住时相机主动避让）")]
    [Tooltip("开启后，玩家被墙挡住时相机会加大俯角越过前方的墙，\n" +
             "从而大幅减少『需要把墙弄透明』的情况——透明必然会泄漏隔壁房间的信息。")]
    [SerializeField] private bool avoidOcclusion = true;
    [Tooltip("留空则自动查找场景里的 CameraOcclusionHandler（复用它的射线结果，相机不再自己打一遍）")]
    [SerializeField] private CameraOcclusionHandler occlusionHandler;
    [Tooltip("完全被遮挡时额外加大的俯角（度）。太大容易晕，15~25 比较稳")]
    [SerializeField] private float avoidPitchAdd = 18f;
    [Tooltip("让位时把相机与玩家的距离缩短的比例（0.25 = 拉近 25%）。\n" +
             "升高俯角后画面会显得变远，配合拉近才是玩家视角里的『镜头推近』")]
    [Range(0f, 0.6f)]
    [SerializeField] private float avoidPullIn = 0.25f;
    [Tooltip("让位响应速度（越大越快；过大会让镜头一直晃）")]
    [SerializeField] private float avoidResponse = 2.5f;

    [Tooltip("遮挡比例 ≥ 此值才【开始】让位")]
    [Range(0f, 1f)]
    [SerializeField] private float avoidEnterThreshold = 0.5f;
    [Tooltip("遮挡比例 ≤ 此值才【解除】让位。\n" +
             "和 Enter 拉开差距形成『迟滞』——遮挡在边界反复闪烁时不会跟着来回横跳，\n" +
             "这正是相机抖动的根源。两个值越分开越稳，但响应越迟钝。")]
    [Range(0f, 1f)]
    [SerializeField] private float avoidExitThreshold = 0.2f;

    [Tooltip("★ 玩家静止时【冻结】让位状态：不再重新判定遮挡。\n" +
             "站在一格上不动时，遮挡判定可能因动画、敌人经过射线等原因来回闪，\n" +
             "镜头就会自己切来切去、视角混乱。开启后只在【移动时】才重新评估——\n" +
             "移动切换视角，玩家静止相机也静止。")]
    [SerializeField] private bool freezeWhileIdle = true;
    [Tooltip("停下后仍允许评估的缓冲时间（秒），让镜头为落脚的这一格定好位再冻结")]
    [SerializeField] private float settleTime = 0.35f;
    [Tooltip("没有 UnitMovement 时的兜底：每帧位移超过此值视为『在移动』")]
    [SerializeField] private float idleMoveEpsilon = 0.01f;

    private float _avoidBlend;        // 0=正常机位 1=完全让位
    private bool  _avoiding;          // 迟滞后的二值状态
    private bool  _handlerSearched;

    private UnitMovement _targetMovement;
    private bool    _movementSearched;
    private Vector3 _lastTargetPos;
    private float   _moveEndTime = -999f;

    /// <summary>
    /// 是否允许重新判定让位状态：移动中、或刚停下不久（settleTime 内）。
    /// 静止时返回 false → 状态冻结，镜头不会再自己横跳。
    /// 注意冻结的只是【状态决策】，_avoidBlend 的平滑过渡照常走完，不会卡在半路。
    /// </summary>
    private bool ShouldReevaluate()
    {
        if (!freezeWhileIdle) return true;
        if (target == null)   return false;

        if (_targetMovement == null && !_movementSearched)
        {
            _targetMovement  = target.GetComponent<UnitMovement>();
            _movementSearched = true;
        }

        bool moving;
        if (_targetMovement != null)
        {
            moving = _targetMovement.IsMoving;
        }
        else
        {
            Vector3 p = target.position;
            moving = (p - _lastTargetPos).sqrMagnitude > idleMoveEpsilon * idleMoveEpsilon;
            _lastTargetPos = p;
        }

        if (moving) _moveEndTime = Time.time;
        return moving || (Time.time - _moveEndTime) < settleTime;
    }

    /// <summary>
    /// 被遮挡时让相机"让位"：加大俯角（更居高临下）越过前方的墙。
    ///
    /// 注意本相机的旋转在 Start 里固定设定，**固定旋转下单纯抬高相机只会让玩家在画面里下移**，
    /// 并不能看到墙后面。所以这里同时做两件事、且角度一致，保证玩家仍大致居中：
    ///   1. 俯角 angleX += θ
    ///   2. offset 绕目标、沿相机右轴旋转 θ（距离不变 → 相机升高且水平靠近）
    /// </summary>
    private Vector3 GetEffectiveOffset()
    {
        if (!avoidOcclusion)
        {
            _avoiding = false;
        }
        else
        {
            if (occlusionHandler == null && !_handlerSearched)
            {
                occlusionHandler = FindObjectOfType<CameraOcclusionHandler>();
                _handlerSearched = true;
            }

            if (occlusionHandler == null)
            {
                _avoiding = false;
            }
            else if (ShouldReevaluate())
            {
                // 两道防抖，缺一不可：
                //   ① 冻结（ShouldReevaluate）——玩家静止时根本不重新判定，站桩时镜头绝对不动
                //   ② 迟滞（下面两档阈值）——移动过程中遮挡比例在边界闪烁时也不会来回横跳
                float occ = occlusionHandler.OcclusionAmount;
                if (!_avoiding && occ >= avoidEnterThreshold)      _avoiding = true;
                else if (_avoiding && occ <= avoidExitThreshold)   _avoiding = false;
            }
        }

        // 目标是 0/1 的二值，再由 MoveTowards 平滑过渡——中间不会有随遮挡比例来回微调的抖动
        _avoidBlend = Mathf.MoveTowards(_avoidBlend, _avoiding ? 1f : 0f, avoidResponse * Time.deltaTime);

        float pitchAdd = avoidPitchAdd * _avoidBlend;
        transform.rotation = Quaternion.Euler(angleX + pitchAdd, angleY, 0f);

        if (_avoidBlend <= 0.001f) return offset;

        // 俯角加大的同时，把 offset 绕右轴转同样角度 → 相机升高、水平靠近，玩家仍居中
        Vector3 right = Quaternion.Euler(0f, angleY, 0f) * Vector3.right;
        Vector3 o = Quaternion.AngleAxis(pitchAdd, right) * offset;

        // 升高之后再整体拉近：玩家视角里就是「镜头推近」
        return o * (1f - avoidPullIn * _avoidBlend);
    }

    /// <summary>
    /// 处理缩放
    /// 通过鼠标滚轮调整相机距离
    /// </summary>
    private void HandleZoom()
    {
        if (!allowZoom)
            return;

        // 获取鼠标滚轮输入
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // 根据滚轮方向调整offset
            Vector3 newOffset = offset - offset.normalized * scroll * zoomSpeed;
            float newDistance = newOffset.magnitude;

            // 限制缩放范围
            if (newDistance >= minZoom && newDistance <= maxZoom)
            {
                offset = newOffset;
                targetPosition = (target != null ? target.position : Vector3.zero) + offset;
            }
        }
    }

    /// <summary>
    /// 处理手动平移（WASD 已禁用，避免与玩家移动冲突）
    /// </summary>
    private void HandlePan()
    {
        // WASD 平移已注释，防止与玩家格子点击移动冲突
        // if (!allowPan) return;
        // Vector3 panDirection = Vector3.zero;
        // if (Input.GetKey(panUpKey))    panDirection += Vector3.forward;
        // if (Input.GetKey(panDownKey))  panDirection += Vector3.back;
        // if (Input.GetKey(panLeftKey))  panDirection += Vector3.left;
        // if (Input.GetKey(panRightKey)) panDirection += Vector3.right;
        // if (panDirection != Vector3.zero)
        // {
        //     followTarget = false;
        //     panDirection.Normalize();
        //     targetPosition += panDirection * panSpeed * Time.deltaTime;
        //     transform.position = targetPosition;
        // }

        // Space 键回到跟随模式也已移除，改为调用 ReturnToFollow() 公开方法
    }

    // ============ 公开接口 ============

    /// <summary>
    /// 设置跟随目标
    /// 用途：切换跟随的单位（如切换控制角色时）
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            followTarget = true;
            targetPosition = target.position + offset;
        }
    }

    /// <summary>
    /// 立即跳转到目标位置（瞬移，无过渡）
    /// 用途：场景切换、传送等需要立即到位的场景
    /// </summary>
    public void SnapToTarget()
    {
        if (target != null)
        {
            targetPosition = target.position + offset;
            transform.position = targetPosition;
        }
    }

    /// <summary>
    /// 回到跟随模式（平滑过渡）
    /// 用途：剧情演出结束后调用，相机平滑回到玩家身上
    /// 示例：cutsceneManager.OnCutsceneEnd += cameraController.ReturnToFollow;
    /// </summary>
    public void ReturnToFollow()
    {
        if (target == null) return;

        followTarget = true;
        // 不直接设置 targetPosition，让 HandleFollow 的 Lerp 平滑过渡回去
        Debug.Log("[Camera] Returning to follow target after cutscene");
    }

    /// <summary>
    /// 移动相机到指定世界坐标（平滑过渡，用于剧情演出）
    /// 用途：剧情中聚焦到某个 NPC 或事件点
    /// 示例：cameraController.FocusOnPoint(npcTransform.position);
    /// </summary>
    public void FocusOnPoint(Vector3 worldPosition)
    {
        followTarget = false;
        targetPosition = worldPosition + offset;
        Debug.Log($"[Camera] Focusing on point: {worldPosition}");
    }

    /// <summary>
    /// 移动相机聚焦到指定 Transform（平滑过渡，用于剧情演出）
    /// 用途：剧情中跟随某个特定角色
    /// 示例：cameraController.FocusOnTarget(enemyTransform);
    /// </summary>
    public void FocusOnTarget(Transform focusTarget)
    {
        if (focusTarget == null) return;

        followTarget = false;
        targetPosition = focusTarget.position + offset;
        Debug.Log($"[Camera] Focusing on target: {focusTarget.name}");
    }

    // ============ 调试可视化 ============
    
    /// <summary>
    /// Gizmos绘制：显示相机到目标的连线
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (target != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, target.position);
            Gizmos.DrawWireSphere(target.position, 0.5f);
        }
    }
}