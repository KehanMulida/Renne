using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 系统诊断工具
/// 用途：诊断为什么右键点击没反应
/// 使用：创建空物体，挂载此脚本，按Play查看Console
/// </summary>
public class SystemDiagnostic : MonoBehaviour
{
    [Header("引用（自动查找）")]
    public GridManager gridManager;
    public TurnSystem turnSystem;
    public UnitMovement playerUnit;
    public TurnBasedUnit turnBasedUnit;
    public PlayerInputController inputController;
    public Camera mainCamera;
    public GameObject ground;

    [Header("测试设置")]
    public LayerMask groundLayer;
    public bool continuousCheck = true;

    private bool hasChecked = false;

    void Start()
    {
        // 自动查找组件
        gridManager = GridManager.Instance;
        turnSystem = TurnSystem.Instance;
        playerUnit = FindObjectOfType<UnitMovement>();
        turnBasedUnit = FindObjectOfType<TurnBasedUnit>();
        inputController = FindObjectOfType<PlayerInputController>();
        mainCamera = Camera.main;
        ground = GameObject.Find("Ground");

        Invoke(nameof(RunDiagnostic), 1f); // 延迟1秒等待初始化
    }

    void Update()
    {
        if (continuousCheck && Input.GetMouseButtonDown(1))
        {
            Debug.Log("====== RIGHT CLICK DETECTED ======");
            CheckMouseRaycast();
        }
    }

    void RunDiagnostic()
    {
        Debug.Log("====================================");
        Debug.Log("===== SYSTEM DIAGNOSTIC START =====");
        Debug.Log("====================================");

        CheckComponents();
        CheckLayers();
        CheckTurnSystem();
        CheckPlayerUnit();
        CheckInputController();
        CheckMouseRaycast();

        Debug.Log("====================================");
        Debug.Log("===== DIAGNOSTIC COMPLETE =====");
        Debug.Log("====================================");
    }

    void CheckComponents()
    {
        Debug.Log("\n----- 1. COMPONENT CHECK -----");
        Debug.Log($"GridManager: {(gridManager != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"TurnSystem: {(turnSystem != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"PlayerUnit: {(playerUnit != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"TurnBasedUnit: {(turnBasedUnit != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"InputController: {(inputController != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"MainCamera: {(mainCamera != null ? "✓ Found" : "✗ MISSING")}");
        Debug.Log($"Ground: {(ground != null ? "✓ Found" : "✗ MISSING")}");
    }

    void CheckLayers()
    {
        Debug.Log("\n----- 2. LAYER CHECK -----");
        
        if (ground != null)
        {
            int groundLayerValue = ground.layer;
            string groundLayerName = LayerMask.LayerToName(groundLayerValue);
            Debug.Log($"Ground Layer: {groundLayerValue} ({groundLayerName})");
            Debug.Log($"Ground has Collider: {(ground.GetComponent<Collider>() != null ? "✓ Yes" : "✗ NO")}");
        }

        Debug.Log($"GroundLayer mask value: {groundLayer.value}");
        Debug.Log($"GroundLayer includes Ground: {((groundLayer.value & (1 << LayerMask.NameToLayer("Ground"))) != 0 ? "✓ Yes" : "✗ NO")}");
    }

    void CheckTurnSystem()
    {
        Debug.Log("\n----- 3. TURN SYSTEM CHECK -----");
        
        if (turnSystem != null)
        {
            Debug.Log($"System Active: {turnSystem.IsSystemActive}");
            Debug.Log($"Current Faction: {turnSystem.CurrentFaction}");
            Debug.Log($"Current Phase: {turnSystem.CurrentPhase}");
            Debug.Log($"Global Turn: {turnSystem.GlobalTurnNumber}");
            Debug.Log($"Player Turns: {turnSystem.GetFactionTurnNumber(TurnFaction.Player)}");
            Debug.Log($"Is Player Turn: {turnSystem.IsCurrentFaction(TurnFaction.Player)}");
        }
    }

    void CheckPlayerUnit()
    {
        Debug.Log("\n----- 4. PLAYER UNIT CHECK -----");
        
        if (playerUnit != null)
        {
            Debug.Log($"Current Position: {playerUnit.CurrentGridPosition}");
            Debug.Log($"World Position: {playerUnit.transform.position}");
            Debug.Log($"Is Moving: {playerUnit.IsMoving}");
            Debug.Log($"Move Range: {playerUnit.MoveRange}");

            var range = playerUnit.GetMovementRange();
            Debug.Log($"Movement Range Count: {range?.Count ?? 0}");
            
            if (range != null && range.Count > 0)
            {
                Debug.Log($"Sample reachable cells: {string.Join(", ", System.Linq.Enumerable.Take(range, 5))}");
            }
        }

        if (turnBasedUnit != null)
        {
            Debug.Log($"Faction: {turnBasedUnit.Faction}");
            Debug.Log($"Is My Turn: {turnBasedUnit.IsMyTurn}");
            Debug.Log($"Can Act: {turnBasedUnit.CanAct}");
            Debug.Log($"Has Acted: {turnBasedUnit.HasActedThisTurn}");
            Debug.Log($"Action Points: {turnBasedUnit.RemainingActionPoints}");
        }
    }

    void CheckInputController()
    {
        Debug.Log("\n----- 5. INPUT CONTROLLER CHECK -----");
        
        if (inputController != null)
        {
            // 通过反射获取私有字段
            var type = inputController.GetType();
            var isEnabledField = type.GetField("isInputEnabled", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var rangeField = type.GetField("currentMovementRange", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (isEnabledField != null)
            {
                bool isEnabled = (bool)isEnabledField.GetValue(inputController);
                Debug.Log($"Input Enabled: {isEnabled}");
            }

            if (rangeField != null)
            {
                var range = rangeField.GetValue(inputController) as HashSet<Vector2Int>;
                Debug.Log($"Cached Range: {(range != null ? $"{range.Count} cells" : "NULL")}");
            }
        }
    }

    void CheckMouseRaycast()
    {
        Debug.Log("\n----- 6. MOUSE RAYCAST CHECK -----");
        
        if (mainCamera == null)
        {
            Debug.LogError("No camera!");
            return;
        }

        Vector3 mousePos = Input.mousePosition;
        Debug.Log($"Mouse Position: {mousePos}");

        Ray ray = mainCamera.ScreenPointToRay(mousePos);
        Debug.Log($"Ray Origin: {ray.origin}, Direction: {ray.direction}");

        // 测试无LayerMask的射线
        RaycastHit hit1;
        bool hitAny = Physics.Raycast(ray, out hit1, Mathf.Infinity);
        Debug.Log($"Raycast (no mask): {(hitAny ? $"✓ Hit {hit1.collider.name} at {hit1.point}" : "✗ No hit")}");

        // 测试带LayerMask的射线
        RaycastHit hit2;
        bool hitGround = Physics.Raycast(ray, out hit2, Mathf.Infinity, groundLayer);
        Debug.Log($"Raycast (with mask): {(hitGround ? $"✓ Hit {hit2.collider.name} at {hit2.point}" : "✗ No hit")}");

        if (hitGround && gridManager != null)
        {
            Vector2Int gridPos = gridManager.WorldToGrid(hit2.point);
            Debug.Log($"Grid Position: {gridPos}");
            Debug.Log($"Is Valid: {gridManager.IsValid(gridPos)}");
            Debug.Log($"Is Walkable: {gridManager.IsWalkable(gridPos)}");

            if (playerUnit != null)
            {
                bool canMove = playerUnit.CanMoveTo(gridPos);
                Debug.Log($"Can Move To: {canMove}");
            }
        }
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.yellow;
        style.alignment = TextAnchor.UpperLeft;

        string info = "[DIAGNOSTIC TOOL]\n" +
                     "Right click anywhere\n" +
                     "Check Console for details\n\n";

        if (turnSystem != null)
        {
            info += $"Turn: {turnSystem.CurrentFaction}\n";
            info += $"Input: {(turnBasedUnit != null && turnBasedUnit.CanAct ? "Enabled" : "Disabled")}\n";
        }

        GUI.Box(new Rect(Screen.width - 210, 10, 200, 120), info, style);
    }
}