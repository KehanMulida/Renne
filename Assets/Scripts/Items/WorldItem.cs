using UnityEngine;

/// <summary>
/// 世界物品组件（简化写实版本）
/// 职责：
/// 1. 代表场景中可拾取的物品实例
/// 2. 关联ItemData配置
/// 3. 提供拾取接口
/// 特点：
/// - 简洁实用，无多余效果
/// - 写实风格
/// - 低耦合
/// </summary>
public class WorldItem : MonoBehaviour
{
    [Header("物品配置")]
    [SerializeField] private ItemData itemData;         // 物品数据
    [SerializeField] private int quantity = 1;          // 数量

    [Header("拾取设置")]
    [SerializeField] private float pickupRange = 1.5f;  // 拾取范围

    [Header("调试")]
    [SerializeField] private bool showPickupRange = true;
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    private bool isPickedUp = false;
    private Renderer itemRenderer;

    // ============ 公开属性 ============

    public ItemData ItemData => itemData;
    public int Quantity => quantity;
    public float PickupRange => pickupRange;
    public bool IsPickedUp => isPickedUp;

    // ============ 事件系统 ============

    /// <summary>物品被拾取事件</summary>
    public event System.Action<WorldItem> OnPickedUp;

    // ============ 初始化 ============

    void Start()
    {
        itemRenderer = GetComponent<Renderer>();

        // 验证配置
        if (itemData == null)
        {
            Debug.LogWarning($"[WorldItem] {gameObject.name} has no ItemData!");
        }

        DebugLog($"Initialized: {itemData?.Name ?? "Unknown"} x{quantity}");
    }

    // ============ 拾取逻辑 ============

    /// <summary>
    /// 检查指定位置是否在拾取范围内
    /// </summary>
    public bool IsInPickupRange(Vector3 position)
    {
        float distance = Vector3.Distance(transform.position, position);
        return distance <= pickupRange;
    }

    /// <summary>
    /// 拾取物品
    /// 返回：是否成功拾取
    /// </summary>
    public bool Pickup(GameObject picker)
    {
        if (isPickedUp)
        {
            return false;
        }

        if (itemData == null)
        {
            Debug.LogWarning("[WorldItem] Cannot pickup: no ItemData");
            return false;
        }

        DebugLog($"Picked up by {picker.name}: {itemData.Name} x{quantity}");

        // 标记为已拾取
        isPickedUp = true;

        // 触发事件
        OnPickedUp?.Invoke(this);

        // 播放音效（如果有）
        if (!string.IsNullOrEmpty(itemData.Sound))
        {
            // 这里可以调用音效系统
            DebugLog($"Play sound: {itemData.Sound}");
        }

        // 销毁物体
        Destroy(gameObject);

        return true;
    }

    /// <summary>
    /// 显示/隐藏高亮效果（简单版本）
    /// </summary>
    public void ShowHighlight(bool show)
    {
        if (itemRenderer == null) return;

        // 简单的颜色变化
        if (show)
        {
            // 稍微提亮颜色
            Color originalColor = itemRenderer.material.color;
            itemRenderer.material.color = originalColor * 1.3f;
        }
        else
        {
            // 恢复原色（使用稀有度颜色）
            if (itemData != null)
            {
                itemRenderer.material.color = itemData.GetRarityColor();
            }
        }
    }

    // ============ 工具方法 ============

    /// <summary>
    /// 创建物品实例（静态工厂方法）
    /// </summary>
    public static WorldItem CreateWorldItem(ItemData data, Vector3 position, int quantity = 1)
    {
        GameObject itemObject;
        
        // 如果有预制体，使用预制体
        if (data.Prefab != null)
        {
            itemObject = Instantiate(data.Prefab, position, Quaternion.identity);
        }
        else
        {
            // 默认创建简单的Cube
            itemObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            itemObject.transform.position = position;
            itemObject.transform.localScale = Vector3.one * 0.3f;
            
            // 设置颜色
            Renderer renderer = itemObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = data.GetRarityColor();
            }
        }

        itemObject.name = $"Item_{data.Name}";

        // 添加或获取WorldItem组件
        WorldItem worldItem = itemObject.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            worldItem = itemObject.AddComponent<WorldItem>();
        }
        
        worldItem.itemData = data;
        worldItem.quantity = quantity;

        return worldItem;
    }

    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[WorldItem:{gameObject.name}] {message}");
        }
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (!showPickupRange) return;

        // 只绘制拾取范围的线框
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }

    void OnDrawGizmosSelected()
    {
        // 选中时显示实心球
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Gizmos.DrawSphere(transform.position, pickupRange);

        // 显示物品信息
        #if UNITY_EDITOR
        if (itemData != null)
        {
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.5f, 
                $"{itemData.Name} x{quantity}"
            );
        }
        #endif
    }
}