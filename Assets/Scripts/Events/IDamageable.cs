using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可受伤接口
/// 任何可以受到伤害的对象都实现此接口
/// 用途：解耦攻击系统和具体的生命值实现
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// 受到伤害
    /// </summary>
    /// <param name="damage">伤害值</param>
    void TakeDamage(int damage);
    
    /// <summary>
    /// 是否存活
    /// </summary>
    bool IsAlive { get; }
}