using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerAnimationSetup
{
    public const string Root = "Assets/player——animation";
    const string ModelPath = Root + "/Models/Rene_Player.fbx";
    const string ControllerPath = Root + "/Player.controller";
    const string VisualPath = Root + "/Rene_PlayerVisual.prefab";
    const string ReportPath = "Artifacts/PlayerAnimation/setup.txt";
    static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

    public static void ApplyAndValidate()
    {
        Apply();
        PlayerAnimationValidation.Validate();
    }

    [MenuItem("Tools/Renne/Player Animation/Import and Connect Player")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before setup.");
        Directory.CreateDirectory("Artifacts/PlayerAnimation");
        AssetDatabase.Refresh();
        Import(ModelPath, false);
        foreach (string file in Directory.GetFiles(Root + "/SourceClips", "*.fbx"))
        {
            string path = file.Replace('\\', '/');
            if (path.Contains("Knockdown")) throw new InvalidOperationException("Unfinished knockdown must not be imported.");
            Import(path, true);
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            string name = Path.GetFileNameWithoutExtension(path);
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = name; clip.hideFlags = HideFlags.None;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = IsLoop(name); settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clips[name] = SaveClip(clip, name);
        }
        MakeIdle("StandIdle", "Rene_Cautious_Walk_15F");
        MakeIdle("CrouchIdle", "Rene_Crouch_Sneak_15F");
        MakeIdle("ProneIdle", "Rene_Prone_Crawl_15F");
        var controller = CreateController();
        CreateVisual(controller);
        ConfigurePlayerPrefab();
        int players = 0;
        foreach (string scenePath in new[] { "Assets/Scenes/Test.unity", "Assets/Scenes/SampleScene.unity" })
        {
            if (!File.Exists(scenePath)) continue;
            var scene = EditorSceneManager.OpenScene(scenePath);
            int playersBefore = players;
            string backup = "Artifacts/PlayerAnimation/Before_" + Path.GetFileName(scenePath);
            if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);
            foreach (var player in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<PlayerController>(true)))
            {
                if (!player.GetComponent<UnitMovement>()) continue;
                AttachVisual(player.gameObject); players++;
            }
            if (players > playersBefore) EditorSceneManager.SaveScene(scene);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        AssetDatabase.SaveAssets();
        File.WriteAllText(ReportPath, "SETUP_PASS\nSource clips: " + (clips.Count - 3) + "\nIdle poses: 3\nScene players: " + players +
            "\nKnockdown excluded\nGeneric rig, no root motion; original grid movement remains authoritative.\n");
        Debug.Log("PLAYER_ANIMATION_SETUP_PASS");
    }

    static void Import(string path, bool animation)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (!importer) throw new InvalidOperationException("Missing FBX: " + path);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.importAnimation = animation;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = true;
        importer.importCameras = false; importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
    }

    static bool IsLoop(string name)
    {
        return name.Contains("Walk") || name.Contains("Sneak") || name.Contains("Crawl") ||
            name.StartsWith("Base_Idle") || name.StartsWith("Rifle_Aim") || name.StartsWith("Run") ||
            name == "Stand_15F" || name.Contains("Relex_Idle");
    }

    static AnimationClip SaveClip(AnimationClip clip, string name)
    {
        string path = Root + "/Animations/" + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing) { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(clip, path); return clip;
    }

    static void MakeIdle(string name, string sourceName)
    {
        var clip = new AnimationClip { name = name, frameRate = 30 };
        foreach (var binding in AnimationUtility.GetCurveBindings(clips[sourceName]))
        {
            var curve = AnimationUtility.GetEditorCurve(clips[sourceName], binding);
            float value = curve.Evaluate(0);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, value));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings); clips[name] = SaveClip(clip, name);
    }

    static AnimatorController CreateController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            foreach (var layer in controller.layers) UnityEngine.Object.DestroyImmediate(layer.stateMachine, true);
            controller.layers = new AnimatorControllerLayer[0]; controller.parameters = new AnimatorControllerParameter[0];
            controller.AddLayer("Base Layer");
        }
        controller.AddParameter("Stance", AnimatorControllerParameterType.Int);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("GaitPhase", AnimatorControllerParameterType.Float);
        controller.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Pickup", AnimatorControllerParameterType.Trigger);
        var sm = controller.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();
        string[,] entries = {
            {"StandIdle","StandIdle"}, {"Walk","Rene_Cautious_Walk_15F"},
            {"CrouchIdle","CrouchIdle"}, {"CrouchWalk","Rene_Crouch_Sneak_15F"},
            {"ProneIdle","ProneIdle"}, {"ProneCrawl","Rene_Prone_Crawl_15F"},
            {"StandToCrouch","Rene_Stand_To_Crouch_15F"},
            {"CrouchToStand","Rene_Transition_Crouch_To_Stand_15F"},
            {"CrouchToProne","Rene_Transition_Crouch_To_Prone_15F"},
            {"ProneToCrouch","Rene_Transition_Prone_To_Crouch_15F"},
            {"Throw","Rene_Throw_Right_15F"}, {"StandPickup","Rene_Search_Pickup_Right_15F"},
            {"CrouchPickup","Rene_Crouch_Pickup_Right_15F"} };
        for (int i=0;i<entries.GetLength(0);i++)
        {
            var state = sm.AddState(entries[i,0], new Vector3(250+(i%3)*300, 50+(i/3)*120));
            state.motion = clips[entries[i,1]]; state.writeDefaultValues = true;
            if (i == 1 || i == 3 || i == 5) { state.timeParameterActive = true; state.timeParameter = "GaitPhase"; }
            if (i>=10) state.tag = "Action";
            states.Add(state.name,state);
        }
        sm.defaultState = states["StandIdle"];
        foreach (string n in new[] {"StandIdle","Walk"}) Link(states[n],states["StandToCrouch"],AnimatorConditionMode.NotEqual,0,"Stance");
        foreach (string n in new[] {"CrouchIdle","CrouchWalk"})
        {
            Link(states[n],states["CrouchToStand"],AnimatorConditionMode.Equals,0,"Stance");
            Link(states[n],states["CrouchToProne"],AnimatorConditionMode.Equals,2,"Stance");
        }
        foreach (string n in new[] {"ProneIdle","ProneCrawl"}) Link(states[n],states["ProneToCrouch"],AnimatorConditionMode.NotEqual,2,"Stance");
        string[,] pairs={{"StandIdle","Walk"},{"CrouchIdle","CrouchWalk"},{"ProneIdle","ProneCrawl"}};
        for(int i=0;i<3;i++)
        {
            Link(states[pairs[i,0]],states[pairs[i,1]],AnimatorConditionMode.If,0,"IsMoving");
            Link(states[pairs[i,1]],states[pairs[i,0]],AnimatorConditionMode.IfNot,0,"IsMoving");
        }
        Exit(states["StandToCrouch"],states["CrouchIdle"]);
        Exit(states["CrouchToStand"],states["StandIdle"]);
        Exit(states["CrouchToProne"],states["ProneIdle"]);
        Exit(states["ProneToCrouch"],states["CrouchIdle"]);
        foreach (var name in new[]{"Throw","StandPickup","CrouchPickup"})
        {
            bool crouch=name=="CrouchPickup";
            var t=sm.AddAnyStateTransition(states[name]); t.hasExitTime=false;t.duration=.12f;t.hasFixedDuration=true;t.canTransitionToSelf=false;
            t.AddCondition(AnimatorConditionMode.If,0,name=="Throw"?"Throw":"Pickup");
            t.AddCondition(AnimatorConditionMode.Equals,crouch?1:0,"Stance");
            Exit(states[name],states[crouch?"CrouchIdle":"StandIdle"]);
        }
        EditorUtility.SetDirty(controller); return controller;
    }

    static void Link(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold, string parameter)
    {
        var t=from.AddTransition(to);t.hasExitTime=false;t.duration=.12f;t.hasFixedDuration=true;
        t.AddCondition(mode,threshold,parameter);
    }
    static void Exit(AnimatorState from, AnimatorState to)
    {
        var t=from.AddTransition(to);t.hasExitTime=true;t.exitTime=1;t.duration=.08f;t.hasFixedDuration=true;
    }

    static void CreateVisual(AnimatorController controller)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        try
        {
            go.name="Rene_PlayerVisual";
            var animator=go.GetComponent<Animator>();if(!animator)animator=go.AddComponent<Animator>();
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            const string materialPath=Root+"/Player.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,materialPath);}
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/Player_BaseColor.png");
            mat.SetTexture("_BaseMap",texture);mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Smoothness",.25f);EditorUtility.SetDirty(mat);
            mat.SetTexture("_EmissionMap",texture);mat.SetColor("_EmissionColor",new Color(.4f,.4f,.4f));mat.EnableKeyword("_EMISSION");
            foreach(var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterials=Enumerable.Repeat(mat,Math.Max(1,renderer.sharedMaterials.Length)).ToArray();
                renderer.updateWhenOffscreen=true;
                renderer.localBounds=new Bounds(new Vector3(0,.8f,0),new Vector3(4,4,4));
            }
            PrefabUtility.SaveAsPrefabAsset(go,VisualPath);
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }

    static void ConfigurePlayerPrefab()
    {
        const string path="Assets/Prefab/Character/Player/Player.prefab";
        string backup="Artifacts/PlayerAnimation/Before_Player.prefab";
        if(!File.Exists(backup))File.Copy(path,backup);
        var go=PrefabUtility.LoadPrefabContents(path);
        try{AttachVisual(go);PrefabUtility.SaveAsPrefabAsset(go,path);}
        finally{PrefabUtility.UnloadPrefabContents(go);}
    }

    static void AttachVisual(GameObject player)
    {
        PlayerCapsuleVisualSetup.RemoveRootVisual(player);
        Transform visual=player.transform.Find("Rene_PlayerVisual");
        if(!visual)visual=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath),player.transform)).transform;
        visual.name="Rene_PlayerVisual";visual.localPosition=Vector3.zero;visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
        foreach(var t in visual.GetComponentsInChildren<Transform>(true))t.gameObject.layer=player.layer;
        var animator=visual.GetComponent<Animator>();
        var driver=player.GetComponent<PlayerAnimationDriver>();if(!driver)driver=player.AddComponent<PlayerAnimationDriver>();
        driver.Configure(animator);EditorUtility.SetDirty(driver);
        var movement=player.GetComponent<UnitMovement>();
        if(movement)
        {
            var so=new SerializedObject(movement);so.FindProperty("animator").objectReferenceValue=animator;
            so.FindProperty("moveAnimationParam").stringValue="IsMoving";so.ApplyModifiedPropertiesWithoutUndo();
        }
        var capsule=player.GetComponent<CapsuleCollider>();
        if(capsule){capsule.direction=1;capsule.height=1.92f;capsule.radius=.3f;capsule.center=Vector3.up*.96f;}
        PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
    }
}
