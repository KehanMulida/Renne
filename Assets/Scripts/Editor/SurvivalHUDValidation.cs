#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Play-mode integration probe. Sample items and stat changes are never saved.</summary>
[InitializeOnLoad]
public static class SurvivalHUDValidation
{
    const string Key="SurvivalHUDValidation.Running";
    static double deadline;
    static int phase;
    static Vector2 draggedPosition;
    static int testSlot;
    static ItemData sample;
    static string report;
    static SurvivalHUDValidation(){EditorApplication.update+=Tick;}

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        SurvivalHUDSetup.Apply();
        SessionState.SetBool(Key,true);
        EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);report+="PASS: "+message+"\n";}
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
        try
        {
            if(phase==0){phase=1;deadline=EditorApplication.timeSinceStartup+3;return;}
            if(EditorApplication.timeSinceStartup<deadline)return;
            var hud=UnityEngine.Object.FindObjectOfType<SurvivalHUD>();
            var player=UnityEngine.Object.FindObjectOfType<PlayerController>();
            var owner=UnityEngine.Object.FindObjectOfType<InventoryCanvas>();
            var equipment=player.GetComponent<EquipmentManager>();
            var inventory=player.GetComponent<Inventory>();
            var hot=hud.GetComponentInChildren<HotbarUI>(true);
            var inv=hud.GetComponentInChildren<InventoryUI>(true);
            var panel=(RectTransform)inv.transform;
            if(phase==1)
            {
                report="Survival HUD play-mode integration\n";
                Check(hud!=null,"HUD starts in Test scene");
                Check(hot.GetComponentsInChildren<ItemSlotUI>(true).Length==8,"Eight preauthored hotbar slots retained");
                owner.OpenInventory();
                sample=ScriptableObject.CreateInstance<ItemData>();sample.ID=-888;sample.Name="Bandage";sample.maxStack=20;sample.weight=0;
                Check(inventory.AddItem(sample,5),"Sample stack added through existing inventory API");
                testSlot=equipment.FindFirstEmptySlot();
                Check(testSlot>=0 && equipment.MoveSingleToHotbar(sample),"Single-item transfer uses existing equipment logic");
                phase=2;deadline=EditorApplication.timeSinceStartup+.6;return;
            }
            if(phase==2)
            {
                Check(inv.GetComponentsInChildren<ItemSlotUI>(true).Length==20,"Inventory opens with exactly twenty slots");
                Check(inventory.GetItemCount(sample)==4 && equipment.GetSlot(testSlot).quantity==1,"Single click transfer preserves remaining stack");
                var slots=hot.GetComponentsInChildren<ItemSlotUI>(true);
                Canvas.ForceUpdateCanvases();
                Check(slots.All(s=>Mathf.Abs(((RectTransform)s.transform).anchoredPosition.y-((RectTransform)slots[0].transform).anchoredPosition.y)<.1f),"Hotbar slots share one aligned row");
                equipment.SelectSlot((testSlot+1)%equipment.HotbarSize);
                slots[testSlot].OnPointerClick(new PointerEventData(EventSystem.current));
                player.Config.CurrentHp=78;player.Config.CurrentSanity=62;hud.Refresh();
                Check(Mathf.Abs(hud.transform.Find("Vitals/Health/Fill_Editable").GetComponent<Image>().fillAmount-78f/player.MaxHp)<.001f,"Health fill follows runtime stats");
                var drag=inv.GetComponentInChildren<InventoryPanelDrag>();
                var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,drag.transform.position)};
                Vector2 before=panel.anchoredPosition;
                drag.OnBeginDrag(pointer);pointer.position+=new Vector2(-120,50);drag.OnDrag(pointer);drag.OnEndDrag(pointer);
                draggedPosition=panel.anchoredPosition;
                Check((draggedPosition-before).sqrMagnitude>1,"Inventory title drag moves panel");
                phase=3;deadline=EditorApplication.timeSinceStartup+.5;return;
            }
            if(phase==3)
            {
                var slots=hot.GetComponentsInChildren<ItemSlotUI>(true);
                Check(equipment.CurrentSlotIndex==testSlot && slots[testSlot].IsSelected,"Pointer click updates selected slot and highlight");
                Check(slots.All(s=>s.transform.localScale==Vector3.one),"Selection does not resize or shift slots");
                // Populate only this disposable Play Mode session for a readable screenshot.
                for(int i=0;i<5;i++)
                {
                    var item=ScriptableObject.CreateInstance<ItemData>();item.ID=-900-i;item.Name=new[]{"Handgun","Bottle","Key","Material","Armour"}[i];
                    item.Type=new[]{ItemType.Weapon,ItemType.Consumable,ItemType.KeyItem,ItemType.Material,ItemType.Equipment}[i];item.weight=0;item.maxStack=20;
                    inventory.AddItem(item,i+1);if(i<3)equipment.MoveToHotbar(item);
                }
                hud.Refresh();Capture(hud,"Artifacts/SurvivalHUD/inventory-open.png");
                owner.CloseInventory();phase=4;deadline=EditorApplication.timeSinceStartup+.4;return;
            }
            if(phase==4)
            {
                Check(!inv.gameObject.activeSelf,"Closing inventory hides panel");
                owner.OpenInventory();phase=5;deadline=EditorApplication.timeSinceStartup+.5;return;
            }
            if(phase==5)
            {
                Check((panel.anchoredPosition-draggedPosition).sqrMagnitude<.1f,"Reopening preserves dragged position");
                // Exercise the existing double-click path to move the selected hotbar stack back.
                var slot=hot.GetComponentsInChildren<ItemSlotUI>(true)[testSlot];
                var pointer=new PointerEventData(EventSystem.current);
                slot.OnPointerClick(pointer);slot.OnPointerClick(pointer);
                Check(equipment.GetSlot(testSlot).IsEmpty && inventory.GetItemCount(sample)==5,"Double click returns stack through original handler");
                var drag=inv.GetComponentInChildren<InventoryPanelDrag>();
                pointer.position=RectTransformUtility.WorldToScreenPoint(null,drag.transform.position);
                drag.OnBeginDrag(pointer);pointer.position+=new Vector2(20000,20000);drag.OnDrag(pointer);drag.OnEndDrag(pointer);
                var parent=(RectTransform)panel.parent;var corners=new Vector3[4];panel.GetWorldCorners(corners);
                Vector2 min=parent.InverseTransformPoint(corners[0]),max=parent.InverseTransformPoint(corners[2]);
                Check(min.x>=parent.rect.xMin-.1f && min.y>=parent.rect.yMin-.1f && max.x<=parent.rect.xMax+.1f && max.y<=parent.rect.yMax+.1f,"Drag clamps panel inside canvas");
                owner.CloseInventory();phase=6;deadline=EditorApplication.timeSinceStartup+.4;return;
            }
            if(phase==6)
            {
                int occupied=Enumerable.Range(0,equipment.HotbarSize).First(i=>!equipment.GetSlot(i).IsEmpty);
                equipment.SelectSlot(occupied);
                Capture(hud,"Artifacts/SurvivalHUD/inventory-closed.png");
                var unit=player.GetComponent<TurnBasedUnit>();
                int before=unit.RemainingActionPoints;
                if(before>1)unit.ConsumeAP(1);
                hud.Refresh();
                Check(hud.transform.Find("Turn_AP/APValue").GetComponent<Text>().text.Contains(unit.RemainingActionPoints.ToString()),"AP label follows actual AP consumption");
                var button=hud.transform.Find("EndTurn").GetComponent<Button>();
                Check(button.onClick.GetPersistentEventCount()==0,"End-turn binding is owned by HUD lifecycle");
                if(TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player))
                {
                    int turn=TurnSystem.Instance.GlobalTurnNumber;
                    button.onClick.Invoke();
                    Check(TurnSystem.Instance.GlobalTurnNumber>turn || !TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player),"End-turn button executes existing turn control");
                }
                File.WriteAllText("Artifacts/SurvivalHUD/playmode-validation.txt",report+"SUCCESS\n");
                SessionState.SetBool(Key,false);EditorApplication.Exit(0);
            }
        }
        catch(Exception error)
        {
            Directory.CreateDirectory("Artifacts/SurvivalHUD");
            File.WriteAllText("Artifacts/SurvivalHUD/playmode-validation.txt",report+"FAIL: "+error);
            Debug.LogException(error);SessionState.SetBool(Key,false);EditorApplication.Exit(1);
        }
    }

    static void Capture(SurvivalHUD hud,string path)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
        var canvas=hud.GetComponentInParent<Canvas>();var source=Camera.main;
        var go=new GameObject("HUD_Validation_Camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);
        camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);camera.aspect=1.6f;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
        var target=new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);target.Create();
        var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;float plane=canvas.planeDistance;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.5f;
        camera.targetTexture=target;Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1920,1200,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1200),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;
        UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);target.Release();UnityEngine.Object.DestroyImmediate(target);
    }
}
#endif
