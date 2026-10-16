using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RenneAutoTest
{
    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestBatch —— batchmode 入口（Unity 编辑器没开时走这条路）
    //
    // 由 Tools/autotest.ps1 这样调用：
    //   Unity.exe -batchmode -nographics -projectPath <proj> -logFile <log>
    //             -executeMethod RenneAutoTest.AutoTestBatch.RunPlayMode
    //             -autotestRunId 20260928-101500 -autotestTurns 10 -autotestTimeout 180
    //
    // ⚠ 命令行里不要加 -quit：本方法返回后 Unity 必须继续活着跑 PlayMode，
    //   退出由 AutoTestDriver 调 EditorApplication.Exit(code) 负责。
    // ══════════════════════════════════════════════════════════════════════════

    public static class AutoTestBatch
    {
        const int ExitEnterPlaymodeFailed = 5;
        const int ExitWatchdog            = 2;

        static double _deadline;
        static bool   _everEnteredPlaymode;

        // ══════════════════════════════════════════════════════════════
        // 入口 1：跑 PlayMode + 模拟回合
        // ══════════════════════════════════════════════════════════════

        public static void RunPlayMode()
        {
            var cfg = new AutoTestRunConfig
            {
                enabled            = true,
                runId              = Arg("autotestRunId", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)),
                maxTurns           = ArgInt("autotestTurns", 10),
                maxSeconds         = ArgFloat("autotestTimeout", 180f),
                playerTurnDelay    = ArgFloat("autotestPlayerDelay", 0.15f),
                autoPassPlayerTurn = ArgBool("autotestNoAutoPass") ? false : true,
                stopOnError        = ArgBool("autotestStopOnError"),
                note               = Arg("autotestNote", ""),
                probePathfinding   = ArgBool("autotestProbePaths"),
                probeDialogue      = ArgBool("autotestProbeDialogue"),
                language           = Arg("autotestLanguage", ""),
            };

            AutoTestPaths.EnsureRoot();
            AutoTestSession.SaveConfig(cfg);
            AutoTestStatus.Write("running", cfg.runId, action: "run", extra: "\"mode\":\"batch\"");

            Log($"配置就绪 runId={cfg.runId} turns={cfg.maxTurns} timeout={cfg.maxSeconds}s");

            var scene = Arg("autotestScene", "");
            if (!string.IsNullOrEmpty(scene) && !OpenSceneByName(scene))
            {
                Log($"指定的场景 [{scene}] 找不到，直接退出");
                AutoTestStatus.Write("error", cfg.runId, action: "run",
                                     extra: $"\"error\":\"scene_not_found:{scene}\"");
                EditorApplication.Exit(ExitEnterPlaymodeFailed);
                return;
            }

            // 看门狗：PlayMode 起不来或 Driver 没能正常收尾时兜底退出，防止 CI/CLI 挂死
            _deadline = EditorApplication.timeSinceStartup + cfg.maxSeconds + 90.0;
            EditorApplication.update += Watchdog;

            Log("进入 PlayMode …");
            EditorApplication.EnterPlaymode();
        }

        static void Watchdog()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
                _everEnteredPlaymode = true;

            if (EditorApplication.timeSinceStartup < _deadline) return;

            EditorApplication.update -= Watchdog;
            Log(_everEnteredPlaymode
                ? "看门狗超时：PlayMode 已启动但驱动器未在预期时间内收尾"
                : "看门狗超时：PlayMode 始终没有启动");

            AutoTestSession.End("watchdog_timeout");
            EditorApplication.Exit(_everEnteredPlaymode ? ExitWatchdog : ExitEnterPlaymodeFailed);
        }

        // ══════════════════════════════════════════════════════════════
        // 入口 2：只验证脚本能编译
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 只要 Unity 能执行到这个方法，就说明所有脚本编译通过了
        /// （有编译错误时 Unity 根本不会调用 -executeMethod，会直接以非 0 退出）。
        /// </summary>
        public static void CompileOnly()
        {
            AutoTestPaths.EnsureRoot();
            AutoTestStatus.Write("done", "compile", action: "compile",
                                 extra: "\"exitCode\":0,\"reason\":\"compile_ok\"");
            Log("脚本编译通过");
            EditorApplication.Exit(0);
        }

        // ══════════════════════════════════════════════════════════════
        // 工具
        // ══════════════════════════════════════════════════════════════

        static bool OpenSceneByName(string name)
        {
            try
            {
                // 允许直接传路径
                if (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && File.Exists(name))
                {
                    EditorSceneManager.OpenScene(name, OpenSceneMode.Single);
                    return true;
                }

                var guids = AssetDatabase.FindAssets("t:Scene");
                var match = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                                             .Equals(name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(match)) return false;

                EditorSceneManager.OpenScene(match, OpenSceneMode.Single);
                Log($"已打开场景 {match}");
                return true;
            }
            catch (Exception e)
            {
                Log($"打开场景失败：{e.Message}");
                return false;
            }
        }

        internal static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("-" + name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return fallback;
        }

        internal static bool ArgBool(string name)
        {
            return Environment.GetCommandLineArgs()
                              .Any(a => a.Equals("-" + name, StringComparison.OrdinalIgnoreCase));
        }

        internal static int ArgInt(string name, int fallback)
            => int.TryParse(Arg(name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        internal static float ArgFloat(string name, float fallback)
            => float.TryParse(Arg(name, null), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static void Log(string msg) => Debug.Log($"[AutoTestBatch] {msg}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // status.json 读写（CLI 靠轮询这个文件知道跑到哪一步了）
    // ══════════════════════════════════════════════════════════════════════════

    internal static class AutoTestStatus
    {
        public static void Write(string state, string runId, string action = "",
                                 int seq = -1, string extra = null)
        {
            try
            {
                AutoTestPaths.EnsureRoot();
                var sb = new System.Text.StringBuilder();
                sb.Append('{');
                sb.Append(AutoTestSession.J("state", state)).Append(',');
                sb.Append(AutoTestSession.J("runId", runId ?? "")).Append(',');
                sb.Append(AutoTestSession.J("action", action ?? "")).Append(',');
                sb.Append(AutoTestSession.JNum("seq", seq)).Append(',');
                sb.Append(AutoTestSession.JBool("compileFailed", EditorUtility.scriptCompilationFailed)).Append(',');
                sb.Append(AutoTestSession.J("updatedAt",
                    DateTime.Now.ToString("s", CultureInfo.InvariantCulture)));
                if (!string.IsNullOrEmpty(extra)) sb.Append(',').Append(extra);
                sb.Append('}');

                File.WriteAllText(AutoTestPaths.Status, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] 写 status.json 失败：{e.Message}");
            }
        }
    }
}
