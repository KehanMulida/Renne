## Summary

- **SceneItemInstance unified interaction**: `ObjectiveInteractExecutor` extended to support both `WorldItem` and `SceneItemInstance` via `ObjectiveInteractMode` (Destroy/Activate/Repair) branching — same BT entry point for both types
- **New MissionBehaviourProfiles**: `ObjectiveActivate` (AI activates item at mission start) and `ObjectiveGuardWithPatrol` (guard with patrol); `ObjectiveGuard` changed from stationary defend to low-intensity patrol (weight 0.6)
- **AI moves toward item**: `ObjectiveInteractExecutor` embeds `MoveToCell()` coroutine — AI moves to adjacent cell within the same turn if out of range; new `MoveToPositionExecutor` registered as a general-purpose BT movement node
- **BT node ordering fix**: `Mission_MoveToPosition` node placed before `Mission_ObjectiveDestroy_Interact` so movement takes priority over interaction attempts
- **Blackboard key residue fix**: `ClearMissionBlackboard()` static method clears all mission-related keys at `ApplyTopContext` time, preventing cross-mission data pollution
- **ConditionEvaluator lookup order fix**: `StoryObjectState` check now goes WorldItem -> SceneItemInstance live state -> Registry; SceneItemInstance `IsOpen` maps to `Active`
- **MissionTypes extensions**: `WorldItemState.Inactive`, `ObjectActiveTurns` condition enum (ConditionCheck=28), `MissionContext.objectActiveTurns` runtime counter, `UpdateObjectActiveTurnCounters()` called per turn

## Changed Files

- `Assets/Scripts/Ai/ObjectiveInteractExecutor.cs` - complete rewrite
- `Assets/Scripts/Ai/MoveToPositionExecutor.cs` - new file
- `Assets/Scripts/Ai/EnemyAIController.cs` - register new executor, fix ApplyTopContext
- `Assets/Scripts/Items/WorldItem.cs` - add Inactive state + TriggerActivate
- `Assets/Scripts/SystemManager/MissionSystem/MissionTypes.cs` - new enums/fields/methods
- `Assets/Scripts/SystemManager/MissionSystem/MissionData.cs` - add interactMode field
- `Assets/Scripts/SystemManager/MissionSystem/MissionManager.cs` - objectActiveTurns counter
- `Assets/Scripts/SystemManager/MissionSystem/ConditionEvaluator.cs` - lookup order fix
- `Assets/BehaviourTree/EnemyBehaviorTree.json` - MoveToPosition node + ordering fix
- `Assets/Data/Mission/TakeCare 1.asset` - ObjectiveActivate mission config

## Test Plan

- [ ] AI assigned ObjectiveActivate mission moves to adjacent cell of SceneItem and triggers TryInteract; mission completes
- [ ] AI assigned ObjectiveGuard patrols the target zone at low intensity instead of standing still
- [ ] After mission switch, Blackboard has no residual keys (e.g. targetZoneId from prior mission does not affect new mission)
- [ ] ConditionEvaluator correctly recognizes SceneItemInstance IsOpen as Active state

Generated with [Claude Code](https://claude.com/claude-code)
