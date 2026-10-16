using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RenneAutoTest
{
    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestDialogueProbe —— 对话优先级机制的定向验证
    //
    // 为什么需要它：抢占 / 排队 / 打断这三条路径，**默认跑游戏根本走不到**。
    // 抢占只在说话人恰好被占用时发生、排队要任务对话真的触发、打断要玩家开枪，
    // 自动化驱动器一个都不会制造。跑十个回合看日志一片空白，
    // 既证明不了能用，也发现不了坏掉。
    //
    // 所以这里直接构造冲突场景，断言每条路径的结果。
    // ══════════════════════════════════════════════════════════════════════════

    public static class AutoTestDialogueProbe
    {
        private const string LowPri  = "_test_lowpri";   // priority 0，占两个人
        private const string HighPri = "_test_highpri";  // priority 100，要一个人

        private static int _pass, _fail;
        private static StringBuilder _sb;

        public static void Run()
        {
            var mgr = DialogueManager.Instance;
            if (mgr == null)
            {
                AutoTestSession.WriteConsoleLine("[对话探针] DialogueManager 不存在，跳过");
                return;
            }

            // 需要两个真实的说话者
            var speakers = new List<DialogueSpeaker>();
            foreach (var sp in DialogueSpeaker.All)
                if (sp != null && sp.isActiveAndEnabled) speakers.Add(sp);

            if (speakers.Count < 2)
            {
                AutoTestSession.WriteConsoleLine(
                    $"[对话探针] 场上只有 {speakers.Count} 个说话者，至少要 2 个，跳过");
                return;
            }

            var a = speakers[0];
            var b = speakers[1];

            _pass = _fail = 0;
            _sb = new StringBuilder();
            _sb.AppendLine("──────── 对话优先级探针 ────────");
            _sb.AppendLine($"  用 {a.SpeakerId} / {b.SpeakerId} 构造冲突");

            CleanUp(mgr);

            TestPreemption(mgr, a, b);
            CleanUp(mgr);

            TestNoPreemptWhenLower(mgr, a, b);
            CleanUp(mgr);

            TestInterruptBelow(mgr, a, b);
            CleanUp(mgr);

            _sb.AppendLine($"  ── 通过 {_pass} / 失败 {_fail} ──");
            _sb.Append("────────────────────────────────");

            AutoTestSession.WriteConsoleLine(_sb.ToString());
            if (_fail > 0) Debug.LogError("[对话探针]\n" + _sb);
            else           Debug.Log("[对话探针]\n" + _sb);
        }

        // ── 用例 ────────────────────────────────────────────────────

        /// <summary>高优先级应当把占着人的低优先级挤掉。这是核心诉求。</summary>
        private static void TestPreemption(DialogueManager mgr, DialogueSpeaker a, DialogueSpeaker b)
        {
            if (!mgr.PlayWithCast(LowPri, new List<DialogueSpeaker> { a, b }))
            {
                Fail("抢占", $"前置失败：低优先级对话没能开起来（{LowPri} 不存在？）");
                return;
            }

            // ⚠ 必须用 PlayWithCast：测试对话写的是抽象角色位 A/B，
            //   Play() 不带演员表时会去场景里找"id 叫 A 的说话者"，场上只有 E01~E04，
            //   根本找不到人，于是在抢占之前就失败了——测不到想测的东西
            bool lowRunning = mgr.IsPlaying(LowPri);
            bool highOk     = mgr.PlayWithCast(HighPri, new List<DialogueSpeaker> { a });  // 需要 a，但 a 正忙
            bool lowGone    = !mgr.IsPlaying(LowPri);
            bool highNow    = mgr.IsPlaying(HighPri);

            if (lowRunning && highOk && lowGone && highNow)
                Pass("抢占", "P100 成功挤掉占着人的 P0");
            else
                Fail("抢占", $"低优先级开播={lowRunning} 高优先级开播={highOk} " +
                             $"低优先级已停={lowGone} 高优先级在播={highNow}");
        }

        /// <summary>反向：低优先级不该挤掉高优先级，必须老实失败。</summary>
        private static void TestNoPreemptWhenLower(DialogueManager mgr, DialogueSpeaker a, DialogueSpeaker b)
        {
            if (!mgr.PlayWithCast(HighPri, new List<DialogueSpeaker> { a }))
            {
                Fail("反向抢占", "前置失败：高优先级对话没能开起来");
                return;
            }

            bool lowOk      = mgr.PlayWithCast(LowPri, new List<DialogueSpeaker> { a, b });
            bool highStill  = mgr.IsPlaying(HighPri);

            if (!lowOk && highStill)
                Pass("反向抢占", "P0 没能挤掉 P100，高优先级演出未被打散");
            else
                Fail("反向抢占", $"低优先级开播={lowOk}（应为 False） 高优先级仍在播={highStill}（应为 True）");
        }

        /// <summary>事件打断：遭遇袭击时掐掉所有低于 Bark 的对话。</summary>
        private static void TestInterruptBelow(DialogueManager mgr, DialogueSpeaker a, DialogueSpeaker b)
        {
            if (!mgr.PlayWithCast(LowPri, new List<DialogueSpeaker> { a, b }))
            {
                Fail("事件打断", "前置失败：低优先级对话没能开起来");
                return;
            }

            int stopped  = mgr.InterruptBelow(DialoguePriority.Bark, "探针测试");
            bool lowGone = !mgr.IsPlaying(LowPri);

            if (stopped >= 1 && lowGone)
                Pass("事件打断", $"InterruptBelow(Bark) 掐掉 {stopped} 段，P0 已停");
            else
                Fail("事件打断", $"掐掉段数={stopped}（应 ≥1） 低优先级已停={lowGone}");
        }

        // ── 工具 ────────────────────────────────────────────────────

        private static void CleanUp(DialogueManager mgr)
        {
            mgr.Stop(LowPri);
            mgr.Stop(HighPri);
        }

        private static void Pass(string name, string detail)
        {
            _pass++;
            _sb.AppendLine($"  ✅ {name,-10} {detail}");
        }

        private static void Fail(string name, string detail)
        {
            _fail++;
            _sb.AppendLine($"  ❌ {name,-10} {detail}");
        }
    }
}
