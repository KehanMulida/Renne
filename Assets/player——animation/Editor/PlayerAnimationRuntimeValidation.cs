using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerAnimationRuntimeValidation
{
    const string Flag="Renne.PlayerAnimationRuntimeValidation";
    static int stage;
    static float next;
    static double deadline;
    static PlayerController player;
    static PlayerAnimationDriver driver;
    static UnitMovement movement;
    static EquipmentManager equipment;
    static Animator animator;
    static ItemData item;
    static Vector3 start;
    static StringBuilder report=new StringBuilder();
    static PlayerAnimationRuntimeValidation(){EditorApplication.update+=Tick;}

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        SessionState.SetBool(Flag,true);
        EditorApplication.EnterPlaymode();
    }
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void State(string name)
    {
        Require(!animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName(name),"Runtime state expected " +name);
        report.AppendLine("PASS "+name);
    }
    static void Wait(int newStage,float seconds){stage=newStage;next=Time.time+seconds;}
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying)return;
        try
        {
            if(stage==0)
            {
                deadline=EditorApplication.timeSinceStartup+100;
                Time.timeScale=3;
                foreach(var turn in UnityEngine.Object.FindObjectsOfType<TurnSystem>()){turn.StopAllCoroutines();turn.enabled=false;}
                foreach(var ai in UnityEngine.Object.FindObjectsOfType<EnemyAIController>()){ai.StopAllCoroutines();ai.enabled=false;}
                foreach(var input in UnityEngine.Object.FindObjectsOfType<PlayerInputController>())input.enabled=false;
                player=GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerController>();
                driver=player.GetComponent<PlayerAnimationDriver>();movement=player.GetComponent<UnitMovement>();
                equipment=player.GetComponent<EquipmentManager>();animator=driver.Animator;
                Wait(1,1f);return;
            }
            Require(EditorApplication.timeSinceStartup<deadline,"Runtime validation timeout");
            if(Time.time<next)return;
            switch(stage)
            {
                case 1: State("StandIdle");player.SetStance(Stance.Crouch);Wait(2,1.7f);break;
                case 2: State("CrouchIdle");player.SetStance(Stance.Prone);Wait(3,2.7f);break;
                case 3: State("ProneIdle");player.SetStance(Stance.Stand);Wait(4,4.5f);break;
                case 4:
                    State("StandIdle");start=player.transform.position;movement.SetMoveSpeed(.8f);
                    var target=movement.GetMovementRange(1).Where(p=>p!=movement.CurrentGridPosition && movement.CanMoveTo(p)).ToArray();
                    Require(target.Length>0,"No reachable player movement cell");movement.MoveToGrid(target[0]);Wait(5,.4f);break;
                case 5: State("Walk");Wait(6,2.5f);break;
                case 6:
                    if(movement.IsMoving){next=Time.time+.5f;break;}
                    Require(Vector3.Distance(start,player.transform.position)>.1f,"Grid movement did not move player");
                    report.AppendLine("PASS actual UnitMovement movement drives Walk");
                    player.SetStance(Stance.Crouch);Wait(7,1.7f);break;
                case 7: driver.PlayPickup(player.transform.position+player.transform.forward*.3f);Wait(8,.4f);break;
                case 8: State("CrouchPickup");Wait(9,3.6f);break;
                case 9: State("CrouchIdle");player.SetStance(Stance.Stand);Wait(10,1.7f);break;
                case 10:
                    item=ScriptableObject.CreateInstance<ItemData>();item.ID=987654;item.Name="Animation validation item";item.UseCost=0;
                    item.isThrowable=true;item.throwableConfig.directDamage=0;item.throwableConfig.splashDamage=0;
                    var world=new GameObject("Animation validation pickup");world.transform.position=player.transform.position+player.transform.forward*.2f;
                    var pickup=world.AddComponent<WorldItem>();pickup.Initialize(item,1);
                    Require(equipment.TryPickupItem(item,1),"Inventory pickup failed");
                    Require(pickup.Pickup(player.gameObject),"Actual WorldItem pickup failed");Wait(11,.4f);break;
                case 11: State("StandPickup");report.AppendLine("PASS actual WorldItem.Pickup triggers standing pickup");Wait(12,3.6f);break;
                case 12:
                    State("StandIdle");
                    for(int i=0;i<8;i++)if(equipment.GetSlot(i)?.itemData==item)equipment.SelectSlot(i);
                    Require(equipment.ThrowItem(player.transform.position+player.transform.forward*2f),"Actual equipment throw failed");Wait(13,.4f);break;
                case 13: State("Throw");report.AppendLine("PASS actual EquipmentManager.ThrowItem triggers throw");Wait(14,3.2f);break;
                case 14: State("StandIdle");player.SetStance(Stance.Prone);Wait(15,4.5f);break;
                case 15: State("ProneIdle");driver.PlayPickup(player.transform.position+player.transform.forward*.2f);Wait(16,2.8f);break;
                case 16: State("CrouchPickup");Wait(17,6f);break;
                case 17: State("ProneIdle");report.AppendLine("PASS prone pickup rises to crouch, picks up, returns prone");Finish(true);break;
            }
        }
        catch(Exception e){report.AppendLine(e.ToString());Debug.LogException(e);Finish(false);}
    }
    static void Finish(bool pass)
    {
        File.WriteAllText("Artifacts/PlayerAnimation/runtime_validation.txt",(pass?"RUNTIME_PASS\n":"RUNTIME_FAIL\n")+report);
        SessionState.SetBool(Flag,false);Time.timeScale=1;
        if(Application.isBatchMode)EditorApplication.Exit(pass?0:1);
        else EditorApplication.ExitPlaymode();
    }
}
