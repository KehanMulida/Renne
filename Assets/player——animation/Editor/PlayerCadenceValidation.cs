using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerCadenceValidation
{
    const string Flag="PlayerCadenceValidation.Run";
    static int stage,steps,cells;
    static float next,moveStart,previousPhase;
    static double timeout;
    static PlayerController player;
    static UnitMovement movement;
    static PlayerAnimationDriver driver;
    static StringBuilder report=new StringBuilder();
    static bool sampledMiddle;
    static Vector3 lastPosition;
    static float lastTime;
    static int lastFrame=-1;
    static bool hadTranslation;
    static int checkedSeams;
    static PlayerCadenceValidation(){EditorApplication.update+=Tick;}
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        SessionState.SetBool(Flag,true);EditorApplication.EnterPlaymode();
    }
    static void Wait(int value,float seconds){stage=value;next=Time.time+seconds;}
    static void Tick()
    {
        if(File.Exists("Artifacts/PlayerAnimation/cadence_pose.request") && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete("Artifacts/PlayerAnimation/cadence_pose.request");
            try{VerifyMotionTime();}catch(Exception e){File.WriteAllText("Artifacts/PlayerAnimation/cadence_pose.txt","FAIL\n"+e);Debug.LogException(e);}return;
        }
        if(File.Exists("Artifacts/PlayerAnimation/cadence_validate.request") && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {File.Delete("Artifacts/PlayerAnimation/cadence_validate.request");Run();return;}
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying)return;
        try
        {
            if(stage==0)
            {
                timeout=EditorApplication.timeSinceStartup+100;
                Time.timeScale=2;
                foreach(var turn in UnityEngine.Object.FindObjectsOfType<TurnSystem>()){turn.StopAllCoroutines();turn.enabled=false;}
                foreach(var ai in UnityEngine.Object.FindObjectsOfType<EnemyAIController>()){ai.StopAllCoroutines();ai.enabled=false;}
                foreach(var input in UnityEngine.Object.FindObjectsOfType<PlayerInputController>())input.enabled=false;
                player=GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerController>();
                movement=player.GetComponent<UnitMovement>();driver=player.GetComponent<PlayerAnimationDriver>();
                movement.OnStep+=()=>{steps++;report.AppendLine("Footfall phase="+driver.GaitPhase.ToString("F3"));};
                movement.OnPositionChanged+=p=>cells++;
                Wait(1,1f);return;
            }
            Require(EditorApplication.timeSinceStartup<timeout,"Cadence runtime test timed out");
            if(movement && movement.IsMoving && (stage==2 || stage==4 || stage==6) && Time.frameCount!=lastFrame)
            {
                float dt=Time.time-lastTime;
                Vector3 delta=player.transform.position-lastPosition;delta.y=0;
                if(hadTranslation && dt>0 && driver.GaitPhase<previousPhase-.4f && driver.GaitPhase<.4f)
                {
                    float actual=delta.magnitude/dt,expected=movement.MoveSpeed*driver.StanceSpeedMultiplier;
                    Require(actual>expected*.94f && actual<expected*1.06f,"Speed drop across grid seam: "+actual+" expected "+expected);
                    checkedSeams++;report.AppendLine("PASS grid seam speed="+actual.ToString("F4")+", expected="+expected.ToString("F4"));
                }
                if(delta.magnitude>.0001f)hadTranslation=true;
                lastPosition=player.transform.position;lastTime=Time.time;lastFrame=Time.frameCount;previousPhase=driver.GaitPhase;
            }
            if(movement && movement.IsMoving && driver.GaitPhase>.15f && driver.GaitPhase<.85f)sampledMiddle=true;
            if(Time.time<next)return;
            switch(stage)
            {
                case 1:
                    Require(Mathf.Abs(movement.MoveSpeed-1.1f)<.001f,"Player config speed was not applied");
                    CaptureColor();BeginMove(2,2);break;
                case 2:
                    if(movement.IsMoving){next=Time.time+.1f;break;}
                    VerifyMove("Stand",2);Require(checkedSeams>0,"No continuous grid seam was observed");player.SetStance(Stance.Crouch);Wait(3,1.8f);break;
                case 3: BeginMove(2,4);break;
                case 4:
                    if(movement.IsMoving){next=Time.time+.1f;break;}
                    VerifyMove("Crouch",2);Require(checkedSeams>0,"No crouch grid seam observed");player.SetStance(Stance.Prone);Wait(5,2.8f);break;
                case 5: BeginMove(2,6);break;
                case 6:
                    if(movement.IsMoving){next=Time.time+.1f;break;}
                    VerifyMove("Prone",2);Require(checkedSeams>0,"No prone grid seam observed");player.SetStance(Stance.Stand);Wait(7,4.5f);break;
                case 7:
                    Require(driver.Animator.GetCurrentAnimatorStateInfo(0).IsName("StandIdle"),"Posture routing failed");
                    report.AppendLine("PASS returns to StandIdle; model world y="+driver.Animator.transform.position.y.ToString("F3")+", root y="+player.transform.position.y.ToString("F3"));
                    Finish(true);break;
            }
        }
        catch(Exception error){report.AppendLine(error.ToString());Debug.LogException(error);Finish(false);}
    }
    static void BeginMove(int length,int nextStage)
    {
        var targets=movement.GetMovementRange(length)
            .Where(p=>p!=movement.CurrentGridPosition && movement.CanMoveTo(p))
            .Select(p=>new{point=p,path=PathfindingService.FindPath(movement.CurrentGridPosition,p,movement.CurrentFloor)})
            .Where(x=>x.path!=null && x.path.Count==length)
            .OrderByDescending(x=>length==2 && x.path[1]-x.path[0]==x.path[0]-movement.CurrentGridPosition).ToArray();
        Require(targets.Length>0,"No reachable test path of length "+length);
        steps=0;cells=0;sampledMiddle=false;moveStart=Time.time;
        lastPosition=player.transform.position;lastTime=Time.time;lastFrame=Time.frameCount;previousPhase=0;hadTranslation=false;checkedSeams=0;
        movement.MoveToGrid(targets[0].point);Wait(nextStage,.3f);
    }
    static void VerifyMove(string name,int expectedCells)
    {
        Require(cells==expectedCells,name+": wrong number of cells "+cells);
        Require(steps==expectedCells*2,name+": expected two footfalls per cell; got "+steps);
        Require(sampledMiddle,name+": gait did not play intermediate frames");
        Require(Mathf.Abs(driver.GaitPhase-1)<.0001f,name+": gait was truncated at arrival");
        Require(Time.time-moveStart>=expectedCells/(movement.MoveSpeed*driver.StanceSpeedMultiplier)-.02f,name+": movement too fast");
        report.AppendLine("PASS "+name+": cells="+cells+", footfalls="+steps+", full gait cycles="+cells+", seconds="+(Time.time-moveStart).ToString("F3"));
    }
    static void CaptureColor()
    {
        var camera=Camera.main;var skin=player.GetComponentInChildren<SkinnedMeshRenderer>();
        var material=skin.material;
        Require(material.GetTexture("_BaseMap") && material.GetTexture("_EmissionMap"),"Character texture or textured fill missing");
        Require(material.IsKeywordEnabled("_EMISSION"),"Character neutral fill disabled");
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        camera.transform.position=player.transform.position+new Vector3(2.2f,2.8f,-3.5f);camera.transform.LookAt(player.transform.position+Vector3.up*.8f);
        var emission=material.GetTexture("_EmissionMap");var color=material.GetColor("_EmissionColor");
        bool shadows=skin.receiveShadows;
        try
        {
            material.SetTexture("_EmissionMap",null);material.SetColor("_EmissionColor",new Color(.018f,.026f,.04f));skin.receiveShadows=true;
            Render(camera,"color_before");
            material.SetTexture("_EmissionMap",emission);material.SetColor("_EmissionColor",color);skin.receiveShadows=shadows;
            Render(camera,"color_after");
        }
        finally{camera.transform.SetPositionAndRotation(position,rotation);material.SetTexture("_EmissionMap",emission);material.SetColor("_EmissionColor",color);skin.receiveShadows=shadows;}
        report.AppendLine("PASS textured neutral character fill; captured same-pose before/after under actual runtime lighting.");
    }
    static void Render(Camera camera,string name)
    {
        var target=new RenderTexture(800,800,24);var image=new Texture2D(800,800,TextureFormat.RGB24,false);
        var previousTarget=camera.targetTexture;var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();File.WriteAllBytes("Artifacts/PlayerAnimation/"+name+".png",image.EncodeToPNG());}
        finally{camera.targetTexture=previousTarget;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
    }
    static void Finish(bool passed)
    {
        File.WriteAllText("Artifacts/PlayerAnimation/cadence_validation.txt",(passed?"PASS\n":"FAIL\n")+report);
        SessionState.SetBool(Flag,false);Time.timeScale=1;
        EditorApplication.ExitPlaymode();
    }

    public static void VerifyMotionTime()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAnimationSetup.Root+"/Rene_PlayerVisual.prefab");
        var animated=UnityEngine.Object.Instantiate(prefab);var reference=UnityEngine.Object.Instantiate(prefab);
        animated.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);reference.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var animator=animated.GetComponent<Animator>();reference.GetComponent<Animator>().enabled=false;
        var result=new StringBuilder();
        try
        {
            string[] states={"Walk","CrouchWalk","ProneCrawl"};
            string[] names={"Rene_Cautious_Walk_15F","Rene_Crouch_Sneak_15F","Rene_Prone_Crawl_15F"};
            for(int i=0;i<3;i++)
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerAnimationSetup.Root+"/Animations/"+names[i]+".anim");
                animator.Rebind();animator.SetInteger("Stance",i);animator.SetBool("IsMoving",true);
                float error=0;
                foreach(float phase in new[]{0f,.25f,.5f,.75f,.9999f})
                {
                    animator.SetFloat("GaitPhase",phase);animator.Play(states[i],0,0);animator.Update(.0001f);
                    clip.SampleAnimation(reference,phase*clip.length);
                    foreach(var bone in animated.GetComponentsInChildren<Transform>())
                    {
                        string path=AnimationUtility.CalculateTransformPath(bone,animated.transform);
                        var other=string.IsNullOrEmpty(path)?reference.transform:reference.transform.Find(path);
                        Require(other,"Reference bone not found: "+path);
                        error=Mathf.Max(error,Vector3.Distance(bone.position,other.position));
                    }
                }
                Require(error<.002f,states[i]+": motion phase mismatch "+error);
                result.AppendLine("PASS "+states[i]+": actual Animator bone positions match source at 0%, 25%, 50%, 75%, 100%; max error="+error.ToString("F7")+"m");
            }
            File.WriteAllText("Artifacts/PlayerAnimation/cadence_pose.txt",result.ToString());
        }
        finally{UnityEngine.Object.DestroyImmediate(animated);UnityEngine.Object.DestroyImmediate(reference);}
    }
}
