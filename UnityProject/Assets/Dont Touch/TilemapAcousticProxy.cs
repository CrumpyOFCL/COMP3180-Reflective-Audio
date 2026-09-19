using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ReflectiveAudio2D
{
    /// <summary>
    /// CONDITION C enabler - generates invisible 3D geometry from a 2D tilemap.
    ///
    /// Steam Audio's Geometry component requires a Mesh Filter or Terrain. A Unity
    /// Tilemap has neither, so Export Active Scene exports NOTHING and every step
    /// afterwards fails silently. This reads the paths out of a CompositeCollider2D
    /// and extrudes each edge along Z into a wall, then caps the top and bottom.
    ///
    /// The caps are NOT optional. Without a floor and ceiling the enclosure is open
    /// on Z, rays escape, and you get almost no reverb.
    ///
    /// Usage: attach to an empty GameObject at the origin, assign the collider,
    /// right-click the component header and choose "Build Acoustic Mesh".
    /// Then add Steam Audio Geometry and run Steam Audio > Export Active Scene.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("Reflective Audio 2D/Tilemap Acoustic Proxy")]
    public class TilemapAcousticProxy : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Your tilemap's CompositeCollider2D. The TilemapCollider2D must have " +
                 "'Used By Composite' ticked or there will be no paths to read.")]
        [SerializeField] private CompositeCollider2D sourceCollider;

        [Header("Extrusion")]
        [Tooltip("Half the depth of the acoustic room on Z, in world units. This is " +
                 "effectively the ceiling height of your 2D world. It has NO visual " +
                 "consequence, so tune it by ear. Small = tight and bright.")]
        [SerializeField] private float halfDepth = 4f;

        [Tooltip("Floor and ceiling caps. Leave this on.")]
        [SerializeField] private bool buildCaps = true;

        [SerializeField] private float capPadding = 2f;

        [ContextMenu("Build Acoustic Mesh")]
        public void Build()
        {
            if (sourceCollider == null)
            {
                Debug.LogError("[TilemapAcousticProxy] No CompositeCollider2D assigned.");
                return;
            }

            if (sourceCollider.pathCount == 0)
            {
                Debug.LogError("[TilemapAcousticProxy] Collider has zero paths. Tick " +
                               "'Used By Composite' on the TilemapCollider2D.");
                return;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Walls: extrude every collider edge along Z.
            for (int p = 0; p < sourceCollider.pathCount; p++)
            {
                int pointCount = sourceCollider.GetPathPointCount(p);
                var points = new Vector2[pointCount];
                sourceCollider.GetPath(p, points);

                for (int i = 0; i < pointCount; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % pointCount];   // closed loop

                    AddQuad(verts, tris,
                        new Vector3(a.x, a.y, -halfDepth),
                        new Vector3(b.x, b.y, -halfDepth),
                        new Vector3(b.x, b.y,  halfDepth),
                        new Vector3(a.x, a.y,  halfDepth));
                }
            }

            // Caps: floor and ceiling across the whole level bounds.
            if (buildCaps)
            {
                Bounds b2 = sourceCollider.bounds;
                float x0 = b2.min.x - capPadding, x1 = b2.max.x + capPadding;
                float y0 = b2.min.y - capPadding, y1 = b2.max.y + capPadding;

                AddQuad(verts, tris,
                    new Vector3(x0, y0, -halfDepth), new Vector3(x1, y0, -halfDepth),
                    new Vector3(x1, y1, -halfDepth), new Vector3(x0, y1, -halfDepth));

                AddQuad(verts, tris,
                    new Vector3(x0, y0,  halfDepth), new Vector3(x1, y0,  halfDepth),
                    new Vector3(x1, y1,  halfDepth), new Vector3(x0, y1,  halfDepth));
            }

            var mesh = new Mesh { name = "AcousticProxyMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;  // levels get big
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;

#if UNITY_EDITOR
            // Save it. A runtime-only mesh does not survive a scene reload, and
            // Steam Audio exports from what is on disk.
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/AcousticProxyMesh.asset");
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(gameObject);
            Debug.Log($"[TilemapAcousticProxy] Saved mesh to {path}");
#endif
            Debug.Log($"[TilemapAcousticProxy] Built {verts.Count} verts, " +
                      $"{tris.Count / 3} triangles. If this is zero, stop and fix it " +
                      "before exporting the scene.");
        }

        // Double-sided so rays cannot leak through a wall from the wrong side.
        private static void AddQuad(List<Vector3> v, List<int> t,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);

            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);

            t.Add(i + 2); t.Add(i + 1); t.Add(i);
            t.Add(i + 3); t.Add(i + 2); t.Add(i);
        }
    }
}
