using UnityEngine;
using System.Collections.Generic;
using System.Linq;  // 添加这个引用，用于ToArray()

/// <summary>
/// 玩家输入控制器（完整版 - 支持回合制和楼层切换）
/// 职责：
/// 1. 接收和处理玩家输入（鼠标、键盘等）
/// 2. 协调各个模块（UnitMovement、TurnSystem、FloorManager等）
/// 3. 管理游戏流程（显示范围、执行移动、楼层切换、结束回合）
/// 4. 只在玩家回合时响应输入
/// </summary>
public class PlayerInputController : MonoBehaviour
{
    // ============ 引用组件 ============
    
    [Header("引用")]
    [SerializeField] private UnitMovement playerUnit;
    [SerializeField] private TurnBasedUnit turnBasedUnit;
    [SerializeField] private Camera mainCamera;

    [Header("输入设置")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private KeyCode endTurnKey = KeyCode.Space;
    [SerializeField] private KeyCode changeFloorKey = KeyCode.E;
    [SerializeField] private KeyCode pickupKey = KeyCode.F;              // 拾取物品按键
    [SerializeField] private float pickupDetectionRange = 2f;             // 拾取检测范围

    // ============ 运行时数据 ============
    
    private HashSet<Vector2Int> currentMovementRange;
    private Vector2Int? hoveredGridPos;
    private bool isInputEnabled = false;
    private FloorConnection availableFloorConnection = null;
    private bool isShowingFloorPrompt = false;
    private List<WorldItem> nearbyItems = new List<WorldItem>();       // 附近的物品
    private Inventory playerInventory;                                  // 玩家背包

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
        {
            turnBasedUnit = playerUnit.GetComponent<TurnBasedUnit>();
        }

        if (turnBasedUnit == null)
        {
            Debug.LogError("[PlayerInputController] TurnBasedUnit component not found!");
            return;
        }

        playerUnit.OnMoveComplete += OnUnitMoveComplete;

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnFactionChanged += OnFactionChanged;
            TurnSystem.Instance.OnTurnStart += OnTurnSystemStart;
            
            if (TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player))
            {
                Debug.Log("[PlayerInputController] System already started, initializing");
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
        
        // 获取或添加背包组件
        playerInventory = playerUnit.GetComponent<Inventory>();
        if (playerInventory == null)
        {
            playerInventory = playerUnit.gameObject.AddComponent<Inventory>();
        }
        
        Debug.Log("[PlayerInputController] Initialized successfully");
    }

    void Update()
    {
        // 拾取功能完全独立，不受回合限制
        CheckNearbyItems();
        HandlePickupInput();

        // 移动功能只在启用输入且未移动时处理
        if (!isInputEnabled || playerUnit.IsMoving)
            return;

        CheckFloorConnection();
        HandleMouseInput();
        HandleTurnInput();  // 回合控制单独处理
    }

    // ============ 输入处理分离 ============

    /// <summary>
    /// 处理拾取输入（独立于回合）
    /// </summary>
    private void HandlePickupInput()
    {
        // 拾取随时可用，不检查isInputEnabled
        if (Input.GetKeyDown(pickupKey))
        {
            TryPickupNearbyItems();
        }
    }

    /// <summary>
    /// 处理回合相关输入
    /// </summary>
    private void HandleTurnInput()
    {
        // 结束回合
        if (Input.GetKeyDown(endTurnKey))
        {
            EndPlayerTurn();
        }

        // 切换楼层
        if (Input.GetKeyDown(changeFloorKey) && availableFloorConnection != null)
        {
            UseFloorConnection();
        }
    }

    private void OnTurnSystemStart(TurnData turnData)
    {
        if (turnData.currentFaction == TurnFaction.Player && !isInputEnabled)
        {
            OnPlayerTurnStart();
        }
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

    // ============ 输入处理 ============

    private void HandleMouseInput()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // 使用RaycastAll获取所有击中的物体
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, groundLayer);

        if (hits.Length > 0)
        {
            // 找到与玩家同楼层的地面
            RaycastHit? correctHit = null;
            float targetY = FloorManager.Instance != null 
                ? FloorManager.Instance.GetFloorWorldY(playerUnit.CurrentFloor)
                : 0f;

            float minYDiff = float.MaxValue;

            foreach (RaycastHit h in hits)
            {
                // 计算击中点与目标楼层的Y坐标差
                float yDiff = Mathf.Abs(h.point.y - targetY);
                
                // 选择最接近目标楼层的击中点
                if (yDiff < minYDiff)
                {
                    minYDiff = yDiff;
                    correctHit = h;
                }
            }

            // 如果找到了合适的击中点
            if (correctHit.HasValue)
            {
                hit = correctHit.Value;
                
                // 强制使用玩家当前楼层的Y坐标
                Vector3 adjustedPoint = hit.point;
                adjustedPoint.y = targetY;
                
                Vector2Int gridPos = GridManager.Instance.WorldToGrid(adjustedPoint);

                if (hoveredGridPos == null || hoveredGridPos.Value != gridPos)
                {
                    hoveredGridPos = gridPos;
                    OnGridHovered(gridPos);
                }

                if (Input.GetMouseButtonDown(1))
                {
                    OnGridClicked(gridPos);
                }
            }
        }
        else
        {
            if (hoveredGridPos != null)
            {
                hoveredGridPos = null;
            }
        }
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(endTurnKey))
        {
            EndPlayerTurn();
        }

        if (Input.GetKeyDown(changeFloorKey) && availableFloorConnection != null)
        {
            UseFloorConnection();
        }
    }

    // ============ 物品拾取逻辑 ============

    /// <summary>
    /// 检测附近的可拾取物品（随时检测，不受回合限制）
    /// </summary>
    private void CheckNearbyItems()
    {
        // 清除旧的高亮
        foreach (var item in nearbyItems)
        {
            if (item != null)
            {
                item.ShowHighlight(false);
            }
        }
        nearbyItems.Clear();

        // 查找场景中所有物品
        WorldItem[] allItems = FindObjectsOfType<WorldItem>();
        
        foreach (var item in allItems)
        {
            if (item == null || item.IsPickedUp) continue;

            // 检查是否在拾取范围内（使用简单的距离计算）
            float distance = Vector3.Distance(playerUnit.transform.position, item.transform.position);
            if (distance <= pickupDetectionRange)
            {
                nearbyItems.Add(item);
                item.ShowHighlight(true);
            }
        }
    }

    /// <summary>
    /// 尝试拾取附近的物品（随时可用，不受回合限制）
    /// </summary>
    private void TryPickupNearbyItems()
    {
        if (nearbyItems.Count == 0)
        {
            Debug.Log("[Input] No items nearby to pickup");
            return;
        }

        Debug.Log($"[Input] Attempting to pickup {nearbyItems.Count} item(s)");

        // 拾取所有附近的物品
        int pickedCount = 0;
        
        // 使用ToArray避免修改集合问题
        foreach (var item in nearbyItems.ToArray())
        {
            if (item != null && !item.IsPickedUp)
            {
                // 尝试添加到背包
                if (playerInventory != null && playerInventory.AddItem(item.ItemData, item.Quantity))
                {
                    // 拾取成功
                    item.Pickup(playerUnit.gameObject);
                    pickedCount++;
                    
                    Debug.Log($"[Input] ✓ Picked up: {item.ItemData.Name} x{item.Quantity}");
                }
                else
                {
                    Debug.Log($"[Input] ✗ Cannot pickup {item.ItemData.Name}: inventory full or too heavy");
                }
            }
        }

        if (pickedCount > 0)
        {
            Debug.Log($"[Input] Successfully picked up {pickedCount} item(s)");
        }

        // 清空列表
        nearbyItems.Clear();
    }

    private void CheckFloorConnection()
    {
        if (FloorManager.Instance == null)
        {
            availableFloorConnection = null;
            isShowingFloorPrompt = false;
            return;
        }

        FloorConnection connection = FloorManager.Instance.GetConnection(
            playerUnit.CurrentGridPosition, 
            playerUnit.CurrentFloor
        );

        if (connection != null && !connection.Equals(availableFloorConnection))
        {
            availableFloorConnection = connection;
            isShowingFloorPrompt = true;
            Debug.Log($"[Input] Floor connection available: {connection.connectionType} to Floor {connection.toFloor}");
        }
        else if (connection == null && availableFloorConnection != null)
        {
            availableFloorConnection = null;
            isShowingFloorPrompt = false;
        }
    }

    private void UseFloorConnection()
    {
        if (availableFloorConnection == null || !turnBasedUnit.CanAct)
            return;

        Debug.Log($"[Input] Using {availableFloorConnection.connectionType} to floor {availableFloorConnection.toFloor}");

        if (turnBasedUnit.StartAction())
        {
            ClearMovementRange();
            playerUnit.MoveToGrid(availableFloorConnection.gridPosition, availableFloorConnection.toFloor);
        }
    }

    // ============ 网格点击处理 ============

    private void OnGridHovered(Vector2Int gridPos)
    {
        if (currentMovementRange != null && currentMovementRange.Contains(gridPos))
        {
            List<Vector2Int> path = PathfindingService.FindPath(playerUnit.CurrentGridPosition, gridPos);
        }
    }
    /// <summary>
    /// 处理网格点击事件
    /// </summary>  
    private void OnGridClicked(Vector2Int gridPos)
    {
        if (!turnBasedUnit.CanAct)
            return;

        // 计算路径长度
        List<Vector2Int> path = PathfindingService.FindPath(
            playerUnit.CurrentGridPosition, 
            gridPos, 
            playerUnit.CurrentFloor
        );

        if (path == null || path.Count == 0)
        {
            Debug.Log("[Input] No valid path");
            return;
        }

        // 检查是否有足够的移动点数
        int pathCost = path.Count;
        if (!turnBasedUnit.HasEnoughMovementPoints(pathCost))
        {
            Debug.Log($"[Input] Not enough movement points! Need: {pathCost}, Have: {turnBasedUnit.RemainingActionPoints}");
            return;
        }

        if (playerUnit.CanMoveTo(gridPos))
        {
            Debug.Log($"[Input] Moving to grid: {gridPos}");
            // 1. 先扣 AP
            //turnBasedUnit.ConsumeActionPoint(pathCost);

            // 2. 再移动
            ClearMovementRange();
            playerUnit.MoveToGrid(gridPos);
        }
    }

    private void OnUnitMoveComplete()
    {
        int remaining = turnBasedUnit.RemainingActionPoints;    
        // 不调用EndAction()！让玩家可以继续行动
        // 移动完成后，立即重新计算并显示移动范围
        if (turnBasedUnit.CanAct)
        {
            ShowMovementRange();

        }
        else
        {
            ClearMovementRange();
        }
    }

    private void EndPlayerTurn()
    {
        if (TurnSystem.Instance != null && TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player))
        {
            ClearMovementRange();
            TurnSystem.Instance.EndCurrentTurn();
        }
    }

    // ============ 移动范围管理 ============

    private void ShowMovementRange()
    {
        if (!turnBasedUnit.CanAct) //如果玩家不能行动，则清除移动范围
        {
            currentMovementRange = null;
            return;
        }

        currentMovementRange = playerUnit.GetMovementRange(); //获取玩家可移动范围
    }

    private void ClearMovementRange()
    {
        currentMovementRange = null;
    }

    // ============ 清理 ============

    void OnDestroy()
    {
        if (playerUnit != null)
        {
            playerUnit.OnMoveComplete -= OnUnitMoveComplete;
        }

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
        if (!Application.isPlaying)
            return;
            
        if (currentMovementRange == null)
        {
            // 每10帧输出一次，避免刷屏
            if (Time.frameCount % 10 == 0)
            {
                //Debug.Log($"[Gizmos] Frame {Time.frameCount}: currentMovementRange is NULL");
            }
            return;
        }

        //Debug.Log($"[Gizmos] Frame {Time.frameCount}: Drawing {currentMovementRange.Count} cells");

        // 获取玩家当前楼层
        int playerFloor = playerUnit != null ? playerUnit.CurrentFloor : 0;

        // 绘制移动范围 - 统一颜色，清晰显示
        Gizmos.color = new Color(0, 1, 0, 0.25f);  // 统一的绿色半透明
        
        foreach (Vector2Int pos in currentMovementRange)
        {
            Vector3 worldPos;
            
            if (FloorManager.Instance != null)
            {
                worldPos = FloorManager.Instance.GridToWorld(pos, playerFloor);//将网格坐标转换为世界坐标
            }
            else
            {
                worldPos = GridManager.Instance.GridToWorld(pos);
            }
            
            // 降低高度，更贴地
            Gizmos.DrawCube(worldPos + Vector3.up * 0.01f, 
                Vector3.one * GridManager.Instance.CellSize * 0.9f);
        }

        // 绘制悬停的格子 - 黄色高亮
        if (hoveredGridPos.HasValue && currentMovementRange.Contains(hoveredGridPos.Value))
        {
            Gizmos.color = new Color(1, 1, 0, 0.5f);  // 黄色，更明显
            
            Vector3 hoverPos;
            if (FloorManager.Instance != null)
            {
                hoverPos = FloorManager.Instance.GridToWorld(hoveredGridPos.Value, playerFloor);
            }
            else
            {
                hoverPos = GridManager.Instance.GridToWorld(hoveredGridPos.Value);
            }
            
            // 悬停格子稍微高一点，更明显
            Gizmos.DrawCube(hoverPos + Vector3.up * 0.02f, 
                Vector3.one * GridManager.Instance.CellSize * 0.95f);
        }
    }

    void OnGUI()
    {
        if (!isInputEnabled) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 14;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;

        string hint = turnBasedUnit.CanAct 
            ? $"Right Click: Move | {changeFloorKey}: Change Floor | {endTurnKey}: End Turn" 
            : $"{endTurnKey}: End Turn";

        GUI.Box(new Rect(Screen.width / 2 - 200, Screen.height - 50, 400, 30), hint, style);

        if (isShowingFloorPrompt && availableFloorConnection != null)
        {
            GUIStyle promptStyle = new GUIStyle(GUI.skin.box);
            promptStyle.fontSize = 18;
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.normal.textColor = Color.yellow;
            
            Texture2D bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, new Color(0, 0, 0, 0.8f));
            bgTexture.Apply();
            promptStyle.normal.background = bgTexture;

            string floorPrompt = $"[{availableFloorConnection.connectionType}]\n" +
                                $"Press [{changeFloorKey}] to go to Floor {availableFloorConnection.toFloor}\n" +
                                $"(Current Floor: {playerUnit.CurrentFloor})";

            GUI.Box(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 50, 400, 100), floorPrompt, promptStyle);
        }
    }
}