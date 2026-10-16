using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RenneAutoTest
{
    // ══════════════════════════════════════════════════════════════════════════
    // AutoTestSession —— 自动化测试会话：日志采集 + 回合快照 + 结果落盘
    //
    // 生命周期：
    //   1. 进入 PlayMode 前，外部（CLI / Editor 桥）写好 Logs/AutoTest/run-config.json
    //   2. Bootstrap() 在场景加载前被 Unity 调用，读到 enabled=true 就开启会话
    //      并立刻把 enabled 置回 false（防止下次手动点播放又被自动驱动）
    //   3. 期间所有 Debug.Log / 未捕获异常写入 console.log + console.jsonl
    //   4. AutoTestDriver 每回合写一条快照到 turns.jsonl
    //   5. End() 写 summary.json，batchmode 下按错误数决定进程退出码
    //
    // 所有产物在 Logs/AutoTest/runs/<runId>/ 下（Logs/ 已在 .gitignore 里）
    // ══════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class AutoTestRunConfig
    {
        public bool   enabled;                     // 本次进入 PlayMode 是否启用自动化
        public string runId = "";                  // 运行编号，决定输出目录
        public int    maxTurns = 10;               // 跑满多少个全局回合后停止
        public float  maxSeconds = 180f;           // 墙钟超时（秒），防卡死
        public float  playerTurnDelay = 0.15f;     // 玩家回合自动跳过前的等待
        public bool   autoPassPlayerTurn = true;   // 是否自动替玩家结束回合
        public bool   stopOnError = false;         // 出现 Error/Exception 立即停止
        public int    maxErrorSamples = 80;        // summary.json 里最多保留多少条错误明细
        public string note = "";                   // 本次运行的备注，原样写进 summary
        public bool   probePathfinding = false;    // 跑一轮寻路直线度受控基准（AutoTestPathProbe）
        public bool   probeDialogue    = false;    // 跑对话优先级探针（AutoTestDialogueProbe）
        public string language        = "";       // 强制本次运行用哪种语言（zh / en），空 = 用玩家设置
    }

    public static class AutoTestPaths
    {
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string Root        => Path.Combine(ProjectRoot, "Logs", "AutoTest");
        public static string RunConfig   => Path.Combine(Root, "run-config.json");
        public static string Command     => Path.Combine(Root, "command.json");
        public static string Status      => Path.Combine(Root, "status.json");
        public static string LatestFile  => Path.Combine(Root, "latest.txt");
        public static string RunsRoot    => Path.Combine(Root, "runs");
        public static string RunDir(string runId) => Path.Combine(RunsRoot, runId);

        public static void EnsureRoot()
        {
            try { Directory.CreateDirectory(Root); Directory.CreateDirectory(RunsRoot); }
            catch (Exception e) { Debug.LogWarning($"[AutoTest] 创建输出目录失败：{e.Message}"); }
        }
    }

    public static class AutoTestSession
    {
        // ── 状态 ──────────────────────────────────────────────────────────────
        public static AutoTestRunConfig Config { get; private set; }
        public static bool   Active { get; private set; }
        public static string RunId  { get; private set; }
        public static string RunDir { get; private set; }

        public static int LogCount       { get; private set; }
        public static int WarnCount      { get; private set; }
        public static int ErrorCount     { get; private set; }
        public static int ExceptionCount { get; private set; }
        public static int FailureCount   => ErrorCount + ExceptionCount;

        static StreamWriter _console, _jsonl, _turns;
        static readonly object _gate = new object();
        static readonly List<PendingLog> _pending = new List<PendingLog>();
        static readonly List<string> _errorSamples = new List<string>();
        static float _startRealtime;
        static bool  _ended;

        // OnLog 可能在子线程被调用，那里碰 Time.* 会抛
        // "can only be called from the main thread"，所以主线程每帧把时间缓存下来
        static volatile float _nowCache;
        static volatile int   _frameCache;

        struct PendingLog
        {
            public LogType type;
            public string  message;
            public string  stack;
            public double  t;       // 会话内相对秒数
            public int     frame;
        }

        // ══════════════════════════════════════════════════════════════════
        // 入口：场景加载前由 Unity 自动调用
        // ══════════════════════════════════════════════════════════════════

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            var cfg = LoadConfig();
            if (cfg == null || !cfg.enabled) return;

            // 立刻消费掉 enabled 标记：一次配置只驱动一次 PlayMode
            cfg.enabled = false;
            SaveConfig(cfg);
            cfg.enabled = true; // 内存里保留 true，供 Driver 判断

            // 强制语言必须在任何系统读 Localization.Current 之前设好，
            // 否则 DialogueManager 可能已经用旧语言缓存了对话
            if (!string.IsNullOrEmpty(cfg.language))
            {
                var lang = cfg.language.Trim().ToLowerInvariant() == "en"
                    ? GameLanguage.English
                    : GameLanguage.ChineseSimplified;
                Localization.Set(lang);
                Debug.Log($"[AutoTest] 本次运行强制语言：{lang}");
            }

            Begin(cfg);

            // 挂一个常驻驱动器（场景切换不销毁）
            var go = new GameObject("[AutoTestDriver]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<AutoTestDriver>();
        }

        // ══════════════════════════════════════════════════════════════════
        // 配置读写
        // ══════════════════════════════════════════════════════════════════

        public static AutoTestRunConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(AutoTestPaths.RunConfig)) return null;
                var json = File.ReadAllText(AutoTestPaths.RunConfig, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<AutoTestRunConfig>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] 读取 run-config.json 失败：{e.Message}");
                return null;
            }
        }

        public static void SaveConfig(AutoTestRunConfig cfg)
        {
            try
            {
                AutoTestPaths.EnsureRoot();
                File.WriteAllText(AutoTestPaths.RunConfig,
                                  JsonUtility.ToJson(cfg, true), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] 写入 run-config.json 失败：{e.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 会话开关
        // ══════════════════════════════════════════════════════════════════

        static void Begin(AutoTestRunConfig cfg)
        {
            // 防御：Project Settings > Editor 里关掉 Domain Reload（快速进入 PlayMode）时，
            // 静态字段不会在两次播放之间重置，上一轮的会话可能还挂着。先收干净再开新的。
            if (Active) End("superseded_by_new_run");

            Config = cfg;
            RunId  = string.IsNullOrEmpty(cfg.runId)
                   ? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                   : cfg.runId;
            RunDir = AutoTestPaths.RunDir(RunId);

            LogCount = WarnCount = ErrorCount = ExceptionCount = 0;
            _errorSamples.Clear();
            _pending.Clear();
            _ended = false;
            _startRealtime = Time.realtimeSinceStartup;

            try
            {
                Directory.CreateDirectory(RunDir);
                _console = OpenShared(Path.Combine(RunDir, "console.log"));
                _jsonl   = OpenShared(Path.Combine(RunDir, "console.jsonl"));
                _turns   = OpenShared(Path.Combine(RunDir, "turns.jsonl"));
                File.WriteAllText(AutoTestPaths.LatestFile, RunId, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoTest] 无法创建日志文件：{e.Message}");
                return;
            }

            Active = true;
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += OnQuitting;

            WriteConsoleLine($"=== AutoTest run {RunId} 开始 | batchmode={Application.isBatchMode} " +
                             $"maxTurns={cfg.maxTurns} maxSeconds={cfg.maxSeconds} " +
                             $"autoPassPlayerTurn={cfg.autoPassPlayerTurn} stopOnError={cfg.stopOnError} ===");
        }

        static StreamWriter OpenShared(string path)
        {
            var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }

        static void OnQuitting()
        {
            if (Active && !_ended) End("application_quit");
        }

        /// <summary>结束会话，落盘 summary.json。reason 会原样写进摘要。</summary>
        public static void End(string reason)
        {
            if (!Active || _ended) return;
            _ended = true;

            Flush();
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= OnQuitting;

            float elapsed = Time.realtimeSinceStartup - _startRealtime;
            WriteConsoleLine($"=== AutoTest run {RunId} 结束 | reason={reason} " +
                             $"elapsed={elapsed:F1}s logs={LogCount} warnings={WarnCount} " +
                             $"errors={ErrorCount} exceptions={ExceptionCount} ===");

            TryWriteSummary(reason, elapsed);

            try { _console?.Dispose(); } catch { }
            try { _jsonl?.Dispose(); }   catch { }
            try { _turns?.Dispose(); }   catch { }
            _console = _jsonl = _turns = null;
            Active = false;
        }

        static void TryWriteSummary(string reason, float elapsed)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('{');
                sb.Append(J("runId", RunId)).Append(',');
                sb.Append(J("reason", reason)).Append(',');
                sb.Append(J("finishedAt", DateTime.Now.ToString("s", CultureInfo.InvariantCulture))).Append(',');
                sb.Append(JNum("elapsedSeconds", elapsed)).Append(',');
                sb.Append(JBool("batchmode", Application.isBatchMode)).Append(',');
                sb.Append(JNum("turnsCompleted", AutoTestDriver.TurnsCompleted)).Append(',');
                sb.Append(JNum("maxTurns", Config?.maxTurns ?? 0)).Append(',');
                sb.Append(JNum("logs", LogCount)).Append(',');
                sb.Append(JNum("warnings", WarnCount)).Append(',');
                sb.Append(JNum("errors", ErrorCount)).Append(',');
                sb.Append(JNum("exceptions", ExceptionCount)).Append(',');
                sb.Append(JBool("passed", FailureCount == 0 && reason != "timeout")).Append(',');
                sb.Append(J("note", Config?.note ?? "")).Append(',');
                sb.Append("\"errorSamples\":[");
                lock (_gate)
                {
                    for (int i = 0; i < _errorSamples.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(Esc(_errorSamples[i]));
                    }
                }
                sb.Append("]}");

                File.WriteAllText(Path.Combine(RunDir, "summary.json"), sb.ToString(), Encoding.UTF8);

                // 方便 CLI / Claude 直接读最近一次结果
                var latestDir = Path.Combine(AutoTestPaths.Root, "latest");
                Directory.CreateDirectory(latestDir);
                foreach (var name in new[] { "summary.json", "console.log", "turns.jsonl", "errors.log" })
                {
                    var src = Path.Combine(RunDir, name);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(latestDir, name), true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoTest] 写 summary 失败：{e.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 日志采集（可能来自非主线程，只入队不写盘）
        // ══════════════════════════════════════════════════════════════════

        static void OnLog(string message, string stack, LogType type)
        {
            var rec = new PendingLog
            {
                type    = type,
                message = message,
                stack   = (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                          ? stack : null,
                t       = _nowCache,      // ⚠ 不要改成 Time.realtimeSinceStartup：本方法可能在子线程
                frame   = _frameCache,
            };

            lock (_gate)
            {
                _pending.Add(rec);
                switch (type)
                {
                    case LogType.Warning:   WarnCount++;      break;
                    case LogType.Error:     ErrorCount++;     break;
                    case LogType.Assert:    ErrorCount++;     break;
                    case LogType.Exception: ExceptionCount++; break;
                    default:                LogCount++;       break;
                }

                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    && _errorSamples.Count < (Config?.maxErrorSamples ?? 80))
                {
                    var first = FirstStackLine(stack);
                    _errorSamples.Add(first == null ? $"[{type}] {message}"
                                                    : $"[{type}] {message}  @ {first}");
                }
            }
        }

        static string FirstStackLine(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return null;
            var lines = stack.Split('\n');
            foreach (var l in lines)
            {
                var s = l.Trim();
                if (s.Length == 0) continue;
                if (s.StartsWith("UnityEngine.Debug")) continue;
                return s;
            }
            return null;
        }

        /// <summary>由 AutoTestDriver 每帧在主线程调用，把队列写进文件。</summary>
        public static void Flush()
        {
            if (!Active) return;

            // 刷新给 OnLog 用的时间缓存（这里保证在主线程）
            _nowCache   = Time.realtimeSinceStartup - _startRealtime;
            _frameCache = Time.frameCount;

            PendingLog[] batch;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                batch = _pending.ToArray();
                _pending.Clear();
            }

            StreamWriter errors = null;
            try
            {
                foreach (var r in batch)
                {
                    string tag = TypeTag(r.type);
                    _console?.WriteLine($"[{r.t,7:F2}s f{r.frame}] {tag} {r.message}");
                    if (r.stack != null)
                        _console?.WriteLine(Indent(r.stack));

                    _jsonl?.WriteLine("{" +
                        JNum("t", r.t) + "," +
                        JNum("frame", r.frame) + "," +
                        J("type", r.type.ToString()) + "," +
                        J("message", r.message) + "," +
                        J("stack", r.stack ?? "") + "}");

                    if (r.type == LogType.Error || r.type == LogType.Exception || r.type == LogType.Assert)
                    {
                        errors = errors ?? OpenAppend(Path.Combine(RunDir, "errors.log"));
                        errors.WriteLine($"[{r.t,7:F2}s f{r.frame}] {tag} {r.message}");
                        if (r.stack != null) errors.WriteLine(Indent(r.stack));
                        errors.WriteLine();
                    }
                }
            }
            catch (Exception)
            {
                // 写盘失败不能反过来把游戏搞崩，静默吞掉
            }
            finally
            {
                try { errors?.Dispose(); } catch { }
            }
        }

        static StreamWriter OpenAppend(string path)
        {
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }

        static string TypeTag(LogType t)
        {
            switch (t)
            {
                case LogType.Warning:   return "WARN ";
                case LogType.Error:     return "ERROR";
                case LogType.Assert:    return "ASSERT";
                case LogType.Exception: return "EXCEPT";
                default:                return "INFO ";
            }
        }

        static string Indent(string stack)
        {
            var lines = stack.TrimEnd().Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("        | ").Append(lines[i].TrimEnd());
            }
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // 回合快照 / 事件
        // ══════════════════════════════════════════════════════════════════

        public static void WriteTurnRecord(string json)
        {
            if (!Active) return;
            try { _turns?.WriteLine(json); } catch { }
        }

        public static void WriteConsoleLine(string line)
        {
            if (!Active) return;
            try { _console?.WriteLine(line); } catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // 极简 JSON 辅助（避免依赖第三方库）
        // ══════════════════════════════════════════════════════════════════

        public static string Esc(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string J(string key, string value)  => $"{Esc(key)}:{Esc(value)}";
        public static string JNum(string key, float v)    => $"{Esc(key)}:{v.ToString("0.###", CultureInfo.InvariantCulture)}";
        public static string JNum(string key, double v)   => $"{Esc(key)}:{v.ToString("0.###", CultureInfo.InvariantCulture)}";
        public static string JNum(string key, int v)      => $"{Esc(key)}:{v.ToString(CultureInfo.InvariantCulture)}";
        public static string JBool(string key, bool v)    => $"{Esc(key)}:{(v ? "true" : "false")}";
    }
}
