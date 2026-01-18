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

        // 计算理想位置
        Vector3 desiredPosition = target.position + offset;
        
        // 只有距离超过阈值才移动（避免微小抖动）
        if (Vector3.Distance(targetPosition, desiredPosition) > followThreshold)
        {
            // 使用Lerp进行平滑插值
            targetPosition = Vector3.Lerp(targetPosition, desiredPosition, followSpeed * Time.deltaTime);
            transform.position = targetPosition;
        }
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
    /// 处理手动平移
    /// 使用WASD键移动相机
    /// </summary>
    private void HandlePan()
    {
        if (!allowPan)
            return;

        Vector3 panDirection = Vector3.zero;

        // 收集输入
        if (Input.GetKey(panUpKey))
            panDirection += Vector3.forward;
        if (Input.GetKey(panDownKey))
            panDirection += Vector3.back;
        if (Input.GetKey(panLeftKey))
            panDirection += Vector3.left;
        if (Input.GetKey(panRightKey))
            panDirection += Vector3.right;

        // 执行平移
        if (panDirection != Vector3.zero)
        {
            // 暂时禁用跟随（让玩家可以自由查看）
            followTarget = false;
            
            panDirection.Normalize();
            targetPosition += panDirection * panSpeed * Time.deltaTime;
            transform.position = targetPosition;
        }

        // 空格键：回到跟随模式
        if (Input.GetKeyDown(KeyCode.Space) && target != null)
        {
            followTarget = true;
            targetPosition = target.position + offset;
        }
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
    /// 立即跳转到目标位置
    /// 用途：瞬移相机（如场景切换、传送等）
    /// </summary>
    public void SnapToTarget()
    {
        if (target != null)
        {
            targetPosition = target.position + offset;
            transform.position = targetPosition;
        }
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