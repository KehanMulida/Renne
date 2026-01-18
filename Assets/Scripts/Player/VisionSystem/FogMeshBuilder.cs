using UnityEngine;
using System.Collections.Generic;

public class FogMeshBuilder : MonoBehaviour
{
    public Mesh BuildMesh(List<Vector3> points, Vector3 origin)
    {
        Mesh mesh = new Mesh();
        int count = points.Count;

        Vector3[] vertices = new Vector3[count + 1];
        int[] triangles = new int[count * 3];

        vertices[0] = origin; // 中心点

        for (int i = 0; i < count; i++)
        {
            vertices[i + 1] = points[i];

            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % count + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
