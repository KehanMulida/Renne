using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>生成并保存独立的卫生间装饰，不添加碰撞器或拾取逻辑。</summary>
public static class WashroomDetailSetup
{
    private const string Folder = "Assets/Art/Environment/WashroomDetails";
    private static Material paper, cardboard, metal, wood, cloth, plastic, green, amber, label, water, dirt;
    private static readonly List<Mesh> generatedMeshes = new List<Mesh>();

    public static void Apply()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        if (GameObject.Find("Washroom Environmental Details") != null)
            throw new System.InvalidOperationException("Washroom details already exist; edit the existing objects instead.");
        Directory.CreateDirectory(Folder);
        paper = Mat("Paper", new Color(.76f, .74f, .64f), 0f, .15f);
        cardboard = Mat("Cardboard", new Color(.23f, .14f, .075f), 0f, .1f);
        metal = Mat("Worn Metal", new Color(.21f, .24f, .25f), .65f, .5f);
        wood = Mat("Handle Wood", new Color(.27f, .15f, .07f), 0f, .25f);
        cloth = Mat("Dirty Mop Cloth", new Color(.27f, .31f, .27f), 0f, .1f);
        plastic = Mat("Old Blue Plastic", new Color(.07f, .19f, .23f), 0f, .45f);
        green = Mat("Green Bottle", new Color(.025f, .115f, .055f), .1f, .85f);
        amber = Mat("Amber Bottle", new Color(.19f, .075f, .018f), .05f, .8f);
        label = Mat("Bottle Label", new Color(.65f, .56f, .36f), 0f, .15f);
        dirt = Mat("Damp Grime", new Color(.085f, .1f, .09f), 0f, .5f);
        water = Mat("Puddle Water", new Color(.09f, .22f, .28f, .36f), .1f, .96f);
        MaterialFadeUtil.SetTransparent(water);
        ConfigureWaterMask(water);
        // Small water surfaces reflect the vision light; no extra lights or reflection cameras.

        var root = new GameObject("Washroom Environmental Details").transform;
        var cleaning = Group(root, "Cleaning Corner", OnFloor(.75f, 3.35f));
        Bucket(cleaning, new Vector3(0f, 0f, 0f));
        Mop(cleaning, new Vector3(-.35f, 0f, .18f), -12f);
        Broom(cleaning, new Vector3(-.52f, 0f, .4f));
        Detergent(cleaning, new Vector3(.28f, 0f, -.15f));
        var bin = Group(root, "Wastebasket", OnFloor(.65f, 10.8f));
        Shape(bin, "Open Waste Bin", Lathe("Waste Bin", new[] {
            new Vector2(0f,0f),new Vector2(.13f,0f),new Vector2(.17f,.37f),
            new Vector2(.145f,.37f),new Vector2(.115f,.03f),new Vector2(0f,.03f)}), plastic);
        for (int i = 0; i < 4; i++)
            Primitive(bin, "Discarded Paper", PrimitiveType.Sphere,
                new Vector3(-.06f + i * .035f, .32f + (i % 2) * .025f, (i % 2) * .05f),
                new Vector3(.085f,.06f,.07f), paper);

        // Wall-side supplies, a fallen roll and paper scraps beside (rather than across) the aisle.
        Roll(root, OnFloor(.7f, 6.6f) + Vector3.up * .105f, new Vector3(0f, 0f, 90f), true);
        Roll(root, OnFloor(.85f, 3.65f), Vector3.zero, false);
        for (int i = 0; i < 4; i++)
        {
            float x = .65f + (i % 2) * .22f;
            float z = 6.1f + i * .2f;
            var scrap = Primitive(root, "Torn Toilet Paper", PrimitiveType.Cube,
                OnFloor(x,z) + Vector3.up * .012f, new Vector3(.12f,.006f,.17f), paper);
            scrap.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
        }

        // Select visible toilet bowls to place paper holders relative to the existing model geometry.
        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (renderer.transform.IsChildOf(root)) continue;
            if (renderer.name.ToLowerInvariant().Contains("toil") || renderer.name.ToLowerInvariant().Contains("cesuo"))
                Debug.Log($"WASHROOM_ANCHOR {renderer.name} center={renderer.bounds.center} size={renderer.bounds.size}");
        }
        Holder(root, new Vector3(3.2f, FloorAt(3.2f, 6.1f) + .8f, 6.1f));
        Holder(root, new Vector3(3.2f, FloorAt(3.2f, 8.3f) + .8f, 8.3f));
        MountHolders(root);

        Bottle(root, OnFloor(.8f, 8.7f), false, green, "Abandoned Green Bottle");
        Bottle(root, OnFloor(.95f, 9.1f) + Vector3.up * .073f, true, amber, "Fallen Beer Bottle");
        Bottle(root, OnFloor(4.5f, 5f), false, amber, "Bottle Beside Toilet");
        Puddle(root, "Washbasin Leak", .65f, 7.9f, .65f, .32f, 2);
        Puddle(root, "Cleaning Water", 1.05f, 3.65f, .48f, .3f, 8);
        Puddle(root, "Stall Damp Patch", 3.9f, 7.9f, .4f, .29f, 12);
        for (int i = 0; i < 5; i++)
            Puddle(root, "Water Drip", 1.08f + .09f * (i % 2), 4.2f + .29f * i,
                .045f + .012f * (i % 2), .065f, 30 + i);

        foreach (var mesh in generatedMeshes)
            AssetDatabase.CreateAsset(mesh, Folder + "/" + mesh.name + ".asset");
        generatedMeshes.Clear();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"WASHROOM_DETAILS_OK: {root.GetComponentsInChildren<Renderer>().Length} decorative renderers, no colliders");
        PlayerLightFogPreview.Render();
    }

    public static void Refine()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Test.unity");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Puddle Water.mat");
        ConfigureWaterMask(material);
        foreach (var filter in GameObject.Find("Washroom Environmental Details").GetComponentsInChildren<MeshFilter>())
        {
            if (filter.GetComponent<Renderer>().sharedMaterial != material ||
                !AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(Folder)) continue;
            var mesh=filter.sharedMesh;
            var vertices=mesh.vertices;
            var uv=new Vector2[vertices.Length];
            float width=mesh.bounds.extents.x, depth=mesh.bounds.extents.z;
            for(int i=0;i<vertices.Length;i++)
                uv[i]=new Vector2(.5f+vertices[i].x/(width*2f),.5f+vertices[i].z/(depth*2f));
            mesh.uv=uv;
            EditorUtility.SetDirty(mesh);
        }
        EditorUtility.SetDirty(material);
        MountHolders(GameObject.Find("Washroom Environmental Details").transform);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        PlayerLightFogPreview.Render();
    }

    private static void ConfigureWaterMask(Material water)
    {
        var waterMask = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Soft Puddle Mask.asset");
        bool create = waterMask == null;
        if (create) waterMask = new Texture2D(128,128,TextureFormat.RGBA32,false);
        waterMask.name = "Soft Puddle Mask";
        waterMask.wrapMode = TextureWrapMode.Clamp;
        for (int y=0;y<128;y++)
            for (int x=0;x<128;x++)
            {
                float u=(x+.5f)/64f-1f,v=(y+.5f)/64f-1f;
                float radius=Mathf.Sqrt(u*u+v*v);
                float edge=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.6f,1f,radius));
                waterMask.SetPixel(x,y,new Color(1,1,1,edge));
            }
        waterMask.Apply();
        if (create) AssetDatabase.CreateAsset(waterMask,Folder+"/Soft Puddle Mask.asset");
        else EditorUtility.SetDirty(waterMask);
        water.SetTexture("_BaseMap",waterMask);
    }

    private static void MountHolders(Transform root)
    {
        var sceneMeshes = Object.FindObjectsOfType<MeshRenderer>();
        foreach (Transform item in root)
        {
            if (item.name != "Wall Toilet Paper Holder") continue;
            float nearest = 1.2f;
            Vector3 position = item.position;
            foreach (var renderer in sceneMeshes)
            {
                if (renderer.transform.IsChildOf(root)) continue;
                var b=renderer.bounds;
                if(b.size.x < .8f || b.size.y < .9f || b.size.z > .4f ||
                    position.x < b.min.x+.15f || position.x > b.max.x-.15f ||
                    position.y < b.min.y || position.y > b.max.y) continue;
                float distance=Mathf.Abs(position.z-b.min.z);
                if(distance >= nearest) continue;
                nearest=distance;
                position.z=b.min.z-.025f;
            }
            item.position=position;
        }
    }

    private static float FloorAt(float x, float z)
    {
        // Query render bounds as some imported floors do not have colliders.
        float floor = -.65f;
        bool found = false;
        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (renderer.GetComponentInParent<Transform>().root.name == "Washroom Environmental Details") continue;
            var b = renderer.bounds;
            if (b.size.x < 1f || b.size.z < 1f || b.size.y > .6f || b.max.y > .3f || b.max.y < -1.5f) continue;
            if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z)
            {
                floor = found ? Mathf.Max(floor, b.max.y) : b.max.y;
                found = true;
            }
        }
        return floor;
    }

    private static Vector3 OnFloor(float x, float z) => new Vector3(x, FloorAt(x,z) + .008f, z);

    private static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = name;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
        return material;
    }

    private static Transform Group(Transform parent, string name, Vector3 position)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        return obj.transform;
    }

    private static GameObject Primitive(Transform parent, string name, PrimitiveType type,
        Vector3 position, Vector3 scale, Material material)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    private static GameObject Shape(Transform parent, string name, Mesh mesh, Material material)
    {
        var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    private static Mesh Lathe(string name, Vector2[] profile)
    {
        const int sides = 24;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int row = 0; row < profile.Length; row++)
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(Mathf.Cos(a) * profile[row].x, profile[row].y, Mathf.Sin(a) * profile[row].x));
                uv.Add(new Vector2(i / (float)sides, row / (float)(profile.Length - 1)));
            }
        for (int row = 0; row < profile.Length - 1; row++)
            for (int i = 0; i < sides; i++)
            {
                int a = row * (sides + 1) + i, b = a + sides + 1;
                triangles.AddRange(new[] { a,b,a+1,a+1,b,b+1 });
            }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return mesh;
    }

    private static void Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material material)
    {
        var rod = Primitive(parent,name,PrimitiveType.Cylinder,(a+b)*.5f,
            new Vector3(radius*2f, Vector3.Distance(a,b)*.5f,radius*2f),material);
        rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up,b-a);
    }

    private static void Bucket(Transform parent, Vector3 position)
    {
        var group = Group(parent,"Mop Bucket",position);
        Shape(group,"Hollow Bucket",Lathe("Bucket",new[] {new Vector2(0,0),new Vector2(.145f,0),
            new Vector2(.19f,.34f),new Vector2(.17f,.35f),new Vector2(.13f,.035f),new Vector2(0,.035f)}),plastic);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 12f, b = (i+1) * Mathf.PI / 12f;
            Rod(group,"Bucket Handle",new Vector3(Mathf.Cos(a)*.18f,.33f+Mathf.Sin(a)*.2f,0),
                new Vector3(Mathf.Cos(b)*.18f,.33f+Mathf.Sin(b)*.2f,0),.009f,metal);
        }
        Primitive(group,"Dirty Water",PrimitiveType.Cylinder,new Vector3(0,.12f,0),new Vector3(.285f,.005f,.285f),water);
    }

    private static void Mop(Transform parent, Vector3 position, float tilt)
    {
        var group = Group(parent,"Leaning Mop",position);
        group.localRotation = Quaternion.Euler(0,0,tilt);
        Rod(group,"Mop Handle",new Vector3(0,.08f,0),new Vector3(0,1.45f,0),.014f,wood);
        Primitive(group,"Mop Clamp",PrimitiveType.Cube,new Vector3(0,.09f,0),new Vector3(.23f,.055f,.09f),metal);
        for (int i=0;i<9;i++)
        {
            var strand = Primitive(group,"Mop Cloth Strand",PrimitiveType.Cube,
                new Vector3((i-4)*.031f,.04f,(i%3-1)*.04f),new Vector3(.035f,.045f,.22f),cloth);
            strand.transform.localRotation = Quaternion.Euler(0,(i-4)*7f,0);
        }
    }

    private static void Broom(Transform parent, Vector3 position)
    {
        var group = Group(parent,"Broom",position);
        group.localRotation = Quaternion.Euler(0,0,8);
        Rod(group,"Broom Handle",new Vector3(0,.08f,0),new Vector3(0,1.25f,0),.012f,wood);
        Primitive(group,"Broom Head",PrimitiveType.Cube,new Vector3(0,.11f,0),new Vector3(.29f,.06f,.075f),plastic);
        for(int i=0;i<10;i++)
            Primitive(group,"Broom Bristles",PrimitiveType.Cube,new Vector3((i-4.5f)*.027f,.045f,0),
                new Vector3(.019f,.08f,.07f),cloth);
    }

    private static void Detergent(Transform parent, Vector3 position)
    {
        var group = Group(parent,"Cleaning Detergent",position);
        Primitive(group,"Detergent Body",PrimitiveType.Cube,new Vector3(0,.13f,0),new Vector3(.14f,.25f,.09f),plastic);
        Primitive(group,"Detergent Label",PrimitiveType.Cube,new Vector3(0,.14f,-.046f),new Vector3(.1f,.11f,.004f),paper);
        Primitive(group,"Detergent Cap",PrimitiveType.Cylinder,new Vector3(0,.28f,0),new Vector3(.052f,.028f,.052f),metal);
    }

    private static void Roll(Transform parent, Vector3 position, Vector3 rotation, bool loosePaper)
    {
        var group = Group(parent,"Toilet Paper Roll",position);
        group.localRotation = Quaternion.Euler(rotation);
        Shape(group,"Paper Roll",Lathe("Paper Roll " + generatedMeshes.Count,new[] {new Vector2(.033f,0),new Vector2(.105f,0),
            new Vector2(.105f,.13f),new Vector2(.033f,.13f),new Vector2(.033f,0)}),paper);
        Shape(group,"Cardboard Core",Lathe("Paper Core " + generatedMeshes.Count,new[] {new Vector2(.028f,0),new Vector2(.033f,0),
            new Vector2(.033f,.132f),new Vector2(.028f,.132f),new Vector2(.028f,0)}),cardboard);
        if(loosePaper)
            Primitive(group,"Unrolled Paper Tail",PrimitiveType.Cube,new Vector3(.13f,.065f,.02f),new Vector3(.16f,.125f,.004f),paper);
    }

    private static void Holder(Transform root, Vector3 position)
    {
        var group = Group(root,"Wall Toilet Paper Holder",position);
        Primitive(group,"Holder Backplate",PrimitiveType.Cube,Vector3.zero,new Vector3(.18f,.12f,.025f),metal);
        Rod(group,"Holder Arm",new Vector3(-.075f,0,0),new Vector3(-.075f,0,-.14f),.012f,metal);
        Rod(group,"Holder Spindle",new Vector3(-.075f,0,-.14f),new Vector3(.1f,0,-.14f),.012f,metal);
        Roll(group,new Vector3(.07f,0,-.14f),new Vector3(0,0,90),false);
        Primitive(group,"Hanging Toilet Paper",PrimitiveType.Cube,new Vector3(0,-.15f,-.245f),new Vector3(.125f,.27f,.004f),paper);
    }

    private static void Bottle(Transform parent, Vector3 position, bool fallen, Material material, string name)
    {
        var group=Group(parent,name,position);
        if(fallen) group.localRotation=Quaternion.Euler(90,35,0);
        Shape(group,"Bottle Body",Lathe(name + " Mesh",new[] {new Vector2(0,0),new Vector2(.065f,0),
            new Vector2(.07f,.025f),new Vector2(.07f,.21f),new Vector2(.035f,.26f),new Vector2(.029f,.35f),
            new Vector2(.033f,.36f),new Vector2(.033f,.375f),new Vector2(0,.375f)}),material);
        Primitive(group,"Bottle Cap",PrimitiveType.Cylinder,new Vector3(0,.375f,0),new Vector3(.069f,.009f,.069f),metal);
        Shape(group,"Worn Label",Lathe(name + " Label",new[] {new Vector2(.0705f,.085f),new Vector2(.0705f,.175f)}),label);
        Primitive(group,"Label Stripe",PrimitiveType.Cube,new Vector3(0,.13f,-.071f),new Vector3(.06f,.012f,.002f),cardboard);
    }

    private static void Puddle(Transform root,string name,float x,float z,float width,float depth,int seed)
    {
        const int count=32;
        var vertices=new Vector3[count+1];
        var triangles=new int[count*3];
        var colors=new Color[count+1];
        var uv=new Vector2[count+1];
        uv[0]=new Vector2(.5f,.5f);
        vertices[0]=Vector3.zero; colors[0]=Color.white;
        for(int i=0;i<count;i++)
        {
            float a=i*Mathf.PI*2/count;
            float r=.85f+.15f*Mathf.Sin(a*3f+seed)+.05f*Mathf.Cos(a*7f+seed);
            vertices[i+1]=new Vector3(Mathf.Cos(a)*width*r,0,Mathf.Sin(a)*depth*r);
            uv[i+1]=new Vector2(.5f+Mathf.Cos(a)*r*.5f,.5f+Mathf.Sin(a)*r*.5f);
            triangles[i*3]=0; triangles[i*3+1]=(i+1)%count+1; triangles[i*3+2]=i+1;
            colors[i+1]=Color.white;
        }
        var mesh=new Mesh {name=name+" "+seed};
        mesh.vertices=vertices;mesh.triangles=triangles;mesh.colors=colors;mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        var obj=Shape(root,name,mesh,water);
        obj.transform.localPosition=OnFloor(x,z)+Vector3.up*.005f;
        obj.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
    }
}
