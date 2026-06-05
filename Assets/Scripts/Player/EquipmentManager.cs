using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class HotbarSlot
{
    public ItemData itemData;
    public int quantity;

    public bool IsEmpty => itemData == null || quantity <= 0;

    public HotbarSlot() { }
    public HotbarSlot(ItemData data, int qty) { itemData = data; quantity = qty; }
}

/// <summary>
/// 装备管理器（快捷栏系统）
/// 职责：纯数据和逻辑层，不处理任何输入
/// 1. 从背包同步消耗品到快捷栏
/// 2. 管理当前选中槽位
/// 3. 更新瞄准目标位置
/// 4. 执行投掷和使用
/// 所有按键/鼠标输入由 PlayerInputController 负责调用本类的公开方法
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    [Header("快捷栏配置")]
    [SerializeField] private int hotbarSize = 5;

    [Header("投掷配置")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask throwTargetLayer;

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;

    private List<HotbarSlot> hotbar = new List<HotbarSlot>();
    private int currentSlotIndex = 0;
    private Inventory inventory;
    private UnitMovement playerUnit;
    private TurnBasedUnit turnBasedUnit;

    // 瞄准状态（由 PlayerInputController 每帧调用 UpdateAiming 更新）
    private bool isAiming = false;
    private Vector3 aimTargetPos;

    // ============ 事件 ============

    public event System.Action<int, HotbarSlot> OnSlotChanged;
    public event System.Action<ItemData> OnItemUsed;

    // ============ 公开属性 ============

    public int CurrentSlotIndex => currentSlotIndex;
    public HotbarSlot CurrentSlot => hotbar.Count > 0 ? hotbar[currentSlotIndex] : null;
    public bool IsAiming => isAiming;
    public Vector3 AimTargetPos => aimTargetPos;
    public int HotbarSize => hotbarSize;
    public HotbarSlot GetSlot(int index) => (index >= 0 && index < hotbar.Count) ? hotbar[index] : null;

    // ============ 初始化 ============

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        playerUnit    = GetComponent<UnitMovement>();
        turnBasedUnit = GetComponent<TurnBasedUnit>();

        for (int i = 0; i < hotbarSize; i++)
            hotbar.Add(new HotbarSlot());

        var existingInventory = GetComponent<Inventory>();
        if (existingInventory != null)
            SetInventory(existingInventory);
        else
            Debug.Log("[EquipmentManager] Waiting for SetInventory() call");
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= SyncHotbarFromInventory;
    }

    // ============ 外部注入 ============

    /// <summary>
    /// 注入 Inventory（由 PlayerInputController 在 Inventory 初始化后调用）
    /// </summary>
    public void SetInventory(Inventory inv)
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= SyncHotbarFromInventory;

        inventory = inv;

        if (inventory != null)
        {
            inventory.OnInventoryChanged += SyncHotbarFromInventory;
            SyncHotbarFromInventory();
            Debug.Log($"[EquipmentManager] Inventory linked: {inventory.gameObject.name}");
        }
    }

    // ============ 槽位管理（由 PlayerInputController 调用）============

    /// <summary>
    /// 切换到指定槽位
    /// </summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= hotbarSize) return;
        currentSlotIndex = index;
        DebugLog($"Selected slot {index}: {CurrentSlot?.itemData?.Name ?? "Empty"}");
        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
    }

    /// <summary>
    /// 滚轮切换槽位（由 PlayerInputController 传入 scrollDelta）
    /// scrollDelta > 0 = 向上滚 = 上一个，scrollDelta < 0 = 下一个
    /// </summary>
    public void ScrollSlot(float scrollDelta)
    {
        if (Mathf.Abs(scrollDelta) < 0.01f) return;
        int dir = scrollDelta > 0 ? -1 : 1;
        SelectSlot((currentSlotIndex + dir + hotbarSize) % hotbarSize);
    }

    // ============ 瞄准（由 PlayerInputController 每帧调用）============

    /// <summary>
    /// 更新瞄准目标位置
    /// 由 PlayerInputController.Update 每帧调用
    /// inMeleeMode = true 时禁用抛物线瞄准显示
    /// </summary>
    public bool UpdateAiming(bool inMeleeMode = false)
    {
        // 近战模式下不显示投掷瞄准线
        if (inMeleeMode)
        {
            isAiming = false;
            return false;
        }

        if (CurrentSlot == null || CurrentSlot.IsEmpty || !CurrentSlot.itemData.IsThrowable)
        {
            isAiming = false;
            return false;
        }

        if (TryGetAimPosition(out Vector3 pos))
        {
            aimTargetPos = pos;
            isAiming = true;
        }
        else
        {
            isAiming = false;
        }

        return isAiming;
    }

    /// <summary>
    /// 立即清除瞄准状态，进入近战模式时调用
    /// </summary>
    public void ClearAiming()
    {
        isAiming = false;
    }

    /// <summary>
    /// 计算鼠标指向的世界坐标（当前楼层过滤）
    /// </summary>
    public bool TryGetAimPosition(out Vector3 result)
    {
        result = Vector3.zero;

        ThrowableConfig cfg = CurrentSlot?.itemData?.ThrowCfg;
        if (cfg == null) return false;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, throwTargetLayer);

        if (hits.Length == 0) return false;

        float targetY = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorWorldY(playerUnit != null ? playerUnit.CurrentFloor : 0)
            : 0f;

        RaycastHit bestHit = hits[0];
        float minYDiff = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            float yDiff = Mathf.Abs(h.point.y - targetY);
            if (yDiff < minYDiff) { minYDiff = yDiff; bestHit = h; }
        }

        Vector3 hitPoint = bestHit.point;
        hitPoint.y = targetY;

        float maxDist = cfg.throwRange * (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);
        Vector3 toHit = hitPoint - transform.position;

        result = toHit.magnitude > maxDist
            ? transform.position + toHit.normalized * maxDist
            : hitPoint;

        return true;
    }

    // ============ 投掷（由 PlayerInputController 调用）============

    /// <summary>
    /// 投掷当前物品
    /// 调用前需要先调用 TryGetAimPosition 获取目标位置
    /// </summary>
    public bool ThrowItem(Vector3 targetPos)
    {
        if (CurrentSlot == null || CurrentSlot.IsEmpty || !CurrentSlot.itemData.IsThrowable)
        {
            DebugLog("Current item is not throwable");
            return false;
        }

        ItemData item = CurrentSlot.itemData;
        ThrowableConfig cfg = item.ThrowCfg;
        Vector3 origin = transform.position + Vector3.up * 1.2f;

        DebugLog($"Throwing [{item.Name}] → {targetPos}");

        ThrowableProjectile.Launch(item, cfg, origin, targetPos, gameObject);

        inventory.RemoveItem(item, 1);

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(item.UseCost);

        OnItemUsed?.Invoke(item);
        SyncHotbarFromInventory();
        isAiming = false;
        return true;
    }

    // ============ 近战攻击 ============

    /// <summary>
    /// 执行近战攻击
    /// 对目标格子上的所有 IDamageable 造成 meleeDamage 伤害
    /// 由 PlayerInputController 在近战模式下左键点击时调用
    /// </summary>
    public void ExecuteMelee(Vector2Int targetGrid)
    {
        if (CurrentSlot == null || CurrentSlot.IsEmpty) return;
        if (!(CurrentSlot.itemData is ConsumableData cd) || cd.meleeDamage <= 0) return;

        // 把目标格子的世界坐标作为检测中心
        Vector3 targetWorldPos = FloorManager.Instance != null
            ? FloorManager.Instance.GridToWorld(targetGrid, playerUnit.CurrentFloor)
            : GridManager.Instance.GridToWorld(targetGrid);

        // 在目标格子做一个小范围检测，半格大小确保只打这一格的敌人
        float halfCell = GridManager.Instance.CellSize * 0.6f;
        Collider[] hits = Physics.OverlapSphere(targetWorldPos, halfCell, cd.meleeLayer);

        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();
        foreach (var hit in hits)
        {
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) continue;
            if (alreadyHit.Contains(damageable)) continue;
            alreadyHit.Add(damageable);

            damageable.TakeDamage(cd.meleeDamage, gameObject);
            DebugLog($"Melee [{CurrentSlot.itemData.Name}] dealt {cd.meleeDamage} to {hit.transform.root.name}");
        }

        // 消耗品近战使用后移除（台球杆等可重复使用的物品 meleeDamage 可以设置 UseCost=0 且不走这里）
        if (cd.Type == ItemType.Consumable && cd.UseCost > 0)
            inventory.RemoveItem(cd, 1);

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(Mathf.Max(cd.UseCost, 1));

        OnItemUsed?.Invoke(CurrentSlot.itemData);
        SyncHotbarFromInventory();
    }

    /// <summary>
    /// 直接使用当前消耗品（回血等）
    /// </summary>
    public bool UseItem()
    {
        if (CurrentSlot == null || CurrentSlot.IsEmpty)
        {
            DebugLog("No item to use");
            return false;
        }

        ItemData item = CurrentSlot.itemData;

        if (inventory != null && inventory.UseItem(item))
        {
            DebugLog($"Used [{item.Name}]");

            if (turnBasedUnit != null)
                turnBasedUnit.ConsumeAP(item.UseCost);

            OnItemUsed?.Invoke(item);
            SyncHotbarFromInventory();
            return true;
        }

        return false;
    }

    // ============ 背包同步 ============

    private void SyncHotbarFromInventory()
    {
        if (inventory == null) return;

        for (int i = 0; i < hotbarSize; i++)
            hotbar[i] = new HotbarSlot();

        int slotIndex = 0;
        foreach (var slot in inventory.GetAllItems())
        {
            if (slotIndex >= hotbarSize) break;
            if (slot.itemData == null) continue;

            if (slot.itemData.Type == ItemType.Consumable)
                hotbar[slotIndex++] = new HotbarSlot(slot.itemData, slot.quantity);
        }

        DebugLog($"Hotbar synced: {slotIndex} consumable item(s)");
        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
    }

    // ============ 调试 ============

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[EquipmentManager] {msg}");
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isAiming) return;

        ThrowableConfig cfg = CurrentSlot?.itemData?.ThrowCfg;
        if (cfg == null) return;

        Vector3 start = transform.position + Vector3.up * 1.2f;

        Gizmos.color = cfg.IsArc ? Color.yellow : Color.cyan;
        Vector3 prev = start;
        for (int i = 1; i <= 20; i++)
        {
            float t = i / 20f;
            Vector3 linear = Vector3.Lerp(start, aimTargetPos, t);
            float arc = cfg.IsArc ? Mathf.Sin(t * Mathf.PI) * cfg.arcHeight : 0f;
            Vector3 pos = linear + Vector3.up * arc;
            Gizmos.DrawLine(prev, pos);
            prev = pos;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(aimTargetPos, 0.3f);

        if (cfg.HasSplash && GridManager.Instance != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.4f);
            Gizmos.DrawWireSphere(aimTargetPos, cfg.splashRadius * GridManager.Instance.CellSize);
        }

        if (GridManager.Instance != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, cfg.throwRange * GridManager.Instance.CellSize);
        }
    }

    void OnGUI()
    {
        if (!enableDebugLog || !Application.isPlaying) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft
        };
        style.normal.textColor = Color.white;

        string info = "[Hotbar]\n";
        for (int i = 0; i < hotbarSize; i++)
        {
            string prefix   = i == currentSlotIndex ? "► " : "  ";
            string itemName = hotbar[i].IsEmpty
                ? "---"
                : $"{hotbar[i].itemData.Name} x{hotbar[i].quantity}";
            string tag = (!hotbar[i].IsEmpty && hotbar[i].itemData.IsThrowable) ? " [T]" : "";
            info += $"{prefix}[{i + 1}] {itemName}{tag}\n";
        }

        if (isAiming)
            info += "\n[AIMING] LClick=Throw";
        else if (CurrentSlot != null && !CurrentSlot.IsEmpty)
            info += "\n[RClick=Use]";

        GUI.Box(new Rect(10, 200, 210, 180), info, style);
    }
}