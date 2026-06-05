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
    private EnemyEquipment enemyEquipment;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
        this.enemyEquipment = owner.GetComponent<EnemyEquipment>();
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

        float distance = new Vector2(
            owner.position.x - context.TargetObject.position.x,
            owner.position.z - context.TargetObject.position.z).magnitude;

        float attackRange = enemyEquipment != null && enemyEquipment.HasWeapon
            ? enemyEquipment.GetAttackRange(config) * GridManager.Instance.CellSize
            : 1f;
        if (distance > attackRange) yield break;

        // 前摇：转向目标（视觉提示）
        owner.LookAt(context.TargetObject);
        if (config.actionInterval > 0.01f)
            yield return new WaitForSeconds(config.actionInterval);

        // 发射攻击
        // QTE 由 BulletProjectile.Fire() 内部触发（子弹生成瞬间），不在此等待
        bool attacked = false;
        if (enemyEquipment != null && enemyEquipment.HasWeapon && enemyEquipment.HasAmmo)
        {
            attacked = enemyEquipment.Shoot(context.TargetObject, config);
        }
        else
        {
            var damageable = context.TargetObject.GetComponent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                damageable.TakeDamage(config.attackDamage, owner.gameObject);
                attacked = true;
            }
        }

        if (attacked)
        {
            lastAttackTime = Time.time;
            context.Blackboard["hasAttackedThisTurn"] = true;
        }
    }
}