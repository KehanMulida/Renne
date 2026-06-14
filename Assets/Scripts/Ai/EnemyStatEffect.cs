/// <summary>
/// EnemyStatEffect — AI 数值变化载体
///
/// 作为 EnemyInventory.OnStatEffectRequested 事件的参数，
/// 将道具效果和具体的数值系统解耦：
///   - 道具层只需写 statKey + value
///   - EnemyAIController 订阅事件后，按 statKey 路由到对应系统
///
/// 扩展新 stat：
///   1. 在下方 Constants 区域加一行 string 常量
///   2. 在 EnemyInventory.FireStatEffects() 里加一行读取
///   3. 在 EnemyAIController.HandleStatEffect() 里加一个 case
/// </summary>
public readonly struct EnemyStatEffect
{
    public readonly string statKey;
    public readonly int    value;

    public EnemyStatEffect(string key, int val)
    {
        statKey = key;
        value   = val;
    }

    // ── Stat Key 常量（统一从这里引用，避免字符串拼写错误）────────────
    public const string HP      = "hp";
    public const string Stamina = "stamina";
    public const string Sanity  = "sanity";
    // 扩展示例：public const string Morale = "morale";
}
