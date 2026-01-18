using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 楼层可见性模式
/// </summary>
public enum FloorVisibilityMode
{
    ShowAll,                // 显示所有楼层
    ShowCurrentAndBelow,    // 显示当前楼层及以下（僵尸毁灭工程模式）
    ShowCurrentOnly,        // 只显示当前楼层
    ShowCurrentAndAbove,    // 显示当前楼层及以上
    Manual                  // 手动控制
}

/// <summary>
/// 楼层对象数据
/// 存储某个楼层的所有可视化对象
/// </summary>
public class FloorObjects
{
    public int floor;                           // 楼层编号
    public List<GameObject> staticObjects;      // 静态物体（地面、墙壁等）
    public List<Renderer> renderers;            // 所有渲染器
    public float currentAlpha = 1f;             // 当前透明度
    public bool isVisible = true;               // 是否可见

    public FloorObjects(int floorNum)
    {
        floor = floorNum;
        staticObjects = new List<GameObject>();
        renderers = new List<Renderer>();
    }
}

/// <summary>
/// 楼层可见性控制器
/// 职责：
/// 1. 根据玩家所在楼层自动隐藏/显示楼层
/// 2. 支持渐变淡入淡出效果
/// 3. 支持半透明显示上层楼板
/// 4. 提供多种可见性模式
/// 特点：
/// - 低耦合：通过监听玩家楼层变化事件
/// - 高性能：缓存渲染器，批量处理
/// - 灵活配置：多种模式和参数
/// 使用场景：
/// - 僵尸毁灭工程式的俯视角游戏
/// - 多楼层建筑内部
/// - 需要清晰视野的策略游戏
/// </summary>
public class FloorVisibilityController : MonoBehaviour
{
    public static FloorVisibilityController Instance { get; private set; }

    [Header("目标跟踪")]
    [SerializeField] private UnitMovement trackedUnit;              // 跟踪的单位（通常是玩家）
    [SerializeField] private bool autoFindPlayer = true;            // 自动查找玩家

    [Header("可见性模式")]
    [SerializeField] private FloorVisibilityMode visibilityMode = FloorVisibilityMode.ShowCurrentAndBelow;
    [SerializeField] private bool smoothTransition = true;          // 平滑过渡
    [SerializeField] private float transitionSpeed = 5f;            // 过渡速度

    [Header("透明度设置")]
    [SerializeField] private float hiddenAlpha = 0f;                // 隐藏时的透明度（0=完全透明）
    [SerializeField] private float visibleAlpha = 1f;               // 显示时的透明度
    [SerializeField] private float aboveFloorAlpha = 0.3f;          // 上方楼层的半透明度

    [Header("高级选项")]
    [SerializeField] private bool hideAboveFloorUnits = true;       // 隐藏上方楼层的单位
    [SerializeField] private bool useShaderTransparency = true;     // 使用Shader透明度（性能更好）
    
    [Header("手动楼层切换")]
    [SerializeField] private KeyCode showUpperFloorKey = KeyCode.PageUp;    // 显示上一层
    [SerializeField] private KeyCode showLowerFloorKey = KeyCode.PageDown;  // 显示下一层
    [SerializeField] private KeyCode resetViewKey = KeyCode.Home;           // 重置视图

    [Header("调试")]
    [SerializeField] private bool enableDebugLog = false;

    // 运行时数据
    private Dictionary<int, FloorObjects> floorObjectsMap;
    private int currentVisibleFloor = 0;        // 当前可见的主楼层
    private int lastTrackedFloor = -1;          // 上次跟踪的楼层

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            floorObjectsMap = new Dictionary<int, FloorObjects>();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 自动查找玩家
        if (autoFindPlayer && trackedUnit == null)
        {
            TurnBasedUnit[] units = FindObjectsOfType<TurnBasedUnit>();
            foreach (var unit in units)
            {
                if (unit.Faction == TurnFaction.Player)
                {
                    trackedUnit = unit.GetComponent<UnitMovement>();
                    break;
                }
            }
        }

        if (trackedUnit == null)
        {
            Debug.LogWarning("[FloorVisibility] No tracked unit found!");
        }
        else
        {
            // 订阅楼层变化事件
            trackedUnit.OnFloorChanged += OnTrackedUnitFloorChanged;
            currentVisibleFloor = trackedUnit.CurrentFloor;
        }

        // 扫描并注册所有楼层物体
        ScanAndRegisterFloorObjects();

        // 初始化可见性
        UpdateFloorVisibility(currentVisibleFloor);

        Debug.Log($"[FloorVisibility] Initialized, mode: {visibilityMode}");
    }

    void Update()
    {
        // 平滑过渡透明度
        if (smoothTransition)
        {
            UpdateTransitions();
        }

        // 手动楼层切换
        HandleManualFloorSwitch();
    }

    void OnDestroy()
    {
        if (trackedUnit != null)
        {
            trackedUnit.OnFloorChanged -= OnTrackedUnitFloorChanged;
        }
    }

    // ============ 楼层扫描与注册 ============

    /// <summary>
    /// 扫描场景中的所有楼层物体并注册
    /// </summary>
    private void ScanAndRegisterFloorObjects()
    {
        if (FloorManager.Instance == null)
        {
            Debug.LogWarning("[FloorVisibility] FloorManager not found, cannot scan floors");
            return;
        }

        int numberOfFloors = FloorManager.Instance.NumberOfFloors;
        float floorHeight = FloorManager.Instance.FloorHeight;

        // 查找所有带有Renderer的物体
        Renderer[] allRenderers = FindObjectsOfType<Renderer>();

        foreach (Renderer renderer in allRenderers)
        {
            // 跳过角色（通常有特殊处理）
            //if (renderer.GetComponent<UnitMovement>() != null && hideAboveFloorUnits)
            //    continue;

            // 根据Y坐标判断属于哪个楼层
            float objectY = renderer.transform.position.y;
            int floor = FloorManager.Instance.GetFloorFromWorldY(objectY);

            // 注册到对应楼层
            RegisterObjectToFloor(renderer.gameObject, floor);
        }

        DebugLog($"Scanned {allRenderers.Length} renderers across {floorObjectsMap.Count} floors");
    }

    /// <summary>
    /// 手动注册物体到指定楼层
    /// </summary>
    public void RegisterObjectToFloor(GameObject obj, int floor)
    {
        if (!floorObjectsMap.ContainsKey(floor))
        {
            floorObjectsMap[floor] = new FloorObjects(floor);
        }

        FloorObjects floorData = floorObjectsMap[floor];
        
        if (!floorData.staticObjects.Contains(obj))
        {
            floorData.staticObjects.Add(obj);
            
            // 收集所有Renderer
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!floorData.renderers.Contains(renderer))
                {
                    floorData.renderers.Add(renderer);
                }
            }
        }
    }

    /// <summary>
    /// 手动取消注册物体
    /// </summary>
    public void UnregisterObjectFromFloor(GameObject obj, int floor)
    {
        if (floorObjectsMap.ContainsKey(floor))
        {
            FloorObjects floorData = floorObjectsMap[floor];
            floorData.staticObjects.Remove(obj);
            
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                floorData.renderers.Remove(renderer);
            }
        }
    }

    // ============ 可见性控制 ============

    /// <summary>
    /// 跟踪单位楼层变化事件
    /// </summary>
    private void OnTrackedUnitFloorChanged(int newFloor)
    {
        DebugLog($"Tracked unit changed to floor {newFloor}");
        UpdateFloorVisibility(newFloor);
    }

    /// <summary>
    /// 更新楼层可见性
    /// </summary>
    public void UpdateFloorVisibility(int centerFloor)
    {
        currentVisibleFloor = centerFloor;

        foreach (var floorData in floorObjectsMap.Values)
        {
            bool shouldBeVisible = ShouldFloorBeVisible(floorData.floor, centerFloor);
            float targetAlpha = CalculateTargetAlpha(floorData.floor, centerFloor);

            if (smoothTransition)
            {
                // 平滑过渡到目标透明度
                floorData.isVisible = shouldBeVisible;
                // currentAlpha会在Update中平滑更新
            }
            else
            {
                // 立即设置
                SetFloorAlpha(floorData, targetAlpha);
                floorData.isVisible = shouldBeVisible;
            }
        }

        DebugLog($"Updated visibility for floor {centerFloor}");
    }

    /// <summary>
    /// 判断楼层是否应该可见
    /// </summary>
    private bool ShouldFloorBeVisible(int floor, int centerFloor)
    {
        switch (visibilityMode)
        {
            case FloorVisibilityMode.ShowAll:
                return true;

            case FloorVisibilityMode.ShowCurrentAndBelow:
                return floor <= centerFloor;

            case FloorVisibilityMode.ShowCurrentOnly:
                return floor == centerFloor;

            case FloorVisibilityMode.ShowCurrentAndAbove:
                return floor >= centerFloor;

            case FloorVisibilityMode.Manual:
                return true; // 手动模式不自动隐藏

            default:
                return true;
        }
    }

    /// <summary>
    /// 计算目标透明度
    /// </summary>
    private float CalculateTargetAlpha(int floor, int centerFloor)
    {
        if (floor < centerFloor)
        {
            // 下方楼层：完全显示
            return visibleAlpha;
        }
        else if (floor == centerFloor)
        {
            // 当前楼层：完全显示
            return visibleAlpha;
        }
        else if (floor == centerFloor + 1)
        {
            // 上方一层：半透明显示（可选）
            return aboveFloorAlpha;
        }
        else
        {
            // 更高楼层：完全隐藏
            return hiddenAlpha;
        }
    }

    /// <summary>
    /// 设置楼层透明度
    /// </summary>
    private void SetFloorAlpha(FloorObjects floorData, float alpha)
    {
        floorData.currentAlpha = alpha;

        foreach (Renderer renderer in floorData.renderers)
        {
            if (renderer == null) continue;

            if (useShaderTransparency)
            {
                // 使用Shader透明度（性能更好）
                SetRendererAlpha(renderer, alpha);
            }
            else
            {
                // 直接启用/禁用渲染器
                renderer.enabled = alpha > 0.01f;
            }
        }
    }

    /// <summary>
    /// 设置渲染器的透明度
    /// </summary>
    private void SetRendererAlpha(Renderer renderer, float alpha)
    {
        // 获取材质（使用sharedMaterial避免实例化）
        Material[] materials = renderer.materials;

        foreach (Material mat in materials)
        {
            // 启用透明度
            if (alpha < 0.99f)
            {
                SetMaterialTransparent(mat);
            }
            else
            {
                SetMaterialOpaque(mat);
            }

            // 设置颜色透明度
            Color color = mat.color;
            color.a = alpha;
            mat.color = color;
        }
    }

    /// <summary>
    /// 设置材质为透明模式
    /// </summary>
    private void SetMaterialTransparent(Material mat)
    {
        mat.SetFloat("_Mode", 3); // Transparent mode
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }

    /// <summary>
    /// 设置材质为不透明模式
    /// </summary>
    private void SetMaterialOpaque(Material mat)
    {
        mat.SetFloat("_Mode", 0); // Opaque mode
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1;
    }

    /// <summary>
    /// 平滑更新透明度过渡
    /// </summary>
    private void UpdateTransitions()
    {
        foreach (var floorData in floorObjectsMap.Values)
        {
            float targetAlpha = CalculateTargetAlpha(floorData.floor, currentVisibleFloor);
            
            if (Mathf.Abs(floorData.currentAlpha - targetAlpha) > 0.01f)
            {
                floorData.currentAlpha = Mathf.Lerp(
                    floorData.currentAlpha, 
                    targetAlpha, 
                    transitionSpeed * Time.deltaTime
                );

                SetFloorAlpha(floorData, floorData.currentAlpha);
            }
        }
    }

    // ============ 手动控制 ============

    /// <summary>
    /// 处理手动楼层切换
    /// </summary>
    private void HandleManualFloorSwitch()
    {
        if (visibilityMode != FloorVisibilityMode.Manual)
            return;

        if (Input.GetKeyDown(showUpperFloorKey))
        {
            ShowFloor(currentVisibleFloor + 1);
        }

        if (Input.GetKeyDown(showLowerFloorKey))
        {
            ShowFloor(currentVisibleFloor - 1);
        }

        if (Input.GetKeyDown(resetViewKey))
        {
            if (trackedUnit != null)
            {
                UpdateFloorVisibility(trackedUnit.CurrentFloor);
            }
        }
    }

    /// <summary>
    /// 手动显示指定楼层
    /// </summary>
    public void ShowFloor(int floor)
    {
        if (FloorManager.Instance != null && 
            floor >= 0 && 
            floor < FloorManager.Instance.NumberOfFloors)
        {
            currentVisibleFloor = floor;
            UpdateFloorVisibility(floor);
            DebugLog($"Manually showing floor {floor}");
        }
    }

    /// <summary>
    /// 强制刷新所有楼层可见性
    /// </summary>
    public void RefreshVisibility()
    {
        ScanAndRegisterFloorObjects();
        
        if (trackedUnit != null)
        {
            UpdateFloorVisibility(trackedUnit.CurrentFloor);
        }
        else
        {
            UpdateFloorVisibility(currentVisibleFloor);
        }
    }

    /// <summary>
    /// 设置可见性模式
    /// </summary>
    public void SetVisibilityMode(FloorVisibilityMode mode)
    {
        visibilityMode = mode;
        
        if (trackedUnit != null)
        {
            UpdateFloorVisibility(trackedUnit.CurrentFloor);
        }
    }

    // ============ 工具方法 ============

    /// <summary>
    /// 调试日志
    /// </summary>
    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[FloorVisibility] {message}");
        }
    }

    // ============ 调试可视化 ============

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;

        string info = $"[Floor Visibility]\n" +
                     $"Mode: {visibilityMode}\n" +
                     $"Current Floor: {currentVisibleFloor}\n" +
                     $"Tracked Floor: {(trackedUnit != null ? trackedUnit.CurrentFloor.ToString() : "N/A")}\n";

        if (visibilityMode == FloorVisibilityMode.Manual)
        {
            info += $"\n{showUpperFloorKey}: Up\n{showLowerFloorKey}: Down\n{resetViewKey}: Reset";
        }

        GUI.Box(new Rect(Screen.width - 180, 500, 170, 140), info, style);
    }
}