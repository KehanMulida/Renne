using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DialogueBootstrap — 零配置：进入 Play 时自动把对话系统需要的组件补齐。
///
/// **场景里什么都不用挂。** 只要场景里有敌人（EnemyAIController）或玩家（Tag = Player），就会：
///   · 缺 StoryManager 就建一个（旗标要落在它身上）
///   · 给玩家补 SoundPerception + EavesdropListener
///   · 给每个敌人补 DialogueSpeaker（id 自动取 enemyId，名字查 Resources/DialogueSpeakers.json）
///   · 缺 DialogueManager / AmbientChatDirector / DialogueUI 就建一个 DialogueSystem 物体挂上
///
/// 已经手动挂了的一律不动——想改参数，照常在场景里挂组件、调 Inspector 就行。
/// 只在运行时补，不改场景文件。
///
/// 为什么要有它：这套系统要六种组件分挂在四类物体上，漏一个（比如忘了 StoryManager、
/// 或者在 Play 模式下加的组件停止后丢了）整套就悄无声息地不工作。
/// </summary>
public static class DialogueBootstrap
{
    /// <summary>总开关。关掉后完全按场景里手动挂的组件工作。</summary>
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnFirstSceneLoaded()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    /// <summary>补齐当前场景缺的组件。可以重复调用，已有的不会重复加。</summary>
    public static void Ensure()
    {
        if (!Enabled) return;

        var enemies = Object.FindObjectsOfType<EnemyAIController>();
        var player  = GameObject.FindGameObjectWithTag("Player");
        if (enemies.Length == 0 && player == null) return;   // 菜单之类的场景用不着对话系统

        var added = new List<string>();

        // 顺序有讲究：后面的组件在 Awake/OnEnable 里要找前面的

        // ① 旗标
        if (Object.FindObjectOfType<StoryManager>() == null)
        {
            new GameObject("StoryManager").AddComponent<StoryManager>();
            added.Add("StoryManager");
        }

        // ② 玩家的耳朵（要在 DialogueUI 之前：UI 启用时会去找它）
        if (player != null)
        {
            if (player.GetComponent<SoundPerception>() == null)
            {
                player.AddComponent<SoundPerception>();
                added.Add("玩家的 SoundPerception");
            }
            if (player.GetComponent<EavesdropListener>() == null)
            {
                player.AddComponent<EavesdropListener>();
                added.Add("玩家的 EavesdropListener");
            }
        }

        // ③ 说话者
        int speakers = 0;
        foreach (var e in enemies)
        {
            if (e.GetComponent<DialogueSpeaker>() != null) continue;
            e.gameObject.AddComponent<DialogueSpeaker>();
            speakers++;
        }
        if (speakers > 0) added.Add($"{speakers} 个敌人的 DialogueSpeaker");

        // ④ 调度、导演、UI（导演启用时要找 DialogueManager.Instance，所以管理器先建）
        var mgr = Object.FindObjectOfType<DialogueManager>();
        if (mgr == null)
        {
            mgr = new GameObject("DialogueSystem").AddComponent<DialogueManager>();
            added.Add("DialogueManager");
        }
        if (Object.FindObjectOfType<AmbientChatDirector>() == null)
        {
            mgr.gameObject.AddComponent<AmbientChatDirector>();
            added.Add("AmbientChatDirector");
        }
        if (Object.FindObjectOfType<DialogueUI>() == null)
        {
            mgr.gameObject.AddComponent<DialogueUI>();
            added.Add("DialogueUI");
        }

        if (added.Count > 0)
            Debug.Log($"[对话] 自动补齐：{string.Join("、", added)}（场景里手动挂了的不会重复加）");
    }
}
