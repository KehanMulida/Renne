using System.Collections.Generic;

/// <summary>
/// 对话优先级档位。用常量而不是枚举——JSON 里可以填 60、150 这种中间值微调，
/// 枚举做不到。数值本身没有魔法，只是比较大小。
/// </summary>
public static class DialoguePriority
{
    /// <summary>闲聊。随便被谁打断，也不该打断任何人</summary>
    public const int Ambient = 0;

    /// <summary>即时反应：遭遇袭击、发现尸体、进入警戒。要能立刻打断闲聊</summary>
    public const int Bark = 50;

    /// <summary>任务开始 / 结算演出</summary>
    public const int Mission = 100;

    /// <summary>剧情阶段推进。几乎不该被任何东西挡住</summary>
    public const int Story = 200;
}

/// <summary>
/// 排队中的一段对话。
///
/// 为什么需要排队而不是「播不了就算了」：场上只有几个敌人，闲聊占用率实测 72.5%，
/// 任务对话十有八九撞上有人在聊。直接丢弃意味着剧情演出静默消失——
/// 玩家不会知道自己错过了什么，策划也查不出为什么没播。
///
/// 为什么要有过期：剧情对话在十个回合之后突然冒出来，比不播更糟
/// （上下文早就变了，玩家会莫名其妙）。所以排队有时限，超时就明确放弃并记日志。
/// </summary>
public class PendingDialogue
{
    /// <summary>对话 key（Resources/Dialogues 下的文件名）</summary>
    public string key;

    /// <summary>优先级。决定在队列里排第几、以及能抢占谁</summary>
    public int priority;

    /// <summary>指定演员表。为 null 则按对话数据里的 speakers 去场景里找</summary>
    public List<DialogueSpeaker> cast;

    /// <summary>入队时的全局回合数</summary>
    public int enqueuedTurn;

    /// <summary>最多等几个回合。超过就放弃，避免过时的对话突然插进来</summary>
    public int expireTurns;

    /// <summary>入队原因，只用于日志，方便回溯「这段怎么没播」</summary>
    public string reason;

    public bool IsExpired(int currentTurn) => currentTurn - enqueuedTurn > expireTurns;
}
