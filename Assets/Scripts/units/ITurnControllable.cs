/// <summary>
/// 回合制单位的 AP 提供接口
/// PlayerController / EnemyAIController 各自实现，TurnBasedUnit 通过此接口读取 AP 上限，
/// 不需要知道对方是玩家还是敌人。
/// </summary>
public interface ITurnControllable
{
    /// <summary>返回当前模式下的 AP 上限（Config 内部处理普通/战斗模式切换）</summary>
    int GetCurrentAP();
}
