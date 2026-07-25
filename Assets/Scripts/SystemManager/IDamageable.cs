using UnityEngine;

/// <summary>
/// 可受伤接口
/// 任何可以受到伤害的对象都实现此接口
/// 用途：解耦攻击系统和具体的生命值实现
/// </summary>
public interface IDamageable
{
    /// <param name="damage">伤害值</param>
    /// <param name="attacker">攻击者（可为 null）</param>
    void TakeDamage(int damage, GameObject attacker = null);

    bool IsAlive { get; }
}
