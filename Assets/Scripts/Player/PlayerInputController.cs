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

    [Header("场景物品交互输入")]
    [SerializeField] private KeyCode interactKey   = KeyCode.E;

    [Header("径向功能菜单")]
    [SerializeField] private KeyCode radialMenuKey = KeyCode.R;

    [Header("快捷栏输入")]
    [Tooltip("投掷 / 近战攻击 / 开枪（左键）")]
    [SerializeField] private KeyCode throwKey  = KeyCode.Mouse0;
    [Tooltip("换弹（武器弹夹补满）")]
    [SerializeField] private KeyCode reloadKey = KeyCode.F;
    // F 键近战切换已由 R 键径向菜单替代，不再使用

    [Header("姿态输入")]
    [SerializeField] private KeyCode crouchKey = KeyCode.C;
    [SerializeField] private KeyCode proneKey  = KeyCode.Z; // 切换匍匐（趴下/起身）

    [Header("掩体动作（贴掩体时按住）")]
    [SerializeField] private KeyCode peekKey      = KeyCode.V;           // 探头：露身精准，暴露
    [SerializeField] private KeyCode blindFireKey = KeyCode.LeftControl; // 廖枪：露枪盲射（+LMB），身体安全

    // 近战模式状态
    private bool isMeleeMode = false;

    private PlayerController _playerController;

    // 反应窗口状态（敌人射击时临时开放移动）
    private bool isReactionWindowOpen = false;

    // 背包/仓库开启状态（Tab 键，开启时屏蔽所有其他输入）
    private bool _inventoryOpen = false;

    // ============ 运行时数据 ============

    private HashSet<Vector2Int> currentMovementRange;
    private Vector2Int? hoveredGridPos;
    private bool isInputEnabled = false;
    private int _proneCrawlUsedThisTurn = 0; // 本回合匍匐已爬格数（回合开始清零，限制总爬行 ≤ ProneCrawlRange）

    // 附近可交互的场景物品
    private List<SceneItemInstance> nearbySceneItems = new List<SceneItemInstance>();
    private SceneItemInstance closestSceneItem = null;
    private FloorConnection availableFloorConnection = null;
    private bool isShowingFloorPrompt = false;
    private List<WorldItem> nearbyItems = new List<WorldItem>();
    private Inventory playerInventory;
    private EquipmentManager equipmentManager;
    private LootUI lootUI;
    private SceneItemInstance  _mountedVehicle;
    private List<Vector2Int>   _ridePreviewCells;

    // ── R 键功能选择菜单 ──────────────────────────────────────────────
    private struct HoldMenuEntry
    {
        public string        label;
        public int           apCost;
        public System.Action onConfirm;
    }

    // ── 静态事件（解耦 UI 订阅） ──────────────────────────────────────
    public static event System.Action        OnToggleInventory;
    public static event System.Action<float> OnHotbarScroll;

    // ── 径向功能选择菜单（按住 R 拖动方向） ──────────────────────────
    private bool               _radialActive     = false;
    private float              _radialTimer      = 0f;
    private const float        RadialThreshold   = 0.12f; // 按住超过此时间弹菜单
    private List<HoldMenuEntry> _radialEntries;
    private int                _radialSelected   = -1;    // -1 = 死区/未选中

    // 当前选中的物品使用模式（由 R 键径向菜单设置，LMB 执行）
    private enum ItemMode { None, Melee, Throw, Consume, Shoot }
    private ItemMode           _currentItemMode  = ItemMode.None;
    private ItemData           _modeLockedItem   = null;

    // GUI 纹理缓存
    private Texture2D _btnDarkTex;
    private Texture2D _btnGoldTex;

    // ============ 初始化 ============

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        _btnDarkTex = MakeTex(1, 1, new Color(0.12f, 0.12f, 0.12f, 0.9f));
        _btnGoldTex = MakeTex(1, 1, new Color(0.95f, 0.80f, 0.10f, 0.95f));

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
                RefreshMovementDisplay();
            }
        }
        else
        {
            Debug.LogError("[PlayerInputController] TurnSystem not found!");
        }

        turnBasedUnit.OnMyTurnStart  += OnPlayerTurnStart;
        turnBasedUnit.OnMyTurnEnd    += OnPlayerTurnEnd;
        turnBasedUnit.OnAPExhausted  += EndPlayerTurn;

        // 初始化背包
        playerInventory = playerUnit.GetComponent<Inventory>();
        if (playerInventory == null)
            playerInventory = playerUnit.gameObject.AddComponent<Inventory>();

        _playerController = playerUnit.GetComponent<PlayerController>();
        if (_playerController != null)
            _playerController.OnStanceChanged += OnStanceChanged;

        // 获取 EquipmentManager 并注入 Inventory
        equipmentManager = playerUnit.GetComponent<EquipmentManager>();
        if (equipmentManager != null)
            equipmentManager.SetInventory(playerInventory);
        else
            Debug.LogWarning("[PlayerInputController] EquipmentManager not found on player");

        // LootUI：优先从玩家身上获取，没有则自动添加
        lootUI = playerUnit.GetComponent<LootUI>();
        if (lootUI == null)
            lootUI = playerUnit.gameObject.AddComponent<LootUI>();

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
        // Tab：事件驱动（乘坐中禁用）
        if (_mountedVehicle == null && Input.GetKeyDown(KeyCode.Tab))
            OnToggleInventory?.Invoke();

        // C：切换下蹲（不消耗 AP，移动中/乘坐/仓库开启时禁用）
        if (_mountedVehicle == null && !_inventoryOpen
            && !(playerUnit != null && playerUnit.IsMoving)
            && Input.GetKeyDown(crouchKey))
            _playerController?.ToggleCrouch();

        // Z：切换匍匐（趴下/起身；不消耗 AP，移动中/乘坐/仓库开启时禁用）
        if (_mountedVehicle == null && !_inventoryOpen
            && !(playerUnit != null && playerUnit.IsMoving)
            && Input.GetKeyDown(proneKey))
            _playerController?.ToggleProne();

        // 掩体动作：探头(V) / 廖枪盲射(Ctrl)，每帧跟踪按住状态
        HandleCoverActions();

        // 滚轮：始终触发，不受仓库门控影响（EquipmentManager 订阅此事件切换槽位）
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            OnHotbarScroll?.Invoke(scroll);
            // 切换槽位时清除当前物品使用模式
            isMeleeMode      = false;
            _currentItemMode = ItemMode.None;
            _modeLockedItem  = null;
            CancelRadialMenu();
        }

        if (_inventoryOpen) return;

        CheckNearbyItems();
        HandlePickupInput();
        CheckNearbySceneItems();

        // 物品用尽时立即清除模式（涵盖 AI 拾取、其他途径消耗等情况）
        RefreshItemMode();

        // 武器槽自动进入/退出射击模式，无需 R 键
        AutoDetectWeaponMode();

        // 投掷/射击模式显示瞄准线，其余禁用
        if (equipmentManager != null)
        {
            bool disableAim = _currentItemMode != ItemMode.Throw && _currentItemMode != ItemMode.Shoot;
            equipmentManager.UpdateAiming(disableAim);
        }

        // 优先级 1：乘坐载具
        if (_mountedVehicle != null) { HandleRideInput(); return; }

        // 优先级 2：径向菜单激活中 → 只处理菜单输入（始终可用，不受回合限制）
        if (_radialActive) { HandleRadialMenuInput(); return; }

        // 优先级 3：R 键检测（始终可用，与滚轮同级）
        HandleRadialMenuDetect();

        // 优先级 4：QTE 反应窗口
        if (isReactionWindowOpen && !playerUnit.IsMoving)
        {
            HandleMouseInput();
            return;
        }

        // 优先级 5：正常回合输入
        if (!isInputEnabled || playerUnit.IsMoving) return;

        CheckFloorConnection();
        HandleMouseInput();
        HandleTurnInput();
        HandleHotbarActions();   // LMB 执行当前模式动作
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

    // ============ 快捷栏输入（分两段，滚轮始终可用） ============

    /// <summary>只处理滚轮切换，始终调用（包括菜单激活期间）</summary>
    private void HandleHotbarScroll()
    {
        if (equipmentManager == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            equipmentManager.ScrollSlot(scroll);
            isMeleeMode      = false;
            _currentItemMode = ItemMode.None;
            _modeLockedItem  = null;
            CancelRadialMenu();
        }
    }

    /// <summary>LMB 执行当前由 R 键径向菜单选定的物品功能</summary>
    private void HandleHotbarActions()
    {
        if (equipmentManager == null) return;
        bool canAct = turnBasedUnit == null || (turnBasedUnit.IsMyTurn && turnBasedUnit.CanAct);

        // 换弹（F 键）：不受模式和 canAct 门控，只要当前槽是武器即可
        if (Input.GetKeyDown(reloadKey))
            equipmentManager.ReloadWeapon();

        if (!canAct) return;

        RefreshItemMode();

        // 射击模式：独立处理按住（全自动）和单次（其他）
        if (_currentItemMode == ItemMode.Shoot)
        {
            bool keyDown = Input.GetKeyDown(throwKey);
            bool keyHeld = Input.GetKey(throwKey) && !keyDown;
            if (keyDown || keyHeld)
                equipmentManager.ShootWeapon(keyHeld);
            return;
        }

        // 其他模式：只响应按下
        if (!Input.GetKeyDown(throwKey)) return;

        switch (_currentItemMode)
        {
            case ItemMode.Melee:
                if (TryGetMeleeTarget(out Vector2Int targetGrid))
                    equipmentManager.ExecuteMelee(targetGrid);
                break;

            case ItemMode.Throw:
                if (equipmentManager.TryGetAimPosition(out Vector3 aimPos))
                {
                    equipmentManager.ThrowItem(aimPos);
                    RefreshItemMode();
                }
                break;

            case ItemMode.Consume:
                equipmentManager.UseItem();
                RefreshItemMode();
                break;
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
        if (slot.itemData.Type != ItemType.Consumable || slot.itemData.meleeDamage <= 0) return false;
        var cd = slot.itemData;

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
        if (slot.itemData.Type != ItemType.Consumable || slot.itemData.meleeDamage <= 0) return null;
        var cd = slot.itemData;

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

    // ============ 场景物品检测 ============

    /// <summary>
    /// 持续扫描附近的 SceneItemInstance，记录最近的可交互物体
    /// 每帧调用，用于 UI 提示和高亮（无论是否玩家回合）
    /// </summary>
    private void CheckNearbySceneItems()
    {
        nearbySceneItems.Clear();
        closestSceneItem = null;

        if (GridManager.Instance == null) return;

        // 用格子距离判断，而不是世界坐标距离
        // 这样推倒的书架覆盖多格时，玩家站在任意相邻格都能触发
        Vector2Int playerCell = playerUnit.CurrentGridPosition;
        int closestDist = int.MaxValue;

        foreach (var item in Object.FindObjectsOfType<SceneItemInstance>())
        {
            if (item == null || item.IsDestroyed) continue;
            if (item.Data == null || !item.Data.PlayerCanInteract) continue;

            int gridDist = item.MinGridDistanceTo(playerCell);
            if (gridDist <= item.Data.interactionRange)
            {
                nearbySceneItems.Add(item);
                if (gridDist < closestDist)
                {
                    closestDist = gridDist;
                    closestSceneItem = item;
                }
            }
        }
    }

    // ============ 径向功能菜单 ============

    /// <summary>
    /// 每帧检测 R 键按下/持续，超过阈值后激活径向菜单。
    /// 仅在菜单未激活时调用。
    /// </summary>
    private void HandleRadialMenuDetect()
    {
        if (Input.GetKeyDown(radialMenuKey))
        {
            _radialTimer = 0f;
            Debug.Log($"[Radial] R 按下  isInputEnabled={isInputEnabled}  slot={equipmentManager?.CurrentSlot?.itemData?.Name ?? "空"}");
        }

        if (Input.GetKey(radialMenuKey))
        {
            _radialTimer += Time.deltaTime;
            if (_radialTimer >= RadialThreshold && !_radialActive)
                TryActivateRadialMenu();
        }

        if (Input.GetKeyUp(radialMenuKey))
        {
            if (!_radialActive && _radialTimer < RadialThreshold)
                ExecuteSceneOrCorpseTap();
            _radialTimer = 0f;
        }
    }

    private void TryActivateRadialMenu()
    {
        List<HoldMenuEntry> entries = null;

        if (closestSceneItem != null)
        {
            entries = BuildSceneItemEntries(closestSceneItem);
            if (entries.Count == 1) { entries[0].onConfirm?.Invoke(); _radialTimer = float.MaxValue; return; }
        }

        if (entries == null || entries.Count == 0)
            entries = BuildItemActionEntries();

        Debug.Log($"[Radial] TryActivate: {entries.Count} 个条目 closestSceneItem={closestSceneItem} slot={equipmentManager?.CurrentSlot?.itemData?.Name}");

        if (entries.Count == 0) return;
        if (entries.Count == 1) { entries[0].onConfirm?.Invoke(); _radialTimer = float.MaxValue; return; }

        _radialEntries  = entries;
        _radialSelected = -1;
        _radialActive   = true;
        Debug.Log($"[Radial] 菜单激活，共 {entries.Count} 个选项");
    }

    /// <summary>径向菜单激活期间每帧调用：更新选项高亮，松键时执行</summary>
    private void HandleRadialMenuInput()
    {
        UpdateRadialSelection();

        if (Input.GetKeyUp(radialMenuKey))
        {
            if (_radialSelected >= 0 && _radialSelected < _radialEntries.Count)
                _radialEntries[_radialSelected].onConfirm?.Invoke();
            CancelRadialMenu();
        }

        // ESC 取消
        if (Input.GetKeyDown(KeyCode.Escape))
            CancelRadialMenu();
    }

    private void UpdateRadialSelection()
    {
        if (_radialEntries == null) { _radialSelected = -1; return; }

        // 锚点与 DrawRadialMenu 保持一致：有摄像机用投影，否则用屏幕中心
        Vector2 anchorPos;
        if (mainCamera != null)
        {
            Vector3 scr = GetRadialAnchorScreen();
            anchorPos = scr.z >= 0
                ? new Vector2(scr.x - 140f, scr.y + 80f)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        else
        {
            anchorPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        Vector2 mousePos = Input.mousePosition;
        Vector2 dir = new Vector2(mousePos.x - anchorPos.x, mousePos.y - anchorPos.y);

        float deadZone = 30f;
        if (dir.magnitude < deadZone) { _radialSelected = -1; return; }

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        int   count = _radialEntries.Count;
        float step  = 360f / count;
        float best  = float.MaxValue;
        int   idx   = 0;
        for (int i = 0; i < count; i++)
        {
            float optAngle = GetRadialAngle(i, count);
            float diff = Mathf.Abs(Mathf.DeltaAngle(angle, optAngle));
            if (diff < best) { best = diff; idx = i; }
        }
        _radialSelected = idx;
    }

    /// <summary>由 InventoryCanvas 或 Tab 键调用，打开/关闭背包仓库</summary>
    public void SetInventoryOpen(bool open)
    {
        _inventoryOpen = open;
        // 打开背包时取消径向菜单
        if (open && _radialActive) CancelRadialMenu();
    }

    // 武器槽选中时自动进入射击模式，切走时退出
    private void AutoDetectWeaponMode()
    {
        if (equipmentManager == null) return;
        var slot = equipmentManager.CurrentSlot;
        bool isWeapon = slot != null && !slot.IsEmpty && slot.itemData?.Type == ItemType.Weapon;

        if (isWeapon)
        {
            if (_currentItemMode != ItemMode.Shoot)
            {
                _currentItemMode = ItemMode.Shoot;
                _modeLockedItem  = slot.itemData;
                isMeleeMode      = false;
                CancelRadialMenu();
            }
        }
        else if (_currentItemMode == ItemMode.Shoot)
        {
            _currentItemMode = ItemMode.None;
            _modeLockedItem  = null;
        }
    }

    private void CancelRadialMenu()
    {
        _radialActive   = false;
        _radialEntries  = null;
        _radialSelected = -1;
        _radialTimer    = 0f;
    }

    // 选项在圆上的角度（从正上方顺时针分布）
    private float GetRadialAngle(int idx, int count)
    {
        // 从 90° 开始，顺时针 = 角度减小
        return 90f - idx * (360f / count);
    }

    // 径向菜单锚点在屏幕坐标（y 未翻转，用于 Input.mousePosition 比较）
    private Vector3 GetRadialAnchorScreen()
    {
        Vector3 world = closestSceneItem != null
            ? closestSceneItem.transform.position + Vector3.up * 1.0f
            : transform.position + Vector3.up * 2.0f;
        return mainCamera != null ? mainCamera.WorldToScreenPoint(world) : Vector3.zero;
    }

    /// <summary>R 键短按：LootUI 关闭 → SceneItem 单动作执行 → 尸体搜刮</summary>
    private void ExecuteSceneOrCorpseTap()
    {
        if (lootUI != null && lootUI.IsOpen) { lootUI.Close(); return; }

        if (closestSceneItem != null)
        {
            var entries = BuildSceneItemEntries(closestSceneItem);
            if (entries.Count == 1) { entries[0].onConfirm?.Invoke(); return; }
            if (entries.Count > 1)  return; // 多动作交给长按菜单，短按不执行
            // entries.Count == 0：SceneItem 无可用动作，继续向下检测尸体
        }

        EnemyInventory corpse = FindNearestSearchableCorpse();
        if (corpse != null) { lootUI?.Open(corpse, playerInventory); return; }
    }

    /// <summary>QTE 窗口期间的简化交互（短按 R 即可）</summary>
    private void HandleSceneInteractTap()
    {
        if (!Input.GetKeyDown(interactKey)) return;
        ExecuteSceneOrCorpseTap();
    }

    // ── 模式持久性 ────────────────────────────────────────────────────

    private void RefreshItemMode()
    {
        if (_modeLockedItem == null) return;
        HotbarSlot slot = equipmentManager?.CurrentSlot;
        // 物品切换或数量耗尽时清除模式
        bool ok = slot != null && !slot.IsEmpty && slot.itemData == _modeLockedItem && slot.quantity > 0;
        if (!ok) { _currentItemMode = ItemMode.None; _modeLockedItem = null; isMeleeMode = false; }
    }

    // ── 条目构建 ──────────────────────────────────────────────────────

    private List<HoldMenuEntry> BuildSceneItemEntries(SceneItemInstance target)
    {
        var entries = new List<HoldMenuEntry>();
        foreach (var opt in target.GetAvailableInteractions())
        {
            var cap = opt; var capT = target;
            entries.Add(new HoldMenuEntry
            {
                label = cap.label, apCost = cap.apCost,
                onConfirm = () =>
                {
                    bool ok = capT.TryInteractAs(cap.kind, playerUnit.gameObject);
                    if (ok && capT.IsOccupied && capT.IsRideable)
                    { _mountedVehicle = capT; currentMovementRange = null; }
                }
            });
        }
        return entries;
    }

    private List<HoldMenuEntry> BuildItemActionEntries()
    {
        var entries = new List<HoldMenuEntry>();
        if (equipmentManager == null) return entries;
        HotbarSlot slot = equipmentManager.CurrentSlot;
        if (slot == null || slot.IsEmpty) return entries;
        ItemData d = slot.itemData;

        foreach (var fn in d.GetAvailableFunctions())
        {
            var cap = fn;
            switch (cap)
            {
                case ItemFunction.Melee:
                    entries.Add(new HoldMenuEntry { label = "近战攻击", apCost = d.UseCost,
                        onConfirm = () => { _currentItemMode = ItemMode.Melee; _modeLockedItem = d; isMeleeMode = true; } });
                    break;
                case ItemFunction.Throw:
                    entries.Add(new HoldMenuEntry { label = "投掷", apCost = d.UseCost,
                        onConfirm = () => { _currentItemMode = ItemMode.Throw; _modeLockedItem = d; isMeleeMode = false; } });
                    break;
                case ItemFunction.Consume:
                    entries.Add(new HoldMenuEntry { label = "食用/使用", apCost = d.UseCost,
                        onConfirm = () => { _currentItemMode = ItemMode.Consume; _modeLockedItem = d; isMeleeMode = false; } });
                    break;
                case ItemFunction.Shoot:
                    entries.Add(new HoldMenuEntry { label = "开枪", apCost = d.UseCost,
                        onConfirm = () => { _currentItemMode = ItemMode.Shoot; _modeLockedItem = d; isMeleeMode = false; } });
                    break;
                case ItemFunction.Equip:
                    entries.Add(new HoldMenuEntry { label = "穿戴", apCost = d.UseCost,
                        onConfirm = () => { _currentItemMode = ItemMode.Consume; _modeLockedItem = d; isMeleeMode = false; } });
                    break;
            }
        }
        return entries;
    }

    // ── 尸体搜刮辅助 ─────────────────────────────────────────────────

    private EnemyInventory FindNearestSearchableCorpse()
    {
        EnemyInventory best = null; float bestDist = float.MaxValue;
        foreach (var inv in Object.FindObjectsOfType<EnemyInventory>())
        {
            if (!inv.IsSearchable) continue;
            float dist = Vector3.Distance(playerUnit.transform.position, inv.transform.position);
            if (dist <= inv.lootRange && dist < bestDist) { bestDist = dist; best = inv; }
        }
        return best;
    }

    // ============ 乘坐输入 ============

    private void HandleRideInput()
    {
        // 动画结束或载具异常 → 自动清除
        if (_mountedVehicle == null || !_mountedVehicle.IsOccupied)
        {
            _mountedVehicle    = null;
            _ridePreviewCells  = null;
            return;
        }

        // 动画进行中：清除预览，等待
        if (_mountedVehicle.IsAnimating)
        {
            _ridePreviewCells = null;
            return;
        }

        // ESC / R：下车
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(interactKey))
        {
            _mountedVehicle.CancelRide();
            _mountedVehicle   = null;
            _ridePreviewCells = null;
            return;
        }

        // 每帧根据鼠标位置更新预览
        Vector2Int dir = ComputeRideDirection();
        bool canAfford = turnBasedUnit.HasEnoughMovementPoints(_mountedVehicle.Data.UseCost);

        if (dir != Vector2Int.zero && canAfford)
            _ridePreviewCells = _mountedVehicle.ComputeSlidePreview(
                dir, _mountedVehicle.Data.rideConfig.maxSlideCells);
        else
            _ridePreviewCells = null;

        // 右键确认发射（AP 不足时不允许发射）
        if (Input.GetMouseButtonDown(1) && dir != Vector2Int.zero)
        {
            if (!canAfford)
            {
                Debug.Log("[Input] AP 不足，无法乘坐发射");
                return;
            }
            _ridePreviewCells = null;
            _mountedVehicle.LaunchRide(dir);
            // 保留 _mountedVehicle 直到 IsOccupied 变 false（动画结束自动下车）
        }
    }

    /// <summary>
    /// 鼠标射线打到地面后，计算鼠标格子相对载具锚点的方向（4 或 8 向）
    /// </summary>
    private Vector2Int ComputeRideDirection()
    {
        if (_mountedVehicle == null || GridManager.Instance == null) return Vector2Int.zero;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, groundLayer);
        if (hits.Length == 0) return Vector2Int.zero;

        float targetY = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorWorldY(playerUnit.CurrentFloor) : 0f;

        RaycastHit best = hits[0];
        float minDiff = float.MaxValue;
        foreach (var h in hits)
        {
            float d = Mathf.Abs(h.point.y - targetY);
            if (d < minDiff) { minDiff = d; best = h; }
        }

        Vector2Int mouseCell = GridManager.Instance.WorldToGrid(best.point);
        Vector2Int cartCell  = _mountedVehicle.AnchorCell;
        Vector2Int delta     = mouseCell - cartCell;
        if (delta == Vector2Int.zero) return Vector2Int.zero;

        bool allowDiag = _mountedVehicle.Data.rideConfig.allowDiagonal;
        if (!allowDiag)
        {
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector2Int(delta.x > 0 ? 1 : -1, 0)
                : new Vector2Int(0, delta.y > 0 ? 1 : -1);
        }
        else
        {
            return new Vector2Int(
                delta.x == 0 ? 0 : (delta.x > 0 ? 1 : -1),
                delta.y == 0 ? 0 : (delta.y > 0 ? 1 : -1));
        }
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
                bool pickedOk = equipmentManager != null
                    ? equipmentManager.TryPickupItem(item.ItemData, item.Quantity)
                    : playerInventory != null && playerInventory.AddItem(item.ItemData, item.Quantity);
                if (pickedOk)
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
        // QTE 反应窗口：绕过 CanAct（敌人回合，IsMyTurn=false）
        // 玩家可以移动最多 2 格，或者使用道具（W 键，见 HandleHotbarInput）
        // QTE 移动不消耗玩家正式回合 AP（IsMyTurn=false，UnitMovement 不会调用 ConsumeAP）
        if (isReactionWindowOpen)
        {
            if (!GridManager.Instance.IsWalkable(gridPos, playerUnit.CurrentFloor)) return;

            // QTE 允许移动最多 2 格（曼哈顿距离 ≤ 2）
            const int qteMaxCells = 2;
            int manhattan = Mathf.Abs(gridPos.x - playerUnit.CurrentGridPosition.x)
                          + Mathf.Abs(gridPos.y - playerUnit.CurrentGridPosition.y);
            if (manhattan > qteMaxCells)
            {
                Debug.Log($"[QTE] 最多移动 {qteMaxCells} 格");
                return;
            }

            // 寻路确认可达
            var qtePath = PathfindingService.FindPath(
                playerUnit.CurrentGridPosition, gridPos, playerUnit.CurrentFloor);
            if (qtePath == null || qtePath.Count == 0) return;

            ClearMovementRange();
            playerUnit.MoveToGrid(gridPos, playerUnit.CurrentFloor, Mathf.Min(qteMaxCells, qtePath.Count));
            return;
        }

        if (!turnBasedUnit.CanAct) return;

        // IsWalkable 快速检查（O(1)），避免点击障碍物还去算寻路
        if (!GridManager.Instance.IsWalkable(gridPos, playerUnit.CurrentFloor))
            return;

        // 寻路只算一次
        List<Vector2Int> path = PathfindingService.FindPath(
            playerUnit.CurrentGridPosition, gridPos, playerUnit.CurrentFloor);

        if (path == null || path.Count == 0) return;

        // 匍匐：本回合总爬行 ≤ ProneCrawlRange，扣除已爬后超出则拒绝（与显示范围一致）
        bool proneMove = _playerController != null && _playerController.IsProne;
        if (proneMove)
        {
            int crawlLeft = Mathf.Max(0, _playerController.ProneCrawlRange - _proneCrawlUsedThisTurn);
            if (path.Count > crawlLeft)
            {
                Debug.Log($"[Input] 匍匐本回合只能再爬 {crawlLeft} 格（需先起身）");
                return;
            }
        }

        // 检查 AP 是否足够
        if (!turnBasedUnit.HasEnoughMovementPoints(path.Count))
        {
            Debug.Log($"[Input] Not enough AP! Need: {path.Count}, Have: {turnBasedUnit.RemainingActionPoints}");
            return;
        }

        ClearMovementRange();
        if (proneMove) _proneCrawlUsedThisTurn += path.Count; // 累计本回合已爬格数
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
            RefreshMovementDisplay();
        }
    }

    private void OnPlayerTurnStart()
    {
        isInputEnabled = true;
        _proneCrawlUsedThisTurn = 0; // 新回合重置匍匐爬行预算
        RefreshMovementDisplay();
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
            RefreshMovementDisplay();
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
            turnBasedUnit.OnMyTurnStart  -= OnPlayerTurnStart;
            turnBasedUnit.OnMyTurnEnd    -= OnPlayerTurnEnd;
            turnBasedUnit.OnAPExhausted  -= EndPlayerTurn;
        }

        if (_playerController != null)
            _playerController.OnStanceChanged -= OnStanceChanged;
    }

    // ── 姿态变化（由 PlayerController.OnStanceChanged 驱动）────
    // 姿态切换已在 PlayerController.SetStance 里即时 RefreshAP，这里只需重画移动范围。
    private void OnStanceChanged(Stance stance)
    {
        if (isInputEnabled)
            RefreshMovementDisplay();
    }

    /// <summary>
    /// 刷新可移动格子显示。默认按 turnBasedUnit 的当前剩余 AP 计算（下蹲等已反映在剩余 AP）。
    /// 匍匐额外把可达范围上限压到 ProneCrawlRange（短距离爬行，射击/其它 AP 不受影响）。
    /// </summary>
    public void RefreshMovementDisplay()
    {
        if (turnBasedUnit == null || playerUnit == null) return;
        if (!turnBasedUnit.CanAct) { currentMovementRange = null; return; }

        if (_playerController != null && _playerController.IsProne)
        {
            // 匍匐：可达范围 = min(剩余AP, 本回合剩余爬行预算)
            int crawlLeft = Mathf.Max(0, _playerController.ProneCrawlRange - _proneCrawlUsedThisTurn);
            int steps = Mathf.Min(turnBasedUnit.RemainingActionPoints, crawlLeft);
            currentMovementRange = steps > 0 ? playerUnit.GetMovementRange(steps) : null;
        }
        else
        {
            currentMovementRange = playerUnit.GetMovementRange();
        }
    }

    // ── 掩体动作：探头(V) / 廖枪盲射(Ctrl) ────────────────────────────────
    // 每帧跟踪按住状态：贴掩体（墙角判定）时，
    //   探头 → 把眼位/被侦测点横移到探出点（PlayerController.SetPeekOffset），身体暴露但能精准看/打；
    //   廖枪 → 打开 EquipmentManager.BlindFireMode（大散布盲射），身体不暴露。
    // 匍匐/乘坐/仓库开启时不可用。
    private void HandleCoverActions()
    {
        if (_playerController == null || playerUnit == null) return;

        bool peekHeld  = Input.GetKey(peekKey);
        bool blindHeld = Input.GetKey(blindFireKey);
        bool canCover  = _mountedVehicle == null && !_inventoryOpen && !_playerController.IsProne;

        CoverUtil.CoverPeek cover = default;
        bool atCover = canCover && (peekHeld || blindHeld)
            && CoverUtil.TryGetCover(playerUnit.CurrentGridPosition, playerUnit.CurrentFloor, out cover);

        // 探头（优先于廖枪）：横移眼位到探出侧
        if (peekHeld && atCover)
        {
            Vector2Int side = PickPeekSide(cover);
            float cellSize  = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            _playerController.SetPeekOffset(new Vector3(side.x, 0f, side.y) * (cellSize * 0.6f));
        }
        else if (_playerController.IsPeeking)
        {
            _playerController.ClearPeek();
        }

        // 廖枪盲射模式（与探头互斥）
        if (equipmentManager != null)
            equipmentManager.BlindFireMode = blindHeld && !peekHeld && atCover;
    }

    // 两侧都可探时，按鼠标所指格的方向选更一致的一侧
    private Vector2Int PickPeekSide(CoverUtil.CoverPeek cover)
    {
        Vector2 desired = Vector2.zero;
        if (hoveredGridPos.HasValue)
        {
            Vector2Int d = hoveredGridPos.Value - playerUnit.CurrentGridPosition;
            desired = new Vector2(d.x, d.y);
        }
        return CoverUtil.PickSide(cover, desired);
    }

    // ============ 可视化 ============

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || playerUnit == null) return;

        int playerFloor = playerUnit.CurrentFloor;

        // 乘坐位移预览（绿色，与移动范围同风格；乘坐期间隐藏普通移动范围）
        if (_mountedVehicle != null)
        {
            if (_ridePreviewCells != null && _ridePreviewCells.Count > 0)
            {
                float cs = GridManager.Instance.CellSize;
                for (int i = 0; i < _ridePreviewCells.Count; i++)
                {
                    Vector3 worldPos = FloorManager.Instance != null
                        ? FloorManager.Instance.GridToWorld(_ridePreviewCells[i], playerFloor)
                        : GridManager.Instance.GridToWorld(_ridePreviewCells[i]);
                    // 最后一格略深，其余与移动范围一致
                    Gizmos.color = (i == _ridePreviewCells.Count - 1)
                        ? new Color(0, 1, 0, 0.55f)
                        : new Color(0, 1, 0, 0.25f);
                    Gizmos.DrawCube(worldPos + Vector3.up * 0.01f, Vector3.one * cs * 0.9f);
                }
            }
        }
        else
        {
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
        } // end else (not riding)

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

    /// <summary>
    /// 极简径向菜单：玩家头顶为圆心，选项为小圆点+文字，无背景。
    /// </summary>
    private void DrawRadialMenu()
    {
        if (_radialEntries == null) return;

        // ── 锚点（玩家头顶屏幕坐标） ──────────────────────────────────
        float cx, cy;
        if (mainCamera != null)
        {
            Vector3 scr = GetRadialAnchorScreen();
            if (scr.z < 0)
            { cx = Screen.width * 0.5f; cy = Screen.height * 0.5f; }
            else
            { cx = scr.x - 140f; cy = Screen.height - scr.y - 80f; }
        }
        else
        { cx = Screen.width * 0.5f; cy = Screen.height * 0.5f; }

        int   count  = _radialEntries.Count;
        float radius = 72f;

        // ── 细连线：中心→各圆点 ───────────────────────────────────────
        for (int i = 0; i < count; i++)
        {
            float ar = GetRadialAngle(i, count) * Mathf.Deg2Rad;
            float ox = Mathf.Cos(ar) * radius;
            float oy = -Mathf.Sin(ar) * radius;
            bool  sel = (i == _radialSelected);

            float lineAngle = Mathf.Atan2(-oy, ox) * Mathf.Rad2Deg;
            float lineLen   = radius - 6f;
            Matrix4x4 mat   = GUI.matrix;
            GUIUtility.RotateAroundPivot(-lineAngle, new Vector2(cx, cy));
            GUI.color = sel
                ? new Color(0.75f, 0.75f, 0.75f, 0.9f)
                : new Color(0.55f, 0.55f, 0.55f, 0.25f);
            GUI.DrawTexture(new Rect(cx, cy - 0.75f, lineLen, 1.5f), Texture2D.whiteTexture);
            GUI.matrix = mat;
        }
        GUI.color = Color.white;

        // ── 方向指示线（鼠标方向，覆盖在连线上层） ───────────────────
        Vector2 mPosGUI = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        Vector2 mDir    = mPosGUI - new Vector2(cx, cy);
        if (mDir.magnitude > 20f)
        {
            float lineAngle = Mathf.Atan2(-mDir.y, mDir.x) * Mathf.Rad2Deg;
            float lineLen   = Mathf.Min(mDir.magnitude, radius - 8f);
            Matrix4x4 mat   = GUI.matrix;
            GUIUtility.RotateAroundPivot(-lineAngle, new Vector2(cx, cy));
            GUI.color = new Color(0.75f, 0.75f, 0.75f, 0.65f);
            GUI.DrawTexture(new Rect(cx, cy - 1f, lineLen, 2f), Texture2D.whiteTexture);
            GUI.matrix = mat;
            GUI.color  = Color.white;
        }

        // ── 中心小点 ──────────────────────────────────────────────────
        const float dotR = 3f;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(cx - dotR, cy - dotR, dotR * 2f, dotR * 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── 各选项：圆点 + 文字 ───────────────────────────────────────
        var lblStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        for (int i = 0; i < count; i++)
        {
            float ar  = GetRadialAngle(i, count) * Mathf.Deg2Rad;
            float ox  = Mathf.Cos(ar) * radius;
            float oy  = -Mathf.Sin(ar) * radius;
            float nx  = cx + ox;
            float ny  = cy + oy;
            bool  sel = (i == _radialSelected);

            float nodeR = sel ? 16f : 12f;
            GUI.color = sel
                ? new Color(0.80f, 0.80f, 0.80f, 1f)
                : new Color(0.50f, 0.50f, 0.50f, 0.75f);
            GUI.DrawTexture(new Rect(nx - nodeR, ny - nodeR, nodeR * 2f, nodeR * 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 文字：只显示标签，AP 放在第二行（选中时才显示）
            var entry = _radialEntries[i];
            lblStyle.normal.textColor = sel ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.85f);
            lblStyle.fontSize         = sel ? 10 : 9;

            // 文字位置：圆点外侧偏移，避免遮住圆点
            float labelOffsetX = ox > 0 ? nodeR + 2f : -(nodeR + 42f);
            if (Mathf.Abs(ox) < 10f) labelOffsetX = -20f; // 正上/正下居中
            GUI.Label(new Rect(nx + labelOffsetX, ny - 8f, 44f, 16f), entry.label, lblStyle);

            if (sel && entry.apCost > 0)
            {
                var costStyle = new GUIStyle(lblStyle) { fontSize = 8 };
                costStyle.normal.textColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
                GUI.Label(new Rect(nx + labelOffsetX, ny + 6f, 44f, 12f), $"{entry.apCost}AP", costStyle);
            }
        }
    }

    private Texture2D MakeTex(int w, int h, Color col)
    {
        var t = new Texture2D(w, h);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        // 径向功能菜单（按住 R 激活）
        if (_radialActive)
        {
            DrawRadialMenu();
            return;
        }

        // 乘坐提示（覆盖正常 HUD）
        if (_mountedVehicle != null)
        {
            // ── 最后一格上方显示"N格"文字 ────────────────────────────────
            if (_ridePreviewCells != null && _ridePreviewCells.Count > 0
                && GridManager.Instance != null && mainCamera != null)
            {
                var lastCell  = _ridePreviewCells[_ridePreviewCells.Count - 1];
                Vector3 wpos  = GridManager.Instance.GridToWorld(lastCell);
                Vector3 spos  = mainCamera.WorldToScreenPoint(wpos);
                if (spos.z > 0)
                {
                    float sx = spos.x, sy = Screen.height - spos.y - 24f;
                    var numStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize  = 13, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    numStyle.normal.textColor = Color.green;
                    GUI.Label(new Rect(sx - 22, sy, 44, 20),
                              $"{_ridePreviewCells.Count}格", numStyle);
                }
            }

            // ── HUD 提示条 ───────────────────────────────────────────────
            string rideMsg = _mountedVehicle.IsAnimating
                ? $"[ {_mountedVehicle.name} ] 滑行中..."
                : $"[ {_mountedVehicle.name} ] 右键选方向发射  |  {interactKey}/ESC 下车";

            var rideStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            rideStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Box(new Rect(Screen.width / 2 - 210, Screen.height - 50, 420, 30), rideMsg, rideStyle);
            return;
        }

        if (!isInputEnabled) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;

        string hint;
        if (_currentItemMode == ItemMode.Melee)
        {
            style.normal.textColor = Color.red;
            hint = "[ 近战模式 ] 左键攻击  |  R 切换功能";
        }
        else if (_currentItemMode == ItemMode.Throw)
        {
            style.normal.textColor = new Color(0.3f, 0.8f, 1f);
            hint = "[ 投掷模式 ] 左键选目标发射  |  R 切换功能";
        }
        else if (_currentItemMode == ItemMode.Consume)
        {
            style.normal.textColor = new Color(0.4f, 1f, 0.4f);
            hint = "[ 使用模式 ] 左键立即使用  |  R 切换功能";
        }
        else if (_currentItemMode == ItemMode.Shoot)
        {
            style.normal.textColor = new Color(1f, 0.6f, 0.2f);
            hint = "[ 射击模式 ] 左键开枪  F:换弹  |  R 切换功能";
        }
        else if (turnBasedUnit.CanAct)
        {
            hint = "右键:移动  |  左键:投掷  |  R:选功能  |  Q:拾取  |  E:换层  |  Space:结束回合";
        }
        else
        {
            hint = "Space: 结束回合";
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