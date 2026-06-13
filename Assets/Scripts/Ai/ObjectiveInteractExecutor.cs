using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ObjectiveInteractExecutor — 物体交互执行器（统一入口）
///
/// 同时支持 WorldItem 和 SceneItemInstance 两类物体：
///   WorldItem      → TriggerActivate / TriggerComplete / TriggerInterrupt
///   SceneItemInstance → TryInteract()（开关/门/设备等，统一走原有交互逻辑）
///
/// 交互模式由 Blackboard["objectiveInteractMode"]（ObjectiveInteractMode int）决定：
///   Destroy  — WorldItem: TriggerComplete；SceneItemInstance: TryInteract
///   Activate — WorldItem: TriggerActivate；SceneItemInstance: TryInteract（开启）
///   Repair   — 同 Activate，额外前置：item 必须处于 Interrupted / !IsOpen 状态
///
/// 移动逻辑：
///   不在 targetZoneId 内 → yield break，BT 回落到 MoveToZone
///   在 zone 但不在交互范围内 → 写 targetGridPosition，BT 命中 MoveToPosition 靠近
///   在交互范围内 → 执行交互
/// </summary>
public class ObjectiveInteractExecutor : IActionExecutor
{
    private Transform    owner;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner        = owner;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        // ── 1. 读取目标物体 ID ────────────────────────────────────────────
        if (!context.Blackboard.TryGetValue("targetObjectId", out var objIdObj) ||
            string.IsNullOrEmpty(objIdObj?.ToString()))
        {
            Debug.LogWarning("[ObjectiveInteractExecutor] Blackboard 里没有 targetObjectId，跳过");
            yield break;
        }
        string targetObjectId = objIdObj.ToString();
        Debug.Log($"[ObjInteract:{owner.name}] 开始执行，targetObjectId={targetObjectId}");

        // ── 2. 检查是否在目标区域内（不在则回落到 MoveToZone）───────────
        if (context.Blackboard.TryGetValue("targetZoneId", out var zoneIdObj) &&
            !string.IsNullOrEmpty(zoneIdObj?.ToString()))
        {
            string zoneId = zoneIdObj.ToString();
            if (ZoneManager.Instance != null &&
                !ZoneManager.Instance.IsUnitInZone(
                    zoneId, unitMovement.CurrentGridPosition, unitMovement.CurrentFloor))
            {
                Debug.Log($"[ObjInteract:{owner.name}] 不在 zone [{zoneId}]，yield break");
                yield break;
            }
        }

        // ── 3. 读取交互模式 ───────────────────────────────────────────────
        var mode = ObjectiveInteractMode.Destroy;
        if (context.Blackboard.TryGetValue("objectiveInteractMode", out var modeObj) &&
            modeObj is int modeInt)
            mode = (ObjectiveInteractMode)modeInt;
        Debug.Log($"[ObjInteract:{owner.name}] mode={mode}");

        // ── 4. 查找物体（WorldItem 优先，找不到再查 SceneItemInstance）────
        WorldItem      worldItem  = FindWorldItemById(targetObjectId);
        SceneItemInstance sceneItem = worldItem == null ? FindSceneItemById(targetObjectId) : null;

        Debug.Log($"[ObjInteract:{owner.name}] worldItem={worldItem}, sceneItem={sceneItem}");

        if (worldItem == null && sceneItem == null)
        {
            Debug.LogWarning($"[ObjInteract:{owner.name}] 找不到物体 [{targetObjectId}]，yield break");
            yield break;
        }

        // ── 5. 判断交互范围，不够近则本回合直接移动靠近 ────────────────
        Vector2Int aiCell = unitMovement.CurrentGridPosition;
        bool inRange = false;

        if (worldItem != null)
        {
            inRange = worldItem.IsInPickupRange(owner.position);
            Debug.Log($"[ObjInteract:{owner.name}] WorldItem inRange={inRange}");
            if (!inRange)
            {
                Vector2Int itemCell = new Vector2Int(
                    Mathf.RoundToInt(worldItem.transform.position.x),
                    Mathf.RoundToInt(worldItem.transform.position.z));
                Debug.Log($"[ObjInteract:{owner.name}] 移动到 WorldItem 位置 {itemCell}");
                yield return MoveToCell(itemCell, unitMovement.CurrentFloor);
                yield break;
            }
        }
        else // SceneItemInstance
        {
            int dist = sceneItem.MinGridDistanceTo(aiCell);
            inRange = dist <= 1;
            Debug.Log($"[ObjInteract:{owner.name}] SceneItem dist={dist} inRange={inRange}，aiCell={aiCell}");
            if (!inRange)
            {
                Vector2Int nearestAdj = FindNearestAdjacentCell(sceneItem, aiCell);
                Debug.Log($"[ObjInteract:{owner.name}] 移动到相邻格 {nearestAdj}（from={aiCell}）");
                if (nearestAdj == aiCell) { Debug.LogWarning($"[ObjInteract:{owner.name}] 周围无可走格，放弃"); yield break; }
                yield return MoveToCell(nearestAdj, unitMovement.CurrentFloor);
                yield break;
            }
        }

        // ── 6. 前置状态检查 ───────────────────────────────────────────────
        if (worldItem != null)
        {
            switch (mode)
            {
                case ObjectiveInteractMode.Destroy:
                    if (worldItem.CurrentState != WorldItemState.Active)
                    {
                        Debug.Log($"[ObjectiveInteractExecutor] Destroy：[{targetObjectId}] 状态 {worldItem.CurrentState}，跳过");
                        yield break;
                    }
                    break;

                case ObjectiveInteractMode.Activate:
                    if (worldItem.CurrentState == WorldItemState.Active)
                    {
                        Debug.Log($"[ObjectiveInteractExecutor] Activate：[{targetObjectId}] 已为 Active，确认结束");
                        owner.GetComponent<TurnBasedUnit>()?.ConsumeAP(1);
                        yield break;
                    }
                    if (worldItem.CurrentState != WorldItemState.Inactive)
                        yield break;
                    break;

                case ObjectiveInteractMode.Repair:
                    if (worldItem.CurrentState != WorldItemState.Interrupted)
                        yield break;
                    break;
            }
        }
        else // SceneItemInstance
        {
            switch (mode)
            {
                case ObjectiveInteractMode.Activate:
                    if (sceneItem.IsOpen)
                    {
                        Debug.Log($"[ObjectiveInteractExecutor] Activate：[{targetObjectId}] 已为 Open，确认结束");
                        owner.GetComponent<TurnBasedUnit>()?.ConsumeAP(1);
                        yield break;
                    }
                    break;

                case ObjectiveInteractMode.Repair:
                    // Repair 只在物体关闭（被玩家关掉）时执行
                    if (sceneItem.IsOpen)
                        yield break;
                    break;

                case ObjectiveInteractMode.Destroy:
                    if (sceneItem.IsDestroyed)
                    {
                        Debug.Log($"[ObjectiveInteractExecutor] Destroy：[{targetObjectId}] 已销毁，跳过");
                        yield break;
                    }
                    break;
            }
        }

        // ── 7. 执行交互 ──────────────────────────────────────────────────
        Debug.Log($"[{owner.name}] ObjectiveInteract [{mode}]: 与 [{targetObjectId}] 交互");

        float elapsed = 0f;
        while (elapsed < 0.3f) { elapsed += Time.deltaTime; yield return null; }

        if (worldItem != null)
        {
            switch (mode)
            {
                case ObjectiveInteractMode.Destroy:
                    worldItem.TriggerComplete(owner.gameObject);
                    break;
                case ObjectiveInteractMode.Activate:
                case ObjectiveInteractMode.Repair:
                    worldItem.TriggerActivate(owner.gameObject);
                    break;
            }
        }
        else
        {
            // SceneItemInstance 统一走 TryInteract，内部逻辑处理开关/AP 消耗
            sceneItem.TryInteract(owner.gameObject);
        }

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        if (worldItem != null && turnUnit != null)
            turnUnit.ConsumeAP(1); // SceneItemInstance.TryInteract 内部已消耗 AP，不重复

        Debug.Log($"[{owner.name}] ObjectiveInteract 完成");
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    private IEnumerator MoveToCell(Vector2Int targetCell, int floor)
    {
        Vector2Int cur = unitMovement.CurrentGridPosition;
        if (cur == targetCell) yield break;

        if (GridManager.Instance.IsOccupied(targetCell, floor))
            targetCell = FindNearestAdjacentCellFromPoint(targetCell, cur, floor);
        if (cur == targetCell) yield break;

        var path = PathfindingService.FindPath(cur, targetCell, floor);
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[ObjectiveInteractExecutor] 无法寻路到 {targetCell}");
            yield break;
        }

        var turnUnit = owner.GetComponent<TurnBasedUnit>();
        int steps = Mathf.Min(
            turnUnit != null ? turnUnit.RemainingActionPoints : path.Count,
            path.Count);

        unitMovement.MoveToGrid(path[steps - 1], floor, steps);
        float deadline = Time.time + 10f;
        while (unitMovement.IsMoving && Time.time < deadline) yield return null;
    }

    private Vector2Int FindNearestAdjacentCellFromPoint(Vector2Int target, Vector2Int from, int floor)
    {
        Vector2Int[] dirs = {
            new Vector2Int(0,1), new Vector2Int(1,0),
            new Vector2Int(0,-1), new Vector2Int(-1,0)
        };
        Vector2Int best = from;
        float bestDist = float.MaxValue;
        foreach (var d in dirs)
        {
            Vector2Int adj = target + d;
            if (!GridManager.Instance.IsWalkable(adj, floor)) continue;
            if (GridManager.Instance.IsOccupied(adj, floor)) continue;
            float dist = Vector2Int.Distance(adj, from);
            if (dist < bestDist) { bestDist = dist; best = adj; }
        }
        return best;
    }

    /// <summary>找 SceneItem 占用格周围最近的可走相邻格</summary>
    private Vector2Int FindNearestAdjacentCell(SceneItemInstance item, Vector2Int from)
    {
        Vector2Int best = from;
        float bestDist  = float.MaxValue;
        int floor       = unitMovement.CurrentFloor;

        Vector2Int[] dirs = {
            new Vector2Int(0,1), new Vector2Int(1,0),
            new Vector2Int(0,-1), new Vector2Int(-1,0)
        };

        foreach (var cell in item.OccupiedCells)
        {
            foreach (var d in dirs)
            {
                Vector2Int adj = cell + d;
                if (!GridManager.Instance.IsWalkable(adj, floor)) continue;
                if (GridManager.Instance.IsOccupied(adj, floor))  continue;
                float dist = Vector2Int.Distance(adj, from);
                if (dist < bestDist) { bestDist = dist; best = adj; }
            }
        }
        return best;
    }

    private static WorldItem FindWorldItemById(string objectId)
    {
        foreach (var item in Object.FindObjectsOfType<WorldItem>())
            if (string.Equals(item.ObjectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }

    private static SceneItemInstance FindSceneItemById(string objectId)
    {
        foreach (var item in Object.FindObjectsOfType<SceneItemInstance>())
            if (item.Data != null &&
                string.Equals(item.Data.sceneObjectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }
}
