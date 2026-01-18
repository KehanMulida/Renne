using UnityEngine;

/// <summary>
/// 楼层物体标记器
/// 职责：
/// 1. 标记物体属于哪个楼层
/// 2. 自动注册到FloorVisibilityController
/// 3. 方便手动指定楼层（当自动识别不准确时）
/// 使用方法：
/// 1. 挂载到需要控制可见性的物体上
/// 2. 设置楼层编号
/// 3. 自动注册到系统
/// </summary>
public class FloorObjectMarker : MonoBehaviour
{
    [Header("楼层配置")]
    [SerializeField] private int floor = 0;                     // 所属楼层
    [SerializeField] private bool autoDetectFloor = false;      // 是否自动检测楼层

    [Header("可见性设置")]
    [SerializeField] private bool alwaysVisible = false;        // 是否始终可见（如天花板、柱子）
    [SerializeField] private bool isCharacter = false;          // 是否是角色（特殊处理）

    void Start()
    {
        // 自动检测楼层
        if (autoDetectFloor && FloorManager.Instance != null)
        {
            floor = FloorManager.Instance.GetFloorFromWorldY(transform.position.y);
            Debug.Log($"[FloorObjectMarker] Auto-detected {gameObject.name} on floor {floor}");
        }

        // 注册到可见性控制器
        if (FloorVisibilityController.Instance != null && !alwaysVisible)
        {
            FloorVisibilityController.Instance.RegisterObjectToFloor(gameObject, floor);
            Debug.Log($"[FloorObjectMarker] Registered {gameObject.name} to floor {floor}");
        }
    }

    void OnDestroy()
    {
        // 取消注册
        if (FloorVisibilityController.Instance != null && !alwaysVisible)
        {
            FloorVisibilityController.Instance.UnregisterObjectFromFloor(gameObject, floor);
        }
    }

    /// <summary>
    /// 手动更改楼层
    /// </summary>
    public void ChangeFloor(int newFloor)
    {
        if (FloorVisibilityController.Instance != null && !alwaysVisible)
        {
            // 从旧楼层移除
            FloorVisibilityController.Instance.UnregisterObjectFromFloor(gameObject, floor);
            
            // 注册到新楼层
            floor = newFloor;
            FloorVisibilityController.Instance.RegisterObjectToFloor(gameObject, floor);
        }
        else
        {
            floor = newFloor;
        }
    }

    void OnDrawGizmosSelected()
    {
        // 显示所属楼层
        Gizmos.color = Color.green;
        Vector3 labelPos = transform.position + Vector3.up * 2f;
        
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(labelPos, $"Floor {floor}");
        #endif
    }
}