using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 战斗模式管理器（单例）
/// 职责：
/// 1. 监听战斗触发条件
/// 2. 广播进入/退出战斗模式事件
/// 3. 各单位自己持有正常/战斗两套 AP 值，收到事件后下一回合生效
/// </summary>
public class CombatModeManager : MonoBehaviour
{
    public static CombatModeManager Instance { get; private set; }

    // ============ 状态 ============

    private bool isInCombatMode = false;
    public bool IsInCombatMode => isInCombatMode;

    // 当前处于战斗状态的敌人
    private HashSet<EnemyAIController> combatEnemies = new HashSet<EnemyAIController>();

    // 注册的所有单位和敌人
    private List<TurnBasedUnit> registeredUnits = new List<TurnBasedUnit>();
    private List<EnemyAIController> registeredEnemies = new List<EnemyAIController>();

    // 事件
    public event System.Action OnEnterCombatMode;
    public event System.Action OnExitCombatMode;

    // ============ 初始化 ============

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ============ 注册 ============

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
        registeredEnemies.Remove(enemy);
        combatEnemies.Remove(enemy);
        CheckExitCombat();
    }

    // ============ 战斗状态通知 ============

    public void NotifyEnemyEnterCombat(EnemyAIController enemy)
    {
        combatEnemies.Add(enemy);
        if (!isInCombatMode)
            EnterCombatMode("Enemy spotted player");
    }

    public void NotifyEnemyExitCombat(EnemyAIController enemy)
    {
        combatEnemies.Remove(enemy);
        CheckExitCombat();
    }

    public void NotifyPlayerAttack()
    {
        if (!isInCombatMode)
            EnterCombatMode("Player ambush");
    }

    /// <summary>
    /// 子弹生成时调用（BulletProjectile.Fire 里触发）
    /// 判断射击者阵营，通知对方单位开放反应移动窗口
    /// </summary>
    public void NotifyAttackLaunched(GameObject shooter, Vector3 bulletDirection)
    {
        if (!isInCombatMode) return;

        // 判断射击者是玩家还是敌人
        bool shooterIsPlayer = shooter.GetComponent<PlayerController>() != null;

        if (shooterIsPlayer)
        {
            // 玩家射击，通知敌人可以反应（暂时不实现AI躲避）
            Debug.Log("[CombatMode] Player fired, enemies can react");
        }
        else
        {
            // 敌人射击，通知玩家可以反应移动
            Debug.Log("[CombatMode] Enemy fired, player can react");
            OnEnemyAttackLaunched?.Invoke(bulletDirection);
        }
    }

    /// <summary>敌人发射子弹时触发，玩家 TurnBasedUnit 监听此事件开放反应移动</summary>
    public event System.Action<Vector3> OnEnemyAttackLaunched;

    // ============ 模式切换 ============

    private void EnterCombatMode(string reason)
    {
        if (isInCombatMode) return;
        isInCombatMode = true;
        Debug.Log($"[CombatMode] Enter | Reason: {reason}");

        // 通知所有 Config 切换到战斗模式
        // TurnBasedUnit 下一回合开始时调用 GetCurrentAP() 自动获取战斗 AP
        foreach (var enemy in registeredEnemies)
            enemy.config?.SetCombatMode(true);

        // 玩家 Config 通过 PlayerController 获取
        foreach (var unit in registeredUnits)
        {
            var playerCtrl = unit.GetComponent<PlayerController>();
            playerCtrl?.Config?.SetCombatMode(true);
        }

        OnEnterCombatMode?.Invoke();
    }

    private void ExitCombatMode()
    {
        if (!isInCombatMode) return;
        isInCombatMode = false;
        Debug.Log("[CombatMode] Exit");

        foreach (var enemy in registeredEnemies)
            enemy.config?.SetCombatMode(false);

        foreach (var unit in registeredUnits)
        {
            var playerCtrl = unit.GetComponent<PlayerController>();
            playerCtrl?.Config?.SetCombatMode(false);
        }

        OnExitCombatMode?.Invoke();
    }

    private void CheckExitCombat()
    {
        if (combatEnemies.Count == 0)
            ExitCombatMode();
    }
}