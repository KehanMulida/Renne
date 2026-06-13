using UnityEngine;

// ── 场景物体运行状态 ─────────────────────────────────────────
// 供 MissionCondition.StoryObjectState 条件读取
// ObjectiveGuard 任务：守护目标物体保持 Active；玩家拾取/关闭后变为 Interrupted → 任务失败
// ObjectiveDestroy 任务：Enemy 到位交互后变为 Completed → 任务成功
// ConditionEvaluator 轮询 WorldItem.CurrentState；物体销毁后从 WorldItemRegistry 读取
public enum WorldItemState
{
    Inactive,     // 初始关闭状态，等待 AI 开启（ObjectiveActivate 任务起点）
    Active,       // 正常运转（AI 开启后 / 默认放置状态）
    Interrupted,  // 被打断 / 破坏（玩家拾取或关闭，用于 ObjectiveGuard 失败判断）
    Completed,    // 交互完成（Enemy 完成 ObjectiveDestroy，或剧情触发）
}

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

    [Header("唯一标识（任务条件用）")]
    [Tooltip("场景中全局唯一 ID，供 MissionCondition.objectId 查找\n" +
             "ObjectiveGuard / ObjectiveDestroy 任务必填，如 generator_b1 / relay_switch_02\n" +
             "普通拾取物品可留空")]
    [SerializeField] private string objectId = "";

    [Header("拾取设置")]
    [SerializeField] private float pickupRange = 1.5f;  // 拾取范围

    [Header("调试")]
    [SerializeField] private bool showPickupRange = true;
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    private bool isPickedUp = false;
    private WorldItemState currentState = WorldItemState.Active;
    private Renderer itemRenderer;

    /// <summary>
    /// 与本物体交互（拾取/关闭/破坏）的最后角色的阵营标识
    /// "Player" / "Enemy" / "" (未触碰 / 脚本直接调用)
    /// 由 Pickup() / TriggerInterrupt() 自动设置；
    /// SetState() 调用时也写入 WorldItemRegistry，物体销毁后仍可查询
    /// </summary>
    private string interactedByFaction = "";

    // ============ 公开属性 ============

    public ItemData ItemData => itemData;
    public int Quantity => quantity;
    public float PickupRange => pickupRange;
    public bool IsPickedUp => isPickedUp;

    /// <summary>场景唯一标识，供 ConditionEvaluator 查找</summary>
    public string ObjectId => objectId;

    /// <summary>当前物体状态（Active / Interrupted / Completed）</summary>
    public WorldItemState CurrentState => currentState;

    /// <summary>
    /// 最后与本物体交互的角色阵营（"Player" / "Enemy" / ""）
    /// ConditionEvaluator 用于 PickedUpByPlayer / PickedUpByEnemy 等扩展条件
    /// </summary>
    public string InteractedByFaction => interactedByFaction;

    // ============ 事件系统 ============

    /// <summary>物品被拾取事件</summary>
    public event System.Action<WorldItem> OnPickedUp;

    /// <summary>物体状态变更事件（旧状态, 新状态）</summary>
    public event System.Action<WorldItem, WorldItemState> OnStateChanged;

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

    // ============ 物体状态 ============

    /// <summary>
    /// 设置物体状态（供任务系统、玩家交互、剧情脚本调用）
    /// ObjectiveGuard 失败时 → SetState(Interrupted)
    /// ObjectiveDestroy 完成时 → SetState(Completed)
    ///
    /// ⚑ 状态变更会同步写入 WorldItemRegistry，物体销毁后
    ///   ConditionEvaluator 仍能通过注册表查询最后已知状态
    /// </summary>
    public void SetState(WorldItemState newState)
    {
        if (currentState == newState) return;
        var prev = currentState;
        currentState = newState;
        Debug.Log($"[WorldItem:{gameObject.name}] State: {prev} → {newState}");
        OnStateChanged?.Invoke(this, newState);

        // 同步到注册表：即使物体随后被销毁，状态记录也不会丢失
        if (!string.IsNullOrEmpty(objectId))
            WorldItemRegistry.Record(objectId, newState, interactedByFaction);
    }

    /// <summary>
    /// 非拾取型交互：关闭/停用/破坏设备（玩家或 NPC 操作开关、发电机等）
    /// 将状态切换为 Interrupted，记录操作方阵营，供 ObjectiveGuard 失败条件使用
    ///
    /// 用法示例（玩家关闭发电机）：
    ///   generator.TriggerInterrupt(gameObject);  // 在玩家的 Interact 处理里调用
    /// </summary>
    public void TriggerInterrupt(GameObject actor = null)
    {
        if (actor != null)
            interactedByFaction = ResolveFaction(actor);
        SetState(WorldItemState.Interrupted);
    }

    /// <summary>
    /// Enemy 完成 ObjectiveDestroy 任务时调用：标记物体为已完成状态
    /// 通常在 EnemyAIController 的 ObjectiveDestroy 行为节点执行完毕后调用
    /// </summary>
    public void TriggerComplete(GameObject actor = null)
    {
        if (actor != null)
            interactedByFaction = ResolveFaction(actor);
        SetState(WorldItemState.Completed);
    }

    /// <summary>
    /// Enemy 开启/修复物体时调用（ObjectiveActivate / ObjectiveRepair）
    /// 将状态切换为 Active，供 ObjectiveGuard 成功条件和守卫巡逻使用
    /// </summary>
    public void TriggerActivate(GameObject actor = null)
    {
        if (actor != null)
            interactedByFaction = ResolveFaction(actor);
        SetState(WorldItemState.Active);
    }

    /// <summary>根据组件判断 actor 所属阵营字符串</summary>
    private static string ResolveFaction(GameObject actor)
    {
        if (actor.GetComponent<PlayerController>() != null)  return "Player";
        if (actor.GetComponent<EnemyAIController>() != null) return "Enemy";
        // 兜底：使用 Tag
        return string.IsNullOrEmpty(actor.tag) ? "Unknown" : actor.tag;
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
    ///
    /// 对于有 objectId 的任务物品，会在销毁前自动切换 WorldItemState：
    ///   玩家拾取  → Interrupted（ObjectiveGuard 失败条件触发）
    ///   敌人拾取  → Completed（ObjectiveDestroy 成功条件触发）
    /// 状态及拾取方阵营同步写入 WorldItemRegistry，销毁后仍可查询。
    /// </summary>
    public bool Pickup(GameObject picker)
    {
        if (isPickedUp) return false;

        if (itemData == null)
        {
            Debug.LogWarning("[WorldItem] Cannot pickup: no ItemData");
            return false;
        }

        DebugLog($"Picked up by {picker?.name ?? "Unknown"}: {itemData.Name} x{quantity}");

        isPickedUp = true;

        // 判断拾取方阵营
        if (picker != null)
            interactedByFaction = ResolveFaction(picker);

        // 有 objectId 的物品是任务追踪物，根据拾取方自动切换任务状态
        // （无 objectId 的普通道具跳过，不影响任务系统）
        if (!string.IsNullOrEmpty(objectId))
        {
            if (interactedByFaction == "Player")
                SetState(WorldItemState.Interrupted);   // 玩家拿走 → 守护失败
            else if (interactedByFaction == "Enemy")
                SetState(WorldItemState.Completed);     // 敌人完成 → 破坏/收集成功
            else
            {
                // 阵营未知时也记录到注册表，保持 Active 状态
                WorldItemRegistry.Record(objectId, currentState, interactedByFaction);
            }
        }

        // 触发拾取事件（Inventory / 音效 / 成就等订阅此事件）
        OnPickedUp?.Invoke(this);

        if (!string.IsNullOrEmpty(itemData.Sound))
            DebugLog($"Play sound: {itemData.Sound}");

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