#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class BuildingWallColours
{
    const string Folder="Assets/Art/Environment/WallColours";
    // The albedo contains four square tiles along each axis.
    public const float WallTileSizeMetres=.20f;
    const float WallTextureRepeatMetres=WallTileSizeMetres*4;
    const string PreviewKey="BuildingWallColours.Preview";
    static double previewAt;
    static BuildingWallColours(){EditorApplication.update+=Preview;}
    static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
    static bool IsOuterWall(Material m)=>m!=null && (AssetDatabase.GetAssetPath(m)=="Assets/Scripts/AOSFogWar/Demo/Materials/URP/Wall.mat" || AssetDatabase.GetAssetPath(m)=="Assets/Scripts/AOSFogWar/Demo/Materials/Built-in/Wall 1.mat");
    public static void AuditBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        Directory.CreateDirectory("Artifacts/WallColourRevision");
        var result=new StringBuilder();
        foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
        {
            string path=PathOf(r.transform);
            if(r.sharedMaterials.Any(IsOuterWall) || (path.StartsWith("Restroom/") && (path.Contains("toilate_door")||path.Contains("shuichi/Cube.067"))))
                result.AppendLine(path+" | center="+r.bounds.center.ToString("F2")+" | size="+r.bounds.size.ToString("F2"));
        }
        File.WriteAllText("Artifacts/WallColourRevision/positions.txt",result.ToString());
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Environment/Apply restroom and mall wall colours")]
    public static void Apply()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/Test.unity")throw new InvalidOperationException("Open Test.unity first.");
        Directory.CreateDirectory(Folder);Directory.CreateDirectory("Artifacts/WallColourRevision");
        EditorSceneManager.SaveScene(scene,"Artifacts/WallColourRevision/Test-before-colours.unity",true);
        var renderers=UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true);
        // Preserve every imported stall renderer, not merely the door leaves.
        var protectedStalls=renderers.Where(r=>PathOf(r.transform).StartsWith("Restroom/chesuobaojian/")).ToDictionary(r=>r,r=>r.sharedMaterials);
        var white=MakeMaterial("RestroomPorcelainWhite",new Color(.95f,.98f,.95f,1),.28f);
        var cream=MakeMaterial("MallWarmIvory",Color.white,.10f);
        var floor=MakeMaterial("RestroomDarkFloor",new Color(.90f,.94f,.90f,1),.18f);
        SetTexture(white,"RestroomWallTiles.png");SetTexture(cream,"MallIvoryPlaster.png");SetTexture(floor,"RestroomFloorTiles.png");
        var report=new StringBuilder("Base checkpoint: 1eedb1a4\n");
        foreach(var r in renderers)
        {
            string path=PathOf(r.transform);
            if(!r.name.StartsWith("Cube") || !path.StartsWith("Environment") || !r.sharedMaterials.Any(m=>IsOuterWall(m) || (m!=null && AssetDatabase.GetAssetPath(m).StartsWith(Folder))))continue;
            Undo.RecordObject(r,"Set wall area colours");
            if(path=="Environment/Cube (4)")
            {
                // West wall runs through both the washroom and the mall.
                SplitWall(r,2,14.85f,"WestWall_ColourZones");r.sharedMaterials=new[]{white,cream};
                report.AppendLine(path+": porcelain for z <= 14.85; warm ivory beyond");
            }
            else if(path=="Environment/Cube (11)")
            {
                // South wall also continues beyond the washroom's eastern boundary.
                SplitWall(r,0,7.23f,"SouthWall_ColourZones");r.sharedMaterials=new[]{white,cream};
                report.AppendLine(path+": porcelain for x <= 7.23; warm ivory beyond");
            }
            else
            {
                bool restroom=path=="Environment/Cube (1)";
                r.sharedMaterials=new[]{restroom?white:cream};
                WorldUV(r);
                report.AppendLine(path+": "+(restroom?"restroom porcelain":"mall warm ivory"));
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        foreach(var r in renderers)
        {
            if(!PathOf(r.transform).StartsWith("Environment/") || !r.name.StartsWith("Plane"))continue;
            var bounds=r.bounds;
            if(bounds.center.y>.3f || bounds.center.y< -1 || bounds.max.x< -1.69f || bounds.min.x>7.23f || bounds.max.z< -1.62f || bounds.min.z>14.85f)continue;
            FloorRegion(r,floor);report.AppendLine(PathOf(r.transform)+": dark restroom floor inside bathroom only");
        }
        foreach(var pair in protectedStalls)
            if(!pair.Value.SequenceEqual(pair.Key.sharedMaterials))throw new InvalidOperationException("Stall material was altered: "+PathOf(pair.Key.transform));
        report.AppendLine("Verified unchanged stall renderers: "+protectedStalls.Count);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Artifacts/WallColourRevision/result.txt",report.ToString());
        Debug.Log("[WallColours] Applied area colours; preserved "+protectedStalls.Count+" stall renderers.");
    }
    static Material MakeMaterial(string name,Color colour,float smoothness)
    {
        string path=Folder+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",colour);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",smoothness);
        material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_ZWrite",1);
        material.SetFloat("_SrcBlend",1);material.SetFloat("_DstBlend",0);material.renderQueue=-1;
        material.SetOverrideTag("RenderType","Opaque");material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_EMISSION");EditorUtility.SetDirty(material);return material;
    }

    struct Vertex
    {
        public Vector3 position,normal;
        public Vector2 uv;
        public static Vertex Lerp(Vertex a,Vertex b,float t)=>new Vertex{position=Vector3.Lerp(a.position,b.position,t),normal=Vector3.Lerp(a.normal,b.normal,t).normalized,uv=Vector2.Lerp(a.uv,b.uv,t)};
    }
    static void SplitWall(MeshRenderer renderer,int axis,float boundary,string name)
    {
        var filter=renderer.GetComponent<MeshFilter>();
        // Always derive from the original primitive, making repeated application stable.
        var primitive=Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var source=filter.sharedMesh.name==name && primitive!=null?primitive:filter.sharedMesh;
        // The source is a built-in cube; split triangles only, leaving positions and colliders intact.
        var vertices=source.vertices;var normals=source.normals;var uvs=source.uv;
        var output=new List<Vector3>();var outputNormals=new List<Vector3>();var outputUVs=new List<Vector2>();
        var indices=new[]{new List<int>(),new List<int>()};
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            var triangles=source.GetTriangles(sub);
            for(int i=0;i<triangles.Length;i+=3)
            {
                var triangle=new List<Vertex>();
                for(int j=0;j<3;j++){int k=triangles[i+j];triangle.Add(new Vertex{position=vertices[k],normal=normals[k],uv=uvs[k]});}
                for(int side=0;side<2;side++)
                {
                    var polygon=Clip(triangle,renderer.transform,axis,boundary,side==0);
                    int start=output.Count;
                    foreach(var v in polygon){output.Add(v.position);outputNormals.Add(v.normal);outputUVs.Add(Project(renderer.transform,v.position,v.normal));}
                    for(int j=1;j<polygon.Count-1;j++){indices[side].Add(start);indices[side].Add(start+j);indices[side].Add(start+j+1);}
                }
            }
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(output);mesh.SetNormals(outputNormals);mesh.SetUVs(0,outputUVs);
        mesh.subMeshCount=2;mesh.SetTriangles(indices[0],0);mesh.SetTriangles(indices[1],1);mesh.RecalculateBounds();
        if((mesh.bounds.size-source.bounds.size).sqrMagnitude>.00001f)throw new InvalidOperationException("Wall shape changed while assigning colours.");
        string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);existing=mesh;}
        else
        {
            EditorUtility.CopySerialized(mesh,existing);
            // CopySerialized updates stored data but may leave the live editor's
            // mesh UV buffer stale. Use the native setter to refresh rendering too.
            existing.uv=mesh.uv;existing.UploadMeshData(false);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        Undo.RecordObject(filter,"Split display material regions");filter.sharedMesh=existing;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);EditorUtility.SetDirty(existing);
    }
    static void SetTexture(Material material,string filename)
    {
        string path=Folder+"/Textures/"+filename;
        AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;
        importer.mipmapEnabled=true;importer.anisoLevel=4;importer.maxTextureSize=1024;importer.SaveAndReimport();
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        material.SetTextureScale("_BaseMap",Vector2.one);EditorUtility.SetDirty(material);
    }
    static Vector2 Project(Transform transform,Vector3 position,Vector3 normal)
    {
        var p=transform.TransformPoint(position);var n=transform.TransformDirection(normal);
        if(Mathf.Abs(n.y)>.7f)return new Vector2(p.x,p.z)/WallTextureRepeatMetres;
        return (Mathf.Abs(n.x)>Mathf.Abs(n.z)?new Vector2(p.z,p.y):new Vector2(p.x,p.y))/WallTextureRepeatMetres;
    }
    static void WorldUV(MeshRenderer r)
    {
        var filter=r.GetComponent<MeshFilter>();if(filter==null)return;
        var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;var normals=mesh.normals;
        mesh.uv=vertices.Select((v,i)=>Project(r.transform,v,normals[i])).ToArray();
        // Different floors contain objects with identical names. Their world UVs must
        // never overwrite one shared mesh asset.
        mesh.name="Wall_"+PathOf(r.transform).Replace("/","_").Replace(" ","_")+"_WorldUV";
        filter.sharedMesh=SaveMesh(mesh,mesh.name);PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Folder+"/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(asset==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,asset);asset.uv=mesh.uv;asset.UploadMeshData(false);
        EditorUtility.SetDirty(asset);UnityEngine.Object.DestroyImmediate(mesh);return asset;
    }
    static void FloorRegion(MeshRenderer r,Material insideMaterial)
    {
        var filter=r.GetComponent<MeshFilter>();if(filter==null)return;
        string name="RestroomFloor_"+r.name.Replace(" ","_").Replace("(","").Replace(")","");
        string originalPath=Folder+"/"+name+"_Original.asset";
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
        if(source==null){source=UnityEngine.Object.Instantiate(filter.sharedMesh);source.name=name+"_Original";AssetDatabase.CreateAsset(source,originalPath);}
        var verts=source.vertices;var normals=source.normals;var uv=source.uv;
        var positions=new List<Vector3>();var outNormals=new List<Vector3>();var outUV=new List<Vector2>();
        var triangles=new[]{new List<int>(),new List<int>()};
        Action<List<Vertex>,int> emit=(polygon,side)=>
        {
            int start=positions.Count;
            foreach(var v in polygon){positions.Add(v.position);outNormals.Add(v.normal);var p=r.transform.TransformPoint(v.position);outUV.Add(side==0?new Vector2(p.x,p.z)/1.6f:v.uv);}
            for(int i=1;i<polygon.Count-1;i++){triangles[side].Add(start);triangles[side].Add(start+i);triangles[side].Add(start+i+1);}
        };
        var input=source.triangles;
        for(int i=0;i<input.Length;i+=3)
        {
            var polygon=new List<Vertex>();for(int j=0;j<3;j++){int k=input[i+j];polygon.Add(new Vertex{position=verts[k],normal=normals[k],uv=uv[k]});}
            int[] axes={0,0,2,2};float[] limits={-1.69f,7.23f,-1.62f,14.85f};bool[] less={false,true,false,true};
            for(int p=0;p<4 && polygon.Count>0;p++)
            {
                emit(Clip(polygon,r.transform,axes[p],limits[p],!less[p]),1);
                polygon=Clip(polygon,r.transform,axes[p],limits[p],less[p]);
            }
            emit(polygon,0);
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(positions);mesh.SetNormals(outNormals);mesh.SetUVs(0,outUV);
        mesh.subMeshCount=2;mesh.SetTriangles(triangles[0],0);mesh.SetTriangles(triangles[1],1);mesh.RecalculateBounds();
        if((mesh.bounds.size-source.bounds.size).sqrMagnitude>.00001f)throw new InvalidOperationException("Floor bounds changed.");
        var outside=r.sharedMaterials.Last();filter.sharedMesh=SaveMesh(mesh,name);r.sharedMaterials=new[]{insideMaterial,outside};
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);PrefabUtility.RecordPrefabInstancePropertyModifications(r);
    }
    static List<Vertex> Clip(List<Vertex> input,Transform transform,int axis,float boundary,bool less)
    {
        var result=new List<Vertex>();
        for(int i=0;i<input.Count;i++)
        {
            var a=input[i];var b=input[(i+1)%input.Count];
            float da=transform.TransformPoint(a.position)[axis]-boundary,db=transform.TransformPoint(b.position)[axis]-boundary;
            bool insideA=less?da<=0:da>=0,insideB=less?db<=0:db>=0;
            if(insideA)result.Add(a);
            if(insideA!=insideB)result.Add(Vertex.Lerp(a,b,da/(da-db)));
        }
        return result;
    }
    public static void ApplyBatch()
    {
        try{EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");Apply();SessionState.SetBool(PreviewKey,true);EditorApplication.EnterPlaymode();}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void CaptureEditorPreview()
    {
        var audit=new StringBuilder();
        foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true).Where(r=>PathOf(r.transform).StartsWith("Environment/") && r.sharedMaterials.Any(m=>m!=null && m.name=="RestroomPorcelainWhite")))
        {
            var mesh=r.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var uv=mesh.uv;var n=mesh.normals;var triangles=mesh.triangles;
            audit.AppendLine(PathOf(r.transform)+" mesh="+mesh.name+" size="+r.bounds.size);
            foreach(var m in r.sharedMaterials) audit.AppendLine(m.name+" tiling="+m.GetTextureScale("_BaseMap")+" texture="+(m.GetTexture("_BaseMap")!=null?m.GetTexture("_BaseMap").name:"none"));
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1];if(Mathf.Abs(n[a].y)>.7f)continue;
                float world=Vector3.Distance(r.transform.TransformPoint(v[a]),r.transform.TransformPoint(v[b]));float tex=Vector2.Distance(uv[a],uv[b]);
                audit.AppendLine("edge metres="+world+" UV="+tex+" ratio="+(world/Mathf.Max(tex,.00001f))+" normal="+n[a]);
                if(i>45)break;
            }
        }
        File.WriteAllText("Artifacts/WallColourRevision/uv-audit.txt",audit.ToString());
        var capture=typeof(SurvivalHUDValidation).GetMethod("Capture",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var camera=Camera.main;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        var hud=UnityEngine.Object.FindObjectOfType<SurvivalHUD>();var canvas=hud.GetComponentInParent<Canvas>();bool wasEnabled=canvas.enabled;
        // Runtime floor visibility is not active in Edit Mode; hide only for this capture.
        var hidden=UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true).Where(r=>r.enabled && (r.bounds.min.y>3.2f || r.name=="Upper wall mask")).ToArray();
        try
        {
            foreach(var r in hidden)r.enabled=false;
            camera.transform.SetPositionAndRotation(new Vector3(3.8f,7.5f,-2f),Quaternion.Euler(50,-12,0));
            canvas.enabled=false;
            capture.Invoke(null,new object[]{hud,"Artifacts/WallColourRevision/textured-preview.png"});
        }
        finally{camera.transform.SetPositionAndRotation(oldPosition,oldRotation);canvas.enabled=wasEnabled;foreach(var r in hidden)r.enabled=true;}
    }
    static void Preview()
    {
        if(!SessionState.GetBool(PreviewKey,false)||!EditorApplication.isPlaying)return;
        if(previewAt==0){previewAt=EditorApplication.timeSinceStartup+3;return;}
        if(EditorApplication.timeSinceStartup<previewAt)return;
        try
        {
            // Reuse the existing editor-only capture helper without modifying the saved UI scripts.
            var capture=typeof(SurvivalHUDValidation).GetMethod("Capture",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            capture.Invoke(null,new object[]{UnityEngine.Object.FindObjectOfType<SurvivalHUD>(),"Artifacts/WallColourRevision/preview.png"});
            SessionState.SetBool(PreviewKey,false);EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);SessionState.SetBool(PreviewKey,false);EditorApplication.Exit(1);}
    }
}
#endif
