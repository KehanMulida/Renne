using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(UnitMovement))]
[RequireComponent(typeof(TurnBasedUnit))]
public class CombatExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private float lastAttackTime;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
    }

    public bool CanExecute()
    {
        return Time.time - lastAttackTime > config.attackCooldown;
    }

/// <summary>
/// 执行攻击
/// </summary>
/// <param name="context">动作上下文</param>
/// <returns>协程</returns>
    public IEnumerator Execute(ActionContext context)
    {
        if (context.TargetObject == null) yield break;

        float distance = Vector3.Distance(owner.position, context.TargetObject.position);
        if (distance > config.attackRange) yield break;

        owner.LookAt(context.TargetObject);
        
        yield return new WaitForSeconds(0.3f);

        var damageable = context.TargetObject.GetComponent<IDamageable>();
        damageable?.TakeDamage(config.attackDamage);

        lastAttackTime = Time.time;
    }
}