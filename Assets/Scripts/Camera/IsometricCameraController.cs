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
    [Header("玩家正上方锁定")]
    [SerializeField] private bool lockAboveTarget = false;
    [SerializeField] private LayerMask cameraObstacleLayers = 129;
    [Min(.05f)] [SerializeField] private float cameraCollisionRadius = .3f;
    [Min(.01f)] [SerializeField] private float cameraWallPadding = .12f;
    private readonly RaycastHit[] overheadHits = new RaycastHit[64];
    private MeshRenderer[] overheadGeometry;
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
        overheadGeometry = FindObjectsOfType<MeshRenderer>();
        
        // 设置相机俯视角度
        // Euler角：(X=俯视角度, Y=方向, Z=倾斜)
        transform.rotation = lockAboveTarget ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(angleX, angleY, 0);

        // 初始化目标位置
        if (target != null)
        {
            targetPosition = ResolveTargetPosition();
        }
        else
        {
            targetPosition = transform.position;
        }

        transform.position = targetPosition;
    }

    void LateUpdate()
    {
        var moveView=GetComponent<MoveEndCameraView>();
        if(moveView!=null && moveView.enabled && followTarget)
        {
            bool controlled=moveView.ControlsCamera;
            moveView.Tick(target,offset,Quaternion.Euler(angleX,angleY,0),Time.unscaledDeltaTime);
            if(controlled || moveView.ControlsCamera){targetPosition=transform.position;return;}
        }
        var roomDirector=GetComponent<RoomCameraDirector>();
        if(roomDirector!=null && roomDirector.enabled)
        {
            bool wasControlled=roomDirector.ControlsCamera;
            roomDirector.Tick(target,offset,Quaternion.Euler(angleX,angleY,0),Time.unscaledDeltaTime);
            if(roomDirector.ControlsCamera || wasControlled)
            {
                targetPosition=transform.position;
                ApplyWallDistance();
                return;
            }
        }
        // LateUpdate确保在所有Update后执行，避免相机抖动
        HandleZoom();
        HandlePan();
        HandleFollow();
        ApplyWallDistance();
    }

    private void ApplyWallDistance()
    {
        var clearance=GetComponent<CameraWallDistance>();
        if(clearance!=null && clearance.enabled && target!=null)
            transform.position=clearance.Resolve(target,transform.position,Time.unscaledDeltaTime);
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

        if (lockAboveTarget)
        {
            Vector3 safe = ResolveTargetPosition();
            float height = Mathf.Lerp(transform.position.y, safe.y, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            // XY drift is forbidden: the camera's horizontal footprint stays on the player.
            // Retreat from a new obstacle immediately; recover height smoothly after it clears.
            targetPosition = new Vector3(target.position.x, Mathf.Min(height, safe.y), target.position.z);
            transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(90f, 0f, 0f));
            return;
        }

        // 固定视角跟随，不再因遮挡旋转或绕玩家改变机位。
        Vector3 desiredPosition = target.position + offset;
        
        // 只有距离超过阈值才移动（避免微小抖动）
        if (Vector3.Distance(targetPosition, desiredPosition) > followThreshold)
        {
            // 使用Lerp进行平滑插值
            targetPosition = Vector3.Lerp(targetPosition, desiredPosition, followSpeed * Time.deltaTime);
            transform.position = targetPosition;
        }
        transform.position = targetPosition;
    }

    private Vector3 ResolveTargetPosition()
    {
        if (!lockAboveTarget) return target.position + offset;
        float desiredHeight = Mathf.Max(1f, offset.y);
        Vector3 origin = target.position + Vector3.up * .4f;
        float travel = Mathf.Max(.1f, desiredHeight - .4f);
        float lensRadius = cam != null ? cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) *
            Mathf.Sqrt(1f + cam.aspect * cam.aspect) : 0f;
        float radius = Mathf.Max(cameraCollisionRadius, lensRadius);
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.up, overheadHits, travel,
            cameraObstacleLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = overheadHits[i].collider;
            if (collider.transform == target || collider.transform.IsChildOf(target)) continue;
            var renderer = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
            if (renderer != null && (!renderer.enabled || !renderer.gameObject.activeInHierarchy)) continue;
            if (collider.GetComponentInParent<EnemyAIController>() != null || collider.GetComponentInParent<WorldItem>() != null) continue;
            travel = Mathf.Min(travel, Mathf.Max(0f, overheadHits[i].distance - cameraWallPadding));
        }
        if (overheadGeometry != null)
        {
            var ray = new Ray(origin, Vector3.up);
            foreach (var renderer in overheadGeometry)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (cameraObstacleLayers.value & (1 << renderer.gameObject.layer)) == 0 ||
                    renderer.GetComponent<Collider>() != null || renderer.transform.IsChildOf(target) || renderer.transform == target) continue;
                var bounds = renderer.bounds;
                if (bounds.max.y <= origin.y || Mathf.Max(bounds.size.x, bounds.size.z) < .8f) continue;
                bounds.Expand(radius * 2f);
                if (bounds.Contains(origin)) continue; // Never choose an exit point through a wall containing the player.
                if (bounds.IntersectRay(ray, out float distance))
                    travel = Mathf.Min(travel, Mathf.Max(0f, distance - cameraWallPadding));
            }
        }
        return new Vector3(target.position.x, origin.y + travel, target.position.z);
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
            targetPosition = ResolveTargetPosition();
            if (lockAboveTarget) SnapToTarget();
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
            targetPosition = ResolveTargetPosition();
            transform.position = targetPosition;
            if (lockAboveTarget) transform.rotation = Quaternion.Euler(90f, 0f, 0f);
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
        if (lockAboveTarget) return;
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
        if (lockAboveTarget) return;
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
