using System.Collections;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RenneAutoTest
{
    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestDriver —— 回合模拟驱动器
    //
    // 由 AutoTestSession.Bootstrap() 在场景加载前自动挂载，不需要往场景里拖任何东西。
    //
    // 干三件事：
    //   1. 替玩家结束回合（否则 TurnSystem 会停在 WaitingForInput 永远不往下走）
    //   2. 每个回合开始/结束时把游戏状态快照写进 turns.jsonl
    //   3. 跑满 maxTurns / 超时 / 出错时收尾，batchmode 下按结果设置进程退出码
    //
    // 退出码：0=通过  1=有 Error/Exception  2=超时  3=stopOnError 提前中断
    //         4=场景里没有 TurnSystem
    // ══════════════════════════════════════════════════════════════════════════

    public class AutoTestDriver : MonoBehaviour
    {
        /// <summary>已完成的全局回合数，供 summary 读取</summary>
        public static int TurnsCompleted { get; private set; }

        AutoTestRunConfig _cfg;
        TurnSystem   _turns;
        MissionManager _missions;
        float _startTime;
        bool  _finishing;
        bool  _turnSystemWarned;
        bool  _dialogueProbed;
        int   _lastGlobalTurn = -1;

        void Awake()
        {
            _cfg = AutoTestSession.Config ?? new AutoTestRunConfig();
            _startTime = Time.realtimeSinceStartup;
            TurnsCompleted = 0;
        }

        void OnDestroy()
        {
            Unhook();
        }

        void Update()
        {
            // 日志队列落盘（logMessageReceivedThreaded 可能来自子线程，只能在这里写）
            AutoTestSession.Flush();

            if (_finishing) return;

            TryHook();

            float elapsed = Time.realtimeSinceStartup - _startTime;

            // ── 超时保护 ─────────────────────────────────────────────
            if (_cfg.maxSeconds > 0f && elapsed > _cfg.maxSeconds)
            {
                Finish("timeout", 2);
                return;
            }

            // ── 场景里压根没有 TurnSystem：给 10 秒宽限期后报错退出 ──
            if (_turns == null && elapsed > 10f)
            {
                if (!_turnSystemWarned)
                {
                    _turnSystemWarned = true;
                    AutoTestSession.WriteConsoleLine(
                        "[AutoTest] 场景中找不到 TurnSystem，无法模拟回合。" +
                        "请用 -Scene 指定正确的场景，或检查场景里是否有 TurnSystem 组件。");
                }
                Finish("no_turnsystem", 4);
                return;
            }

            // ── stopOnError ──────────────────────────────────────────
            if (_cfg.stopOnError && AutoTestSession.FailureCount > 0)
            {
                Finish("stopped_on_error", 3);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 事件挂接（TurnSystem / MissionManager 在场景 Awake 才出现，需轮询）
        // ══════════════════════════════════════════════════════════════

        void TryHook()
        {
            if (_turns == null && TurnSystem.Instance != null)
            {
                _turns = TurnSystem.Instance;
                _turns.OnTurnStart += OnTurnStart;
                _turns.OnTurnEnd   += OnTurnEnd;
                AutoTestSession.WriteConsoleLine(
                    $"[AutoTest] 已挂接 TurnSystem，当前全局回合 #{_turns.GlobalTurnNumber}");

                // 网格这时已经建好了，跑寻路基准正好
                if (_cfg.probePathfinding) AutoTestPathProbe.Run();

                // 挂接时可能已经错过了第一个回合的 OnTurnStart，补一次玩家回合处理
                if (_turns.CurrentFaction == TurnFaction.Player)
                    ScheduleEndPlayerTurn();
            }

            if (_missions == null && MissionManager.Instance != null)
            {
                _missions = MissionManager.Instance;
                _missions.OnMissionSettled += OnMissionSettled;
                AutoTestSession.WriteConsoleLine("[AutoTest] 已挂接 MissionManager");
            }

            // 对话探针要等 DialogueManager 和至少两个 DialogueSpeaker 就绪
            // （All 是 IEnumerable，没有 Count，这里数够两个就够了）
            if (!_dialogueProbed && _cfg.probeDialogue && DialogueManager.Instance != null)
            {
                int n = 0;
                foreach (var sp in DialogueSpeaker.All)
                {
                    if (sp != null && sp.isActiveAndEnabled) n++;
                    if (n >= 2) break;
                }

                if (n >= 2)
                {
                    _dialogueProbed = true;
                    AutoTestDialogueProbe.Run();
                }
            }
        }

        void Unhook()
        {
            if (_turns != null)
            {
                _turns.OnTurnStart -= OnTurnStart;
                _turns.OnTurnEnd   -= OnTurnEnd;
                _turns = null;
            }
            if (_missions != null)
            {
                _missions.OnMissionSettled -= OnMissionSettled;
                _missions = null;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 回合流转
        // ══════════════════════════════════════════════════════════════

        void OnTurnStart(TurnData data)
        {
            if (_finishing || data == null) return;

            if (data.globalTurnNumber != _lastGlobalTurn)
            {
                _lastGlobalTurn = data.globalTurnNumber;
                TurnsCompleted  = Mathf.Max(TurnsCompleted, data.globalTurnNumber - 1);
            }

            // 跑满了：在新回合开始的瞬间收尾，保证最后一个回合的结算已经写完
            if (_cfg.maxTurns > 0 && data.globalTurnNumber > _cfg.maxTurns)
            {
                TurnsCompleted = _cfg.maxTurns;
                Snapshot("run_end", data);
                Finish("completed", AutoTestSession.FailureCount > 0 ? 1 : 0);
                return;
            }

            Snapshot("turn_start", data);

            // 玩家回合不会自己结束（等输入），替它按一下结束回合
            if (data.currentFaction == TurnFaction.Player && _cfg.autoPassPlayerTurn)
                ScheduleEndPlayerTurn();
        }

        void OnTurnEnd(TurnData data)
        {
            if (_finishing || data == null) return;

            // 注意：MissionManager 在 Start() 里订阅 OnTurnEnd，比本驱动器早，
            // 所以这里拿到的已经是本回合任务结算之后的状态
            Snapshot("turn_end", data);

            // 一个"全局回合"要等 turnOrder 里所有阵营都走完才算数。
            // 下一个阵营是起始阵营（Player）时说明本轮刚好走完，这时才 +1，
            // 否则 Player 回合一结束就计满，数字会虚高
            if (_turns != null && _turns.GetNextFaction() == TurnFaction.Player)
                TurnsCompleted = Mathf.Max(TurnsCompleted, data.globalTurnNumber);
        }

        void ScheduleEndPlayerTurn()
        {
            // 必须延后到下一帧：OnTurnStart 是在 TurnSystem.StartNewTurn 内部触发的，
            // 此刻同步调 EndCurrentTurn 会撞上 isProcessingTurn 保护直接被丢弃
            StartCoroutine(EndPlayerTurnNextFrame());
        }

        IEnumerator EndPlayerTurnNextFrame()
        {
            yield return null;
            if (_cfg.playerTurnDelay > 0f)
                yield return new WaitForSeconds(_cfg.playerTurnDelay);

            if (_finishing || _turns == null) yield break;
            if (!_turns.IsSystemActive) yield break;
            if (_turns.CurrentFaction != TurnFaction.Player) yield break;

            // 等玩家单位的移动协程跑完再结束，避免打断正在进行的动作
            float guard = Time.realtimeSinceStartup + 5f;
            while (IsPlayerBusy() && Time.realtimeSinceStartup < guard)
                yield return null;

            if (_finishing || _turns == null) yield break;
            if (_turns.CurrentFaction != TurnFaction.Player) yield break;
            if (_turns.IsProcessingTurn) yield break;

            _turns.EndCurrentTurn();
        }

        static bool IsPlayerBusy()
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) return false;
            var mov = go.GetComponent<UnitMovement>();
            return mov != null && mov.IsMoving;
        }

        void OnMissionSettled(MissionData data, bool success)
        {
            if (data == null) return;
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append(AutoTestSession.J("hook", "mission_settled")).Append(',');
            sb.Append(AutoTestSession.JNum("globalTurn", _turns != null ? _turns.GlobalTurnNumber : 0)).Append(',');
            sb.Append(AutoTestSession.J("missionId", data.missionId ?? "")).Append(',');
            sb.Append(AutoTestSession.J("displayName", data.displayName ?? "")).Append(',');
            sb.Append(AutoTestSession.JBool("success", success)).Append(',');
            sb.Append(AutoTestSession.JNum("planProgress", _missions != null ? _missions.PlanProgress : 0f));
            sb.Append('}');
            AutoTestSession.WriteTurnRecord(sb.ToString());

            AutoTestSession.WriteConsoleLine(
                $"[AutoTest] >>> 任务结算 [{data.missionId}] {(success ? "成功" : "失败")} " +
                $"| PlanProgress={(_missions != null ? _missions.PlanProgress : 0f):F1}");
        }

        // ══════════════════════════════════════════════════════════════
        // 状态快照
        // ══════════════════════════════════════════════════════════════

        void Snapshot(string hook, TurnData data)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append(AutoTestSession.J("hook", hook)).Append(',');
            sb.Append(AutoTestSession.JNum("globalTurn", data.globalTurnNumber)).Append(',');
            sb.Append(AutoTestSession.J("faction", data.currentFaction.ToString())).Append(',');
            sb.Append(AutoTestSession.JNum("t", Time.realtimeSinceStartup - _startTime)).Append(',');
            sb.Append(AutoTestSession.JNum("errors", AutoTestSession.FailureCount)).Append(',');

            AppendGamePhase(sb);
            AppendMissions(sb);
            AppendEnemies(sb);
            AppendPlayer(sb);

            sb.Append('}');
            AutoTestSession.WriteTurnRecord(sb.ToString());
        }

        void AppendGamePhase(StringBuilder sb)
        {
            string phaseId = "";
            float progress = 0f;
            if (GameManager.Instance != null)
            {
                var pd = GameManager.Instance.GetCurrentPhaseData();
                if (pd != null) phaseId = pd.id ?? "";
            }
            if (_missions != null) progress = _missions.PlanProgress;

            sb.Append(AutoTestSession.J("gamePhase", phaseId)).Append(',');
            sb.Append(AutoTestSession.JNum("planProgress", progress)).Append(',');
        }

        void AppendMissions(StringBuilder sb)
        {
            sb.Append("\"missions\":[");
            if (_missions != null && _missions.ActiveMissions != null)
            {
                bool first = true;
                foreach (var m in _missions.ActiveMissions)
                {
                    if (m == null || m.data == null) continue;
                    if (!first) sb.Append(',');
                    first = false;

                    sb.Append('{');
                    sb.Append(AutoTestSession.J("id", m.data.missionId ?? "")).Append(',');
                    sb.Append(AutoTestSession.JNum("priority", m.priority)).Append(',');
                    sb.Append(AutoTestSession.JBool("active", m.isActive)).Append(',');
                    sb.Append(AutoTestSession.JNum("repeatsDone", m.repeatsDone)).Append(',');
                    sb.Append(AutoTestSession.JNum("repeatCount", m.data.repeatCount)).Append(',');
                    sb.Append(AutoTestSession.JNum("turnCount", m.turnCount)).Append(',');
                    sb.Append(AutoTestSession.JNum("apConsumed", m.totalAPConsumed)).Append(',');
                    sb.Append(AutoTestSession.J("profile", m.data.behaviourProfile.ToString())).Append(',');
                    sb.Append(AutoTestSession.J("phase", m.currentPhase ?? ""));

                    if (m.turnsInZone != null && m.turnsInZone.Count > 0)
                    {
                        sb.Append(",\"turnsInZone\":{");
                        bool f2 = true;
                        foreach (var kv in m.turnsInZone)
                        {
                            if (!f2) sb.Append(',');
                            f2 = false;
                            sb.Append(AutoTestSession.JNum(kv.Key, kv.Value));
                        }
                        sb.Append('}');
                    }
                    sb.Append('}');
                }
            }
            sb.Append("],");
        }

        void AppendEnemies(StringBuilder sb)
        {
            sb.Append("\"enemies\":[");
            var all = FindObjectsOfType<EnemyAIController>();
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null) continue;
                if (i > 0) sb.Append(',');

                var mov  = e.GetComponent<UnitMovement>();
                var unit = e.GetComponent<TurnBasedUnit>();
                var ctx  = e.CurrentMission;

                sb.Append('{');
                sb.Append(AutoTestSession.J("id", string.IsNullOrEmpty(e.EnemyId) ? e.name : e.EnemyId)).Append(',');
                sb.Append(AutoTestSession.JBool("alive", e.IsAlive)).Append(',');
                sb.Append(AutoTestSession.JNum("hp", e.CurrentHp)).Append(',');
                sb.Append(AutoTestSession.JNum("maxHp", e.MaxHp)).Append(',');
                if (mov != null)
                {
                    sb.Append(AutoTestSession.J("grid", $"{mov.CurrentGridPosition.x},{mov.CurrentGridPosition.y}")).Append(',');
                    sb.Append(AutoTestSession.JNum("floor", mov.CurrentFloor)).Append(',');
                }
                if (unit != null)
                    sb.Append(AutoTestSession.JNum("ap", unit.RemainingActionPoints)).Append(',');

                sb.Append(AutoTestSession.J("mission", ctx != null ? (ctx.missionId ?? "") : "")).Append(',');
                sb.Append(AutoTestSession.JNum("missionPriority", ctx != null ? ctx.priority : 0)).Append(',');
                sb.Append(AutoTestSession.J("targetZone", ctx != null ? (ctx.targetZoneId ?? "") : ""));
                sb.Append('}');
            }
            sb.Append("],");
        }

        void AppendPlayer(StringBuilder sb)
        {
            sb.Append("\"player\":");
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) { sb.Append("null"); return; }

            var ctrl = go.GetComponent<PlayerController>();
            var mov  = go.GetComponent<UnitMovement>();

            sb.Append('{');
            sb.Append(AutoTestSession.JNum("hp", ctrl != null ? ctrl.CurrentHp : 0)).Append(',');
            sb.Append(AutoTestSession.JNum("maxHp", ctrl != null ? ctrl.MaxHp : 0));
            if (mov != null)
            {
                sb.Append(',');
                sb.Append(AutoTestSession.J("grid", $"{mov.CurrentGridPosition.x},{mov.CurrentGridPosition.y}")).Append(',');
                sb.Append(AutoTestSession.JNum("floor", mov.CurrentFloor));
            }
            sb.Append('}');
        }

        // ══════════════════════════════════════════════════════════════
        // 收尾
        // ══════════════════════════════════════════════════════════════

        void Finish(string reason, int exitCode)
        {
            if (_finishing) return;
            _finishing = true;

            // 有 Error 时即使 reason=completed 也要把退出码抬成 1
            if (reason == "completed" && AutoTestSession.FailureCount > 0) exitCode = 1;

            Unhook();
            StartCoroutine(FinishRoutine(reason, exitCode));
        }

        IEnumerator FinishRoutine(string reason, int exitCode)
        {
            // 多等两帧，让本帧内还在排队的日志和协程收尾
            yield return null;
            yield return null;

            AutoTestSession.Flush();
            AutoTestSession.End(reason);

            Debug.Log($"[AutoTest] 运行结束：reason={reason} exitCode={exitCode} " +
                      $"turns={TurnsCompleted}");

#if UNITY_EDITOR
            AutoTestBridgeSignal.WriteFinished(reason, exitCode, TurnsCompleted);

            if (Application.isBatchMode)
            {
                // batchmode 下没有 Editor 桥接手，只能由运行时自己把 status 写成 done，
                // 否则 CLI 的 status 子命令会一直显示上一次的 running
                AutoTestBridgeSignal.WriteDoneStatus(reason, exitCode, TurnsCompleted);
                EditorApplication.Exit(exitCode);
            }
            else
            {
                EditorApplication.isPlaying = false;
            }
#else
            Application.Quit(exitCode);
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 运行时 → Editor 桥的单向信号。
    /// PlayMode 结束时 Editor 侧的 AutoTestBridge 会读走这个文件并写进 status.json，
    /// 这样 CLI 在「编辑器已打开」模式下也能拿到退出码。
    /// </summary>
    internal static class AutoTestBridgeSignal
    {
        public static void WriteFinished(string reason, int exitCode, int turns)
        {
            try
            {
                AutoTestPaths.EnsureRoot();
                var path = System.IO.Path.Combine(AutoTestPaths.Root, "finished.json");
                var json = "{" +
                    AutoTestSession.J("runId", AutoTestSession.RunId ?? "") + "," +
                    AutoTestSession.J("reason", reason) + "," +
                    AutoTestSession.JNum("exitCode", exitCode) + "," +
                    AutoTestSession.JNum("turns", turns) + "," +
                    AutoTestSession.JNum("errors", AutoTestSession.FailureCount) + "}";
                System.IO.File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>batchmode 专用：直接把 status.json 写成 done，CLI 才能看到正确状态。</summary>
        public static void WriteDoneStatus(string reason, int exitCode, int turns)
        {
            try
            {
                AutoTestPaths.EnsureRoot();
                var json = "{" +
                    AutoTestSession.J("state", "done") + "," +
                    AutoTestSession.J("runId", AutoTestSession.RunId ?? "") + "," +
                    AutoTestSession.J("action", "run") + "," +
                    AutoTestSession.JNum("seq", -1) + "," +
                    AutoTestSession.JBool("compileFailed", false) + "," +
                    AutoTestSession.J("mode", "batch") + "," +
                    AutoTestSession.J("reason", reason) + "," +
                    AutoTestSession.JNum("exitCode", exitCode) + "," +
                    AutoTestSession.JNum("turns", turns) + "," +
                    AutoTestSession.JNum("errors", AutoTestSession.FailureCount) + "," +
                    AutoTestSession.J("updatedAt",
                        System.DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)) + "}";
                System.IO.File.WriteAllText(AutoTestPaths.Status, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
#endif
}
