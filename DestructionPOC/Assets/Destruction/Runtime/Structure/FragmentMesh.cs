using UnityEngine;

namespace DestructionLab
{
    /// <summary>Builds a flat-shaded mesh for a convex cell, recentred on the cell's centre of mass.</summary>
    public static class FragmentMesh
    {
        public static Mesh Build(ConvexCell cell, string name)
        {
            int triangles = 0;
            foreach (var poly in cell.faces) triangles += Mathf.Max(0, poly.Count - 2);
            var verts = new Vector3[triangles * 3];
            var normals = new Vector3[triangles * 3];
            var tris = new int[triangles * 3];

            int v = 0;
            foreach (var poly in cell.faces)
            {
                if (poly.Count < 3) continue;
                Vector3 n = ConvexCell.Normal(poly);
                for (int i = 1; i + 1 < poly.Count; i++)
                {
                    // Faces are already wound so that cross(b-a, c-a) points out of the solid, which is the
                    // direction Unity treats as the triangle's front.
                    verts[v] = poly[0] - cell.centroid;
                    verts[v + 1] = poly[i] - cell.centroid;
                    verts[v + 2] = poly[i + 1] - cell.centroid;
                    normals[v] = normals[v + 1] = normals[v + 2] = n;
                    tris[v] = v;
                    tris[v + 1] = v + 1;
                    tris[v + 2] = v + 2;
                    v += 3;
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
