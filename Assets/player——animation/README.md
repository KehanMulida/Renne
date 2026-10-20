# 玩家模型与动画

当前 Blender 会话导出的角色，74 根变形骨骼，包含领带骨骼及烘焙摆动。原 Blender 文件及其关键帧未修改。

- `Models/Rene_Player.fbx`：角色模型。
- `SourceClips/`：24 段完整骨骼 FBX 动画，排除未完成的击倒和领带测试片段。
- `Animations/`：24 段独立 `.anim`，另有从行走首帧提取的站立、下蹲和匍匐静止姿势。
- `Player.controller`：当前接入的 Animator Controller。
- `Rene_PlayerVisual.prefab`：可复用的角色显示预制体。

已接入 `Assets/Scenes/Test.unity` 中原来的 Player，并更新原 Player prefab。保留玩家根对象、网格移动、输入、碰撞和其他玩法组件；移除根对象的胶囊 MeshFilter 和 MeshRenderer，根对象作为空容器，由角色模型负责显示；CapsuleCollider 继续保留。模型采用 Generic 骨架，Root Motion 关闭，位置继续由 UnitMovement 管理。

## 参数与调用

当前移动按网格校准：`MoveSpeed=1.1` 世界单位／秒；站立行走速度 ×1.2，下蹲速度 ×0.75，匍匐速度 ×0.6。每走一格，`GaitPhase` 从 0 到 1 驱动一整段左右步态，触发两次脚步。1 米网格的纯位移时间分别约为 0.76、1.21、1.52 秒；姿态转换会先播放完再开始位移。站立／下蹲／匍匐的有效动画速度约为原速 2.82／2.2／2.64 倍。角色显示高度运行时对齐实际 Ground 地面，不改变网格根坐标。

角色使用原始彩色贴图作为中性补色贴图，避免低照明下统一变成灰色轮廓，同时保留场景整体的暗度。

| 参数 | 用途 |
| --- | --- |
| `Stance` (int) | 0 站立、1 下蹲、2 匍匐 |
| `IsMoving` (bool) | 当前姿态下的移动／静止切换 |
| `GaitPhase` (float) | 每格一个完整步态循环，0→1 |
| `Throw` (trigger) | 站立投掷 |
| `Pickup` (trigger) | 站立或下蹲拿取 |

PlayerAnimationDriver 读取现有 PlayerController.CurrentStance 和 UnitMovement.IsMoving。原有 C / Z 切姿态输入保持有效。站立到匍匐经过下蹲，反向亦然。站立到下蹲采用当前编辑的 `Rene_Stand_To_Crouch_15F`；其余过渡和备用动画保留为独立资源。

EquipmentManager 成功投掷后发出 OnItemThrown，驱动投掷动画。WorldItem.Pickup 成功后触发拿取。匍匐时拿取会先撑起到下蹲，完成后回到匍匐；蹲伏／匍匐投掷会先站起并在动作后返回原姿态。

本次接入保持原有物品入包、扣 AP 和抛射物发射时机；这些玩法效果仍立即执行，尚未改为由手部接触／松手帧延迟执行。动画导出按 30 fps 烘焙约束求解结果，Blender 中原有每 15 帧关键帧保持不变。当前过渡沿用用户编辑的姿态，包括其中的贴地高度。

Tools > Renne > Player Animation 提供导入连接与验证菜单。验证报告位于项目 `Artifacts/PlayerAnimation/`。重新导入连接会重建本文件夹的导入动画与控制器；在 Unity 手动编辑它们后，请先备份再执行该菜单。

行走循环已对首尾各 0.2 秒做姿势及切线平滑，保留首帧和中段动作；原片段备份于 Artifacts/PlayerAnimation/Before_WalkLoop.anim。逐格移动会连续传递剩余帧时间，避免每格边界停顿。可用 Tools > Renne > Player Animation > Smooth Walk Loop Seam 重新处理行走循环。
