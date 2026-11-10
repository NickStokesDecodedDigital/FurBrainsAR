using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FurBrains.EditorTools
{
    /// <summary>
    /// Repairs known problems in imported FurBrains models, automatically, every time Unity imports them.
    /// The source FBX files are left untouched.
    /// </summary>
    /// <remarks>
    /// <b>Inside-out parts.</b> Ziara's right eye was made by mirroring the left one, and the mirror flipped its faces:
    /// both the triangle winding and the stored normals point inward. Unreal hides this because its eye material renders
    /// both sides; Unity's default (back-face culling) only draws faces that point at the camera, so the eye was drawn
    /// from the inside. For the files listed below we find each separate piece of the mesh (each eye), check whether its
    /// normals point away from its centre, and if not, flip its triangles and normals.
    ///
    /// This is opt-in per file because "normals point away from the centre" is only a safe test for closed, roughly
    /// round shapes like eyeballs, not for things like capes or eyelash cards.
    /// </remarks>
    public class FurBrainsModelPostprocessor : AssetPostprocessor
    {
        static readonly HashSet<string> k_FixInsideOutParts = new()
        {
            "Assets/FurBrains/Models/Ziara/Eyebrows.FBX",
        };

        // Bump when changing the logic so Unity reimports affected models.
        public override uint GetVersion() => 2;

        void OnPostprocessModel(GameObject root)
        {
            if (!k_FixInsideOutParts.Contains(assetPath))
                return;

            var meshes = new HashSet<Mesh>();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null) meshes.Add(r.sharedMesh);
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null) meshes.Add(f.sharedMesh);

            foreach (var mesh in meshes)
            {
                var flipped = FixInsideOutPieces(mesh);
                if (flipped > 0)
                    Debug.Log($"[FurBrains] {assetPath}: flipped {flipped} inside-out piece(s) of mesh '{mesh.name}'.");
            }
        }

        /// <summary>Returns the number of pieces that were flipped.</summary>
        static int FixInsideOutPieces(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (normals.Length != vertices.Length)
                return 0;

            // 1. Group vertices into connected pieces (union-find, like a disjoint-set in C++).
            //    Vertices at the same position are merged too, because Unity splits vertices along UV seams.
            var parent = new int[vertices.Length];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            var byPosition = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 10000f);
                if (byPosition.TryGetValue(key, out var other)) Union(i, other);
                else byPosition[key] = i;
            }

            var subMeshTriangles = new int[mesh.subMeshCount][];
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = subMeshTriangles[s] = mesh.GetTriangles(s);
                for (var t = 0; t < tris.Length; t += 3)
                {
                    Union(tris[t], tris[t + 1]);
                    Union(tris[t], tris[t + 2]);
                }
            }

            // 2. For each piece: centre, and how many normals point away from it.
            var centre = new Dictionary<int, Vector3>();
            var count = new Dictionary<int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = Find(i);
                centre[p] = centre.GetValueOrDefault(p) + vertices[i];
                count[p] = count.GetValueOrDefault(p) + 1;
            }

            var outward = new Dictionary<int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = Find(i);
                if (Vector3.Dot(normals[i], vertices[i] - centre[p] / count[p]) > 0f)
                    outward[p] = outward.GetValueOrDefault(p) + 1;
            }

            // 3. Pieces where most normals point inward are inside-out.
            var flip = new HashSet<int>();
            foreach (var p in count.Keys)
                if (outward.GetValueOrDefault(p) * 2 < count[p])
                    flip.Add(p);
            if (flip.Count == 0)
                return 0;

            // 4. Flip them: reverse each triangle's winding (swap two corners) and negate the normals.
            for (var i = 0; i < vertices.Length; i++)
                if (flip.Contains(Find(i)))
                    normals[i] = -normals[i];
            mesh.normals = normals;

            for (var s = 0; s < subMeshTriangles.Length; s++)
            {
                var tris = subMeshTriangles[s];
                for (var t = 0; t < tris.Length; t += 3)
                    if (flip.Contains(Find(tris[t])))
                        (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
                mesh.SetTriangles(tris, s);
            }

            if (mesh.tangents.Length == vertices.Length)
                mesh.RecalculateTangents();

            return flip.Count;
        }
    }
}
