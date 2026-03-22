using System.Collections;
using UnityEngine;

public class CombatExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private EnemyEquipment equipment;

    // 回合制游戏不需要实时冷却，改为每回合只能攻击一次的标记
    private bool hasAttackedThisTurn = false;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner     = owner;
        this.config    = config;
        this.equipment = owner.GetComponent<EnemyEquipment>();

        // 监听回合开始事件，每回合重置攻击标记
        TurnBasedUnit unit = owner.GetComponent<TurnBasedUnit>();
        if (unit != null)
            unit.OnMyTurnStart += ResetAttack;
    }

    private void ResetAttack()
    {
        hasAttackedThisTurn = false;
    }

    public bool CanExecute()
    {
        if (hasAttackedThisTurn)
        {
            Debug.Log("[CombatExecutor] Already attacked this turn");
            return false;
        }
        return true;
    }

    public IEnumerator Execute(ActionContext context)
    {
        if (context.TargetObject == null)
        {
            Debug.LogWarning("[CombatExecutor] TargetObject is null, cannot attack");
            yield break;
        }

        float range = equipment != null
            ? equipment.GetAttackRange(config)
            : 1;

        // 攻击范围也需要转世界单位
        float rangeWorld = range * (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);

        float distance = Vector3.Distance(owner.position, context.TargetObject.position);
        Debug.Log($"[CombatExecutor] Attacking | dist:{distance:F1} | range:{rangeWorld:F1}");

        if (distance > rangeWorld)
        {
            Debug.LogWarning($"[CombatExecutor] Target out of range: {distance:F1} > {rangeWorld:F1}");
            yield break;
        }

        owner.LookAt(context.TargetObject);
        yield return new WaitForSeconds(0.3f);

        if (equipment != null && equipment.HasWeapon)
        {
            bool fired = equipment.Shoot(context.TargetObject, config);
            if (!fired)
                FallbackMeleeAttack(context.TargetObject);
        }
        else
        {
            FallbackMeleeAttack(context.TargetObject);
        }

        hasAttackedThisTurn = true;

        // 写入 blackboard，让 EnemyAIController 知道本回合已攻击
        context.Blackboard["hasAttackedThisTurn"] = true;

        // 消耗武器对应的 AP
        TurnBasedUnit turnUnit = owner.GetComponent<TurnBasedUnit>();
        if (turnUnit != null)
        {
            int cost = (equipment != null && equipment.HasWeapon)
                ? equipment.Weapon.UseCost
                : 1;
            turnUnit.ConsumeAP(cost);
            Debug.Log($"[CombatExecutor] Consumed {cost} AP for attack");
        }
    }

    private void FallbackMeleeAttack(Transform target)
    {
        var damageable = target.GetComponent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
            damageable.TakeDamage(config.attackDamage);
            Debug.Log($"[CombatExecutor] Melee: {config.attackDamage} dmg to {target.name}");
        }
    }
}