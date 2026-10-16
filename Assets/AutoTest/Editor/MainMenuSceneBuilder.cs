using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenneAutoTest
{
    /// <summary>
    /// 生成 Assets/Scenes/MainMenu.unity。
    ///
    /// 为什么用脚本生成而不是手写 .unity YAML：场景文件格式复杂、fileID 要自洽，
    /// 手写极易产生 Unity 能打开但行为诡异的文件。交给 Unity 自己序列化最稳。
    ///
    /// 场景内容很薄：一个相机 + 一个挂 MainMenuUI 的物体，界面全在运行时构建。
    /// 所以以后改界面改脚本就行，不用重新生成场景。
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        private const string ScenePath   = "Assets/Scenes/MainMenu.unity";
        private const string GameScene   = "Assets/Scenes/Test.unity";

        [MenuItem("Tools/AutoTest/生成主菜单场景")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 相机：UI 是 Overlay 画布，不经过相机，但没有相机 Unity 会一直报警
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            cam.orthographic    = true;

            new GameObject("MainMenu", typeof(MainMenuUI));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterInBuildSettings();

            Debug.Log($"[主菜单] 已生成 {ScenePath}，并加入 Build Settings");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 把主菜单和玩法场景加进 Build Settings，主菜单排第一。
        /// 不加的话 SceneManager.LoadScene 会直接失败——这是新手最常踩的坑，
        /// 而且报错信息很不直观，所以这里自动处理掉。
        /// </summary>
        private static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();

            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));

            if (File.Exists(GameScene) && !scenes.Any(s => s.path == GameScene))
                scenes.Add(new EditorBuildSettingsScene(GameScene, true));

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>batchmode 入口：Unity.exe -executeMethod RenneAutoTest.MainMenuSceneBuilder.BuildBatch</summary>
        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }
    }
}
