using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对话清晰度档位。由玩家与说话者的距离决定——站得越近听得越清楚，
/// 但也越容易被发现，这是潜行调查里“风险换信息量”的核心交换。
/// </summary>
public enum ClarityTier
{
    None,      // 没听到
    Faint,     // 极模糊：只知道“有人在低声交谈”+ 方位，没有内容
    Blurred,   // 模糊：只有关键词可辨，其余遮成「…」。旗标与内心独白在这一档触发
    Clear      // 清晰：完整台词，能理解这句在说什么
}

/// <summary>
/// 玩家偷听到的一句话——**只有结构化数据，不含任何拼好的显示文字**。
/// 玩家看得到的固定文字（方位词、"有人在低声交谈"、括号、名字前缀）全部由 DialogueUI 按
/// Inspector 里的模板拼，偷听逻辑只负责判档。
///
/// 证据（听到的）与主观反应（想到的）分开存，不能混为一谈。
/// </summary>
public class HeardLine
{
    public int         turn;        // 听到时的全局回合数
    public string      tag;         // 对话行标签
    public string      speakerId;   // 实际说话者 id（E01），不是台词里的角色位（A/B）
    public Transform   speaker;     // 实际说话者，飘字跟着他走；可能为空
    public Vector3     position;    // 发声那一刻的嘴部位置（speaker 为空时的飘字锚点）
    public ClarityTier tier;

    // 以下都是已替换好 {E02}/{B} 名字占位符的台词内容，UI 按档位挑着用
    public string      text;        // 完整台词（清晰档）
    public string      blurred;     // 手写的模糊版本（可能为空，空时 UI 按 keyword 自动遮蔽）
    public string      keyword;     // 关键词（可能为空）
    public string      thought;     // 角色的内心独白（只在这一次捕到关键词时才有）

    // 极模糊档：相对玩家朝向的方位，0=正前方，顺时针每 45° 加 1，共 8 个；-1 = 近处
    public int         direction = -1;
}

/// <summary>
/// 按本次演员表解析过的一句台词。DialogueManager 发声时生成，偷听方凭 tag 取回。
///
/// 为什么要在发声那一刻解析：闲聊是随机配对的，同一行 "{B}，借个火" 这次 B 是老王、
/// 下次可能是老李。只有发声的那一刻，演员表才是确定的。
/// </summary>
public class ResolvedLine
{
    public DialogueData    data;
    public DialogueLine    line;          // 原始配置（未替换）
    public int             index;
    public DialogueSpeaker speaker;       // 实际说这句的人

    public string text;
    public string blurred;
    public string keyword;
    public string hint;
    public string flag;

    /// <summary>这句的关键词是谁的名字（speakerId）。null = 关键词不是人名。</summary>
    public string nameOfSpeakerId;
}

/// <summary>
/// 说话者处于什么状态时这段对话可以开。**战斗中一律不聊**，不受这个选项影响。
/// </summary>
public enum ChatState
{
    Idle,      // 只在空闲时（没看到玩家、没在查动静）——闲聊、情报对话
    Alerted,   // 只在查动静时（听到声音、正在过去看）——“嗯？谁在那儿？”
    Any        // 两种都行
}

/// <summary>
/// 对话的发起方式。
/// </summary>
public enum DialogueTrigger
{
    Manual,   // 只能由外部显式 Play（任务演出、阶段演出、调试触发）
    Ambient   // 可被闲聊导演随机挑中：两个空闲敌人凑一块就可能开聊
}

/// <summary>
/// 对话触发条件（JSON 里写字符串，加载时解析成任务系统的 MissionCondition）。
///
/// 为什么不直接用 MissionCondition：JsonUtility 只能把数字读进枚举，
/// 写成 "target": "StoryFlag" 会解析失败。这里用字符串字段过渡，
/// 让编剧能写可读的 JSON，同时判定仍然走现成的 ConditionEvaluator，不另起一套。
/// </summary>
[Serializable]
public class DialogueCondition
{
    public string target;   // ConditionTarget，如 "StoryFlag"
    public string check;    // ConditionCheck，如 "FlagValue"
    public string op;       // ConditionOperator，如 "Equals"

    public string flagKey;
    public string targetId;
    public string zoneId;
    public string objectId;
    public string itemId;
    public string stringValue;
    public float  numericValue;
    public bool   boolValue;

    /// <summary>转成任务系统的条件对象；枚举名写错时返回 null 并告警（视为条件不成立）。</summary>
    public MissionCondition ToMissionCondition(string ownerId)
    {
        if (!TryParseEnum(target, out ConditionTarget t, ownerId, nameof(target))) return null;
        if (!TryParseEnum(check,  out ConditionCheck  c, ownerId, nameof(check)))  return null;

        // op 留空时默认 Equals——绝大多数旗标判定都是“等于”，省掉编剧的样板字段
        ConditionOperator o = ConditionOperator.Equals;
        if (!string.IsNullOrEmpty(op) && !TryParseEnum(op, out o, ownerId, nameof(op))) return null;

        return new MissionCondition
        {
            target       = t,
            check        = c,
            op           = o,
            flagKey      = flagKey,
            targetId     = targetId,
            zoneId       = zoneId,
            objectId     = objectId,
            itemId       = itemId,
            stringValue  = stringValue,
            numericValue = numericValue,
            boolValue    = boolValue
        };
    }

    private static bool TryParseEnum<T>(string raw, out T value, string ownerId, string fieldName)
        where T : struct
    {
        value = default;
        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogWarning($"[对话:{ownerId}] conditions.{fieldName} 为空，该条件被忽略");
            return false;
        }
        if (Enum.TryParse(raw, true, out value)) return true;

        Debug.LogWarning($"[对话:{ownerId}] conditions.{fieldName} = \"{raw}\" 不是合法的 {typeof(T).Name}，该条件被忽略");
        return false;
    }
}

/// <summary>
/// 一句台词。关键词碎片绑定在“句”上而非整段——一段对话可以埋好几条碎片，各自独立结算。
/// </summary>
[Serializable]
public class DialogueLine
{
    [Tooltip("说话者 id，对应场景里 DialogueSpeaker.speakerId（如 E01）")]
    public string speaker;

    [Tooltip("完整台词（清晰档听到的内容）")]
    public string text;

    [Tooltip("模糊档听到的内容。留空则由系统按 keyword 自动遮蔽")]
    public string blurred;

    [Tooltip("模糊档能辨出的关键词。触发 hint 与 flag。\n" +
             "编剧约束：单独看应当不足以得出结论——「地下室」「钥匙」这种孤立名词才是好碎片")]
    public string keyword;

    [Tooltip("捕到关键词时浮出的【玩家角色内心独白】。\n" +
             "铁律：只能提问或引出联想，不能给结论、不能指路，也不能描述 flag 做了什么。\n" +
             "不是每条都要填——留白本身也是设计")]
    public string hint;

    [Tooltip("写进 StoryManager 的旗标键。静默生效，不向玩家播报")]
    public string flag;

    [Tooltip("这句的关键词是谁的名字（角色位 A/B 或单位 id E02）。玩家捕到关键词时，\n" +
             "那个人的名字前缀就从「？」变成这个关键词。\n" +
             "keyword 直接写成 {B} 时可以不填——会自动当作 B 的名字。\n" +
             "只有想让名字“变”的时候才需要显式填：比如先知道叫小刘，后来这句 keyword 写“刘队”、nameOf 写 E02")]
    public string nameOf;
}

/// <summary>
/// 一段对话。放在 Resources/Dialogues/&lt;id&gt;.json，由 DialogueManager 加载。
///
/// ⚠ JsonUtility 不支持注释，写 .json 时不要带 //。
/// </summary>
[Serializable]
public class DialogueData
{
    public string id;

    [Tooltip("turn = 每回合一句（潜行偷听用）；realtime = 按秒播完（任务/阶段演出用）")]
    public string mode = "turn";

    // ══ 发起方式 ══════════════════════════════════════════════

    [Tooltip("manual = 只能被显式 Play（任务/阶段演出）；\n" +
             "ambient = 交给闲聊导演随机挑——两个空闲敌人凑一块就可能开聊")]
    public string trigger = "manual";

    [Tooltip("随机抽取的权重（只对 ambient 有意义）。越大越常被选中")]
    public float weight = 1f;

    [Tooltip("话题标签，纯给编剧看的分类，如 吃饭 / 抱怨 / 换班。系统不解读它")]
    public string topic;

    [Tooltip("ambient：两人相距多少米以内才可能开聊")]
    public float maxSpeakerDistance = 3f;

    [Tooltip("ambient：说话者处于什么状态时才会开这段。\n" +
             "idle = 只在空闲时（闲聊、情报）；alerted = 只在听到动静、正在查看时（“嗯？谁？”）；any = 都行。\n" +
             "战斗中一律不聊。警觉的人不会站着扯淡，但会嘀咕")]
    public string state = "idle";

    public ChatState StateKind =>
        string.Equals(state, "alerted", StringComparison.OrdinalIgnoreCase) ? ChatState.Alerted :
        string.Equals(state, "any",     StringComparison.OrdinalIgnoreCase) ? ChatState.Any :
                                                                               ChatState.Idle;

    public DialogueTrigger TriggerKind =>
        string.Equals(trigger, "ambient", StringComparison.OrdinalIgnoreCase)
            ? DialogueTrigger.Ambient
            : DialogueTrigger.Manual;

    // ══ 三道距离（米）══════════════════════════════════════════
    // 由近到远：听懂 ≤ 抓到关键词 ≤ 察觉有人在说话。
    // 拆开是为了让“察觉”的范围远大于“听懂”——玩家老远就知道那边有人在聊，
    // 再自己决定要不要冒险贴上去。三者写乱了会被自动夹成单调递增。
    //
    // 与听者的 SoundPerception.hearingRange 是两道独立的闸：
    // hearingRange 决定“这一声能不能到达我”，这三个半径决定“到达后听得多清”。
    // 实际察觉范围 = min(hearingRange, audibleRadius)。

    [Tooltip("清晰档：在这个距离内听到完整台词")]
    public float clearRadius = 3f;

    [Tooltip("模糊档：在这个距离内能辨出关键词（旗标与内心独白在这一档触发）")]
    public float keywordRadius = 6f;

    [Tooltip("极模糊档 / 硬截断：在这个距离内只知道“有人在低声交谈”+ 方位；超出则完全听不到。\n" +
             "也是发出的声音事件半径")]
    public float audibleRadius = 10f;

    /// <summary>按距离判档（未计墙体衰减）。三个半径写乱时自动夹成 clear ≤ keyword ≤ audible。</summary>
    public ClarityTier TierAtDistance(float distance)
    {
        float clear   = Mathf.Max(0f, clearRadius);
        float keyword = Mathf.Max(clear, keywordRadius);
        float audible = Mathf.Max(keyword, audibleRadius);

        if (distance <= clear)   return ClarityTier.Clear;
        if (distance <= keyword) return ClarityTier.Blurred;
        if (distance <= audible) return ClarityTier.Faint;
        return ClarityTier.None;
    }

    [Tooltip("true = 以 Alert 发声，会惊动其它敌人（警戒喊话用）；false = Voice，敌人之间互不干扰")]
    public bool alertsOthers = false;

    public bool repeatable = true;

    [Tooltip("重复触发的冷却回合数")]
    public int cooldownTurns = 10;

    [Tooltip("turn 模式下，每个敌人回合开始时连续说几句（句间隔 secondsPerLine），说完停到下一回合。\n" +
             "0 = 一口气说完。闲聊一般填 0；情报对话填 2 左右，保留“蹲几回合才能听全”的张力。\n" +
             "为什么不是固定一回合一句：说话不占用行动，敌人是边聊边巡逻的，拖太多回合人就走散了")]
    public int linesPerTurn = 2;

    [Tooltip("句与句之间隔几秒。turn 模式的回合内连说、realtime 模式都用它")]
    public float secondsPerLine = 2.5f;

    public List<DialogueCondition> conditions;

    [Tooltip("参与者 id 列表，用于占用检查（同一个敌人不会同时参与两段对话）")]
    public List<string> speakers;

    public List<DialogueLine> lines;

    public bool IsRealtime => string.Equals(mode, "realtime", StringComparison.OrdinalIgnoreCase);

    /// <summary>行号 → 声音标签。EavesdropListener 凭它反查台词。</summary>
    public string MakeTag(int lineIndex) => $"{id}#{lineIndex}";

    /// <summary>解析声音标签。格式 "对话id#行号"。</summary>
    public static bool ParseTag(string tag, out string dialogueId, out int lineIndex)
    {
        dialogueId = null;
        lineIndex = -1;
        if (string.IsNullOrEmpty(tag)) return false;

        int hash = tag.LastIndexOf('#');
        if (hash <= 0 || hash == tag.Length - 1) return false;

        dialogueId = tag.Substring(0, hash);
        return int.TryParse(tag.Substring(hash + 1), out lineIndex);
    }
}
