using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class HotbarSlot
{
    public ItemData itemData;
    public int quantity;

    // 武器槽弹药耗尽时仍保留槽位（枪还在，只是没子弹）
    public bool IsEmpty => itemData == null ||
        (quantity <= 0 && itemData.Type != ItemType.Weapon);

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
    [SerializeField][Range(5, 10)] private int hotbarSize = 8;

    [Header("投掷配置")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask throwTargetLayer;

    [Header("射击配置")]
    [Tooltip("武器开枪位置（枪口/肩部）；未设置则回退到 transform + 1.2m 高")]
    [SerializeField] private Transform firePoint;

    [Header("廖枪（盲射）")]
    [Tooltip("盲射额外随机散布角度（度）——朝鼠标方向大范围乱打")]
    [SerializeField] private float blindFireAngle = 20f;
    [Tooltip("盲射时枪口沿瞄准方向前伸的距离（越过掩体，避免打到自己的掩体）")]
    [SerializeField] private float blindFirePoke = 0.7f;

    /// <summary>廖枪盲射模式：由 PlayerInputController 在贴掩体按住廖枪键时置真。</summary>
    public bool BlindFireMode { get; set; }

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = true;
    [Tooltip("画枪口高度/朝向、廖枪盲射前伸出膛点与散布锥（调试用）")]
    [SerializeField] private bool debugFireGizmos = true;

    private List<HotbarSlot> hotbar = new List<HotbarSlot>();
    private int currentSlotIndex = 0;
    private Inventory inventory;
    private UnitMovement playerUnit;
    private TurnBasedUnit turnBasedUnit;
    private PlayerController _playerController;

    // firePoint 的“基准”本地偏移（半径/高度）缓存一次，避免每帧读回被自身写入污染，
    // 也让按姿态缩放枪口高度不会逐帧累乘。SetFirePoint / RecacheFireBase 时失效重算。
    private float _fireBaseRadius;
    private float _fireBaseHeight;
    private bool  _fireBaseCached;

    // 装备槽：防弹背心等 Equipment 类型物品
    private ItemData equippedArmor = null;
    private int currentArmorDurability = 0;

    // 瞄准状态（由 PlayerInputController 每帧调用 UpdateAiming 更新）
    private bool isAiming = false;
    private Vector3 aimTargetPos;

    // ============ 事件 ============

    public event System.Action<int, HotbarSlot> OnSlotChanged;
    public event System.Action<ItemData> OnItemUsed;

    // ============ 公开属性 ============

    public int CurrentSlotIndex => currentSlotIndex;
    // 探头时枪口横移到探出点（PlayerController.PeekOffset），子弹从探出位置出膛、绕过掩体。
    // 无 firePoint 时的回退高度也按姿态缩放（下蹲/匍匐枪口下沉）；有 firePoint 时高度已在
    // UpdateFirePointRotation 里按姿态处理，这里不重复缩放。
    public Vector3 FireOrigin
    {
        get
        {
            float mul = _playerController != null ? _playerController.FireHeightMultiplier : 1f;
            Vector3 basePos = firePoint != null
                ? firePoint.position
                : transform.position + Vector3.up * (1.2f * mul);
            return basePos + (_playerController != null ? _playerController.PeekOffset : Vector3.zero);
        }
    }
    public HotbarSlot CurrentSlot => hotbar.Count > 0 ? hotbar[currentSlotIndex] : null;
    public bool IsAiming => isAiming;
    public Vector3 AimTargetPos => aimTargetPos;
    public int HotbarSize => hotbarSize;
    public HotbarSlot GetSlot(int index) => (index >= 0 && index < hotbar.Count) ? hotbar[index] : null;

    /// <summary>开枪位置(枪口)Transform；未设置时 FireOrigin 回退到 transform + 1.2m。</summary>
    public Transform FirePoint => firePoint;
    /// <summary>调试/装配用：外部指定开枪位置（枪口）。用于占位枪校准廖枪高度/方向。</summary>
    public void SetFirePoint(Transform t) { firePoint = t; _fireBaseCached = false; }
    /// <summary>firePoint 基准偏移变化后（如运行时改枪口高度）强制重算缓存。</summary>
    public void RecacheFireBase() => _fireBaseCached = false;

    // ============ 初始化 ============

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        playerUnit    = GetComponent<UnitMovement>();
        turnBasedUnit = GetComponent<TurnBasedUnit>();
        _playerController = GetComponent<PlayerController>();

        for (int i = 0; i < hotbarSize; i++)
            hotbar.Add(new HotbarSlot());

        var existingInventory = GetComponent<Inventory>();
        if (existingInventory != null)
            SetInventory(existingInventory);
        else
            Debug.Log("[EquipmentManager] Waiting for SetInventory() call");

        PlayerInputController.OnHotbarScroll += OnHotbarScrollEvent;
    }

    void Update()
    {
        UpdateFirePointRotation();
    }

    /// <summary>
    /// 武器槽激活时，FirePoint 绕玩家 Y 轴随鼠标方向旋转。
    /// 保持 FirePoint 与玩家中心的水平偏移距离和高度不变。
    /// </summary>
    private void UpdateFirePointRotation()
    {
        if (firePoint == null) return;
        if (!_fireBaseCached) CacheFireBase();

        var slot = CurrentSlot;
        if (slot == null || slot.IsEmpty || slot.itemData?.Type != ItemType.Weapon) return;

        Vector3 hitPoint = GetWeaponAimPosition();
        Vector3 aimDir   = hitPoint - transform.position;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.001f) return;
        aimDir.Normalize();

        // 用缓存的基准半径/高度（不每帧读回被自身写入污染的 localPosition）。
        // 高度按当前姿态缩放：站立×1 / 下蹲、匍匐依次下沉，枪口随身体上下移动。
        float mul    = _playerController != null ? _playerController.FireHeightMultiplier : 1f;
        float height = _fireBaseHeight * mul;

        Vector3 newWorldPos = transform.position
                            + aimDir * _fireBaseRadius
                            + Vector3.up * height;

        firePoint.position = newWorldPos;
        firePoint.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
    }

    // 缓存 firePoint 的基准本地偏移（半径/高度），只在首次或 SetFirePoint/RecacheFireBase 后取一次
    private void CacheFireBase()
    {
        if (firePoint == null) { _fireBaseCached = false; return; }
        Vector3 lp = firePoint.localPosition;
        _fireBaseRadius = new Vector2(lp.x, lp.z).magnitude;
        _fireBaseHeight = lp.y;
        _fireBaseCached = true;
    }

    void OnHotbarScrollEvent(float delta)
    {
        ScrollSlot(delta);
    }

    void OnDestroy()
    {
        PlayerInputController.OnHotbarScroll -= OnHotbarScrollEvent;
    }

    // ============ 外部注入 ============

    /// <summary>
    /// 注入 Inventory（由 PlayerInputController 在 Inventory 初始化后调用）
    /// </summary>
    public void SetInventory(Inventory inv)
    {
        inventory = inv;

        if (inventory != null)
        {
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
    public bool UpdateAiming(bool disableAim = false)
    {
        if (disableAim) { isAiming = false; return false; }
        if (CurrentSlot == null || CurrentSlot.IsEmpty) { isAiming = false; return false; }

        var item = CurrentSlot.itemData;

        // 武器：直线瞄准，始终显示
        if (item.Type == ItemType.Weapon)
        {
            aimTargetPos = GetWeaponAimPosition();
            isAiming = true;
            return true;
        }

        // 消耗品投掷：弧线瞄准
        if (!item.IsThrowable) { isAiming = false; return false; }

        if (TryGetAimPosition(out Vector3 pos)) { aimTargetPos = pos; isAiming = true; }
        else isAiming = false;

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

    // ============ 射击（由 PlayerInputController 调用）============

    // 全自动射击计时器
    private float _autoFireTimer = 0f;
    // 点射协程状态（避免同帧重复触发）
    private bool _burstFiring = false;

    /// <summary>
    /// 玩家射击入口，由 PlayerInputController 每帧或按键时调用。
    /// GetKeyDown = SemiAuto / Burst；GetKey = FullAuto
    /// </summary>
    public bool ShootWeapon(bool held = false)
    {
        if (CurrentSlot == null) return false;
        var slot = hotbar[currentSlotIndex];
        if (slot.itemData == null || slot.itemData.Type != ItemType.Weapon) return false;

        WeaponData weapon = slot.itemData as WeaponData;
        if (weapon == null) return false;

        if (weapon.IshasBullet && slot.quantity <= 0)
        {
            DebugLog("弹药耗尽，按 F 换弹");
            return false;
        }

        switch (weapon.fireMode)
        {
            case FireMode.SemiAuto:
                if (held) return false; // 半自动不响应按住
                FireOneBurst(weapon, slot, 1);
                break;

            case FireMode.Burst:
                if (held) return false;
                if (!_burstFiring)
                    StartCoroutine(FireBurstCoroutine(weapon, slot));
                break;

            case FireMode.FullAuto:
                if (!held) { _autoFireTimer = 0f; return false; }
                _autoFireTimer -= Time.deltaTime;
                if (_autoFireTimer > 0f) return false;
                _autoFireTimer = weapon.fireInterval;
                FireOneBurst(weapon, slot, 1);
                break;

            case FireMode.Shotgun:
                if (held) return false;
                FireOneBurst(weapon, slot, weapon.pelletsPerShot, isPellet: true);
                break;
        }

        return true;
    }

    // 发射 count 颗弹丸（散弹时 isPellet=true，叠加额外散布）
    private void FireOneBurst(WeaponData weapon, HotbarSlot slot, int count, bool isPellet = false)
    {
        if (weapon.IshasBullet && slot.quantity <= 0) return;

        Vector3 origin   = FireOrigin;
        Vector3 hitPoint = GetWeaponAimPosition();
        Vector3 toTarget = hitPoint - origin;
        toTarget.y = 0f; // 水平方向，与 Straight throwable 一致
        Vector3 baseDir  = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;

        // 廖枪盲射：把枪口沿瞄准方向前伸，越过掩体后再出膛（避免子弹打到自己的掩体）
        if (BlindFireMode)
            origin += baseDir * blindFirePoke + Vector3.up * 0.2f;

        for (int i = 0; i < count; i++)
        {
            float spreadH = weapon.CalculateSpreadAngle();
            float spreadV = weapon.CalculateSpreadAngle();
            if (isPellet)
            {
                spreadH += Random.Range(-weapon.pelletSpreadAngle, weapon.pelletSpreadAngle);
                spreadV += Random.Range(-weapon.pelletSpreadAngle, weapon.pelletSpreadAngle);
            }
            // 盲射：叠加大范围随机散布（保证无论武器多准都“乱打”）
            if (BlindFireMode)
            {
                spreadH += Random.Range(-blindFireAngle, blindFireAngle);
                spreadV += Random.Range(-blindFireAngle, blindFireAngle);
            }
            Vector3 dir = Quaternion.Euler(spreadV, spreadH, 0) * baseDir;
            int dmg = weapon.CalculateDamage();
            BulletProjectile.Fire(weapon, origin, dir, dmg, gameObject, weapon.weaponHitLayer);
        }

        if (weapon.IshasBullet)
        {
            slot.quantity--; // 散弹每次消耗 1 发弹药
            DebugLog($"开枪 [{weapon.Name}] x{count}  弹药: {slot.quantity}/{weapon.MaxBullet}");
        }

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(weapon.UseCost);

        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
    }

    // 点射协程：按 fireInterval 间隔依次发射 burstCount 发
    private System.Collections.IEnumerator FireBurstCoroutine(WeaponData weapon, HotbarSlot slot)
    {
        _burstFiring = true;
        for (int i = 0; i < weapon.burstCount; i++)
        {
            if (weapon.IshasBullet && slot.quantity <= 0) break;
            FireOneBurst(weapon, slot, 1);
            if (i < weapon.burstCount - 1)
                yield return new WaitForSeconds(weapon.fireInterval);
        }
        _burstFiring = false;
    }

    /// <summary>
    /// 换弹：弹夹补满，消耗 ReloadApCost AP
    /// </summary>
    public bool ReloadWeapon()
    {
        var slot = hotbar[currentSlotIndex];
        if (slot.itemData == null || slot.itemData.Type != ItemType.Weapon) return false;

        WeaponData weapon = slot.itemData as WeaponData;
        if (weapon == null || !weapon.IshasBullet) return false;
        if (slot.quantity >= weapon.MaxBullet) { DebugLog("弹夹已满"); return false; }

        slot.quantity = weapon.MaxBullet;

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(weapon.ReloadApCost);

        DebugLog($"换弹 [{weapon.Name}]  {slot.quantity}/{weapon.MaxBullet}");
        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
        return true;
    }

    /// <summary>
    /// 武器瞄准：射线打地面得到鼠标指向的世界坐标（与 Straight throwable 逻辑一致）。
    /// 返回地面命中点；ShootWeapon / Visualizer 用 origin→hitPoint 方向后自行延伸到最大射程。
    /// </summary>
    public Vector3 GetWeaponAimPosition()
    {
        if (mainCamera == null) return transform.position + transform.forward * 20f;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, throwTargetLayer);

        if (hits.Length == 0)
        {
            // 无地面命中时沿摄像机射线取一个远点
            return transform.position + ray.direction.normalized * 50f;
        }

        float targetY = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorWorldY(playerUnit != null ? playerUnit.CurrentFloor : 0)
            : 0f;

        RaycastHit best = hits[0];
        float minDiff = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            float d = Mathf.Abs(h.point.y - targetY);
            if (d < minDiff) { minDiff = d; best = h; }
        }
        return best.point;
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
        Vector3 origin = FireOrigin;

        DebugLog($"Throwing [{item.Name}] → {targetPos}");

        ThrowableProjectile.Launch(item, cfg, origin, targetPos, gameObject);

        hotbar[currentSlotIndex].quantity -= 1;
        if (hotbar[currentSlotIndex].quantity <= 0)
            hotbar[currentSlotIndex] = new HotbarSlot();

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(item.UseCost);

        OnItemUsed?.Invoke(item);
        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
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
        if (CurrentSlot.itemData.Type != ItemType.Consumable || CurrentSlot.itemData.meleeDamage <= 0) return;
        var cd = CurrentSlot.itemData;

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

        if (turnBasedUnit != null)
            turnBasedUnit.ConsumeAP(Mathf.Max(cd.UseCost, 1));

        // 消耗品近战才扣数量（可重复用的武器 UseCost=0 不扣）
        if (cd.Type == ItemType.Consumable && cd.UseCost > 0)
        {
            hotbar[currentSlotIndex].quantity -= 1;
            if (hotbar[currentSlotIndex].quantity <= 0)
                hotbar[currentSlotIndex] = new HotbarSlot();
        }

        OnItemUsed?.Invoke(cd);
        OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
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

        if (CurrentSlot.quantity > 0)
        {
            DebugLog($"Used [{item.Name}]");

            if (item.Type == ItemType.Equipment)
            {
                EquipArmor(item);
                hotbar[currentSlotIndex] = new HotbarSlot(); // 装备只有1个
            }
            else
            {
                inventory?.ApplyEffect(item);
                if (item.apBonus > 0 && turnBasedUnit != null)
                    turnBasedUnit.AddAP(item.apBonus);

                hotbar[currentSlotIndex].quantity--;
                if (hotbar[currentSlotIndex].quantity <= 0)
                    hotbar[currentSlotIndex] = new HotbarSlot();
            }

            turnBasedUnit?.ConsumeAP(item.UseCost);
            OnItemUsed?.Invoke(item);
            OnSlotChanged?.Invoke(currentSlotIndex, CurrentSlot);
            return true;
        }

        return false;
    }

    // ============ 拾取路由（捡到物品优先进 Hotbar，满了才进 Inventory） ============

    /// <summary>
    /// 拾取物品统一入口：Hotbar 有位置则进 Hotbar，否则进 Inventory。
    /// 由 PlayerInputController 拾取时调用。
    /// </summary>
    public bool TryPickupItem(ItemData item, int qty)
    {
        if (item == null || qty <= 0) return false;
        int remaining = qty;

        // 先堆叠到 Hotbar 已有同类槽
        for (int i = 0; i < hotbarSize && remaining > 0; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemData.ID == item.ID && item.maxStack > 1)
            {
                int space = item.maxStack - hotbar[i].quantity;
                int add   = Mathf.Min(space, remaining);
                hotbar[i].quantity += add;
                remaining -= add;
                OnSlotChanged?.Invoke(i, hotbar[i]);
            }
        }

        // 再放空槽
        for (int i = 0; i < hotbarSize && remaining > 0; i++)
        {
            if (hotbar[i].IsEmpty)
            {
                // 武器拾取时 quantity = MaxBullet（满弹），忽略传入的 qty
                int slotQty = (item.Type == ItemType.Weapon)
                    ? Mathf.Max(1, item.MaxBullet)
                    : (item.maxStack > 1 ? Mathf.Min(item.maxStack, remaining) : remaining);
                hotbar[i] = new HotbarSlot(item, slotQty);
                remaining -= (item.Type == ItemType.Weapon) ? remaining : slotQty; // 武器一次放完
                OnSlotChanged?.Invoke(i, hotbar[i]);
            }
        }

        // Hotbar 满了，剩余进 Inventory
        if (remaining > 0 && inventory != null)
            inventory.AddItem(item, remaining);

        DebugLog($"TryPickupItem: {item.Name} x{qty} → hotbar got {qty - remaining}, inventory got {remaining}");
        return true;
    }

    // ============ Hotbar ↔ Inventory 手动搬运 ============

    /// <summary>双击 Inventory 格：整栈从 Inventory 转移到 Hotbar 空槽。</summary>
    public bool MoveToHotbar(ItemData item)
    {
        if (inventory == null || item == null) return false;
        int qty = inventory.GetItemCount(item);
        if (qty <= 0) return false;
        int slot = FindFirstEmptySlot();
        if (slot < 0) { DebugLog("Hotbar is full"); return false; }

        inventory.RemoveItem(item, qty);
        hotbar[slot] = new HotbarSlot(item, qty);
        DebugLog($"MoveToHotbar: {item.Name} x{qty} → slot {slot}");
        OnSlotChanged?.Invoke(slot, hotbar[slot]);
        return true;
    }

    /// <summary>单击 Inventory 格：从 Inventory 取 1 个放到 Hotbar（堆叠或空槽）。</summary>
    public bool MoveSingleToHotbar(ItemData item)
    {
        if (inventory == null || item == null) return false;
        if (inventory.GetItemCount(item) <= 0) return false;

        for (int i = 0; i < hotbarSize; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemData.ID == item.ID
                && item.maxStack > 1 && hotbar[i].quantity < item.maxStack)
            {
                inventory.RemoveItem(item, 1);
                hotbar[i].quantity++;
                OnSlotChanged?.Invoke(i, hotbar[i]);
                return true;
            }
        }

        int empty = FindFirstEmptySlot();
        if (empty < 0) { DebugLog("Hotbar is full"); return false; }
        inventory.RemoveItem(item, 1);
        hotbar[empty] = new HotbarSlot(item, 1);
        OnSlotChanged?.Invoke(empty, hotbar[empty]);
        return true;
    }

    /// <summary>双击 Hotbar 格：整栈从 Hotbar 搬回 Inventory。</summary>
    public bool MoveToInventory(int slot)
    {
        if (slot < 0 || slot >= hotbarSize || hotbar[slot].IsEmpty) return false;
        if (inventory == null) return false;

        inventory.AddItem(hotbar[slot].itemData, hotbar[slot].quantity);
        DebugLog($"MoveToInventory: {hotbar[slot].itemData.Name} x{hotbar[slot].quantity} from slot {slot}");
        hotbar[slot] = new HotbarSlot();
        OnSlotChanged?.Invoke(slot, hotbar[slot]);
        return true;
    }

    /// <summary>交换两个 Hotbar 槽位（拖拽换位）。</summary>
    public void SwapHotbarSlots(int a, int b)
    {
        if (a < 0 || a >= hotbarSize || b < 0 || b >= hotbarSize || a == b) return;
        (hotbar[a], hotbar[b]) = (hotbar[b], hotbar[a]);
        OnSlotChanged?.Invoke(a, hotbar[a]);
        OnSlotChanged?.Invoke(b, hotbar[b]);
    }

    /// <summary>拖拽到指定空 Hotbar 槽（跨库拖拽，由 DragDropController 调用）。</summary>
    public bool DragToHotbarSlot(ItemData item, int qty, int targetSlot)
    {
        if (item == null || qty <= 0 || targetSlot < 0 || targetSlot >= hotbarSize) return false;
        if (!hotbar[targetSlot].IsEmpty) return false;
        hotbar[targetSlot] = new HotbarSlot(item, qty);
        OnSlotChanged?.Invoke(targetSlot, hotbar[targetSlot]);
        return true;
    }

    /// <summary>返回第一个空 Hotbar 槽位索引，全满时返回 -1。</summary>
    public int FindFirstEmptySlot()
    {
        for (int i = 0; i < hotbarSize; i++)
            if (hotbar[i].IsEmpty) return i;
        return -1;
    }

    // ============ 初始同步（首次绑定 Inventory 时，把已有物品转移到 Hotbar） ============

    private void SyncHotbarFromInventory()
    {
        if (inventory == null) return;

        // 已有物品时不覆盖
        for (int i = 0; i < hotbarSize; i++)
            if (!hotbar[i].IsEmpty) return;

        var items = inventory.GetAllItems();
        int filled = 0;
        for (int i = 0; i < items.Count && i < hotbarSize; i++)
        {
            if (items[i].itemData == null) continue;
            hotbar[i] = new HotbarSlot(items[i].itemData, items[i].quantity);
            filled++;
        }

        // 从 Inventory 移除（转移而非复制），静默移除不触发多余事件
        foreach (var s in items)
            if (s.itemData != null) inventory.RemoveItem(s.itemData, s.quantity);

        // 每个填充的槽都通知 UI，而非只通知 currentSlotIndex
        for (int i = 0; i < hotbarSize; i++)
            OnSlotChanged?.Invoke(i, hotbar[i]);

        DebugLog($"SyncHotbarFromInventory: transferred {filled} item type(s) to Hotbar");
    }

    // ============ 调试 ============

    private void DebugLog(string msg)
    {
        if (enableDebugLog) Debug.Log($"[EquipmentManager] {msg}");
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isAiming) return;

        Vector3 start = FireOrigin;
        var item = CurrentSlot?.itemData;
        if (item == null) return;

        // 武器：画枪口高度/朝向 + 廖枪盲射前伸出膛点与散布锥（调试）；直线弹道仍由 WeaponAimVisualizer 负责
        if (item.Type == ItemType.Weapon) { DrawWeaponFireGizmos(start); return; }

        // 消耗品投掷：弧线
        ThrowableConfig cfg = item.ThrowCfg;
        if (cfg == null) return;

        Gizmos.color = cfg.IsArc ? Color.yellow : Color.cyan;
        Vector3 prev = start;
        for (int i = 1; i <= 20; i++)
        {
            float t = i / 20f;
            Vector3 linear = Vector3.Lerp(start, aimTargetPos, t);
            float arc = cfg.IsArc ? Mathf.Sin(t * Mathf.PI) * cfg.arcHeight : 0f;
            Vector3 p = linear + Vector3.up * arc;
            Gizmos.DrawLine(prev, p);
            prev = p;
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

    // 廖枪/射击调试可视化：枪口(高度)、瞄准方向；盲射时画前伸出膛点与 ±blindFireAngle 水平散布锥
    private void DrawWeaponFireGizmos(Vector3 origin)
    {
        if (!debugFireGizmos) return;

        Vector3 flat = GetWeaponAimPosition() - origin; flat.y = 0f;
        Vector3 dir  = flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;

        // 枪口(高度)：黄点 + 到地面的竖直参考线
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(origin, 0.06f);
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawLine(origin, new Vector3(origin.x, transform.position.y, origin.z));

        // 瞄准方向：青线
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + dir * 2f);

        if (!BlindFireMode) return;

        // 廖枪盲射：真实出膛点（沿瞄准方向前伸 blindFirePoke + 抬高 0.2）
        Vector3 poke = origin + dir * blindFirePoke + Vector3.up * 0.2f;
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(poke, 0.06f);

        // ±blindFireAngle 水平散布锥
        Gizmos.color = new Color(1f, 0.35f, 0f, 0.9f);
        Vector3 l = Quaternion.Euler(0f, -blindFireAngle, 0f) * dir;
        Vector3 r = Quaternion.Euler(0f,  blindFireAngle, 0f) * dir;
        Gizmos.DrawLine(poke, poke + l * 3f);
        Gizmos.DrawLine(poke, poke + r * 3f);
    }

    // ============ 防具管理 ============

    public ItemData EquippedArmor => equippedArmor;

    public bool EquipArmor(ItemData item)
    {
        if (item == null || item.Type != ItemType.Equipment) return false;
        equippedArmor = item;
        currentArmorDurability = item.armorDurability;
        DebugLog($"装备防具: {item.Name}  减伤 {item.damageReduction}%  耐久 {item.armorDurability}");
        return true;
    }

    public void UnequipArmor()
    {
        if (equippedArmor != null) DebugLog($"卸下防具: {equippedArmor.Name}");
        equippedArmor = null;
        currentArmorDurability = 0;
    }

    /// <summary>
    /// 返回当前防具减伤百分比（0~50），并扣除耐久。
    /// 调用时机：玩家受到物理伤害时（由 PlayerController.TakeDamage 调用）
    /// </summary>
    public int ConsumeArmorAndGetReduction()
    {
        if (equippedArmor == null) return 0;
        int reduction = equippedArmor.damageReduction;

        // 无限耐久不扣
        if (equippedArmor.armorDurability == -1) return reduction;

        currentArmorDurability--;
        DebugLog($"防具耐久 -{1} → {currentArmorDurability}/{equippedArmor.armorDurability}");

        if (currentArmorDurability <= 0)
        {
            DebugLog($"防具 [{equippedArmor.Name}] 已损毁");
            equippedArmor = null;
            currentArmorDurability = 0;
        }
        return reduction;
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