#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds editable UGUI objects in the current scene; never replaces gameplay controllers.</summary>
public static class SurvivalHUDSetup
{
    const string Folder="Assets/Prefab/UI/SurvivalHUD";
    static readonly Color Ink=new Color(.69f,.76f,.7f,1);
    static readonly Color Edge=new Color(.33f,.41f,.4f,1);
    static readonly Color Panel=new Color(.055f,.08f,.1f,.96f);
    static Font font;

    [MenuItem("Tools/Survival HUD/Apply reference layout to current scene")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before applying the HUD.");
        var controller=UnityEngine.Object.FindObjectOfType<InventoryCanvas>(true);
        if(controller==null)throw new InvalidOperationException("Current scene has no InventoryCanvas; open the gameplay scene first.");
        var scene=controller.gameObject.scene;
        Directory.CreateDirectory("Artifacts/SurvivalHUD");
        EditorSceneManager.SaveScene(scene,"Artifacts/SurvivalHUD/"+scene.name+"-before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
        Directory.CreateDirectory(Folder);
        font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        const string eyesPath="Assets/Art/UI/SurvivalHUD/EmotionEyes.png";
        AssetDatabase.ImportAsset(eyesPath);
        var importer=AssetImporter.GetAtPath(eyesPath) as TextureImporter;
        if(importer!=null && importer.textureType!=TextureImporterType.Sprite)
        {importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.SaveAndReimport();}
        var canvas=controller.GetComponent<Canvas>();
        if(canvas==null)throw new InvalidOperationException("InventoryCanvas needs a Canvas component.");
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject,"Apply survival HUD layout");
        var scaler=canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1200);
        scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight=.5f;
        var hot=canvas.GetComponentsInChildren<HotbarUI>(true).First();
        var inv=canvas.GetComponentsInChildren<InventoryUI>(true).First();
        hot.transform.SetParent(canvas.transform,false);inv.transform.SetParent(canvas.transform,false);
        var old=canvas.transform.Find("SurvivalHUD_Editable");
        if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        ClearChildren(hot.transform); ClearChildren(inv.transform);
        var root=Rect("SurvivalHUD_Editable",canvas.transform,Vector2.zero,new Vector2(1920,1200),Vector2.zero);
        root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
        var hud=root.gameObject.AddComponent<SurvivalHUD>();
        var player=UnityEngine.Object.FindObjectOfType<PlayerController>();
        if(player!=null) Bool(player,"showLegacyStatusLabel",false);
        var input=UnityEngine.Object.FindObjectOfType<PlayerInputController>();
        if(input!=null) Float(input,"hudHintBottomOffset",158);
        Bind(hud,"input",input);
        hot.transform.SetParent(root,false); inv.transform.SetParent(root,false);
        var hotRect=(RectTransform)hot.transform;
        Place(hotRect,new Vector2(.5f,0),new Vector2(0,48),new Vector2(620,94),new Vector2(.5f,0));
        RemoveLayouts(hotRect); Decorate(hotRect);
        var slots=Rect("Slots_8_Horizontal",hotRect,new Vector2(10,-10),new Vector2(600,74),new Vector2(0,1));
        var layout=slots.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize=new Vector2(70,70);layout.spacing=new Vector2(5,0);
        layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=8;
        var template=CreateSlot(null,"SurvivalSlot",false,0);
        var slotPrefab=PrefabUtility.SaveAsPrefabAsset(template.gameObject,Folder+"/SurvivalSlot.prefab");
        UnityEngine.Object.DestroyImmediate(template.gameObject);
        for(int i=0;i<8;i++)CreateSlot(slots,"Slot_"+(i+1).ToString("00"),true,i);
        Bind(hot,"slotContainer",slots);Bind(hot,"slotPrefab",slotPrefab);

        var invRect=(RectTransform)inv.transform;
        RemoveLayouts(invRect);
        Place(invRect,new Vector2(1,1),new Vector2(-130,-355),new Vector2(444,420),new Vector2(1,1));
        Decorate(invRect);
        var header=Rect("TitleBar_DragToMove",invRect,new Vector2(8,-8),new Vector2(428,40),new Vector2(0,1));
        var headerImage=header.gameObject.AddComponent<Image>();headerImage.color=new Color(.07f,.105f,.125f,1);
        var drag=header.gameObject.AddComponent<InventoryPanelDrag>();Bind(drag,"panel",invRect);Bind(drag,"inventoryCanvas",controller);
        Label(header,"Title","INVENTORY",new Vector2(10,-5),new Vector2(190,27),17);
        var count=Label(header,"Capacity","0 / 20",new Vector2(280,-5),new Vector2(84,27),14);
        var close=Rect("CloseButton",header,new Vector2(386,-2),new Vector2(36,32),new Vector2(0,1));
        close.gameObject.AddComponent<Image>().color=new Color(0,0,0,.01f);
        var closeButton=close.gameObject.AddComponent<Button>();
        Label(close,"Label","×",new Vector2(10,-1),new Vector2(25,29),23);
        UnityEventTools.AddPersistentListener(closeButton.onClick,controller.CloseInventory);
        var grid=Rect("InventoryGrid_5x4",invRect,new Vector2(16,-60),new Vector2(412,324),new Vector2(0,1));
        var gl=grid.gameObject.AddComponent<GridLayoutGroup>();gl.cellSize=new Vector2(78,78);gl.spacing=new Vector2(5,4);
        gl.constraint=GridLayoutGroup.Constraint.FixedColumnCount;gl.constraintCount=5;
        for(int i=0;i<20;i++)CreateSlot(grid,"InventorySlot_"+(i+1).ToString("00"),false,i);
        Bind(inv,"gridContainer",grid);Bind(inv,"slotPrefab",slotPrefab);Int(inv,"columns",5);Int(inv,"rows",4);
        Label(invRect,"DragHint","DRAG TITLE TO MOVE   ·   TAB TO CLOSE",new Vector2(16,-391),new Vector2(410,20),11);
        Bool(controller,"alignWithHotbar",false);
        Bind(controller,"inventoryPanel",inv.gameObject);Bind(controller,"inventoryUI",inv);
        Bind(controller,"hotbarPanelRect",hotRect);Bind(controller,"hotbarSlotContainer",slots);Bind(controller,"inventoryGridContainer",grid);

        var vitals=Box("Vitals",root,new Vector2(1,0),new Vector2(232,48),new Vector2(282,68),Vector2.zero);
        // Vitals stay beside the static eye monitor at bottom left.
        vitals.anchorMin=vitals.anchorMax=Vector2.zero;
        var health=Bar(vitals,"Health",12,-12,new Color(.68f,.74f,.63f,1),out var hpText);
        var sanity=Bar(vitals,"Sanity",12,-43,new Color(.47f,.6f,.56f,1),out var spText);
        Bind(hud,"healthFill",health);Bind(hud,"sanityFill",sanity);Bind(hud,"healthValue",hpText);Bind(hud,"sanityValue",spText);

        var emotion=Box("EmotionMonitor_Static_ArtOnly",root,Vector2.zero,new Vector2(28,48),new Vector2(188,116),Vector2.zero);
        var raw=Rect("EyesImage_ReplaceSprite",emotion,new Vector2(6,-6),new Vector2(176,84),new Vector2(0,1));
        var eyeImage=raw.gameObject.AddComponent<Image>();eyeImage.color=Color.white;eyeImage.raycastTarget=false;
        eyeImage.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/SurvivalHUD/EmotionEyes.png");
        Label(emotion,"Caption_Static","CAM 01",new Vector2(8,-94),new Vector2(80,16),10);
        Label(emotion,"Emotion_Static","UNEASY",new Vector2(104,-94),new Vector2(78,16),10);
        var statuses=Rect("StatusIcons_Reserved",root,new Vector2(232,126),new Vector2(210,42),Vector2.zero);
        for(int i=0;i<4;i++)
        {
            var status=Box("Status_"+(i+1)+"_ArtPlaceholder",statuses,new Vector2(0,1),new Vector2(i*44,0),new Vector2(38,38),new Vector2(0,1));
            Label(status,"Icon_Editable","○",new Vector2(7,-2),new Vector2(28,32),24);
        }
        Label(statuses,"Overflow","…",new Vector2(178,-6),new Vector2(28,32),20);
        // Conditions are reserved rather than presenting fictional gameplay effects.
        statuses.gameObject.SetActive(false);

        var ap=Box("Turn_AP",root,Vector2.one,new Vector2(-65,-50),new Vector2(390,98),Vector2.one);
        var turn=Label(ap,"TurnLabel","YOUR TURN",new Vector2(14,-10),new Vector2(270,24),16);
        var apValue=Label(ap,"APValue","AP 0 / 8",new Vector2(220,-58),new Vector2(110,22),13);
        var pips=new Image[16];
        for(int i=0;i<pips.Length;i++)
        {
            var pip=Rect("AP_Pip_"+(i+1).ToString("00"),ap,new Vector2(14+(i%8)*22,-56-(i/8)*17),new Vector2(17,10),new Vector2(0,1));
            pips[i]=pip.gameObject.AddComponent<Image>();pips[i].color=new Color(.65f,.74f,.62f);pips[i].raycastTarget=false;
            pip.gameObject.SetActive(i<8);
        }
        var silhouette=Rect("PlayerSilhouette_Editable",ap,new Vector2(335,-10),new Vector2(37,75),new Vector2(0,1));
        Solid(silhouette,"Head",12,-3,13,13,Ink);Solid(silhouette,"Body",8,-21,21,28,Ink);
        Solid(silhouette,"LeftLeg",9,-48,7,23,Ink);Solid(silhouette,"RightLeg",21,-48,7,23,Ink);
        Bind(hud,"turnLabel",turn);Bind(hud,"apValue",apValue);Array(hud,"apPips",pips);
        Bind(hud,"inventoryCount",count);
        var end=Box("EndTurn",root,new Vector2(1,0),new Vector2(-50,48),new Vector2(196,90),new Vector2(1,0));
        var endButton=end.gameObject.AddComponent<Button>();endButton.targetGraphic=end.GetComponent<Image>();
        Label(end,"Label","END TURN",new Vector2(13,-15),new Vector2(152,27),18);
        Label(end,"KeyHint","SPACE",new Vector2(13,-58),new Vector2(104,18),11);
        Label(end,"Arrow","→",new Vector2(158,-20),new Vector2(30,34),22);
        Bind(hud,"endTurnButton",endButton);

        var drop=controller.GetComponent<DragDropController>();
        if(drop!=null){Bind(drop,"hotbarPanel",hotRect);Bind(drop,"inventoryPanel",invRect);}
        inv.gameObject.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(canvas.gameObject,Folder+"/SurvivalHUDCanvas.prefab");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Validate();
        Selection.activeGameObject=canvas.gameObject;
        Debug.Log("[SurvivalHUD] Editable layout installed in "+scene.path);
    }

    static RectTransform CreateSlot(Transform parent,string name,bool hotbar,int index)
    {
        var rt=Rect(name,parent,Vector2.zero,new Vector2(70,70),new Vector2(0,1));
        var bg=rt.gameObject.AddComponent<Image>();bg.color=new Color(.085f,.1f,.15f,1);
        var border=rt.gameObject.AddComponent<Outline>();border.effectColor=Edge;border.effectDistance=new Vector2(1,-1);border.useGraphicAlpha=false;
        var slot=rt.gameObject.AddComponent<ItemSlotUI>();
        var icon=Rect("Icon_AssignedSprite",rt,new Vector2(13,-14),new Vector2(44,43),new Vector2(0,1));
        var image=icon.gameObject.AddComponent<Image>();image.preserveAspect=true;image.raycastTarget=false;image.enabled=false;
        var glyph=Rect("Icon_EditableFallback",rt,new Vector2(13,-14),new Vector2(44,43),new Vector2(0,1)).gameObject.AddComponent<ItemGlyphGraphic>();
        glyph.color=Ink;glyph.raycastTarget=false;glyph.enabled=false;
        var quantity=Label(rt,"Quantity","",new Vector2(38,-48),new Vector2(26,18),12);
        quantity.alignment=TextAnchor.MiddleRight;
        if(hotbar)Label(rt,"ShortcutKey",(index+1).ToString(),new Vector2(5,-3),new Vector2(18,14),10);
        var selected=Rect("SelectedBorder",rt,Vector2.zero,rt.sizeDelta,new Vector2(0,1));
        selected.anchorMin=Vector2.zero;selected.anchorMax=Vector2.one;selected.offsetMin=selected.offsetMax=Vector2.zero;
        var highlight=selected.gameObject.AddComponent<Image>();highlight.color=new Color(.72f,.82f,.67f,.12f);highlight.raycastTarget=false;
        var selectedEdge=new Color(.74f,.84f,.68f,1);
        Solid(selected,"Top",0,0,70,2,selectedEdge);
        Solid(selected,"Bottom",0,-68,70,2,selectedEdge);
        Solid(selected,"Left",0,0,2,70,selectedEdge);
        Solid(selected,"Right",68,0,2,70,selectedEdge);
        // Stretch the four editable border strips with the slot's layout-driven dimensions.
        var top=(RectTransform)selected.Find("Top");top.anchorMin=new Vector2(0,1);top.anchorMax=Vector2.one;top.sizeDelta=new Vector2(0,2);
        var bottom=(RectTransform)selected.Find("Bottom");bottom.anchorMin=Vector2.zero;bottom.anchorMax=new Vector2(1,0);bottom.anchoredPosition=new Vector2(0,2);bottom.sizeDelta=new Vector2(0,2);
        var left=(RectTransform)selected.Find("Left");left.anchorMin=Vector2.zero;left.anchorMax=new Vector2(0,1);left.sizeDelta=new Vector2(2,0);left.anchoredPosition=Vector2.zero;
        var right=(RectTransform)selected.Find("Right");right.anchorMin=new Vector2(1,0);right.anchorMax=Vector2.one;right.anchoredPosition=new Vector2(-2,0);right.sizeDelta=new Vector2(2,0);
        selected.gameObject.SetActive(false);
        Bind(slot,"bgImage",bg);Bind(slot,"iconImage",image);Bind(slot,"quantityText",quantity);
        Bind(slot,"fallbackGraphic",glyph);Bind(slot,"selectedBorder",highlight);
        ColorValue(slot,"colorNormal",new Color(.085f,.105f,.15f,1));ColorValue(slot,"colorHover",new Color(.16f,.22f,.24f,1));ColorValue(slot,"colorSelected",new Color(.20f,.29f,.25f,1));
        Float(slot,"scaleSelected",1);Float(slot,"scaleNormal",1);
        return rt;
    }
    static Image Bar(RectTransform parent,string name,float x,float y,Color color,out Text value)
    {
        var row=Rect(name,parent,new Vector2(x,y),new Vector2(260,24),new Vector2(0,1));
        Label(row,"Label",name.ToUpperInvariant(),Vector2.zero,new Vector2(78,22),13);
        Solid(row,"Track",80,-7,90,8,new Color(.12f,.17f,.18f,1));
        var fill=Solid(row,"Fill_Editable",80,-7,90,8,color);fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillOrigin=0;fill.fillAmount=1;
        value=Label(row,"Value","100 / 100",new Vector2(178,0),new Vector2(78,22),11);
        return fill;
    }
    static RectTransform Box(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size,Vector2 pivot)
    {var rt=Rect(name,parent,position,size,anchor);rt.pivot=pivot;Decorate(rt);return rt;}
    static void Decorate(RectTransform rt)
    {
        var image=rt.GetComponent<Image>()??rt.gameObject.AddComponent<Image>();image.sprite=null;image.color=Panel;
        var outline=rt.GetComponent<Outline>()??rt.gameObject.AddComponent<Outline>();outline.effectColor=Edge;outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=false;
        // Thin scan lines are editable Images; none intercept clicks.
        for(int i=5;i<rt.sizeDelta.y-3;i+=5)Solid(rt,"Scanline_"+i,4,-i,rt.sizeDelta.x-8,1,new Color(.38f,.48f,.45f,.035f));
    }
    static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size,Vector2 anchor)
    {var obj=new GameObject(name,typeof(RectTransform));obj.layer=5;var rt=(RectTransform)obj.transform;rt.SetParent(parent,false);Place(rt,anchor,pos,size,new Vector2(0,1));return rt;}
    static void Place(RectTransform rt,Vector2 anchor,Vector2 pos,Vector2 size,Vector2 pivot)
    {rt.anchorMin=rt.anchorMax=anchor;rt.pivot=pivot;rt.sizeDelta=size;rt.anchoredPosition=pos;rt.localScale=Vector3.one;rt.localRotation=Quaternion.identity;}
    static Image Solid(Transform parent,string name,float x,float y,float w,float h,Color color)
    {var im=Rect(name,parent,new Vector2(x,y),new Vector2(w,h),new Vector2(0,1)).gameObject.AddComponent<Image>();im.color=color;im.raycastTarget=false;return im;}
    static Text Label(Transform parent,string name,string text,Vector2 pos,Vector2 size,int fontSize)
    {var label=Rect(name,parent,pos,size,new Vector2(0,1)).gameObject.AddComponent<Text>();label.font=font;label.fontSize=fontSize;label.text=text;label.color=Ink;label.alignment=TextAnchor.MiddleLeft;label.raycastTarget=false;label.horizontalOverflow=HorizontalWrapMode.Overflow;return label;}
    static void ClearChildren(Transform root){for(int i=root.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);}
    static void RemoveLayouts(RectTransform rt)
    {foreach(var c in rt.GetComponents<LayoutGroup>())UnityEngine.Object.DestroyImmediate(c);foreach(var c in rt.GetComponents<ContentSizeFitter>())UnityEngine.Object.DestroyImmediate(c);}
    static void Bind(UnityEngine.Object target,string name,UnityEngine.Object value){var so=new SerializedObject(target);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Int(UnityEngine.Object target,string name,int value){var so=new SerializedObject(target);so.FindProperty(name).intValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Float(UnityEngine.Object target,string name,float value){var so=new SerializedObject(target);so.FindProperty(name).floatValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Bool(UnityEngine.Object target,string name,bool value){var so=new SerializedObject(target);so.FindProperty(name).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void ColorValue(UnityEngine.Object target,string name,Color value){var so=new SerializedObject(target);so.FindProperty(name).colorValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static void Array(UnityEngine.Object target,string name,Image[] images){var so=new SerializedObject(target);var p=so.FindProperty(name);p.arraySize=images.Length;for(int i=0;i<images.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=images[i];so.ApplyModifiedPropertiesWithoutUndo();}

    [MenuItem("Tools/Survival HUD/Validate current layout")]
    public static void Validate()
    {
        var hud=UnityEngine.Object.FindObjectOfType<SurvivalHUD>(true);
        if(hud==null)throw new Exception("SurvivalHUD is missing.");
        var canvas=hud.GetComponentInParent<Canvas>();
        var hot=canvas.GetComponentInChildren<HotbarUI>(true);
        var inv=canvas.GetComponentInChildren<InventoryUI>(true);
        if(hot.GetComponentsInChildren<ItemSlotUI>(true).Length!=8)throw new Exception("Expected eight editable hotbar slots.");
        if(inv.GetComponentsInChildren<ItemSlotUI>(true).Length!=20)throw new Exception("Expected twenty editable inventory slots.");
        if(inv.GetComponentInChildren<InventoryPanelDrag>(true)==null)throw new Exception("Inventory title drag handle missing.");
        var slots=hot.GetComponentsInChildren<ItemSlotUI>(true);
        if(slots.Any(s=>Mathf.Abs(new SerializedObject(s).FindProperty("scaleSelected").floatValue-1)>0.001f))throw new Exception("Hotbar selection must not disturb alignment.");
        Directory.CreateDirectory("Artifacts/SurvivalHUD");
        File.WriteAllText("Artifacts/SurvivalHUD/validation.txt","PASS: 8 aligned authored hotbar slots; 20 authored inventory slots; draggable title bar; native editable Text/Image graphics; no eye behavior component. Scene: "+hud.gameObject.scene.path);
    }

    public static void ApplyBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
            Apply();
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            Directory.CreateDirectory("Artifacts/SurvivalHUD");
            File.WriteAllText("Artifacts/SurvivalHUD/error.txt",error.ToString());
            Debug.LogException(error);EditorApplication.Exit(1);
        }
    }
}
#endif
