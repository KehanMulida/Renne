using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RenneAutoTest
{
    /// <summary>
    /// Tools > AutoTest > 控制台
    /// 手动触发一次自动化运行，或者查看上一次的结果。
    /// 走的是和 CLI 完全相同的命令文件通道，行为一致。
    /// </summary>
    public class AutoTestWindow : EditorWindow
    {
        int    _turns = 10;
        float  _timeout = 180f;
        float  _playerDelay = 0.15f;
        bool   _autoPass = true;
        bool   _stopOnError = false;
        string _scene = "";
        string _note = "";
        Vector2 _scroll;
        string _summaryCache;
        double _nextRefresh;

        [MenuItem("Tools/AutoTest/控制台 %#t")]
        public static void Open()
        {
            var w = GetWindow<AutoTestWindow>("AutoTest");
            w.minSize = new Vector2(380, 420);
            w.Show();
        }

        [MenuItem("Tools/AutoTest/打开输出目录")]
        public static void OpenOutputFolder()
        {
            AutoTestPaths.EnsureRoot();
            EditorUtility.RevealInFinder(AutoTestPaths.Root + Path.DirectorySeparatorChar);
        }

        void OnEnable() { titleContent = new GUIContent("AutoTest"); }

        void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 1.0;
            _summaryCache = null;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("运行配置", EditorStyles.boldLabel);

            _turns       = EditorGUILayout.IntField(
                new GUIContent("模拟回合数", "跑满多少个全局回合后自动停止"), _turns);
            _timeout     = EditorGUILayout.FloatField(
                new GUIContent("超时 (秒)", "墙钟超时，防止 AI 卡死导致永远跑不完"), _timeout);
            _playerDelay = EditorGUILayout.FloatField(
                new GUIContent("玩家回合等待", "自动结束玩家回合前等待的秒数"), _playerDelay);
            _autoPass    = EditorGUILayout.Toggle(
                new GUIContent("自动跳过玩家回合", "关掉的话玩家回合需要你手动操作"), _autoPass);
            _stopOnError = EditorGUILayout.Toggle(
                new GUIContent("出错即停", "捕获到第一个 Error/Exception 就结束运行"), _stopOnError);
            _scene       = EditorGUILayout.TextField(
                new GUIContent("场景", "留空 = 用当前打开的场景"), _scene);
            _note        = EditorGUILayout.TextField(
                new GUIContent("备注", "原样写进 summary.json，方便回溯这次跑的是什么"), _note);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("▶ 开始运行", GUILayout.Height(32)))
                    SendRun();
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("■ 停止"))
                    EditorApplication.ExitPlaymode();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开输出目录")) OpenOutputFolder();
                if (GUILayout.Button("打开最近一次日志")) OpenLatestConsole();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("最近一次结果", EditorStyles.boldLabel);

            if (_summaryCache == null) _summaryCache = ReadLatestSummary();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(_summaryCache) ? "（还没有运行过）" : _summaryCache,
                EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void SendRun()
        {
            var cmd = new AutoTestCommand
            {
                seq                = NextSeq(),
                action             = "run",
                turns              = _turns,
                timeout            = _timeout,
                playerDelay        = _playerDelay,
                autoPassPlayerTurn = _autoPass,
                stopOnError        = _stopOnError,
                scene              = _scene ?? "",
                note               = _note ?? "",
            };

            try
            {
                AutoTestPaths.EnsureRoot();
                File.WriteAllText(AutoTestPaths.Command,
                                  JsonUtility.ToJson(cmd, true), System.Text.Encoding.UTF8);
                Debug.Log($"[AutoTest] 已提交运行请求 seq={cmd.seq}，桥会在 0.5 秒内接手");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoTest] 写命令文件失败：{e.Message}");
            }
        }

        static int NextSeq()
        {
            try
            {
                if (!File.Exists(AutoTestPaths.Command)) return 1;
                var old = JsonUtility.FromJson<AutoTestCommand>(
                    File.ReadAllText(AutoTestPaths.Command));
                return (old?.seq ?? 0) + 1;
            }
            catch { return 1; }
        }

        static string ReadLatestSummary()
        {
            try
            {
                var p = Path.Combine(AutoTestPaths.Root, "latest", "summary.json");
                return File.Exists(p) ? File.ReadAllText(p) : "";
            }
            catch { return ""; }
        }

        static void OpenLatestConsole()
        {
            var p = Path.Combine(AutoTestPaths.Root, "latest", "console.log");
            if (!File.Exists(p))
            {
                Debug.LogWarning("[AutoTest] 还没有日志文件");
                return;
            }
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
            catch (Exception e) { Debug.LogWarning($"[AutoTest] 打不开日志：{e.Message}"); }
        }
    }
}
