using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 楼层连接标记器
/// 职责：
/// 1. 在Scene中可视化标记楼层连接点
/// 2. 在游戏启动时自动注册到FloorManager
/// 3. 方便关卡设计师放置楼梯、电梯等
/// 使用方法：
/// 1. 创建空物体放在楼梯位置
/// 2. 添加此组件
/// 3. 配置连接参数
/// 4. 自动注册到系统
/// </summary>
public class FloorConnectionMarker : MonoBehaviour
{
    [Header("连接配置")]
    [SerializeField] private int fromFloor = 0;                             // 起始楼层
    [SerializeField] private int toFloor = 1;                               // 目标楼层
    [SerializeField] private FloorConnectionType connectionType = FloorConnectionType.Stairs;
    [SerializeField] private bool isBidirectional = true;                   // 是否双向
    [SerializeField] private int moveCost = 1;                              // 移动消耗

    [Header("自动定位")]
    [SerializeField] private bool autoSnapToGrid = true;                    // 是否自动对齐到网格

    [Header("可视化")]
    [SerializeField] private bool showInGame = false;                       // 游戏中是否显示标记
    [SerializeField] private Color markerColor = Color.cyan;

    private Vector2Int gridPosition;
    private bool isRegistered = false;

    void Start()
    {
        // 自动对齐到网格
        if (autoSnapToGrid && GridManager.Instance != null)
        {
            gridPosition = GridManager.Instance.WorldToGrid(transform.position);
            Vector3 alignedPos = GridManager.Instance.GridToWorld(gridPosition);
            alignedPos.y = transform.position.y;  // 保持Y坐标
            transform.position = alignedPos;
        }
        else
        {
            gridPosition = GridManager.Instance.WorldToGrid(transform.position);
        }

        // 注册到FloorManager
        RegisterConnection();

        // 隐藏游戏中的标记（除非配置为显示）
        if (!showInGame)
        {
            //GetComponent<MeshRenderer>()?.enabled = false;
            this.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 注册连接到FloorManager
    /// </summary>
    private void RegisterConnection()
    {
        if (FloorManager.Instance == null)
        {
            Debug.LogError($"[FloorConnectionMarker] FloorManager not found! Cannot register connection.");
            return;
        }

        FloorManager.Instance.AddConnection(gridPosition, fromFloor, toFloor, connectionType);
        isRegistered = true;

        Debug.Log($"[FloorConnectionMarker] Registered {connectionType} at {gridPosition}: Floor {fromFloor} <-> {toFloor}");
    }

    // ============ 辅助功能 ============

    /// <summary>
    /// 创建可视化模型（可选）
    /// 在Scene中显示楼梯/电梯的3D模型
    /// </summary>
    public void CreateVisualModel()
    {
        // 根据连接类型创建不同的可视化
        GameObject visual = null;

        switch (connectionType)
        {
            case FloorConnectionType.Stairs:
                visual = CreateStairsVisual();
                break;
            case FloorConnectionType.Escalator:
                visual = CreateEscalatorVisual();
                break;
            case FloorConnectionType.Elevator:
                visual = CreateElevatorVisual();
                break;
        }

        if (visual != null)
        {
            visual.transform.SetParent(transform);
            visual.transform.localPosition = Vector3.zero;
        }
    }

    private GameObject CreateStairsVisual()
    {
        // 简单的楼梯可视化：一个倾斜的方块
        GameObject stairs = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stairs.name = "StairsVisual";
        
        float heightDiff = FloorManager.Instance.FloorHeight;
        stairs.transform.localScale = new Vector3(1f, 0.2f, heightDiff);
        stairs.transform.localRotation = Quaternion.Euler(-45, 0, 0);
        
        return stairs;
    }

    private GameObject CreateEscalatorVisual()
    {
        // 扶梯可视化
        GameObject escalator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        escalator.name = "EscalatorVisual";
        
        float heightDiff = FloorManager.Instance.FloorHeight;
        escalator.transform.localScale = new Vector3(1f, 0.1f, heightDiff);
        escalator.transform.localRotation = Quaternion.Euler(-30, 0, 0);
        
        // 可以添加材质表示移动方向
        return escalator;
    }

    private GameObject CreateElevatorVisual()
    {
        // 电梯可视化：简单的方块
        GameObject elevator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        elevator.name = "ElevatorVisual";
        elevator.transform.localScale = new Vector3(1.5f, 0.2f, 1.5f);
        
        return elevator;
    }

    // ============ 调试可视化 ============

    void OnDrawGizmos()
    {
        if (GridManager.Instance == null) return;

        Vector2Int pos = GridManager.Instance.WorldToGrid(transform.position);

        // 绘制连接点标记
        Gizmos.color = markerColor;
        
        if (FloorManager.Instance != null)
        {
            Vector3 fromPos = FloorManager.Instance.GridToWorld(pos, fromFloor);
            Vector3 toPos = FloorManager.Instance.GridToWorld(pos, toFloor);

            // 绘制起点
            Gizmos.DrawSphere(fromPos, 0.4f);
            
            // 绘制连接线
            Gizmos.color = new Color(markerColor.r, markerColor.g, markerColor.b, 0.5f);
            Gizmos.DrawLine(fromPos, toPos);
            
            // 绘制终点
            Gizmos.DrawWireSphere(toPos, 0.4f);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (GridManager.Instance == null) return;

        Vector2Int pos = GridManager.Instance.WorldToGrid(transform.position);
        
        // 绘制网格位置
        Gizmos.color = Color.yellow;
        Vector3 gridWorldPos = GridManager.Instance.GridToWorld(pos);
        gridWorldPos.y = transform.position.y;
        Gizmos.DrawWireCube(gridWorldPos, Vector3.one * GridManager.Instance.CellSize);

        // 显示连接类型文字
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up, 
            $"{connectionType}\n{fromFloor} → {toFloor}\nGrid: {pos}");
        #endif
    }
}