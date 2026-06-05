using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 战斗模式管理器（单例）
/// 职责：
/// 1. 监听战斗触发条件
/// 2. 广播进入/退出战斗模式事件
/// 3. 各单位自己持有正常/战斗两套 AP 值
///    · 当前正在行动的单位（isMyTurn == true）立即刷新剩余 AP（取旧值与新上限的较小值）
///    · 尚未行动的单位在其回合开始时自动读取新 AP 上限
/// </summary>
public class CombatModeManager : MonoBehaviour
{
    public static CombatModeManager Instance { get; private set; }

    private bool isInCombatMode = false;
    public bool IsInCombatMode => isInCombatMode;

    private HashSet<EnemyAIController> combatEnemies = new HashSet<EnemyAIController>();
    private List<TurnBasedUnit> registeredUnits = new List<TurnBasedUnit>();
    private List<EnemyAIController> registeredEnemies = new List<EnemyAIController>();

    public event System.Action OnEnterCombatMode;
    public event System.Action OnExitCombatMode;
    public event System.Action<Vector3> OnEnemyAttackLaunched;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 注册 ──────────────────────────────────────────────

    public void RegisterUnit(TurnBasedUnit unit)
    {
        if (!registeredUnits.Contains(unit))
            registeredUnits.Add(unit);
    }

    public void RegisterEnemy(EnemyAIController enemy)
    {
        if (!registeredEnemies.Contains(enemy))
            registeredEnemies.Add(enemy);
    }

    public void UnregisterEnemy(EnemyAIController enemy)
    {
        // 用 null 检查防止 MissingReferenceException
        if (enemy == null) return;

        registeredEnemies.Remove(enemy);
        combatEnemies.Remove(enemy);

        // 延迟一帧检查是否退出战斗，避免在 Enemy 回合执行中途切换 AP 导致卡死
        StartCoroutine(DelayedCheckExitCombat());
    }

    // ── 战斗状态通知 ──────────────────────────────────────

    public void NotifyEnemyEnterCombat(EnemyAIController enemy)
    {
        if (enemy == null) return;
        combatEnemies.Add(enemy);
        if (!isInCombatMode)
            EnterCombatMode("Enemy spotted player");
    }

    public void NotifyEnemyExitCombat(EnemyAIController enemy)
    {
        if (enemy == null) return;
        combatEnemies.Remove(enemy);
        StartCoroutine(DelayedCheckExitCombat());
    }

    public void NotifyPlayerAttack()
    {
        if (!isInCombatMode)
            EnterCombatMode("Player ambush");
    }

    /// <summary>
    /// 通知有攻击发出（供 UI / 音效订阅）
    /// </summary>
    public void NotifyAttackLaunched(GameObject shooter, Vector3 bulletDirection)
    {
        if (!isInCombatMode) return;
        bool shooterIsPlayer = shooter != null &&
                               shooter.GetComponent<PlayerController>() != null;
        if (!shooterIsPlayer)
            OnEnemyAttackLaunched?.Invoke(bulletDirection);
    }

    /// <summary>
    /// 敌人实际行动时触发玩家 QTE（移动 OR 射击）
    /// 在 MoveExecutor / BulletProjectile.Fire 中调用
    /// qteDuration = 0 时不开放 QTE
    /// </summary>
    public void NotifyEnemyAction(GameObject source, Vector3 direction, float qteDuration)
    {
        if (!isInCombatMode || qteDuration <= 0f) return;

        bool sourceIsPlayer = source != null &&
                              source.GetComponent<PlayerController>() != null;
        if (sourceIsPlayer) return;

        // 通知 UI
        OnEnemyAttackLaunched?.Invoke(direction);

        // 开放玩家 QTE 反应窗口
        foreach (var unit in registeredUnits)
        {
            if (unit == null) continue;
            if (unit.GetComponent<PlayerController>() != null)
                unit.OpenReactionWindow(direction, qteDuration);
        }
    }

    // ── 模式切换 ─────────────────────────────────────────

    private void EnterCombatMode(string reason)
    {
        if (isInCombatMode) return;
        isInCombatMode = true;
        Debug.Log($"[CombatMode] Enter | Reason: {reason}");

        // 1. 更新所有 Config 到战斗模式
        foreach (var enemy in registeredEnemies)
        {
            if (enemy != null) enemy.config?.SetCombatMode(true);
        }

        foreach (var unit in registeredUnits)
        {
            if (unit == null) continue;
            var playerCtrl = unit.GetComponent<PlayerController>();
            playerCtrl?.Config?.SetCombatMode(true);
        }

        // 2. Config 全部更新后，立即刷新当前正在行动单位的剩余 AP
        //    （尚未行动的单位在其回合开始时会自动读取新 AP 上限）
        foreach (var unit in registeredUnits)
        {
            if (unit != null) unit.RefreshCombatAP();
        }

        OnEnterCombatMode?.Invoke();
    }

    private void ExitCombatMode()
    {
        if (!isInCombatMode) return;
        isInCombatMode = false;
        Debug.Log("[CombatMode] Exit");

        // 1. 更新所有 Config 到正常模式
        foreach (var enemy in registeredEnemies)
        {
            if (enemy != null) enemy.config?.SetCombatMode(false);
        }

        foreach (var unit in registeredUnits)
        {
            if (unit == null) continue;
            var playerCtrl = unit.GetComponent<PlayerController>();
            playerCtrl?.Config?.SetCombatMode(false);
        }

        // 2. Config 全部更新后，立即刷新当前正在行动单位的剩余 AP
        foreach (var unit in registeredUnits)
        {
            if (unit != null) unit.RefreshCombatAP();
        }

        OnExitCombatMode?.Invoke();
    }

    private void CheckExitCombat()
    {
        // 清理已销毁的 Enemy 引用
        combatEnemies.RemoveWhere(e => e == null);

        if (combatEnemies.Count == 0)
            ExitCombatMode();
    }

    /// <summary>
    /// 延迟一帧再检查是否退出战斗
    /// 避免 Enemy 被销毁时在回合执行中途切换 AP 导致 WaitForMovementComplete 卡死
    /// </summary>
    private IEnumerator DelayedCheckExitCombat()
    {
        yield return null;
        CheckExitCombat();
    }
}