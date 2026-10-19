using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
#endif

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Builds the acoustics test scene: a row of closed rooms of exact, known sizes
    /// joined by doorways, ending in an open "outdoor" area.
    ///
    /// The sizes matter. They are the ground truth for RoomEstimationTask, and the
    /// same geometry feeds all three conditions (zones, raycasts, Steam Audio's
    /// proxy mesh), so every condition is judged on identical rooms.
    ///
    /// Usage: put this on the Tilemap object, assign the wall tile, then right-click
    /// the component header and choose "Build Rooms". It is safe to run again after
    /// changing sizes: tiles are redrawn and the generated zones and spots are
    /// moved in place, so snapshots assigned to the zones are kept.
    ///
    /// Sizes are in tiles. With the default Grid cell size (1, 1), 1 tile = 1 unit,
    /// which is 1 m to EarlyReflections2D at Units Per Metre = 1.
    /// </summary>
    // The settings are only read by the editor-only builder, so player builds
    // would otherwise warn that they are never used.
#pragma warning disable 0414
    [AddComponentMenu("Reflective Audio 2D/Acoustics Gallery Builder")]
    public class AcousticsGalleryBuilder : MonoBehaviour
    {
        [System.Serializable]
        public class Room
        {
            public string name = "Room";
            [Tooltip("Interior width in tiles.")]
            [Min(1)] public int width = 10;
            [Tooltip("Interior height in tiles. Ignored for an open room.")]
            [Min(1)] public int height = 8;
            [Tooltip("Open = floor only, no walls or ceiling (the outdoor contrast).")]
            public bool open;
        }

        [Header("Tiles")]
        [SerializeField] private Tilemap tilemap;
        [Tooltip("Greybox Rule (Assets/Tiles/Foreground/Greybox).")]
        [SerializeField] private TileBase wallTile;
        [Tooltip("Layer the walls go on. The probe, the reflections and the player's " +
                 "Ground Layers must all include it.")]
        [SerializeField] private string wallLayer = "Solid";

        [Header("Layout (tiles)")]
        [Min(1)][SerializeField] private int wallThickness = 2;
        [Tooltip("Height of the doorway cut into each shared wall, from the floor.")]
        [Min(1)][SerializeField] private int doorHeight = 3;

        [SerializeField]
        private Room[] rooms =
        {
            new Room { name = "A Small",    width = 4,  height = 4 },
            new Room { name = "B Corridor", width = 20, height = 3 },
            new Room { name = "C Medium",   width = 10, height = 8 },
            new Room { name = "D Hall",     width = 25, height = 15 },
            new Room { name = "E Outdoors", width = 30, height = 10, open = true },
        };

        [Header("Generated objects")]
        [Tooltip("One trigger per room with an AcousticZone2D (Condition A). Zones " +
                 "overlap across each doorway so the snapshots cross-fade there.")]
        [SerializeField] private bool createZones = true;
        [Tooltip("Height of each listening spot above the floor, in tiles. " +
                 "RoomEstimationTask moves the player here.")]
        [Min(0f)][SerializeField] private float spotHeight = 1f;

        [Tooltip("Optional. Its test spaces are filled from the rooms (spot, true " +
                 "width, true enclosure) and a blindfold overlay is made if it has none.")]
        [SerializeField] private RoomEstimationTask estimationTask;

        private const string ZonesRootName = "Acoustic Zones (Condition A)";
        private const string SpotsRootName = "Listening Spots";
        private const string EdgesRootName = "World Edges";
        private const string ZonePrefix = "Zone - ";
        private const string SpotPrefix = "Spot - ";
        private const int OpenZoneHeight = 12;
        private const int EdgeHeight = 30;

#if UNITY_EDITOR
        /// <summary>Editor only. Also called by Tools > Reflective Audio 2D > Create Demo Scene.</summary>
        [ContextMenu("Build Rooms")]
        public void Build()
        {
            if (tilemap == null) tilemap = GetComponent<Tilemap>();
            if (tilemap == null || wallTile == null)
            {
                Debug.LogError("[AcousticsGalleryBuilder] Assign the Tilemap and the wall tile first.", this);
                return;
            }
            if (rooms == null || rooms.Length == 0) return;

            SetUpCollision();

            Undo.RegisterCompleteObjectUndo(tilemap, "Build acoustics rooms");
            tilemap.ClearAllTiles();

            int t = wallThickness;
            var starts = new int[rooms.Length];
            int x = 0;
            for (int i = 0; i < rooms.Length; i++)
            {
                starts[i] = x;
                x += rooms[i].width + t;   // every room is followed by a wall slot
            }

            for (int i = 0; i < rooms.Length; i++)
                DrawRoom(i, starts[i]);

            tilemap.CompressBounds();

            // Build the wall collision now. The tilemap collider otherwise catches up at the
            // end of the frame, so a scene saved straight after building keeps an empty
            // composite collider and the player falls through the walls.
            var tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
            if (tilemapCollider != null) tilemapCollider.ProcessTilemapChanges();
            var compositeCollider = tilemap.GetComponent<CompositeCollider2D>();
            if (compositeCollider != null) compositeCollider.GenerateGeometry();

            var zones = new List<Transform>();
            var spots = new List<Transform>();
            Transform zonesRoot = createZones ? FindOrCreateRoot(ZonesRootName) : null;
            Transform spotsRoot = FindOrCreateRoot(SpotsRootName);

            for (int i = 0; i < rooms.Length; i++)
            {
                if (createZones) zones.Add(PlaceZone(zonesRoot, rooms[i], starts[i]));
                spots.Add(PlaceSpot(spotsRoot, rooms[i], starts[i]));
            }

            if (zonesRoot != null) RemoveStale(zonesRoot, ZonePrefix);
            RemoveStale(spotsRoot, SpotPrefix);
            PlaceEdges(starts);

            if (estimationTask != null) FillEstimationTask(spots);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log(Report(starts), this);
        }

        // ---------- Geometry ----------

        private void DrawRoom(int i, int x)
        {
            int t = wallThickness;
            Room r = rooms[i];
            Room prev = i > 0 ? rooms[i - 1] : null;
            Room next = i + 1 < rooms.Length ? rooms[i + 1] : null;

            // Floor under the room and under the wall slots either side.
            Fill(x - t, -t, x + r.width + t, 0);

            if (r.open) return;

            // Ceiling, spanning both side walls.
            Fill(x - t, r.height, x + r.width + t, r.height + t);

            // Left wall: only needed for the first room or after an open area.
            // Otherwise the previous room's right wall is this room's left wall.
            if (prev == null || prev.open)
            {
                Fill(x - t, 0, x, r.height + t);
                if (prev != null) Clear(x - t, 0, x, Mathf.Min(doorHeight, r.height));
            }

            // Right wall, tall enough for whichever side is taller.
            int top = r.height + t;
            if (next != null && !next.open) top = Mathf.Max(top, next.height + t);
            Fill(x + r.width, 0, x + r.width + t, top);

            // Doorway into the next room.
            if (next != null)
            {
                int door = Mathf.Min(doorHeight, r.height);
                if (!next.open) door = Mathf.Min(door, next.height);
                Clear(x + r.width, 0, x + r.width + t, door);
            }
        }

        private void Fill(int x0, int y0, int x1, int y1)
        {
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), wallTile);
        }

        private void Clear(int x0, int y0, int x1, int y1)
        {
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), null);
        }

        // Layer, TilemapCollider2D merged into a CompositeCollider2D on a static body.
        // TilemapAcousticProxy reads the composite's paths for Condition C.
        private void SetUpCollision()
        {
            GameObject go = tilemap.gameObject;

            int layer = LayerMask.NameToLayer(wallLayer);
            if (layer < 0)
                Debug.LogWarning($"[AcousticsGalleryBuilder] No layer called '{wallLayer}'. Walls left on {LayerMask.LayerToName(go.layer)}.", this);
            else if (go.layer != layer)
            {
                Undo.RecordObject(go, "Set wall layer");
                go.layer = layer;
            }

            var tileCollider = go.GetComponent<TilemapCollider2D>();
            if (tileCollider == null) tileCollider = Undo.AddComponent<TilemapCollider2D>(go);

            var body = go.GetComponent<Rigidbody2D>();
            if (body == null) body = Undo.AddComponent<Rigidbody2D>(go);

            var composite = go.GetComponent<CompositeCollider2D>();
            if (composite == null) composite = Undo.AddComponent<CompositeCollider2D>(go);

            Undo.RecordObjects(new Object[] { tileCollider, body, composite }, "Set up wall collision");
            tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            body.bodyType = RigidbodyType2D.Static;
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }

        // ---------- Generated objects ----------

        private Rect RoomRectWorld(Room r, int x, int x0Pad, int x1Pad)
        {
            int h = r.open ? OpenZoneHeight : r.height;
            Vector3 min = tilemap.CellToWorld(new Vector3Int(x - x0Pad, 0, 0));
            Vector3 max = tilemap.CellToWorld(new Vector3Int(x + r.width + x1Pad, h, 0));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Transform PlaceZone(Transform root, Room r, int x)
        {
            // Padded by the wall thickness both sides, so neighbouring zones overlap
            // inside the doorway and the snapshots blend there instead of dropping out.
            Rect rect = RoomRectWorld(r, x, wallThickness, wallThickness);
            Transform zone = FindOrCreateChild(root, ZonePrefix + r.name);
            zone.gameObject.layer = 2;   // Ignore Raycast

            Undo.RecordObject(zone, "Place zone");
            zone.position = rect.center;

            var box = zone.GetComponent<BoxCollider2D>();
            if (box == null) box = Undo.AddComponent<BoxCollider2D>(zone.gameObject);
            Undo.RecordObject(box, "Size zone");
            box.isTrigger = true;
            box.offset = Vector2.zero;
            box.size = rect.size;

            if (zone.GetComponent<AcousticZone2D>() == null)
                Undo.AddComponent<AcousticZone2D>(zone.gameObject);

            return zone;
        }

        private Transform PlaceSpot(Transform root, Room r, int x)
        {
            Rect rect = RoomRectWorld(r, x, 0, 0);
            float cellHeight = CellSize().y;

            Transform spot = FindOrCreateChild(root, SpotPrefix + r.name);
            Undo.RecordObject(spot, "Place spot");
            spot.position = new Vector3(rect.center.x, rect.yMin + spotHeight * cellHeight, 0f);
            return spot;
        }

        // An open area has no walls, so stop the player walking off the end of the
        // world. Ignore Raycast keeps these out of the probe and reflection rays.
        private void PlaceEdges(int[] starts)
        {
            Transform root = FindOrCreateRoot(EdgesRootName);
            bool firstOpen = rooms[0].open;
            int last = rooms.Length - 1;
            bool lastOpen = rooms[last].open;

            PlaceEdge(root, "Edge - Left", firstOpen, starts[0] - wallThickness);
            PlaceEdge(root, "Edge - Right", lastOpen, starts[last] + rooms[last].width + wallThickness);
        }

        private void PlaceEdge(Transform root, string edgeName, bool needed, int cellX)
        {
            Transform existing = root.Find(edgeName);
            if (!needed)
            {
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
                return;
            }

            Vector2 cell = CellSize();
            Vector3 foot = tilemap.CellToWorld(new Vector3Int(cellX, 0, 0));

            Transform edge = existing != null ? existing : FindOrCreateChild(root, edgeName);
            edge.gameObject.layer = 2;   // Ignore Raycast
            Undo.RecordObject(edge, "Place edge");
            edge.position = foot + new Vector3(0f, EdgeHeight * cell.y * 0.5f, 0f);

            var box = edge.GetComponent<BoxCollider2D>();
            if (box == null) box = Undo.AddComponent<BoxCollider2D>(edge.gameObject);
            Undo.RecordObject(box, "Size edge");
            box.isTrigger = false;
            box.size = new Vector2(cell.x, EdgeHeight * cell.y);
        }

        private void FillEstimationTask(List<Transform> spots)
        {
            var so = new SerializedObject(estimationTask);
            SerializedProperty spaces = so.FindProperty("spaces");
            if (spaces == null)
            {
                Debug.LogWarning("[AcousticsGalleryBuilder] RoomEstimationTask has no 'spaces' field; fill it by hand.", this);
                return;
            }

            float cellWidth = CellSize().x;
            spaces.arraySize = rooms.Length;
            for (int i = 0; i < rooms.Length; i++)
            {
                SerializedProperty s = spaces.GetArrayElementAtIndex(i);
                s.FindPropertyRelative("spaceId").stringValue = rooms[i].name;
                s.FindPropertyRelative("listenerPosition").objectReferenceValue = spots[i];
                s.FindPropertyRelative("trueWidth").floatValue = rooms[i].width * cellWidth;
                s.FindPropertyRelative("trueEnclosure").floatValue = rooms[i].open ? 0f : 1f;
            }

            SerializedProperty blindfold = so.FindProperty("blindfoldOverlay");
            if (blindfold != null && blindfold.objectReferenceValue == null)
                blindfold.objectReferenceValue = CreateBlindfold();

            WirePlayer(so);
            so.ApplyModifiedProperties();
        }

        // Player scripts switched off during the estimation run, matched by class
        // name so this file doesn't depend on any one game's player code.
        private static readonly string[] FreezeTypeNames =
            { "Player", "PlayerMovement", "PlayerAimAndShoot", "DemoPlayer2D" };

        // Fills the task's player references if they are empty: the player to move,
        // the FMOD listener the probe sound plays at, and the scripts to switch off.
        // The player is the object tagged "Player", the same tag AcousticZone2D uses.
        private static void WirePlayer(SerializedObject so)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            SetIfEmpty(so.FindProperty("playerRoot"), player.transform);

            var listener = player.GetComponentInChildren<FMODUnity.StudioListener>(true);
            if (listener != null) SetIfEmpty(so.FindProperty("listenerRoot"), listener.transform);

            SerializedProperty freeze = so.FindProperty("disableWhileRunning");
            if (freeze != null && freeze.arraySize == 0)
            {
                var found = new List<MonoBehaviour>();
                foreach (MonoBehaviour behaviour in player.GetComponents<MonoBehaviour>())
                    if (behaviour != null && System.Array.IndexOf(FreezeTypeNames, behaviour.GetType().Name) >= 0)
                        found.Add(behaviour);

                freeze.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                    freeze.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }
        }

        private static void SetIfEmpty(SerializedProperty property, Object value)
        {
            if (property != null && property.objectReferenceValue == null)
                property.objectReferenceValue = value;
        }

        // Full-screen black panel. RoomEstimationTask shows it during the run so the
        // participant judges by ear. Its question box (OnGUI) draws on top.
        private GameObject CreateBlindfold()
        {
            var canvasGo = new GameObject("Blindfold (Room Estimation)", typeof(RectTransform), typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create blindfold");
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)black.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = black.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            canvasGo.SetActive(false);
            return canvasGo;
        }

        // ---------- Helpers ----------

        private Vector2 CellSize()
        {
            Vector3 o = tilemap.CellToWorld(Vector3Int.zero);
            Vector3 x = tilemap.CellToWorld(new Vector3Int(1, 0, 0));
            Vector3 y = tilemap.CellToWorld(new Vector3Int(0, 1, 0));
            return new Vector2(Vector3.Distance(o, x), Vector3.Distance(o, y));
        }

        private Transform FindOrCreateRoot(string rootName)
        {
            foreach (GameObject go in gameObject.scene.GetRootGameObjects())
                if (go.name == rootName) return go.transform;

            var created = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(created, "Create " + rootName);
            return created.transform;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null) return child;

            var created = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(created, "Create " + childName);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        // Removes generated children whose room no longer exists.
        private void RemoveStale(Transform root, string prefix)
        {
            var wanted = new HashSet<string>();
            foreach (Room r in rooms) wanted.Add(prefix + r.name);

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name.StartsWith(prefix) && !wanted.Contains(child.name))
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private string Report(int[] starts)
        {
            Vector2 cell = CellSize();
            var sb = new StringBuilder();
            sb.AppendLine($"[AcousticsGalleryBuilder] Built {rooms.Length} rooms (cell {cell.x:0.##} x {cell.y:0.##} units). Ground truth:");
            sb.AppendLine("room, width, height, enclosure");
            for (int i = 0; i < rooms.Length; i++)
            {
                Room r = rooms[i];
                string h = r.open ? "open" : (r.height * cell.y).ToString("0.##");
                sb.AppendLine($"{r.name}, {r.width * cell.x:0.##}, {h}, {(r.open ? 0 : 1)}");
            }
            if (!Mathf.Approximately(cell.x, 1f) || !Mathf.Approximately(cell.y, 1f))
                sb.AppendLine("Note: cells are not 1 unit, so sizes above are in world units, not tiles.");
            return sb.ToString();
        }
#endif
    }
#pragma warning restore 0414
}