using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RenneAutoTest
{
    [Serializable]
    public class AutoTestCommand
    {
        public int    seq;                       // 递增序号，桥只执行没消费过的序号
        public string action = "";               // run / stop / compile
        public int    turns = 10;
        public float  timeout = 180f;
        public float  playerDelay = 0.15f;
        public bool   autoPassPlayerTurn = true;
        public bool   stopOnError = false;
        public string scene = "";
        public string note = "";
        public string language = "";         // 强制语言 zh/en，空 = 用玩家设置
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestBridge —— Unity 编辑器已经打开时的通信桥
    //
    // batchmode 没法在编辑器开着的时候用（Library 被 Temp/UnityLockfile 锁住），
    // 所以这里用「命令文件轮询」的方式驱动已打开的编辑器：
    //
    //   CLI 写 Logs/AutoTest/command.json  (seq 自增)
    //        ↓ 本桥每 0.5s 轮询一次，发现新 seq
    //   写 run-config.json → 打开场景 → EnterPlaymode
    //        ↓ AutoTestDriver 跑完写 finished.json 并退出 PlayMode
    //   本桥把结果汇总进 status.json
    //        ↓ CLI 轮询到 state=done，打印摘要
    //
    // 编辑器里也能手动用：菜单 Tools > AutoTest > 控制台
    // ══════════════════════════════════════════════════════════════════════════

    [InitializeOnLoad]
    public static class AutoTestBridge
    {
        const double PollInterval = 0.5;
        const string ConsumedFile = "consumed.txt";

        static double _nextPoll;
        static bool   _waitingForPlaymodeEnd;

        static AutoTestBridge()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ══════════════════════════════════════════════════════════════
        // 轮询命令文件
        // ══════════════════════════════════════════════════════════════

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;

            // batchmode 下由 AutoTestBatch 直接驱动，桥不要插手
            if (Application.isBatchMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            var cmd = ReadCommand();
            if (cmd == null || cmd.seq <= ReadConsumedSeq()) return;

            // ⚠ 关键：编辑器失焦时 Unity 不会自动导入磁盘上改过的脚本。
            // 此时直接进 PlayMode 跑的是**旧程序集**，测的根本不是刚改的代码——
            // 现象是改完跑一遍、日志却还是老样子，极难察觉。所以执行前强制导入一次。
            //
            // 这里刻意**先不消费 seq**：Refresh 若触发重新编译会引发程序集重载，
            // 本类的静态字段全部清空、Execute 也执行不完。不消费的话，重载后下一轮
            // 轮询会看到同一个未消费的 seq，那时编译已结束，再正常执行。
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.Log("[AutoTestBridge] 检测到脚本改动，先编译，编译完再执行命令");
                return;
            }

            WriteConsumedSeq(cmd.seq);
            Execute(cmd);
        }

        static void Execute(AutoTestCommand cmd)
        {
            switch ((cmd.action ?? "").ToLowerInvariant())
            {
                case "walltiles":
                    try { global::BuildingWallColours.Apply();global::BuildingWallColours.CaptureEditorPreview();AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":0"); }
                    catch(Exception error) {Debug.LogException(error);Directory.CreateDirectory("Artifacts/WallColourRevision");File.WriteAllText("Artifacts/WallColourRevision/tiles-error.txt",error.ToString());AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":1");}
                    break;
                case "hudsetup":
                    try { global::SurvivalHUDSetup.Apply(); AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":0"); }
                    catch(Exception error) { Debug.LogException(error);Directory.CreateDirectory("Artifacts/SurvivalHUD");File.WriteAllText("Artifacts/SurvivalHUD/error.txt",error.ToString());AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":1"); }
                    break;
                case "enemytest":
                    try { global::EnemyRangeTestSetup.Apply();AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":0"); }
                    catch(Exception error){Debug.LogException(error);Directory.CreateDirectory("Artifacts/EnemyRangeTest");File.WriteAllText("Artifacts/EnemyRangeTest/error.txt",error.ToString());AutoTestStatus.Write("done","",cmd.action,cmd.seq,"\"exitCode\":1");}
                    break;
                case "openscene":
                    if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Test.unity")
                    {
                        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())break;
                        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
                    }
                    var sceneView=ScriptableObject.CreateInstance<SceneView>();
                    sceneView.titleContent=new GUIContent("Renne - Test Scene");
                    sceneView.ShowUtility();sceneView.Focus();
                    AutoTestStatus.Write("done", "", cmd.action, cmd.seq, "\"exitCode\":0,\"reason\":\"scene_opened\"");
                    break;
                case "roomcamera":
                    try { global::RoomCameraSetup.Apply(); AutoTestStatus.Write("done", "", cmd.action, cmd.seq, "\"exitCode\":0"); }
                    catch(Exception error) { Debug.LogException(error); System.IO.File.WriteAllText("Artifacts/Visibility/room-error.txt",error.ToString()); AutoTestStatus.Write("done", "", cmd.action, cmd.seq, "\"exitCode\":1"); }
                    break;
                case "run":     ExecuteRun(cmd);     break;
                case "stop":    ExecuteStop(cmd);    break;
                case "compile": ExecuteCompile(cmd); break;
                case "save":
                    AssetDatabase.SaveAssets();
                    bool saved = EditorSceneManager.SaveOpenScenes();
                    AutoTestStatus.Write("done", "", cmd.action, cmd.seq,
                        saved ? "\"exitCode\":0,\"reason\":\"scenes_and_assets_saved\"" : "\"exitCode\":1,\"reason\":\"scene_save_failed\"");
                    break;
                case "visibility":
                    try
                    {
                        global::PlayerVisibilitySetup.Apply();
                        global::PlayerVisibilityValidation.Validate();
                        AutoTestStatus.Write("done", "", cmd.action, cmd.seq, "\"exitCode\":0,\"reason\":\"visibility_validated\"");
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                        AutoTestStatus.Write("done", "", cmd.action, cmd.seq, "\"exitCode\":1,\"reason\":\"visibility_validation_failed\"");
                    }
                    break;
                default:
                    Debug.LogWarning($"[AutoTestBridge] 未知命令：{cmd.action}");
                    AutoTestStatus.Write("error", "", cmd.action, cmd.seq,
                                         $"\"error\":\"unknown_action\"");
                    break;
            }
        }

        static void ExecuteRun(AutoTestCommand cmd)
        {
            var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogError("[AutoTestBridge] 脚本存在编译错误，拒绝进入 PlayMode");
                AutoTestStatus.Write("done", runId, "run", cmd.seq,
                    "\"exitCode\":6,\"reason\":\"compile_failed\",\"turns\":0,\"errors\":1");
                return;
            }

            if (!string.IsNullOrEmpty(cmd.scene) && !OpenSceneByName(cmd.scene))
            {
                AutoTestStatus.Write("done", runId, "run", cmd.seq,
                    $"\"exitCode\":5,\"reason\":\"scene_not_found\",\"scene\":\"{cmd.scene}\"");
                return;
            }

            var cfg = new AutoTestRunConfig
            {
                enabled            = true,
                runId              = runId,
                maxTurns           = cmd.turns,
                maxSeconds         = cmd.timeout,
                playerTurnDelay    = cmd.playerDelay,
                autoPassPlayerTurn = cmd.autoPassPlayerTurn,
                stopOnError        = cmd.stopOnError,
                note               = cmd.note ?? "",
                language           = cmd.language ?? "",
            };
            AutoTestSession.SaveConfig(cfg);
            ClearFinishedSignal();

            AutoTestStatus.Write("running", runId, "run", cmd.seq, "\"mode\":\"bridge\"");
            _waitingForPlaymodeEnd = true;

            Debug.Log($"[AutoTestBridge] 开始运行 runId={runId} turns={cmd.turns} timeout={cmd.timeout}s");
            EditorApplication.EnterPlaymode();
        }

        static void ExecuteStop(AutoTestCommand cmd)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[AutoTestBridge] 收到 stop，退出 PlayMode");
                EditorApplication.ExitPlaymode();
            }
            AutoTestStatus.Write("done", "", "stop", cmd.seq,
                                 "\"exitCode\":0,\"reason\":\"stopped_by_user\"");
        }

        static void ExecuteCompile(AutoTestCommand cmd)
        {
            bool failed = EditorUtility.scriptCompilationFailed;
            Debug.Log($"[AutoTestBridge] 编译状态检查：{(failed ? "有编译错误" : "通过")}");
            AutoTestStatus.Write("done", "compile", "compile", cmd.seq,
                $"\"exitCode\":{(failed ? 6 : 0)},\"reason\":\"{(failed ? "compile_failed" : "compile_ok")}\"");
        }

        // ══════════════════════════════════════════════════════════════
        // PlayMode 结束后汇总结果
        // ══════════════════════════════════════════════════════════════

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            if (!_waitingForPlaymodeEnd) return;
            _waitingForPlaymodeEnd = false;

            // 正常收尾时 AutoTestDriver 在退出 PlayMode 前写了 finished.json
            var finished = ReadFinishedSignal();

            // 没有 finished.json 说明驱动器没走到 Finish()，最常见的原因是
            // 用户手动点了停止按钮。但 AutoTestSession 在 Application.quitting 里
            // 兜底写过 summary.json，数据是全的，从那儿还原结果比直接报
            // playmode_ended_without_result 有用得多
            if (finished == null) finished = SynthesizeFromSummary();

            if (finished == null)
            {
                AutoTestStatus.Write("done", "", "run", ReadConsumedSeq(),
                    "\"exitCode\":2,\"reason\":\"playmode_ended_without_result\",\"turns\":0");
                Debug.LogWarning("[AutoTestBridge] PlayMode 结束但既没有 finished.json 也没有 summary.json");
                return;
            }

            AutoTestStatus.Write("done", ExtractString(finished, "runId"), "run", ReadConsumedSeq(),
                                 TrimBraces(finished));
            Debug.Log($"[AutoTestBridge] 运行完成：{finished}");
        }

        /// <summary>
        /// 从 latest/summary.json 还原一份和 finished.json 同构的结果。
        /// 用于 PlayMode 被手动中断、驱动器来不及写 finished.json 的情况。
        /// </summary>
        static string SynthesizeFromSummary()
        {
            try
            {
                var p = Path.Combine(AutoTestPaths.Root, "latest", "summary.json");
                if (!File.Exists(p)) return null;

                var json = File.ReadAllText(p);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var runId  = ExtractString(json, "runId");
                var reason = ExtractString(json, "reason");
                int turns  = ExtractInt(json, "turnsCompleted");
                int errs   = ExtractInt(json, "errors") + ExtractInt(json, "exceptions");

                int exitCode;
                if      (errs > 0)                  exitCode = 1;
                else if (reason == "timeout")       exitCode = 2;
                else if (reason == "stopped_on_error") exitCode = 3;
                else if (reason == "no_turnsystem") exitCode = 4;
                else if (reason == "completed")     exitCode = 0;
                else                                exitCode = 8; // 手动中断 / 非正常收尾

                return "{" +
                    AutoTestSession.J("runId", runId) + "," +
                    AutoTestSession.J("reason", reason) + "," +
                    AutoTestSession.JNum("exitCode", exitCode) + "," +
                    AutoTestSession.JNum("turns", turns) + "," +
                    AutoTestSession.JNum("errors", errs) + "," +
                    AutoTestSession.JBool("recoveredFromSummary", true) + "}";
            }
            catch { return null; }
        }

        // ══════════════════════════════════════════════════════════════
        // 文件辅助
        // ══════════════════════════════════════════════════════════════

        static AutoTestCommand ReadCommand()
        {
            try
            {
                if (!File.Exists(AutoTestPaths.Command)) return null;
                var json = ReadAllTextShared(AutoTestPaths.Command);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<AutoTestCommand>(json);
            }
            catch { return null; }
        }

        static string ReadAllTextShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                return sr.ReadToEnd();
        }

        static int ReadConsumedSeq()
        {
            try
            {
                var p = Path.Combine(AutoTestPaths.Root, ConsumedFile);
                if (!File.Exists(p)) return 0;
                return int.TryParse(File.ReadAllText(p).Trim(), out var v) ? v : 0;
            }
            catch { return 0; }
        }

        static void WriteConsumedSeq(int seq)
        {
            try
            {
                AutoTestPaths.EnsureRoot();
                File.WriteAllText(Path.Combine(AutoTestPaths.Root, ConsumedFile),
                                  seq.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }

        static string FinishedPath => Path.Combine(AutoTestPaths.Root, "finished.json");

        static void ClearFinishedSignal()
        {
            try { if (File.Exists(FinishedPath)) File.Delete(FinishedPath); } catch { }
        }

        static string ReadFinishedSignal()
        {
            try { return File.Exists(FinishedPath) ? File.ReadAllText(FinishedPath) : null; }
            catch { return null; }
        }

        static string TrimBraces(string json)
        {
            var s = (json ?? "").Trim();
            if (s.StartsWith("{")) s = s.Substring(1);
            if (s.EndsWith("}"))   s = s.Substring(0, s.Length - 1);
            return s;
        }

        /// <summary>从极简 JSON 里抠一个数字字段，失败返回 0。</summary>
        static int ExtractInt(string json, string key)
        {
            try
            {
                var needle = $"\"{key}\":";
                int i = json.IndexOf(needle, StringComparison.Ordinal);
                if (i < 0) return 0;
                i += needle.Length;

                int j = i;
                while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '-' || json[j] == '.')) j++;
                if (j == i) return 0;

                var slice = json.Substring(i, j - i);
                return (int)float.Parse(slice, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        static string ExtractString(string json, string key)
        {
            try
            {
                var needle = $"\"{key}\":\"";
                int i = json.IndexOf(needle, StringComparison.Ordinal);
                if (i < 0) return "";
                i += needle.Length;
                int j = json.IndexOf('"', i);
                return j < 0 ? "" : json.Substring(i, j - i);
            }
            catch { return ""; }
        }

        internal static bool OpenSceneByName(string name)
        {
            try
            {
                if (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && File.Exists(name))
                {
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    EditorSceneManager.OpenScene(name, OpenSceneMode.Single);
                    return true;
                }

                var match = AssetDatabase.FindAssets("t:Scene")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                                             .Equals(name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(match))
                {
                    Debug.LogError($"[AutoTestBridge] 找不到场景 [{name}]");
                    return false;
                }

                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                EditorSceneManager.OpenScene(match, OpenSceneMode.Single);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoTestBridge] 打开场景失败：{e.Message}");
                return false;
            }
        }
    }
}
