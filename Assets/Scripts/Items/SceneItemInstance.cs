using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SceneItemInstance — 场景物品运行时组件
///
/// 挂在场景物品 Prefab 的根 GameObject 上，读取 SceneItemData 配置，
/// 驱动所有勾选的布尔行为（开关 / 锁 / 推动 / 推倒 / 掩体 / 破坏 / 容器 / 爆炸）。
///
/// 外部调用入口：
///   TryInteract(interactor)   ← PlayerInputController / EnemyAI
///   TakeDamage(dmg, source)   ← 武器命中、爆炸溅射
///   TriggerExplosion(source)  ← 链式爆炸
/// </summary>
public class SceneItemInstance : MonoBehaviour
{
    // ── 配置 ──────────────────────────────────────────────────────────

    [Header("配置")]
    [SerializeField] private SceneItemData data;

    // ── 运行时状态 ────────────────────────────────────────────────────

    private bool _isOpen        = false; // isToggleable 的当前开/关
    private bool _isLocked      = false; // isLockable 的锁定状态
    private int  _currentHp     = 0;     // isDestroyable 的剩余 HP
    private bool _isDestroyed   = false; // 已被破坏
    private bool _isToppled     = false; // 已被推倒（一次性）
    private bool _hasBeenLooted = false; // 容器已开启
    private bool       _isAnimating   = false; // 推动/推倒动画进行中（屏蔽新交互）
    private bool       _isOccupied   = false;  // 有骑手正在乘坐
    private GameObject _currentRider = null;

    public bool IsAnimating => _isAnimating;
    public bool IsOccupied  => _isOccupied;
    public bool IsRideable  => data != null && data.isRideable && !_isDestroyed;

    // ── 网格追踪 ─────────────────────────────────────────────────────

    private Vector2Int       _anchorCell;                     // 左下角锚点格坐标
    private int              _floor;                          // 所在楼层
    private List<Vector2Int> _occupiedCells = new List<Vector2Int>(); // 所有占用格

    // 推倒状态追踪（ComputeToppledCells 用）
    private Vector2Int _preToppingAnchor;              // 推倒前的锚点
    private Vector3    _toppledFallDir = Vector3.zero; // 推倒方向（zero = 未推倒）

    // 门旋转动画：铰链世界坐标（InitializeGrid 时由 HingeSide + Collider 计算一次，之后固定不变）
    private Vector3 _hingeWorldPos;

    // ── 组件缓存 ─────────────────────────────────────────────────────

    private Animator _animator;

    // ── 公开属性 ─────────────────────────────────────────────────────

    public SceneItemData Data     => data;
    public bool IsOpen            => _isOpen;
    public bool IsLocked          => _isLocked;
    public bool IsDestroyed       => _isDestroyed;
    public bool IsToppled         => _isToppled;
    public bool HasBeenLooted     => _hasBeenLooted;
    public Vector2Int AnchorCell  => _anchorCell;
    public int Floor              => _floor;

    /// <summary>当前占用的所有格子（只读）</summary>
    public System.Collections.Generic.IReadOnlyList<Vector2Int> OccupiedCells => _occupiedCells;

    /// <summary>
    /// 补正：重新应用格子阻挡（供单位离开时调用）
    /// 场景：推倒时某格因有单位被跳过 → 单位移走后应补标为不可走
    /// 由 UnitMovement.OnMoveComplete 或 TurnSystem 的回合末触发
    /// </summary>
    public void ReapplyGridBlocking() => RefreshGridBlocking();

    /// <summary>
    /// 计算 playerCell 到最近占用格的曼哈顿距离
    /// PlayerInputController 用此判断玩家是否在交互范围内
    /// </summary>
    public int MinGridDistanceTo(Vector2Int playerCell)
    {
        int min = int.MaxValue;
        foreach (var cell in _occupiedCells)
        {
            int d = Mathf.Abs(cell.x - playerCell.x) + Mathf.Abs(cell.y - playerCell.y);
            if (d < min) min = d;
        }
        return min == int.MaxValue ? 999 : min;
    }

    // ── 事件 ─────────────────────────────────────────────────────────

    /// <summary>成功交互后触发（开门、推箱子等）</summary>
    public event System.Action<SceneItemInstance> OnInteracted;

    /// <summary>物体被破坏时触发</summary>
    public event System.Action<SceneItemInstance> OnDestroyed;

    /// <summary>容器被打开时触发（外部系统负责生成战利品）</summary>
    public event System.Action<SceneItemInstance> OnContainerOpened;

    // ══════════════════════════════════════════════════════════════════
    // 初始化
    // ══════════════════════════════════════════════════════════════════

    void Start()
    {
        if (data == null) { Debug.LogError($"[SceneItemInstance:{name}] 缺少 SceneItemData！"); return; }

        _animator = GetComponent<Animator>();
        if (_animator != null && data.animatorController != null)
            _animator.runtimeAnimatorController = data.animatorController;

        // 初始化运行时状态
        _isLocked  = data.isLockable    && data.lockConfig.startLocked;
        _isOpen    = data.isToggleable  && data.toggleConfig.startOpen;
        _currentHp = data.isDestroyable ? data.destroyConfig.maxHp : 0;

        // 注册格子并应用初始阻挡
        InitializeGrid();
        RefreshGridBlocking();
    }

    void OnDestroy()
    {
        // 释放所有占用格（防止残留不可走标记）
        SetCellsWalkable(true);
    }

    // ── 网格初始化 ────────────────────────────────────────────────────

    private void InitializeGrid()
    {
        if (GridManager.Instance == null) return;

        _floor = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorFromWorldY(transform.position.y)
            : 0;

        // 根据当前位置计算所在格子——不修改 transform
        // 格子对齐由设计师在 Editor 中手动保证（或配合 Editor 吸附工具）
        // 运行时强制 Snap 会覆盖设计师手动调整的位置，因此去掉
        _anchorCell = GridManager.Instance.WorldToGrid(transform.position);

        // 门旋转动画：在初始（关闭）状态计算铰链世界坐标，之后固定不变
        if (data.isToggleable && Mathf.Abs(data.toggleConfig.openAngle) > 0.01f)
            _hingeWorldPos = ComputeHingeWorldPos(data.toggleConfig.hingeSide);

        ComputeGridCells();
        SetCellsWalkable(!CurrentBlocksMovement());
    }

    // ── 配置驱动的格子计算 ───────────────────────────────────────────

    /// <summary>
    /// 根据当前状态（站立 / 推倒）重新计算占用格
    /// 完全基于 SceneItemData 的 gridWidth / gridDepth / gridHeight，不依赖 Collider
    /// </summary>
    private void ComputeGridCells()
    {
        _occupiedCells.Clear();

        if (_isToppled && _toppledFallDir != Vector3.zero)
            ComputeToppledCells();
        else
            ComputeStandingCells();
    }

    /// <summary>站立状态：anchor 为左下角，向 +X / +Z 扩展 gridWidth × gridDepth 格</summary>
    private void ComputeStandingCells()
    {
        // Mathf.Max(1,...) 防止 ScriptableObject 字段未配置时值为 0 导致循环不执行
        int w = Mathf.Max(1, data.gridWidth);
        int d = Mathf.Max(1, data.gridDepth);

        for (int dx = 0; dx < w; dx++)
            for (int dz = 0; dz < d; dz++)
                _occupiedCells.Add(_anchorCell + new Vector2Int(dx, dz));

        Debug.Log($"[SceneItem:{name}] 站立 {w}×{d} 格  anchor={_anchorCell}");
    }

    /// <summary>
    /// 推倒状态：根据 _toppledFallDir 和 _preToppingAnchor 计算新 footprint
    ///
    /// 推倒原理（以 gridWidth=1, gridDepth=1, gridHeight=2 为例）：
    ///   向东 (+X)：高度倒向 X → 新占格 2×1，起点 = (原anchor.x + gridWidth, 原anchor.z)
    ///   向西 (-X)：高度倒向 -X → 新占格 2×1，起点 = (原anchor.x - gridHeight, 原anchor.z)
    ///   向北 (+Z)：高度倒向 Z → 新占格 1×2，起点 = (原anchor.x, 原anchor.z + gridDepth)
    ///   向南 (-Z)：高度倒向 -Z → 新占格 1×2，起点 = (原anchor.x, 原anchor.z - gridHeight)
    /// </summary>
    private void ComputeToppledCells()
    {
        // 同样防止值为 0 的情况
        int gw = Mathf.Max(1, data.gridWidth);
        int gd = Mathf.Max(1, data.gridDepth);
        int gh = Mathf.Max(1, data.gridHeight);
        Vector2Int toppleAnchor;
        int cellsX, cellsZ;

        if (_toppledFallDir == Vector3.right)
        {
            toppleAnchor = new Vector2Int(_preToppingAnchor.x + gw, _preToppingAnchor.y);
            cellsX = gh; cellsZ = gd;
        }
        else if (_toppledFallDir == Vector3.left)
        {
            toppleAnchor = new Vector2Int(_preToppingAnchor.x - gh, _preToppingAnchor.y);
            cellsX = gh; cellsZ = gd;
        }
        else if (_toppledFallDir == Vector3.forward)
        {
            toppleAnchor = new Vector2Int(_preToppingAnchor.x, _preToppingAnchor.y + gd);
            cellsX = gw; cellsZ = gh;
        }
        else // Vector3.back
        {
            toppleAnchor = new Vector2Int(_preToppingAnchor.x, _preToppingAnchor.y - gh);
            cellsX = gw; cellsZ = gh;
        }

        _anchorCell = toppleAnchor;

        for (int dx = 0; dx < cellsX; dx++)
            for (int dz = 0; dz < cellsZ; dz++)
                _occupiedCells.Add(toppleAnchor + new Vector2Int(dx, dz));

        Debug.Log($"[SceneItem:{name}] 推倒({_toppledFallDir}) {cellsX}×{cellsZ} 格 anchor={toppleAnchor}");
    }

    // ── 阻挡状态计算 ─────────────────────────────────────────────────

    /// <summary>当前状态下是否阻挡移动（受开/关、推倒等影响）</summary>
    private bool CurrentBlocksMovement()
    {
        if (_isDestroyed)                         return false;
        if (_isToppled  && data.isToppleable)     return data.toppleConfig.toppledBlocksMovement;
        if (_isOpen     && data.isToggleable)     return data.toggleConfig.openStateBlocksMovement;
        return data.blocksMovement;
    }

    private void RefreshGridBlocking() => SetCellsWalkable(!CurrentBlocksMovement());

    private void SetCellsWalkable(bool walkable)
    {
        if (GridManager.Instance == null) return;
        foreach (var cell in _occupiedCells)
        {
            var gc = GridManager.Instance.GetCell(cell, _floor);
            if (gc == null) continue;

            // 阻挡时：若格子上有单位（玩家/敌人）则跳过，不强制覆盖其站立格
            // 该格的静态 walkable 会在单位离开后由下次 RefreshGridBlocking 补正
            if (!walkable && GridManager.Instance.IsOccupied(cell, _floor)) continue;

            gc.isWalkable = walkable;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 主交互入口
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 主交互入口：PlayerInputController（F 键）或 EnemyAI 调用
    /// 自动选取优先级最高的可用行为执行
    /// 返回 true = 成功执行了一个行动
    /// </summary>
    public bool TryInteract(GameObject interactor)
    {
        if (data == null || _isDestroyed || _isAnimating) return false;

        // 确定本次行动类型与 AP 消耗
        int apCost;
        if (!ResolveInteraction(interactor, out apCost))
        {
            Debug.Log($"[SceneItem:{name}] 无可用交互");
            return false;
        }

        // AP 检查
        var turnUnit = interactor.GetComponent<TurnBasedUnit>();
        if (turnUnit != null && apCost > 0 && !turnUnit.HasEnoughMovementPoints(apCost))
        {
            Debug.Log($"[SceneItem:{name}] 行动点不足（需要 {apCost}）");
            return false;
        }

        // 执行行动
        bool success = ExecuteResolvedInteraction(interactor);
        if (!success) return false;

        // 消耗 AP
        if (turnUnit != null && apCost > 0)
            turnUnit.ConsumeAP(apCost);

        OnInteracted?.Invoke(this);

        // 同步任务注册表
        if (!string.IsNullOrEmpty(data.sceneObjectId))
            SyncMissionRegistry(interactor);

        // StoryFlag
        if (data.writesStoryFlag && !string.IsNullOrEmpty(data.storyFlagKey))
            StoryManager.Instance?.SetFlag(data.storyFlagKey);

        return true;
    }

    // ── 行动路由（两阶段：先确认类型+AP，再执行）──────────────────────

    private enum InteractionKind { None, Unlock, Toggle, Push, Topple, Explosive, Container, Ride }
    private InteractionKind _resolvedKind = InteractionKind.None;

    /// <summary>确定本次 TryInteract 执行哪种行动和消耗多少 AP</summary>
    private bool ResolveInteraction(GameObject interactor, out int apCost)
    {
        apCost = 0;

        if (data.isLockable && _isLocked)
        {
            // 解锁：需要钥匙或允许撬锁
            if (data.lockConfig.requiredKeyItemId > 0 && !data.lockConfig.canPickLock)
            {
                Debug.Log($"[SceneItem:{name}] 上锁，需要钥匙 ID:{data.lockConfig.requiredKeyItemId}");
                return false;
            }
            _resolvedKind = InteractionKind.Unlock;
            apCost = data.lockConfig.canPickLock ? data.lockConfig.pickLockApCost : 1;
            return true;
        }

        if (data.isRideable && !_isOccupied)
        {
            _resolvedKind = InteractionKind.Ride;
            apCost = data.rideConfig.apCostToBoard;
            return true;
        }

        if (data.isToggleable)
        {
            _resolvedKind = InteractionKind.Toggle;
            apCost = data.toggleConfig.apCostToToggle;
            return true;
        }

        if (data.isMovable)
        {
            _resolvedKind = InteractionKind.Push;
            apCost = data.pushConfig.apCostPerPush;
            return true;
        }

        if (data.isToppleable && !_isToppled && !_isDestroyed)
        {
            _resolvedKind = InteractionKind.Topple;
            apCost = data.toppleConfig.apCostToTopple;
            return true;
        }

        if (data.isExplosive && data.explosiveConfig.canManuallyTrigger)
        {
            _resolvedKind = InteractionKind.Explosive;
            apCost = 1;
            return true;
        }

        if (data.isContainer && !_hasBeenLooted)
        {
            _resolvedKind = InteractionKind.Container;
            apCost = 1;
            return true;
        }

        return false;
    }

    private bool ExecuteResolvedInteraction(GameObject interactor)
    {
        switch (_resolvedKind)
        {
            case InteractionKind.Unlock:    return ExecuteUnlock(interactor);
            case InteractionKind.Toggle:    return ExecuteToggle();
            case InteractionKind.Push:      return ExecutePush(interactor);
            case InteractionKind.Topple:    return ExecuteTopple(interactor);
            case InteractionKind.Explosive: return TriggerExplosion(interactor);
            case InteractionKind.Container: return ExecuteOpenContainer();
            case InteractionKind.Ride:      return ExecuteStartRide(interactor);
            default:                        return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 各行动实现
    // ══════════════════════════════════════════════════════════════════

    // ── 解锁 ─────────────────────────────────────────────────────────

    private bool ExecuteUnlock(GameObject interactor)
    {
        _isLocked = false;
        Debug.Log($"[SceneItem:{name}] 已解锁");

        // 解锁后立即顺势开门（不额外消耗 AP）
        if (data.isToggleable)
            ExecuteToggle();

        return true;
    }

    // ── 开/关切换 ─────────────────────────────────────────────────────

    private bool ExecuteToggle()
    {
        _isOpen = !_isOpen;
        var cfg = data.toggleConfig;

        // 格子阻挡立即生效（游戏逻辑即时）
        RefreshGridBlocking();
        BroadcastNoise(_isOpen ? cfg.openNoiseLevel : cfg.closeNoiseLevel);
        PlayVFX(data.stateChangeVFXPrefab);

        Debug.Log($"[SceneItem:{name}] {(_isOpen ? "打开" : "关闭")}");

        // 关门时：推开门格里的单位
        if (!_isOpen && cfg.slamDamage >= 0)
            KnockbackUnitsInDoorCells(cfg.slamDamage);

        // 旋转动画：openAngle != 0 时播门旋转，否则用 Animator Trigger
        if (Mathf.Abs(cfg.openAngle) > 0.01f)
            StartCoroutine(AnimateDoorSwing(_isOpen));
        else
            TriggerAnimator(_isOpen ? cfg.openAnimTrigger : cfg.closeAnimTrigger);

        // 打开且是容器 → 同时开箱
        if (_isOpen && data.isContainer && !_hasBeenLooted)
            ExecuteOpenContainer();

        return true;
    }

    // ── 推动 ─────────────────────────────────────────────────────────

    private bool ExecutePush(GameObject interactor)
    {
        if (GridManager.Instance == null) return false;

        Vector2Int dir = ComputePushDirection(interactor);
        if (dir == Vector2Int.zero) return false;

        // 检查目标格是否可走
        Vector2Int newAnchor = _anchorCell + dir;
        var newCells = ComputeCellsForAnchor(newAnchor);
        foreach (var c in newCells)
        {
            if (!GridManager.Instance.IsWalkable(c, _floor, ignoreOccupied: false))
            {
                Debug.Log($"[SceneItem:{name}] 推动受阻（目标格 {c} 不可走）");
                return false;
            }
        }

        // 立即更新格子数据（游戏逻辑即时生效）
        SetCellsWalkable(true);
        _anchorCell    = newAnchor;
        _occupiedCells = newCells;
        RefreshGridBlocking();

        // 目标世界坐标（新格子中心，保持原 Y）
        Vector3 wp     = GridManager.Instance.GridToWorld(newAnchor);
        Vector3 toPos  = new Vector3(wp.x, transform.position.y, wp.z);

        BroadcastNoise(data.pushConfig.pushNoiseLevel);
        Debug.Log($"[SceneItem:{name}] 推动到 {newAnchor}");

        // 启动平滑滑动动画
        StartCoroutine(AnimatePush(transform.position, toPos));
        return true;
    }

    private System.Collections.IEnumerator AnimatePush(Vector3 from, Vector3 to)
    {
        _isAnimating = true;
        const float duration = 0.22f;   // 推箱子：快速滑动
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            // Ease-out：开始快、到位前减速，像在地板上滑行
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = Vector3.Lerp(from, to, eased);
            yield return null;
        }

        transform.position = to;   // 确保精确到位
        _isAnimating = false;
    }

    // ── 门旋转动画 ────────────────────────────────────────────────────

    /// <summary>
    /// 根据 HingeSide 计算铰链的世界坐标。
    /// 在物体初始（关闭）状态调用一次，结果存入 _hingeWorldPos 固定使用。
    ///
    /// 半宽取法（优先级）：
    ///   1. BoxCollider.size.x / 2（本地空间，最准确）
    ///   2. gridWidth × cellSize / 2（无 Collider 时 fallback）
    /// </summary>
    private Vector3 ComputeHingeWorldPos(HingeSide side)
    {
        float halfWidth;
        var boxCol = GetComponentInChildren<BoxCollider>();
        if (boxCol != null)
        {
            // BoxCollider.size.x 是本地空间的宽度，transform.TransformPoint 会正确处理任意旋转
            halfWidth = boxCol.size.x * 0.5f;
        }
        else
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            halfWidth = Mathf.Max(1, data.gridWidth) * cellSize * 0.5f;
        }

        float localOffsetX;
        switch (side)
        {
            case HingeSide.Left:   localOffsetX = -halfWidth; break;
            case HingeSide.Right:  localOffsetX = +halfWidth; break;
            default:               localOffsetX = 0f;          break; // Center
        }

        return transform.TransformPoint(new Vector3(localOffsetX, 0f, 0f));
    }

    /// <summary>
    /// 门开/关旋转动画：绕固定铰链轴（Y 轴）转到目标角度。
    /// 使用预算终点 + Lerp，避免多帧 RotateAround 积累误差。
    /// opening = true  → 开门（旋转 +openAngle 度）
    /// opening = false → 关门（旋转 −openAngle 度，回到初始状态）
    /// </summary>
    private System.Collections.IEnumerator AnimateDoorSwing(bool opening)
    {
        _isAnimating = true;

        var cfg   = data.toggleConfig;
        float angle = opening ? cfg.openAngle : -cfg.openAngle;

        Vector3    startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // 预算终点：绕铰链世界坐标、Y 轴旋转 angle 度
        transform.RotateAround(_hingeWorldPos, Vector3.up, angle);
        Vector3    endPos = transform.position;
        Quaternion endRot = transform.rotation;

        // 还原到起点
        transform.position = startPos;
        transform.rotation = startRot;

        // 平滑插值（ease in-out）
        float elapsed = 0f;
        while (elapsed < cfg.swingDuration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / cfg.swingDuration);
            // smoothstep：t²(3-2t) 起末均减速，门感觉有惯性
            float eased = t * t * (3f - 2f * t);

            transform.position = Vector3.Lerp(startPos, endPos, eased);
            transform.rotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        transform.position = endPos;
        transform.rotation = endRot;

        _isAnimating = false;
    }

    // ── 推倒 ─────────────────────────────────────────────────────────

    private bool ExecuteTopple(GameObject interactor)
    {
        var cfg = data.toppleConfig;

        // 1. 计算倒向（interactor → 物体方向，吸附四方向）
        Vector3 fallDir = ComputeFallDirection(interactor);

        // 2. 检查此方向是否被允许
        if (!IsFallDirectionAllowed(fallDir, cfg.allowedDirections))
        {
            Debug.Log($"[SceneItem:{name}] 该方向({fallDir})不允许推倒");
            return false;
        }

        // 3. 记录推倒前状态
        _preToppingAnchor = _anchorCell;
        _toppledFallDir   = fallDir;
        _isToppled        = true;

        // 4. 释放旧格（动画前立即释放）
        SetCellsWalkable(true);

        // 5. 计算旋转参数
        Vector3 pivot   = ComputeTopplePivot(fallDir);
        Vector3 rotAxis = Vector3.Cross(fallDir, Vector3.up).normalized;

        BroadcastNoise(cfg.toppleNoiseLevel);
        Debug.Log($"[SceneItem:{name}] 推倒 → {fallDir}  圆心:{pivot}");

        // 6. 启动动画协程（格子在动画结束后更新）
        StartCoroutine(AnimateTopple(pivot, rotAxis));
        return true;
    }

    private System.Collections.IEnumerator AnimateTopple(Vector3 pivot, Vector3 rotAxis)
    {
        _isAnimating = true;

        // ── 预算最终状态 ─────────────────────────────────────────────────
        // 问题：RotateAround 让物体绕底边轴心画弧线，
        //       弧线中段物体中心会先向上偏移，造成"先升后落"的视觉。
        // 解法：预先算出倒下后的位置/朝向，再直接从起点 Lerp 到终点，
        //       路径是直线而非弧线，没有中间的上升过程。

        Vector3    startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // 在当前帧瞬间执行完整旋转，得到终点状态
        transform.RotateAround(pivot, rotAxis, -90f);

        // 贴地修正：以 pivot.y 为目标（pivot 就是旋转前的底面 Y，最可靠）
        // 不使用 FloorManager，避免因楼层配置不匹配导致偏差
        Physics.SyncTransforms();
        var snapCol = GetComponentInChildren<Collider>();
        if (snapCol != null)
        {
            float correction = pivot.y - snapCol.bounds.min.y;
            if (Mathf.Abs(correction) > 0.0001f)
                transform.position += Vector3.up * correction;
        }

        Vector3    endPos = transform.position;
        Quaternion endRot = transform.rotation;

        // 立即还原到起点，准备播动画
        transform.position = startPos;
        transform.rotation = startRot;

        // ── 动画插值 ─────────────────────────────────────────────────────
        const float duration = 0.45f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            // t² 重力加速：开始缓慢，越接近落地越快
            float eased = t * t;

            transform.position = Vector3.Lerp(startPos, endPos, eased);
            transform.rotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        // 确保精确落在终点
        transform.position = endPos;
        transform.rotation = endRot;

        // 格子 / 特效在动画结束后统一更新
        ComputeGridCells();

        // 推开落点范围内的单位（书架砸到人）
        var toppleCfg = data.toppleConfig;
        if (toppleCfg.knockbackDamage > 0 || toppleCfg.knockbackDistance > 0)
        {
            Vector2Int pushDir = FallDirToGridDir(_toppledFallDir);
            KnockbackUnitsInCells(_occupiedCells, pushDir,
                toppleCfg.knockbackDamage, toppleCfg.knockbackDistance);
        }

        RefreshGridBlocking();
        PlayVFX(data.stateChangeVFXPrefab);

        _isAnimating = false;
    }

    /// <summary>
    /// 计算倒向（interactor → 物体，吸附到四个正方向之一）
    /// </summary>
    private Vector3 ComputeFallDirection(GameObject interactor)
    {
        if (interactor == null) return transform.forward;

        Vector3 toItem = transform.position - interactor.transform.position;
        toItem.y = 0f;

        if (toItem.sqrMagnitude < 0.001f) return transform.forward;

        float ax = Mathf.Abs(toItem.x);
        float az = Mathf.Abs(toItem.z);

        if (ax >= az)
            return toItem.x >= 0 ? Vector3.right   : Vector3.left;
        else
            return toItem.z >= 0 ? Vector3.forward  : Vector3.back;
    }

    /// <summary>
    /// 检查倒向是否在允许列表里
    /// </summary>
    private static bool IsFallDirectionAllowed(Vector3 fallDir, ToppleDirectionFlags allowed)
    {
        if (fallDir == Vector3.right)   return (allowed & ToppleDirectionFlags.Right)   != 0;
        if (fallDir == Vector3.left)    return (allowed & ToppleDirectionFlags.Left)    != 0;
        if (fallDir == Vector3.forward) return (allowed & ToppleDirectionFlags.Forward) != 0;
        if (fallDir == Vector3.back)    return (allowed & ToppleDirectionFlags.Back)    != 0;
        return false;
    }

    /// <summary>
    /// 计算旋转圆心（纯格子坐标，不依赖 Collider）
    ///
    /// 圆心 = 占用格 footprint 在倒向一侧的底部边缘中点
    /// 格子 cell center 由 GridToWorld 给出，边缘 = center ± cellSize/2
    ///
    ///   向东 → x = anchorWorld.x + (gridWidth  - 0.5) * cellSize
    ///   向西 → x = anchorWorld.x - 0.5 * cellSize
    ///   向北 → z = anchorWorld.z + (gridDepth  - 0.5) * cellSize
    ///   向南 → z = anchorWorld.z - 0.5 * cellSize
    ///   y   = 楼层地面高度（不依赖 Collider.bounds）
    ///
    /// 由于 InitializeGrid 已经将 transform 吸附到格子中心，
    /// 此计算与 ComputeToppledCells 使用相同的参考点，视觉与格子完全对齐。
    /// </summary>
    private Vector3 ComputeTopplePivot(Vector3 fallDir)
    {
        var col = GetComponentInChildren<Collider>();

        // ── 有 Collider：直接用实际 Bounds 边缘作为 pivot（最准确）──────
        // 物体在哪里，pivot 就在哪里，完全尊重设计师的摆放位置
        if (col != null)
        {
            Bounds b = col.bounds;
            return new Vector3(
                b.center.x + fallDir.x * b.extents.x,
                b.min.y,
                b.center.z + fallDir.z * b.extents.z
            );
        }

        // ── 无 Collider：退回格子中心推算（fallback）───────────────────
        if (GridManager.Instance == null) return transform.position;

        float   cellSize    = GridManager.Instance.CellSize;
        Vector3 anchorWorld = GridManager.Instance.GridToWorld(_anchorCell);
        float   pivotY      = FloorManager.Instance != null
                                ? FloorManager.Instance.GetFloorWorldY(_floor) : 0f;
        float pivotX = anchorWorld.x;
        float pivotZ = anchorWorld.z;

        if      (fallDir == Vector3.right)   pivotX = anchorWorld.x + (Mathf.Max(1, data.gridWidth)  - 0.5f) * cellSize;
        else if (fallDir == Vector3.left)    pivotX = anchorWorld.x - 0.5f * cellSize;
        else if (fallDir == Vector3.forward) pivotZ = anchorWorld.z + (Mathf.Max(1, data.gridDepth) - 0.5f) * cellSize;
        else if (fallDir == Vector3.back)    pivotZ = anchorWorld.z - 0.5f * cellSize;

        return new Vector3(pivotX, pivotY, pivotZ);
    }

    /// <summary>
    /// 旋转后把物体最低点精确贴回地面
    ///
    /// 问题背景：
    ///   RotateAround 直接修改 Transform，但 Unity 物理系统在同帧内
    ///   不会立即更新 Collider.bounds（需要等到下一个 FixedUpdate）。
    ///   Physics.SyncTransforms() 强制同帧同步，确保 bounds 反映旋转后的状态。
    ///
    /// 无 Collider 时：
    ///   旋转轴心 pivot.y = groundY，旋转后轴心保持不动，
    ///   物体底部边缘就是轴心本身（理论上已贴地）。
    ///   此时不做额外修正，避免用 transform.position.y（不是底面）误算。
    /// </summary>
    /// <summary>
    /// 把物体最低点贴到目标地面 Y。
    /// targetY 默认从 FloorManager 读取；topple 时传入 pivot.y 更可靠。
    /// </summary>
    private void SnapToGround(float? targetY = null)
    {
        var col = GetComponentInChildren<Collider>();
        if (col == null) return;

        Physics.SyncTransforms();

        float groundY = targetY ??
                        (FloorManager.Instance != null
                            ? FloorManager.Instance.GetFloorWorldY(_floor)
                            : 0f);

        float correction = groundY - col.bounds.min.y;
        if (Mathf.Abs(correction) > 0.0001f)
            transform.position += Vector3.up * correction;
    }

    // ── 容器开箱 ─────────────────────────────────────────────────────

    private bool ExecuteOpenContainer()
    {
        if (_hasBeenLooted) return false;
        _hasBeenLooted = true;

        // 通知外部系统生成战利品（由 ItemSpawnManager 或关卡脚本订阅此事件）
        OnContainerOpened?.Invoke(this);

        Debug.Log($"[SceneItem:{name}] 容器已打开，" +
                  $"固定战利品 {data.containerConfig.fixedLootIds.Length} 件，" +
                  $"掉落表 ID:{data.containerConfig.lootTableId}");
        return true;
    }

    // ── 乘坐位移 ─────────────────────────────────────────────────────

    /// <summary>上车：将骑手定位到载具上，进入等待方向输入的状态</summary>
    private bool ExecuteStartRide(GameObject rider)
    {
        if (_isOccupied || rider == null) return false;

        _isOccupied   = true;
        _currentRider = rider;

        // 关键：把骑手的格子占据从原格移到载具格，防止原格阻断滑行方向检测
        var riderMovement = rider.GetComponent<UnitMovement>();
        if (riderMovement != null)
            riderMovement.SetGridPosition(_anchorCell);

        // 视觉定位到载具上（覆盖 SetGridPosition 设置的 transform.position）
        rider.transform.position = transform.position + data.rideConfig.riderOffset;

        TriggerAnimator(data.rideConfig.boardAnimTrigger);
        Debug.Log($"[SceneItem:{name}] {rider.name} 上车，按右键选方向");
        return true;
    }

    /// <summary>由 PlayerInputController 在骑手选好方向后调用，启动滑行协程</summary>
    public void LaunchRide(Vector2Int dir)
    {
        if (!_isOccupied || _currentRider == null || _isAnimating) return;
        StartCoroutine(AnimateRideSlide(_currentRider, dir));
    }

    /// <summary>
    /// 预计算沿 dir 方向能走的格子列表（不执行，仅供 UI 预览）。
    /// maxCells 由调用方根据 AP 上限传入。
    /// 遇到不可走格或敌人占据格时停止。
    /// </summary>
    public List<Vector2Int> ComputeSlidePreview(Vector2Int dir, int maxCells)
    {
        var result = new List<Vector2Int>();
        if (GridManager.Instance == null || maxCells <= 0) return result;

        Vector2Int cur = _anchorCell;
        for (int i = 0; i < maxCells; i++)
        {
            Vector2Int next = cur + dir;
            if (!GridManager.Instance.IsWalkable(next, _floor, ignoreOccupied: false))
                break;
            result.Add(next);
            cur = next;
        }
        return result;
    }

    /// <summary>骑手主动下车（ESC / 死亡等意外情况），移到载具旁边最近的可走格</summary>
    public void CancelRide()
    {
        if (!_isOccupied || _currentRider == null) return;

        DismountRider(_currentRider);
        _isOccupied   = false;
        _currentRider = null;
    }

    /// <summary>将骑手移到载具旁边第一个可走格（四方向扫描）</summary>
    private void DismountRider(GameObject rider)
    {
        var riderMovement = rider.GetComponent<UnitMovement>();
        if (riderMovement == null || GridManager.Instance == null) return;

        Vector2Int dismountCell = FindDismountCell();
        riderMovement.SetGridPosition(dismountCell);
        Debug.Log($"[SceneItem:{name}] {rider.name} 下车到格子 {dismountCell}");
    }

    /// <summary>在载具四周找第一个可走（且未被占据）的格子；找不到时返回当前锚点（兜底）</summary>
    private Vector2Int FindDismountCell()
    {
        if (GridManager.Instance == null) return _anchorCell;

        Vector2Int[] dirs = {
            new Vector2Int( 1,  0),
            new Vector2Int(-1,  0),
            new Vector2Int( 0,  1),
            new Vector2Int( 0, -1),
        };

        foreach (var dir in dirs)
        {
            Vector2Int candidate = _anchorCell + dir;
            if (GridManager.Instance.IsWalkable(candidate, _floor, ignoreOccupied: false))
                return candidate;
        }

        return _anchorCell; // 极端情况四面都堵死，原地下车
    }

    /// <summary>沿 dir 方向逐格滑行，每格检查是否可走 + AP 是否充足</summary>
    private System.Collections.IEnumerator AnimateRideSlide(GameObject rider, Vector2Int dir)
    {
        _isAnimating = true;

        var cfg           = data.rideConfig;
        var riderMovement = rider.GetComponent<UnitMovement>();
        var riderUnit     = rider.GetComponent<TurnBasedUnit>();
        float cellDur     = cfg.slideSpeed > 0f ? 1f / cfg.slideSpeed : 0.18f;

        // 一次性扣除 AP（滑行本身不再逐格计费）
        riderUnit?.ConsumeAP(data.UseCost);

        // 广播噪音（购物车推出去会有声响）
        BroadcastNoise(cfg.slideNoiseLevel);

        TriggerAnimator(cfg.slideAnimTrigger);

        int moved = 0;
        while (moved < cfg.maxSlideCells)
        {
            Vector2Int nextAnchor = _anchorCell + dir;

            // 释放当前格，为碰撞检测让路
            SetCellsWalkable(true);
            var newCells = ComputeCellsForAnchor(nextAnchor);

            // 检查目标格是否可走（障碍物 / 地图边界 / 其他单位）
            bool blocked = false;
            foreach (var c in newCells)
            {
                if (!GridManager.Instance.IsWalkable(c, _floor, ignoreOccupied: false))
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                RefreshGridBlocking(); // 恢复当前格阻挡
                break;
            }

            // ── 动画：平滑滑动一格 ──────────────────────────────────────
            Vector3 cartStart  = transform.position;
            Vector3 cartEnd    = GridManager.Instance.GridToWorld(nextAnchor);
            cartEnd.y          = cartStart.y; // 保持高度

            Vector3 riderStart = rider.transform.position;
            Vector3 riderEnd   = cartEnd + cfg.riderOffset;

            float elapsed = 0f;
            while (elapsed < cellDur)
            {
                elapsed += Time.deltaTime;
                float t     = Mathf.Clamp01(elapsed / cellDur);
                float eased = 1f - (1f - t) * (1f - t); // ease-out
                transform.position     = Vector3.Lerp(cartStart, cartEnd, eased);
                rider.transform.position = Vector3.Lerp(riderStart, riderEnd, eased);
                yield return null;
            }

            transform.position = cartEnd;

            // ── 更新格子数据 ────────────────────────────────────────────
            _anchorCell    = nextAnchor;
            _occupiedCells = newCells;
            RefreshGridBlocking();

            // 更新骑手格子位置（处理占据标记），再覆盖 transform 到载具上
            if (riderMovement != null)
            {
                riderMovement.SetGridPosition(nextAnchor);
                rider.transform.position = cartEnd + cfg.riderOffset;
            }

            moved++;
        }

        // ── 自动下车：移到载具旁边格子 ─────────────────────────────────
        TriggerAnimator(cfg.dismountAnimTrigger);
        DismountRider(rider);
        _isOccupied   = false;
        _currentRider = null;
        _isAnimating  = false;

        Debug.Log($"[SceneItem:{name}] 滑行结束，共 {moved} 格");
    }

    // ══════════════════════════════════════════════════════════════════
    // 受伤 / 破坏 / 爆炸（由武器/爆炸溅射调用）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 对本物体造成伤害（武器命中、爆炸溅射）
    /// isDestroyable = true 且 HP 归零时自动销毁
    /// </summary>
    public void TakeDamage(int damage, GameObject source = null)
    {
        if (!data.isDestroyable || _isDestroyed) return;

        _currentHp -= damage;
        Debug.Log($"[SceneItem:{name}] 受到 {damage} 伤害，剩余 HP:{_currentHp}");

        if (_currentHp <= 0)
            ExecuteDestroy(source);
    }

    private void ExecuteDestroy(GameObject source = null)
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        var cfg = data.destroyConfig;

        PlayVFX(cfg.destroyVFXPrefab);

        // 生成碎片
        if (cfg.debrisPrefab != null)
        {
            Instantiate(cfg.debrisPrefab, transform.position, transform.rotation);
            // TODO: 碎片持续回合计时（连接 TurnSystem）
        }

        // 释放格子（碎片阻挡由 DebrisPrefab 上自身的 Collider + obstacleLayer 处理）
        SetCellsWalkable(true);

        OnDestroyed?.Invoke(this);

        // 链式爆炸
        if (data.isExplosive && data.explosiveConfig.explodeOnDestroy)
            TriggerExplosion(source);

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 触发爆炸（主动引爆 / 链式爆炸 / 破坏触发）
    /// </summary>
    public bool TriggerExplosion(GameObject source = null)
    {
        if (!data.isExplosive) return false;
        var cfg = data.explosiveConfig;

        PlayVFX(cfg.explosionVFXPrefab);
        BroadcastNoise(5); // 爆炸 = 最大噪音

        // AoE 伤害
        float radius = cfg.explosionRadius * (GridManager.Instance?.CellSize ?? 1f);
        var alreadyHit = new HashSet<IDamageable>();
        foreach (var col in Physics.OverlapSphere(transform.position, radius))
        {
            var dmg = col.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive || alreadyHit.Contains(dmg)) continue;
            alreadyHit.Add(dmg);
            dmg.TakeDamage(cfg.explosionDamage, gameObject);
        }

        // 链式引爆
        if (cfg.chainExplode)
        {
            foreach (var col in Physics.OverlapSphere(transform.position, radius))
            {
                var other = col.GetComponentInParent<SceneItemInstance>();
                if (other != null && other != this && !other._isDestroyed)
                    other.TriggerExplosion(gameObject);
            }
        }

        Debug.Log($"[SceneItem:{name}] 爆炸！伤害={cfg.explosionDamage} 范围={cfg.explosionRadius}格");

        if (!_isDestroyed) ExecuteDestroy(source);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════
    // 工具方法
    // ══════════════════════════════════════════════════════════════════

    /// <summary>根据 interactor 位置计算推动方向（四方向之一）</summary>
    private Vector2Int ComputePushDirection(GameObject interactor)
    {
        if (GridManager.Instance == null) return Vector2Int.zero;

        Vector2Int fromCell = GridManager.Instance.WorldToGrid(interactor.transform.position);
        Vector2Int delta    = _anchorCell - fromCell;

        if (delta == Vector2Int.zero) return Vector2Int.zero;

        // 取绝对值更大的轴作为推动方向
        return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int(delta.x > 0 ? 1 : -1, 0)
            : new Vector2Int(0, delta.y > 0 ? 1 : -1);
    }

    private List<Vector2Int> ComputeCellsForAnchor(Vector2Int anchor)
    {
        return new List<Vector2Int> { anchor };
    }

    private void TriggerAnimator(string triggerName)
    {
        if (_animator != null && !string.IsNullOrEmpty(triggerName))
            _animator.SetTrigger(triggerName);
    }

    private static void PlayVFX(GameObject vfxPrefab)
    {
        // VFX Prefab 应在播放完成后自毁（Particle System → Stop Action = Destroy）
    }

    private void BroadcastNoise(int level)
    {
        if (level <= 0) return;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.BroadcastSound(new SoundEvent(
                transform.position,
                level * 2f,       // 半径：与 DrillOperator 保持一致
                gameObject,
                SoundType.Environmental,
                level / 5f        // 强度：0~1
            ));
        }
        Debug.Log($"[SceneItem:{name}] 噪音等级 {level}，半径 {level * 2f}m");
    }

    // ── 击退辅助 ─────────────────────────────────────────────────────

    /// <summary>
    /// 将 Vector3 方向（right/left/forward/back）转换为格子 Vector2Int 方向
    /// </summary>
    private static Vector2Int FallDirToGridDir(Vector3 fallDir)
    {
        return new Vector2Int(Mathf.RoundToInt(fallDir.x), Mathf.RoundToInt(fallDir.z));
    }

    /// <summary>
    /// 在指定格子列表中查找所有单位，向 pushDir 方向推 pushDistance 格并造成 damage 伤害。
    /// 用于：推倒（书架砸到人）
    /// </summary>
    private void KnockbackUnitsInCells(
        System.Collections.Generic.IEnumerable<Vector2Int> cells,
        Vector2Int pushDir, int damage, int pushDistance = 1)
    {
        foreach (var cell in cells)
        {
            foreach (var unit in Object.FindObjectsOfType<UnitMovement>())
            {
                if (unit.CurrentGridPosition != cell) continue;
                if (unit.CurrentFloor != _floor) continue;

                unit.ApplyKnockback(pushDir, pushDistance, damage, gameObject);
                Debug.Log($"[SceneItem:{name}] 击退 {unit.name} → {pushDir}，伤害 {damage}");
            }
        }
    }

    /// <summary>
    /// 关门时：将门格内的单位推向最近的空格（自动寻找推开方向）。
    /// 适用于门、闸门等关闭时可能夹到单位的物体。
    /// </summary>
    private void KnockbackUnitsInDoorCells(int damage)
    {
        if (GridManager.Instance == null) return;

        Vector2Int[] cardinalDirs =
        {
            new Vector2Int( 1, 0),   // 东
            new Vector2Int(-1, 0),   // 西
            new Vector2Int( 0, 1),   // 北
            new Vector2Int( 0,-1),   // 南
        };

        foreach (var cell in _occupiedCells)
        {
            foreach (var unit in Object.FindObjectsOfType<UnitMovement>())
            {
                if (unit.CurrentGridPosition != cell) continue;
                if (unit.CurrentFloor != _floor) continue;

                // 找最近的可走相邻格，把单位推向那里
                Vector2Int bestDir = Vector2Int.zero;
                foreach (var dir in cardinalDirs)
                {
                    var nb = cell + dir;
                    if (GridManager.Instance.IsValid(nb) &&
                        GridManager.Instance.IsWalkable(nb, _floor, ignoreOccupied: false))
                    {
                        bestDir = dir;
                        break;
                    }
                }

                if (bestDir != Vector2Int.zero)
                    unit.ApplyKnockback(bestDir, 1, damage, gameObject);

                Debug.Log($"[SceneItem:{name}] 关门击退 {unit.name} → {bestDir}，伤害 {damage}");
            }
        }
    }

    private void SyncMissionRegistry(GameObject interactor)
    {
        string faction = interactor.GetComponent<PlayerController>() != null ? "Player" : "Enemy";
        WorldItemState state = _isDestroyed  ? WorldItemState.Interrupted :
                               _isOpen       ? WorldItemState.Completed   :
                                               WorldItemState.Active;
        WorldItemRegistry.Record(data.sceneObjectId, state, faction);
    }

    // ══════════════════════════════════════════════════════════════════
    // 调试可视化
    // ══════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (data == null || GridManager.Instance == null) return;

        float cellSize = GridManager.Instance.CellSize;
        float y        = transform.position.y;

        // ── 占用格を计算（编辑模式 / 运行模式 兼容）──────────────────
        // 运行时直接用 _occupiedCells；编辑时从 Transform + config 临时计算
        List<Vector2Int> drawCells;

        if (Application.isPlaying && _occupiedCells != null && _occupiedCells.Count > 0)
        {
            drawCells = _occupiedCells;
        }
        else
        {
            // 编辑模式：根据当前 Transform 和 config 计算（不依赖 Start()）
            drawCells = new List<Vector2Int>();
            Vector2Int anchor = GridManager.Instance.WorldToGrid(transform.position);
            int w = Mathf.Max(1, data.gridWidth);
            int d = Mathf.Max(1, data.gridDepth);
            for (int dx = 0; dx < w; dx++)
                for (int dz = 0; dz < d; dz++)
                    drawCells.Add(anchor + new Vector2Int(dx, dz));
        }

        if (drawCells.Count == 0) return;

        // ── 1. 占用格（青色实心）────────────────────────────────────
        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        foreach (var cell in drawCells)
        {
            Vector3 c = GridManager.Instance.GridToWorld(cell);
            c.y = y + 0.06f;
            Gizmos.DrawCube(c, new Vector3(cellSize * 0.88f, 0.06f, cellSize * 0.88f));
        }

        // ── 2. 交互范围格（黄色半透明）──────────────────────────────
        var occupiedSet  = new HashSet<Vector2Int>(drawCells);
        var interactZone = new HashSet<Vector2Int>();
        int range        = data.interactionRange;

        foreach (var cell in drawCells)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dz = -range; dz <= range; dz++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dz) > range) continue;
                    var nb = cell + new Vector2Int(dx, dz);
                    if (!occupiedSet.Contains(nb))
                        interactZone.Add(nb);
                }
            }
        }

        Gizmos.color = new Color(1f, 1f, 0f, 0.18f);
        foreach (var cell in interactZone)
        {
            Vector3 c = GridManager.Instance.GridToWorld(cell);
            c.y = y + 0.03f;
            Gizmos.DrawCube(c, new Vector3(cellSize * 0.92f, 0.03f, cellSize * 0.92f));
        }
    }
#endif
}
