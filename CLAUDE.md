# Renne — Claude 工作指引

Unity **2022.3.50f1** 回合制潜行/战术游戏。主要代码在 `Assets/Scripts/`，
脚本大多不带 asmdef，统一编进 `Assembly-CSharp`。

---

## 要看运行时报错 / 验证改动是否生效，用 AutoTest

不要让用户手动点播放再复制 Console —— 项目里有现成的自动化插件。

```bash
# 跑 10 个回合，自动抓 Console，自动判断该走 batchmode 还是 Editor 桥
# ⚠ 必须用 .cmd 包装或 -ExecutionPolicy Bypass：本机 ExecutionPolicy 是 Restricted
Tools/autotest.cmd run -Turns 10 -Scene Test
```

跑完直接读这几个文件拿结论（不需要再问用户）：

| 文件 | 内容 |
|---|---|
| `Logs/AutoTest/latest/summary.json` | 结果摘要：回合数、error/exception 计数、`errorSamples` 错误明细 |
| `Logs/AutoTest/latest/errors.log` | 只有 Error / Exception，带堆栈 |
| `Logs/AutoTest/latest/turns.jsonl` | 每回合快照：任务进度、敌人位置/HP/当前任务、PlanProgress |
| `Logs/AutoTest/latest/console.log` | 全量 Console |

其他子命令：`errors` / `turns` / `summary` / `logs` / `status` / `compile` / `tests`。
完整说明见 [Tools/AUTOTEST.md](Tools/AUTOTEST.md)。

**退出码**：0 通过 / 1 有报错 / 2 超时 / 3 出错即停 / 4 场景没有 TurnSystem /
5 场景找不到 / 6 编译失败 / 7 编辑器开着导致 batchmode 不可用 /
8 PlayMode 被手动中断（`reason=application_quit`，数据仍从 summary.json 还原）。

几个要记住的点：
- 改完 C# 后想快速确认能不能编译：`powershell -File Tools/autotest.ps1 compile`
- 场景里必须有 `TurnSystem`，否则 `reason=no_turnsystem`。
  **目前只有 `Assets/Scenes/Test.unity` 有**，`SampleScene` 没有 → 一律用 `-Scene Test`
- 编辑器开着时走 Editor 桥，**需要 Unity 窗口在前台**，否则 `EditorApplication.update` 降频会导致超时
- `Logs/` 已在 `.gitignore` 里，AutoTest 的产物不会进版本库

---

## 代码约定

- 注释和 Debug 日志用中文，和现有风格保持一致
- 日志前缀统一成 `[系统名]`，例如 `[MissionManager]` / `[AutoTest]` / `[Enemy:{name}]`
- `MissionTypes.cs` 里的 `ConditionCheck` / `ConditionTarget` 枚举**只能往末尾追加**，
  中间插入会让已配置的 `.asset` 枚举值错位
- 修改 `Assets/Scripts/Ai/` 或 `Assets/Scripts/SystemManager/MissionSystem/` 后，
  用 AutoTest 跑几个回合验证，别只靠读代码下结论

---

## 系统概览

| 目录 | 内容 |
|---|---|
| `Assets/Scripts/Ai/` | 敌人 AI：`EnemyAIController` 主控，`*Executor` 是具体行为，`BehaviourTree/` 是 BT 运行时 |
| `Assets/Scripts/SystemManager/MissionSystem/` | 任务系统：`MissionManager` 调度，`MissionData`/`MissionPool` 配置，`ConditionEvaluator` 条件求值 |
| `Assets/Scripts/units/` | `TurnSystem` 回合流转、`TurnBasedUnit` AP、`UnitMovement` 格子移动 |
| `Assets/BehaviourTree/EnemyBehaviorTree.json` | 行为树定义，分支顺序即优先级（战斗层 > 拾取层 > 任务层） |
| `Assets/AutoTest/` | 自动化测试插件本体 |

任务系统的数据流：
`MissionPool` → `MissionManager` 激活 → `MissionContext` 推送给敌人 →
写进 Blackboard 的 `weight_*` / `targetZoneId` → 行为树选分支 → Executor 执行 →
敌人回合结束时 `MissionManager` 检查成功/失败条件并结算。
