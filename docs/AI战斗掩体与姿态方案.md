# AI 战斗掩体与姿态方案（v1，待评审）

## 目标
- AI 战斗不再站桩对枪，而是**按战术找掩体、蹲半掩体、探头射击**。
- AI 姿态（站/蹲/趴）与**玩家本体一致**（共享同一套机制，单一真相源）。
- 掩体**真实挡火力**（物理挡弹）。
- 玩家反制手段本轮**不做**（后续单独一轮）。

---

## 现状（已核实）
- **子弹是物理抛射物**：`BulletProjectile` 飞行中 `Physics.Linecast(last,new,hitLayer)`，撞 `hitLayer` 上的碰撞体即命中/消失。
- **敌人 `EnemyEquipment.hitLayer = Player(层3) only（m_Bits:8）`** → 子弹**穿墙**，掩体现在纯摆样子。
- 层：Player=3, Ground=6, **Obstacle=7**, Enemy=8, Mirror=9。
- **战斗执行器 `CombatExecutor`**：只做"距离≤射程→瞄准→开火"，不挪位、不找掩体。所谓"保持射击距离"是 Chase 分支怼到射程内、Combat 原地站射。
- **战术分层已存在**：BT `combat_tactic`（Aggressive/Engage/Defensive/CallSupport/Retreat，按 HP+友军选），目前基本没被利用。
- **掩体几何基建已有**：`CoverUtil.TryGetCover(cell,floor)`（邻格是否障碍 + 可探头侧），为玩家廖枪写的，AI 可复用/扩展。
- **玩家姿态系统**（`PlayerController`）：`enum Stance{Stand,Crouch,Prone}`，随姿态变化：
  - `SoundRadiusMultiplier`（趴0.1/蹲0.3/站1）
  - `FireHeightMultiplier`（趴0.2/蹲0.55/站1）— 开火高度
  - `DetectionPosition` 眼高（趴0.15/蹲0.4/站）— 被视线检测点（`IDetectable`）
  - `_moveMultiplier`（蹲0.6）、`OnStanceChanged` 事件

---

## 一、掩体挡弹（前置机制）
- **改**：敌人 `EnemyEquipment.hitLayer` 加入 **Obstacle(7)**（→ Player+Obstacle）；玩家枪 `WeaponData.weaponHitLayer` 确认含 **Obstacle+Enemy**（待核实真实枪械资源）。
- 子弹撞 Obstacle → `OnHit` 无 `IDamageable` 时仅消失（自毁），符合"掩体挡弹"。
- **关键：命中点要用姿态调整后的身体点，而非 `target.position`**。现在 `FirePellets` 用 `target.position`（模型 pivot 在脚底=很低），高度型半掩体判定会失真。改为瞄准目标的**站姿身体点**（复用 `IDetectable.DetectionPosition` 思路，按目标姿态取胸/头高）。这样：
  - 站立目标在低掩体后 → 弹道高、越过低墙 → 命中（暴露）。
  - 蹲下目标在低掩体后 → 弹道低、被低墙挡 → 保护（半掩体生效）。
- **注意枪口自撞**：origin 用 `muzzlePoint`（已存在），且掩体走位时站在**掩体外沿/探头位**，避免贴墙第一段 Linecast 自撞。
- **测试点**：全掩体挡站姿弹；半掩体挡蹲姿弹、放行站姿弹；探头位能打出。

---

## 二、共享姿态组件 `StanceController`
把姿态逻辑从 `PlayerController` 抽成可复用 MonoBehaviour，玩家和敌人都挂，保证"一致"。

- **StanceController（新）**：持 `Stance _stance`；暴露 `CurrentStance / SoundRadiusMultiplier / FireHeightMultiplier / EyeHeight(DetectionPosition) / MoveMultiplier`；`SetStance()`、`OnStanceChanged`。倍率/高度从一份 `StanceProfile`（ScriptableObject 或 config 字段）取。
- **玩家**：`PlayerController` 委托给 `StanceController`（保留现有公开 API 向后兼容，内部转调）。
- **敌人**：挂 `StanceController` + 实现 `IDetectable`（按 `EyeHeight` 返回被侦测/被瞄准点——**核实敌人现在是否已实现 IDetectable**；没有则补，让玩家看/打敌人时半掩体、蹲姿生效）。
- **接线**：`EnemyEquipment` 开火 origin 高度、被瞄准点都走 StanceController，与玩家同源。

---

## 三、AI 掩体走位（战术分层）

### 掩体评分（新 util，扩 CoverUtil 或 `CombatPositioning`）
输入：玩家格 `Pg`、AI 格、`floor`、`tactic`、搜索半径 `R`、`attackRange`。
对每个候选格 `C`（R 内、可走、可达 `PathfindingService.FindPath`）打分：
- **防护** `cover(C,P)`：`C` 朝 `P` 方向的邻格是障碍（挡枪）。区分**全掩体（高障碍）** vs **半掩体（低障碍，需蹲）**。
- **可射** `canShoot(C,P)`：从 `C`（或其探头侧 `CoverUtil`）对 `P` 有 LoS（`Physics.Raycast`,obstacleLayer）且 `≤attackRange`。
- **交战距离** `inRange`。
- **战术权重**：
  - **Aggressive**：靠近 P 优先，cover 权重低（逼近）。
  - **Engage / Defensive**：cover 高 + 可射 + inRange（蹲掩体探头射）。
  - **Retreat**：远离 P + cover（撤退）。

### `CombatMoveExecutor`（新执行器）
- 读 `lastSeenPosition/TargetObject` + `combat_tactic`。
- 算最佳战斗位：不在→移过去（AP 限制，仿 `InvestigationExecutor`）；已在且可射→交给 `CombatExecutor` 开火。
- 写黑板 `inCombatCover`（当前格是否有掩体）、`coverIsHalf`（是否半掩体、需蹲）。

### BT 接线（改动最小）
- 现有四分支 `Chase_Aggressive / Chase_Engage / Defensive_Hold / Retreat` 的执行器换成新 `combatMove`（各自的 `TacticCondition` 已在）。
- `Combat_InRange`（开火）加门：`inCombatCover || tactic==Aggressive` 才原地射，否则先走 `combatMove`。
- `executors` 字典注册 `"combatMove"`。

---

## 四、AI 蹲掩体联动
- 评分区分**全/半掩体**（障碍高度：场景障碍标注，或按 collider 高度阈值判定）。
- AI 到半掩体：`StanceController.SetStance(Crouch)` 降 `FireHeight/EyeHeight` → 低障碍挡住射向它的弹（物理，靠身体/瞄准高度）。要射时临时站起/探头（暴露一回合）。
- 站/蹲倾向：Defensive/Retreat 蹲；Aggressive 站着推进。
- 与玩家同一 `StanceController`，fireHeight/eyeHeight 一致。

---

## 五、config 参数
- `EnemyConfig`：`coverSearchRadius`、各战术 `preferredEngageDist`；姿态倍率/高度（对齐 `PlayerConfig`，或共用 `StanceProfile`）。

---

## 实施分期（可逐段测）
1. **Phase 1 掩体挡弹**：hitLayer 加 Obstacle（敌+玩家）+ 瞄准点改姿态身体点 + 测试子弹被墙挡/探头打出。
2. **Phase 2 共享 StanceController**：抽取 + 玩家委托 + 敌人挂载 + 敌人 IDetectable。
3. **Phase 3 AI 掩体走位**：掩体评分 + `CombatMoveExecutor` + BT 接线。
4. **Phase 4 AI 蹲半掩体联动**：全/半掩体区分 + 蹲/站时机。

---

## 待定 / 测试点
- 枪口自撞（origin 在探头外沿）。
- 敌人是否已实现 `IDetectable`（未实现则补，按姿态降点）。
- 全/半掩体的障碍高度数据来源（标注 vs collider 高度阈值）。
- 数值：搜索半径、各战术交战距离、蹲下时机、走位抖动阈值（避免每回合换掩体）。
- 玩家真实枪械 `weaponHitLayer` 是否已含 Obstacle（核实）。
