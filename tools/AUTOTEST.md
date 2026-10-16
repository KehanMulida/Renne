# AutoTest —— Unity 自动化运行 / 日志抓取 / 回合模拟

让 Unity **自动跑起来、自动推进 N 个回合、把 Console 全部落盘**，
这样不用盯着编辑器也能知道「跑 10 回合之后任务系统会发生什么、有没有报错」。

---

## 一分钟上手

用 `.cmd` 包装（推荐，绕过 PowerShell 执行策略限制）：

```bat
:: 跑 10 个回合，自动收集日志（Unity 开没开都行，脚本自己判断走哪条路）
:: ⚠ 场景必须有 TurnSystem —— 目前只有 Test.unity 有，SampleScene 没有
Tools\autotest.cmd run -Turns 10 -Scene Test

Tools\autotest.cmd errors     :: 只看这次跑出来的报错
Tools\autotest.cmd turns      :: 每回合的任务进度 / 敌人状态快照
Tools\autotest.cmd compile    :: 只确认脚本能不能编译
```

编辑器里也能手动触发：菜单 **Tools > AutoTest > 控制台**（快捷键 `Ctrl+Shift+T`）。

---

## 需要配置什么？

**Unity 这边：什么都不用配。** 没有 asmdef、不用装包、不用改 Player/Build Settings、
不用往场景里拖任何组件、不用配 Script Execution Order。把 `Assets/AutoTest/`
放进项目 Unity 自动导入就能用。

**唯一要注意的是 Windows 的 PowerShell 执行策略。** 默认是 `Restricted`，
直接跑 `.ps1` 会被拦：

```
无法加载文件 ...\autotest.ps1，因为在此系统上禁止运行脚本。
```

三选一：

| 方案 | 做法 | 说明 |
|---|---|---|
| **用 .cmd 包装**（推荐） | `Tools\autotest.cmd run -Turns 10 -Scene Test` | 只对这一次调用生效，不改机器设置 |
| 临时绕过 | `powershell -ExecutionPolicy Bypass -File Tools\autotest.ps1 run -Turns 10 -Scene Test` | 同上，手敲长一点 |
| 一次性放开 | `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` | 改当前用户的全局设置，之后 `.ps1` 都能直接跑 |

另外两个隐性前提（当前机器都已满足，换机器时才需要检查）：

- **Unity 许可证有效**。batchmode 要求已激活的许可证，Personal 授权过期后
  batchmode 会直接失败退出。编辑器能正常打开就说明没问题。
- **Unity 安装路径可发现**。脚本按 `ProjectSettings/ProjectVersion.txt` 里的版本号，
  先查 Unity Hub 的 `%APPDATA%\UnityHub\editors.json`，再查几个常见目录。
  找不到就用 `-UnityPath "D:\...\Unity.exe"` 手动指。用 `status` 子命令可以确认：

```bat
Tools\autotest.cmd status
```

---

## 两种运行模式

Unity 的 `-batchmode` **没法在编辑器开着的时候用**（项目被 `Temp/UnityLockfile` 锁住），
所以这套工具做了两条通路，`run` 会自动选：

| 模式 | 触发条件 | 原理 |
|---|---|---|
| **batchmode** | 编辑器没开 | 直接用 `Unity.exe -batchmode` 拉起项目跑 PlayMode，进程退出码就是测试结果 |
| **Editor 桥** | 编辑器开着 | CLI 写 `command.json`，编辑器里的 `AutoTestBridge` 每 0.5s 轮询到新命令就进 PlayMode |

想强制指定：`-Mode batch` / `-Mode bridge`。

> ⚠ **Editor 桥模式下请让 Unity 窗口保持在前台。** 编辑器失焦时
> `EditorApplication.update` 会降频，桥的响应会变得很慢甚至停住。

---

## 它到底做了什么

```
run-config.json (enabled=true)
      ↓ Unity 进入 PlayMode
AutoTestSession.Bootstrap()          ← [RuntimeInitializeOnLoadMethod] 场景加载前
      ├ 挂 Application.logMessageReceivedThreaded → 所有 Debug.Log 落盘
      └ 生成 [AutoTestDriver] 常驻对象
            ├ 玩家回合到了 → 自动替它 EndCurrentTurn（否则 TurnSystem 卡在 WaitingForInput）
            ├ 每个回合开始/结束 → 写一条状态快照到 turns.jsonl
            ├ 任务结算 → 记一条 mission_settled 事件
            └ 跑满 N 回合 / 超时 / 出错 → 写 summary.json 并按结果设置退出码
```

**不需要往场景里拖任何东西**，也不会影响你手动点播放——
`run-config.json` 里的 `enabled` 标记是一次性的，被消费后立刻置回 `false`。

---

## 产物

全部在 `Logs/AutoTest/`（`Logs/` 已在 `.gitignore` 里，不会进版本库）：

```
Logs/AutoTest/
├── command.json           CLI → 编辑器的命令（seq 自增）
├── status.json            编辑器 → CLI 的状态
├── run-config.json        下一次 PlayMode 的配置
├── latest.txt             最近一次 runId
├── latest/                最近一次结果的副本（Claude 和 CLI 都读这里）
│   ├── summary.json
│   ├── console.log
│   ├── errors.log
│   └── turns.jsonl
└── runs/<runId>/          每次运行的完整归档
    ├── console.log        人读的全量日志，带相对时间戳和帧号
    ├── console.jsonl      同样内容的结构化版本，一行一条，方便 grep/jq
    ├── errors.log         只有 Error / Exception / Assert，带堆栈
    ├── turns.jsonl        每回合状态快照
    ├── summary.json       结果摘要
    └── unity.log          Unity 原始日志（batchmode 才有，编译错误看这个）
```

### summary.json

```json
{
  "runId": "20260928-143022",
  "reason": "completed",
  "elapsedSeconds": 42.7,
  "turnsCompleted": 10,
  "maxTurns": 10,
  "logs": 1843, "warnings": 12, "errors": 0, "exceptions": 0,
  "passed": true,
  "errorSamples": []
}
```

`reason` 可能是：`completed` / `timeout` / `stopped_on_error` /
`no_turnsystem` / `application_quit` / `watchdog_timeout`。

### turns.jsonl

一行一条，`hook` 区分是回合开始、回合结束还是任务结算：

```json
{"hook":"turn_end","globalTurn":3,"faction":"Enemy","planProgress":10,
 "missions":[{"id":"guard_generator_b1","priority":5,"repeatsDone":0,"repeatCount":1,
              "turnCount":2,"apConsumed":18,"profile":"Defensive",
              "turnsInZone":{"zone_b1":2}}],
 "enemies":[{"id":"E01","alive":true,"hp":30,"maxHp":30,"grid":"12,7","floor":0,
             "ap":4,"mission":"guard_generator_b1","targetZone":"zone_b1"}],
 "player":{"hp":50,"maxHp":50,"grid":"3,3","floor":0}}
```

这就是「跑 N 回合之后会发生什么」的答案：任务 `turnCount` 有没有在涨、
`turnsInZone` 有没有累积、敌人有没有真的走到目标区域、`planProgress` 有没有推进。

---

## 退出码

| 码 | 含义 |
|---|---|
| 0 | 通过，没有 Error/Exception |
| 1 | 跑完了，但捕获到 Error / Exception |
| 2 | 超时（`maxSeconds` 用尽或看门狗触发） |
| 3 | `-StopOnError` 且确实出错了，提前中断 |
| 4 | 场景里没有 TurnSystem，没法模拟回合 |
| 5 | 场景找不到 / PlayMode 起不来 |
| 6 | 脚本编译失败 |
| 7 | 编辑器开着，batchmode 用不了 |
| 8 | PlayMode 被手动中断（在 Unity 里点了停止）。`summary.json` 仍然是完整的——<br>`AutoTestSession` 在 `Application.quitting` 里兜底写过，桥会从它还原结果 |

---

## 参数

```powershell
.\Tools\autotest.ps1 run
    -Turns 10            # 模拟多少个全局回合（默认 10）
    -Timeout 180         # 墙钟超时秒数，防 AI 卡死（默认 180）
    -PlayerDelay 0.15    # 自动结束玩家回合前等多久
    -NoAutoPass          # 不替玩家结束回合（玩家回合需要你手动操作）
    -StopOnError         # 第一个 Error 就停
    -Scene SampleScene   # 指定场景，留空 = 当前场景
    -Note "验证守点任务" # 写进 summary.json 方便回溯
    -Mode auto|batch|bridge
    -WithGraphics        # batchmode 默认带 -nographics，URP/渲染相关报错时去掉它
    -UnityPath "..."     # 手动指定 Unity.exe
```

查看类命令默认只打印末尾 120 行，用 `-Tail 500` 或 `-All` 看更多。

---

## 常见问题

**跑完 reason=no_turnsystem？**
当前场景里没有 `TurnSystem` 组件。项目里目前只有 `Assets/Scenes/Test.unity` 挂了它，
`SampleScene` 没有 —— 用 `-Scene Test`。

**batchmode 下一堆渲染/Shader 报错？**
加 `-WithGraphics` 去掉 `-nographics`。URP 在纯无图形模式下有些 pass 会抱怨。

**编辑器桥没反应？**
1. 确认 Unity 窗口在前台（失焦会让 `EditorApplication.update` 降频）
2. 确认没有编译错误——有编译错误时桥会拒绝进 PlayMode，退出码 6
3. 直接用菜单 `Tools > AutoTest > 控制台` 手动触发，看 Console 有没有 `[AutoTestBridge]` 日志

**关掉了 Domain Reload（Project Settings > Editor > Enter Play Mode Settings）？**
可以用，`[RuntimeInitializeOnLoadMethod]` 依然会触发。静态字段不重置的问题已经在
`AutoTestSession.Begin()` 里做了防御——发现上一轮会话还开着会先收干净再开新的。

**日志太大？**
`console.log` 会把项目里所有 `Debug.Log` 都收进来，而 `EnemyAIController.ExecuteSingleAction`
和 `MissionManager` 的调试日志非常密集。只关心错误就看 `errors.log`，
或者先把那几处 `Debug.Log` 收到开关后面。

---

## 文件清单

| 文件 | 作用 |
|---|---|
| `Assets/AutoTest/Runtime/AutoTestSession.cs` | 日志采集、文件读写、summary 落盘 |
| `Assets/AutoTest/Runtime/AutoTestDriver.cs` | 回合驱动、状态快照、退出码 |
| `Assets/AutoTest/Editor/AutoTestBatch.cs` | batchmode 入口 + 看门狗 |
| `Assets/AutoTest/Editor/AutoTestBridge.cs` | 编辑器命令文件轮询桥 |
| `Assets/AutoTest/Editor/AutoTestWindow.cs` | `Tools > AutoTest > 控制台` |
| `Tools/autotest.ps1` | CLI 统一入口 |

`Assets/AutoTest/Runtime/` 编进 `Assembly-CSharp`，可以直接引用
`TurnSystem` / `MissionManager` / `EnemyAIController`；
`Assets/AutoTest/Editor/` 靠 `Editor` 目录名自动进 `Assembly-CSharp-Editor`。
两个目录都没有 asmdef，跟项目现有结构保持一致。
