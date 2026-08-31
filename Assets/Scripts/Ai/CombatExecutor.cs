using System.Collections;
using UnityEngine;

[RequireComponent(typeof(UnitMovement))]
[RequireComponent(typeof(TurnBasedUnit))]
public class CombatExecutor : IActionExecutor
{
    private Transform      owner;
    private EnemyConfig    config;
    private EnemyEquipment enemyEquipment;
    private TurnBasedUnit  turnBasedUnit;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner          = owner;
        this.config         = config;
        this.enemyEquipment = owner.GetComponent<EnemyEquipment>();
        this.turnBasedUnit  = owner.GetComponent<TurnBasedUnit>();
    }

    /// <summary>
    /// 攻击可用条件：当前是自己的回合且有 AP，且本回合尚未攻击。
    /// 时间冷却由回合制保证（每回合只能行动一次），不再用 Time.time。
    /// </summary>
    public bool CanExecute()
    {
        if (turnBasedUnit == null) return true; // 无回合组件时不卡
        return turnBasedUnit.IsMyTurn && turnBasedUnit.CanAct;
    }

    public IEnumerator Execute(ActionContext context)
    {
        if (context.TargetObject == null) yield break;
        if (turnBasedUnit != null && !turnBasedUnit.CanAct) yield break;

        // 距离检查（格数 → 世界单位）
        float distance = new Vector2(
            owner.position.x - context.TargetObject.position.x,
            owner.position.z - context.TargetObject.position.z).magnitude;

        float attackRange = enemyEquipment != null && enemyEquipment.HasWeapon
            ? enemyEquipment.GetAttackRange(config) * GridManager.Instance.CellSize
            : (config != null ? config.attackRange : 1f) * GridManager.Instance.CellSize;

        if (distance > attackRange) yield break;

        // 前摇：转向目标。有程序化瞄准则交给它（±40°转root、超出转身），否则整体转身。
        var aim = owner.GetComponent<ProceduralAimController>();
        if (aim != null) aim.AimAt(context.TargetObject.position);
        else owner.LookAt(context.TargetObject);
        if (config != null && config.actionInterval > 0.01f)
            yield return new WaitForSeconds(config.actionInterval);

        bool attacked = false;

        if (enemyEquipment != null && enemyEquipment.HasWeapon && enemyEquipment.HasAmmo)
        {
            yield return enemyEquipment.ShootCoroutine(context.TargetObject, config);

            // 武器射击消耗 AP（UseCost 来自武器）
            int apCost = enemyEquipment.Weapon != null ? enemyEquipment.Weapon.UseCost : 1;
            turnBasedUnit?.ConsumeAP(apCost);
            attacked = true;
        }
        else
        {
            var damageable = context.TargetObject.GetComponent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                damageable.TakeDamage(config != null ? config.attackDamage : 10, owner.gameObject);
                turnBasedUnit?.ConsumeAP(1);
                attacked = true;
            }
        }

        if (attacked)
            context.Blackboard["hasAttackedThisTurn"] = true;
    }
}
