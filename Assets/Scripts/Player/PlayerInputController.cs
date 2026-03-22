using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 玩家输入控制器
/// 职责：所有玩家输入的唯一入口
/// 1. 移动输入（右键点击格子）
/// 2. 回合控制（结束回合、楼层切换）
/// 3. 拾取输入
/// 4. 快捷栏输入（滚轮切换、投掷、使用物品）
/// 设计原则：
/// - 只负责读取输入和调用其他系统的公开方法
/// - 不包含任何游戏逻辑，逻辑在各自的 Manager 里
/// </summary>
public class PlayerInputController : MonoBehaviour
{
    // ============ 引用 ============

    [Header("引用")]
    [SerializeField] private UnitMovement playerUnit;
    [SerializeField] private TurnBasedUnit turnBasedUnit;
    [SerializeField] private Camera mainCamera;

    [Header("移动 & 回合输入")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private KeyCode endTurnKey = KeyCode.Space;
    [SerializeField] private KeyCode changeFloorKey = KeyCode.E;

    [Header("拾取输入")]
    [SerializeField] private KeyCode pickupKey = KeyCode.Q;
    [SerializeField] private float pickupDetectionRange = 2f;

    [Header("快捷栏输入")]
    [Tooltip("切换近战模式")]
    [SerializeField] private KeyCode meleeModeKey = KeyCode.W;
    [Tooltip("投掷 / 近战攻击（左键）")]
    [SerializeField] private KeyCode throwKey = KeyCode.Mouse0;

    // 近战模式状态
    private bool isMeleeMode = false;

    // 反应窗口状态（敌人射击时临时开放移动）
    private bool isReactionWindowOpen = false;

    // ============ 运行时数据 ============

    private HashSet<Vector2Int> currentMovementRange;
    private Vector2Int? hoveredGridPos;
    private bool isInputEnabled = false;
    private FloorConnection availableFloorConnection = null;
    private bool isShowingFloorPrompt = false;
    private List<WorldItem> nearbyItems = new List<WorldItem>();
    private Inventory playerInventory;
    private EquipmentManager equipmentManager;

    // ============ 初始化 ============

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (playerUnit == null)
        {
            Debug.LogError("[PlayerInputController] Player unit not assigned!");
            return;
        }

        if (turnBasedUnit == null)
            turnBasedUnit = playerUnit.GetComponent<TurnBasedUnit>();

        if (turnBasedUnit == null)
        {
            Debug.LogError("[PlayerInputController] TurnBasedUnit not found!");
            return;
        }

        playerUnit.OnMoveComplete += OnUnitMoveComplete;

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnFactionChanged += OnFactionChanged;
            TurnSystem.Instance.OnTurnStart += OnTurnSystemStart;

            if (TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player))
            {
                isInputEnabled = true;
                ShowMovementRange();
            }
        }
        else
        {
            Debug.LogError("[PlayerInputController] TurnSystem not found!");
        }

        turnBasedUnit.OnMyTurnStart += OnPlayerTurnStart;
        turnBasedUnit.OnMyTurnEnd += OnPlayerTurnEnd;

        // 初始化背包
        playerInventory = playerUnit.GetComponent<Inventory>();
        if (playerInventory == null)
            playerInventory = playerUnit.gameObject.AddComponent<Inventory>();

        // 获取 EquipmentManager 并注入 Inventory
        equipmentManager = playerUnit.GetComponent<EquipmentManager>();
        if (equipmentManager != null)
            equipmentManager.SetInventory(playerInventory);
        else
            Debug.LogWarning("[PlayerInputController] EquipmentManager not found on player");

        // 监听反应窗口事件
        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnReactionWindowOpened += OnReactionWindowOpened;
            turnBasedUnit.OnReactionWindowClosed += OnReactionWindowClosed;
        }

        Debug.Log("[PlayerInputController] Initialized successfully");
    }

    void Update()
    {
        CheckNearbyItems();
        HandlePickupInput();

        if (equipmentManager != null)
            equipmentManager.UpdateAiming(isMeleeMode);

        HandleHotbarInput();

        // 反应窗口：不是玩家回合也可以移动一次
        if (isReactionWindowOpen && !playerUnit.IsMoving)
        {
            HandleMouseInput();
            return;
        }

        // 正常回合输入
        if (!isInputEnabled || playerUnit.IsMoving)
            return;

        CheckFloorConnection();
        HandleMouseInput();
        HandleTurnInput();
    }

    private void OnReactionWindowOpened()
    {
        isReactionWindowOpen = true;
        Debug.Log("[Input] Reaction window opened, move to dodge!");
    }

    private void OnReactionWindowClosed()
    {
        isReactionWindowOpen = false;
        Debug.Log("[Input] Reaction window closed");
    }

    // ============ 快捷栏输入 ============

    private void HandleHotbarInput()
    {
        if (equipmentManager == null) return;

        // 滚轮切换槽位（切换时退出近战模式）
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            equipmentManager.ScrollSlot(scroll);
            isMeleeMode = false;
        }

        bool canAct = turnBasedUnit == null || (turnBasedUnit.IsMyTurn && turnBasedUnit.CanAct);
        HotbarSlot slot = equipmentManager.CurrentSlot;
        bool hasItem = slot != null && !slot.IsEmpty;

        // W 键：切换近战模式
        // 只有当前物品有近战伤害配置时才能进入近战模式
        if (Input.GetKeyDown(meleeModeKey))
        {
            bool canMelee = hasItem && slot.itemData is ConsumableData cd && cd.meleeDamage > 0;
            if (canMelee)
            {
                isMeleeMode = !isMeleeMode;
                Debug.Log($"[Input] Melee mode: {isMeleeMode}");
            }
            else
            {
                isMeleeMode = false;
            }
        }

        // 左键：近战模式下攻击，否则投掷
        if (Input.GetKeyDown(throwKey))
        {
            if (!canAct) return;

            if (isMeleeMode)
            {
                // 近战模式：检测鼠标指向的格子是否在攻击范围内
                if (TryGetMeleeTarget(out Vector2Int targetGrid))
                {
                    equipmentManager.ExecuteMelee(targetGrid);
                    isMeleeMode = false; // 攻击后退出近战模式
                }
            }
            else if (hasItem && slot.itemData.IsThrowable)
            {
                // 投掷模式
                if (equipmentManager.TryGetAimPosition(out Vector3 aimPos))
                    equipmentManager.ThrowItem(aimPos);
            }
        }
    }

    /// <summary>
    /// 获取鼠标指向的近战目标格子
    /// 必须在当前物品的 meleeRange 范围内
    /// </summary>
    private bool TryGetMeleeTarget(out Vector2Int targetGrid)
    {
        targetGrid = Vector2Int.zero;

        HotbarSlot slot = equipmentManager?.CurrentSlot;
        if (slot == null || slot.IsEmpty) return false;
        if (!(slot.itemData is ConsumableData cd) || cd.meleeDamage <= 0) return false;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, groundLayer);
        if (hits.Length == 0) return false;

        float targetY = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorWorldY(playerUnit.CurrentFloor) : 0f;

        RaycastHit bestHit = hits[0];
        float minYDiff = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            float yDiff = Mathf.Abs(h.point.y - targetY);
            if (yDiff < minYDiff) { minYDiff = yDiff; bestHit = h; }
        }

        Vector3 hitPoint = bestHit.point;
        hitPoint.y = targetY;
        Vector3 snapped = GridManager.Instance.SnapToGrid(hitPoint, playerUnit.CurrentFloor);
        targetGrid = GridManager.Instance.WorldToGrid(snapped);

        // 检查是否在近战范围内
        int range = Mathf.Max(cd.meleeRange, 1); // 最少 1 格
        int dist = Mathf.Abs(targetGrid.x - playerUnit.CurrentGridPosition.x) +
                   Mathf.Abs(targetGrid.y - playerUnit.CurrentGridPosition.y);

        return dist <= range;
    }

    /// <summary>
    /// 获取当前物品的近战范围格子集合（用于 Gizmos 显示）
    /// </summary>
    private HashSet<Vector2Int> GetMeleeRange()
    {
        HotbarSlot slot = equipmentManager?.CurrentSlot;
        if (slot == null || slot.IsEmpty) return null;
        if (!(slot.itemData is ConsumableData cd) || cd.meleeDamage <= 0) return null;

        int range = Mathf.Max(cd.meleeRange, 1);
        var result = new HashSet<Vector2Int>();
        Vector2Int center = playerUnit.CurrentGridPosition;

        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(y) > range) continue;
                if (x == 0 && y == 0) continue; // 排除玩家自身格子

                Vector2Int pos = center + new Vector2Int(x, y);
                if (GridManager.Instance.IsValid(pos))
                    result.Add(pos);
            }
        }

        return result;
    }

    // ============ 拾取输入 ============

    private void HandlePickupInput()
    {
        if (Input.GetKeyDown(pickupKey))
            TryPickupNearbyItems();
    }

    private void CheckNearbyItems()
    {
        foreach (var item in nearbyItems)
            if (item != null) item.ShowHighlight(false);
        nearbyItems.Clear();

        WorldItem[] allItems = FindObjectsOfType<WorldItem>();
        foreach (var item in allItems)
        {
            if (item == null || item.IsPickedUp) continue;
            float distance = Vector3.Distance(playerUnit.transform.position, item.transform.position);
            if (distance <= pickupDetectionRange)
            {
                nearbyItems.Add(item);
                item.ShowHighlight(true);
            }
        }
    }

    private void TryPickupNearbyItems()
    {
        if (nearbyItems.Count == 0) return;

        int pickedCount = 0;
        foreach (var item in nearbyItems.ToArray())
        {
            if (item != null && !item.IsPickedUp)
            {
                if (playerInventory != null && playerInventory.AddItem(item.ItemData, item.Quantity))
                {
                    item.Pickup(playerUnit.gameObject);
                    pickedCount++;
                    Debug.Log($"[Input] Picked up: {item.ItemData.Name} x{item.Quantity}");
                }
            }
        }

        nearbyItems.Clear();
    }

    // ============ 回合输入 ============

    private void HandleTurnInput()
    {
        if (Input.GetKeyDown(endTurnKey))
            EndPlayerTurn();

        if (Input.GetKeyDown(changeFloorKey) && availableFloorConnection != null)
            UseFloorConnection();
    }

    // ============ 鼠标移动输入 ============

    private void HandleMouseInput()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, groundLayer);

        if (hits.Length > 0)
        {
            float targetY = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloorWorldY(playerUnit.CurrentFloor)
                : 0f;

            RaycastHit? correctHit = null;
            float minYDiff = float.MaxValue;

            foreach (RaycastHit h in hits)
            {
                float yDiff = Mathf.Abs(h.point.y - targetY);
                if (yDiff < minYDiff) { minYDiff = yDiff; correctHit = h; }
            }

            if (correctHit.HasValue)
            {
                // 把命中点 Y 强制对齐到当前楼层
                Vector3 hitPoint = correctHit.Value.point;
                hitPoint.y = targetY;

                // 吸附到最近格子中心，消除射线命中点的浮点偏差
                Vector3 snappedPoint = GridManager.Instance.SnapToGrid(hitPoint, playerUnit.CurrentFloor);
                Vector2Int gridPos = GridManager.Instance.WorldToGrid(snappedPoint);

                if (hoveredGridPos == null || hoveredGridPos.Value != gridPos)
                {
                    hoveredGridPos = gridPos;
                    OnGridHovered(gridPos);
                }

                if (Input.GetMouseButtonDown(1))
                    OnGridClicked(gridPos);
            }
        }
        else
        {
            hoveredGridPos = null;
        }
    }

    private void OnGridHovered(Vector2Int gridPos)
    {
        // 预留：可以用来显示路径预览
    }

    private void OnGridClicked(Vector2Int gridPos)
    {
        if (!turnBasedUnit.CanAct) return;

        // IsWalkable 快速检查（O(1)），避免点击障碍物还去算寻路
        if (!GridManager.Instance.IsWalkable(gridPos, playerUnit.CurrentFloor))
            return;

        // 寻路只算一次
        List<Vector2Int> path = PathfindingService.FindPath(
            playerUnit.CurrentGridPosition, gridPos, playerUnit.CurrentFloor);

        if (path == null || path.Count == 0) return;

        // 检查 AP 是否足够
        if (!turnBasedUnit.HasEnoughMovementPoints(path.Count))
        {
            Debug.Log($"[Input] Not enough AP! Need: {path.Count}, Have: {turnBasedUnit.RemainingActionPoints}");
            return;
        }

        // 不再调用 CanMoveTo（内部会重复算一遍移动范围）
        // 直接移动，path 非空已经证明目标可达
        ClearMovementRange();
        playerUnit.MoveToGrid(gridPos);
    }

    // ============ 楼层切换 ============

    private void CheckFloorConnection()
    {
        if (FloorManager.Instance == null)
        {
            availableFloorConnection = null;
            isShowingFloorPrompt = false;
            return;
        }

        FloorConnection connection = FloorManager.Instance.GetConnection(
            playerUnit.CurrentGridPosition, playerUnit.CurrentFloor);

        if (connection != null && !connection.Equals(availableFloorConnection))
        {
            availableFloorConnection = connection;
            isShowingFloorPrompt = true;
        }
        else if (connection == null && availableFloorConnection != null)
        {
            availableFloorConnection = null;
            isShowingFloorPrompt = false;
        }
    }

    private void UseFloorConnection()
    {
        if (availableFloorConnection == null || !turnBasedUnit.CanAct) return;

        if (turnBasedUnit.StartAction())
        {
            ClearMovementRange();
            playerUnit.MoveToGrid(availableFloorConnection.gridPosition, availableFloorConnection.toFloor);
        }
    }

    // ============ 回合事件 ============

    private void OnTurnSystemStart(TurnData turnData)
    {
        if (turnData.currentFaction == TurnFaction.Player && !isInputEnabled)
            OnPlayerTurnStart();
    }

    private void OnFactionChanged(TurnFaction newFaction, int factionTurnNumber)
    {
        isInputEnabled = (newFaction == TurnFaction.Player);

        if (!isInputEnabled)
        {
            ClearMovementRange();
            availableFloorConnection = null;
            isShowingFloorPrompt = false;
        }
        else
        {
            ShowMovementRange();
        }
    }

    private void OnPlayerTurnStart()
    {
        isInputEnabled = true;
        ShowMovementRange();
    }

    private void OnPlayerTurnEnd()
    {
        isInputEnabled = false;
        ClearMovementRange();
        availableFloorConnection = null;
        isShowingFloorPrompt = false;
    }

    private void OnUnitMoveComplete()
    {
        // 反应窗口中移动完成，关闭窗口
        if (isReactionWindowOpen)
        {
            turnBasedUnit.CloseReactionWindow();
            return;
        }

        if (turnBasedUnit.CanAct)
            ShowMovementRange();
        else
            ClearMovementRange();
    }

    private void EndPlayerTurn()
    {
        if (TurnSystem.Instance != null && TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player))
        {
            ClearMovementRange();
            TurnSystem.Instance.EndCurrentTurn();
        }
    }

    // ============ 移动范围 ============

    private void ShowMovementRange()
    {
        if (!turnBasedUnit.CanAct) { currentMovementRange = null; return; }
        currentMovementRange = playerUnit.GetMovementRange();
    }

    private void ClearMovementRange()
    {
        currentMovementRange = null;
    }

    // ============ 清理 ============

    void OnDestroy()
    {
        if (playerUnit != null)
            playerUnit.OnMoveComplete -= OnUnitMoveComplete;

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnFactionChanged -= OnFactionChanged;
            TurnSystem.Instance.OnTurnStart -= OnTurnSystemStart;
        }

        if (turnBasedUnit != null)
        {
            turnBasedUnit.OnMyTurnStart -= OnPlayerTurnStart;
            turnBasedUnit.OnMyTurnEnd -= OnPlayerTurnEnd;
        }
    }

    // ============ 可视化 ============

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || playerUnit == null) return;

        int playerFloor = playerUnit.CurrentFloor;

        // 移动范围（绿色）
        if (currentMovementRange != null && !isMeleeMode)
        {
            Gizmos.color = new Color(0, 1, 0, 0.25f);
            foreach (Vector2Int pos in currentMovementRange)
            {
                Vector3 worldPos = FloorManager.Instance != null
                    ? FloorManager.Instance.GridToWorld(pos, playerFloor)
                    : GridManager.Instance.GridToWorld(pos);
                Gizmos.DrawCube(worldPos + Vector3.up * 0.01f,
                    Vector3.one * GridManager.Instance.CellSize * 0.9f);
            }
        }

        // 近战范围（红色）
        if (isMeleeMode)
        {
            var meleeRange = GetMeleeRange();
            if (meleeRange != null)
            {
                foreach (Vector2Int pos in meleeRange)
                {
                    Vector3 worldPos = FloorManager.Instance != null
                        ? FloorManager.Instance.GridToWorld(pos, playerFloor)
                        : GridManager.Instance.GridToWorld(pos);
                    Gizmos.color = new Color(1, 0, 0, 0.35f);
                    Gizmos.DrawCube(worldPos + Vector3.up * 0.02f,
                        Vector3.one * GridManager.Instance.CellSize * 0.9f);
                }
            }
        }

        // 鼠标悬停格子高亮
        if (hoveredGridPos.HasValue)
        {
            bool inMeleeRange = isMeleeMode && GetMeleeRange()?.Contains(hoveredGridPos.Value) == true;
            bool inMoveRange = !isMeleeMode && currentMovementRange != null &&
                               currentMovementRange.Contains(hoveredGridPos.Value);

            if (inMeleeRange || inMoveRange)
            {
                Gizmos.color = new Color(1, 1, 0, 0.5f);
                Vector3 hoverPos = FloorManager.Instance != null
                    ? FloorManager.Instance.GridToWorld(hoveredGridPos.Value, playerFloor)
                    : GridManager.Instance.GridToWorld(hoveredGridPos.Value);
                Gizmos.DrawCube(hoverPos + Vector3.up * 0.03f,
                    Vector3.one * GridManager.Instance.CellSize * 0.95f);
            }
        }
    }

    void OnGUI()
    {
        if (!isInputEnabled) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;

        string hint;
        if (isMeleeMode)
        {
            style.normal.textColor = Color.red;
            hint = "[ MELEE MODE ] LClick: Attack | W: Cancel";
        }
        else if (turnBasedUnit.CanAct)
        {
            hint = "RClick:Move | LClick:Throw | W:Melee | Q:Pickup | E:Floor | Space:EndTurn";
        }
        else
        {
            hint = "Space: End Turn";
        }

        GUI.Box(new Rect(Screen.width / 2 - 280, Screen.height - 50, 560, 30), hint, style);

        if (isShowingFloorPrompt && availableFloorConnection != null)
        {
            GUIStyle promptStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            promptStyle.normal.textColor = Color.yellow;

            string floorPrompt = $"[{availableFloorConnection.connectionType}]\n" +
                                 $"Press [{changeFloorKey}] to go to Floor {availableFloorConnection.toFloor}";

            GUI.Box(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 50, 400, 80), floorPrompt, promptStyle);
        }
    }
}