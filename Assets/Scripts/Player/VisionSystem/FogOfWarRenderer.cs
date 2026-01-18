using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FogOfWarRenderer : MonoBehaviour
{
    private MeshFilter filter;
    private MeshRenderer meshRenderer;

    void Awake()
    {
        filter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        // 建议你放自定义 FOW Shader，这里先用透明材质
        meshRenderer.material = new Material(Shader.Find("Unlit/Color"))
        {
            color = new Color(0, 0, 0, 0.75f)
        };
    }

    public void ApplyMesh(Mesh mesh)
    {
        filter.mesh = mesh;
    }
}
