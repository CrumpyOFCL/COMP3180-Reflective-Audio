using System.IO;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ReflectiveAudio2D.EditorTools
{
    /// <summary>
    /// Tools > Reflective Audio 2D > Create Demo Scene.
    ///
    /// Builds a playable demo in a new scene, using only this package: the
    /// acoustics gallery (five rooms of known size), a stand-in player with the
    /// listener rig (Listener2D, AcousticProbe2D, EarlyReflections2D,
    /// ImageSourceEchoes2D), a ConditionManager set to Condition B, and the
    /// room-estimation task. It makes a plain wall tile, so no art is needed.
    ///
    /// Your FMOD project still has to provide the buses and global parameters
    /// (see README.md), and the player's Impact Event has to be assigned.
    /// </summary>
    public static class DemoSceneCreator
    {
        // Paths follow wherever the package folder was put (Assets/ReflectiveAudio2D,
        // Assets/Scripts/ReflectiveAudio2D, ...).
        private static string DemoFolder => PackageRoot + "/Demo";
        private static string ScenePath => DemoFolder + "/ReflectiveAudio2D_Demo.unity";
        private static string WallTexturePath => DemoFolder + "/DemoWall.png";
        private static string WallTilePath => DemoFolder + "/DemoWall.asset";
        private static string UnlitMaterialPath => DemoFolder + "/DemoUnlit.mat";

        /// <summary>The package folder: the parent of the Editor folder holding this script.</summary>
        internal static string PackageRoot
        {
            get
            {
                foreach (string guid in AssetDatabase.FindAssets("DemoSceneCreator t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path) != "DemoSceneCreator.cs") continue;
                    string editorFolder = Path.GetDirectoryName(path).Replace('\\', '/');
                    return Path.GetDirectoryName(editorFolder).Replace('\\', '/');
                }
                return "Assets/ReflectiveAudio2D";
            }
        }
        private const string WallLayerName = "Solid";

        [MenuItem("Tools/Reflective Audio 2D/Create Demo Scene")]
        private static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int wallLayer = EnsureLayer(WallLayerName);
            if (wallLayer < 0)
            {
                EditorUtility.DisplayDialog("Reflective Audio 2D",
                    $"There is no free user layer for '{WallLayerName}'. Free one in Project Settings > Tags and Layers.", "OK");
                return;
            }
            int wallMask = 1 << wallLayer;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load the assets only after the new scene exists. Opening a scene unloads
            // assets nothing in it uses, which turned earlier references into None.
            EnsureFolder(DemoFolder);
            TileBase wallTile = EnsureWallTile();
            Material unlit = EnsureUnlitMaterial();

            // Walls: one tilemap, built into rooms by the gallery builder.
            var grid = new GameObject("Grid", typeof(Grid));
            var wallsGo = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer));
            wallsGo.transform.SetParent(grid.transform, false);
            wallsGo.layer = wallLayer;
            if (unlit != null) wallsGo.GetComponent<TilemapRenderer>().sharedMaterial = unlit;
            var builder = wallsGo.AddComponent<AcousticsGalleryBuilder>();

            // Player: a plain block that walks, jumps and fires at walls.
            var player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            player.tag = "Player";
            // Same layer as a game's own player, if the project has one (created if not).
            int playerLayer = EnsureLayer("Player");
            if (playerLayer >= 0) player.layer = playerLayer;
            if (Physics2D.GetIgnoreLayerCollision(player.layer, wallLayer))
                Debug.LogWarning($"[Reflective Audio 2D] The {LayerMask.LayerToName(player.layer)} layer doesn't collide " +
                                 $"with {WallLayerName}, so the player will fall through the walls. Tick that pair in " +
                                 "Project Settings > Physics 2D > Layer Collision Matrix.");
            player.transform.localScale = new Vector3(0.8f, 1.6f, 1f);
            var box = player.GetComponent<BoxCollider2D>();
            box.size = Vector2.one;           // 1 x 1 block, scaled to 0.8 x 1.6
            box.offset = Vector2.zero;
            var body = player.GetComponent<Rigidbody2D>();
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.freezeRotation = true;
            if (unlit != null) player.GetComponent<SpriteRenderer>().sharedMaterial = unlit;
            player.AddComponent<DemoPlayer2D>();

            // Camera follows the player by being its child.
            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(player.transform, false);
            cameraGo.transform.localScale = new Vector3(1f / 0.8f, 1f / 1.6f, 1f);
            cameraGo.transform.localPosition = new Vector3(0f, 1f, -10f);
            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);

            // Listener rig, as in the showcase project.
            var listenerGo = new GameObject("Listener");
            listenerGo.transform.SetParent(player.transform, false);
            listenerGo.AddComponent<StudioListener>();
            var listener2D = listenerGo.AddComponent<Listener2D>();
            var probe = listenerGo.AddComponent<AcousticProbe2D>();
            var reflections = listenerGo.AddComponent<EarlyReflections2D>();
            var echoes = listenerGo.AddComponent<ImageSourceEchoes2D>();

            Edit(listener2D, so =>
            {
                SetObject(so, "player", player.transform);
                SetObject(so, "cameraTransform", cameraGo.transform);
                SetFloat(so, "cameraBias", 0f);
                SetVector2(so, "earOffset", new Vector2(0f, 0.5f));   // the block's pivot is its centre
            });
            Edit(probe, so => SetMask(so, "surfaceLayers", wallMask));
            Edit(reflections, so =>
            {
                SetMask(so, "surfaceLayers", wallMask);
                SetFloat(so, "minUsefulDelayMs", 6f);
            });
            Edit(echoes, so => SetMask(so, "wallLayers", wallMask));

            // One condition at a time; starts on B so the raycast system is heard first.
            var conditionGo = new GameObject("Condition Manager");
            var conditions = conditionGo.AddComponent<ConditionManager>();
            Edit(conditions, so =>
            {
                SetObject(so, "probe", probe);
                SetObject(so, "reflections", reflections);
                SerializedProperty start = so.FindProperty("startingCondition");
                if (start != null) start.enumValueIndex = (int)AcousticCondition.RaycastDynamic;
            });

            var taskGo = new GameObject("Room Estimation Task");
            var task = taskGo.AddComponent<RoomEstimationTask>();

            Edit(builder, so =>
            {
                SetObject(so, "tilemap", wallsGo.GetComponent<Tilemap>());
                SetObject(so, "wallTile", wallTile);
                SetObject(so, "estimationTask", task);
                SerializedProperty layerName = so.FindProperty("wallLayer");
                if (layerName != null) layerName.stringValue = WallLayerName;
            });

            builder.Build();
            ReportWalls(wallsGo.GetComponent<Tilemap>(), wallTile, unlit);

            // Start in the first room.
            GameObject spots = GameObject.Find("Listening Spots");
            if (spots != null && spots.transform.childCount > 0)
                player.transform.position = spots.transform.GetChild(0).position;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = player;

            Debug.Log("[Reflective Audio 2D] Demo scene saved to " + ScenePath +
                      ". Assign an Impact Event on Player > Demo Player 2D, check the bus paths on " +
                      "Condition Manager, then press Play. 7 / 8 / 9 switch conditions A / B / C.");
        }

        // ---------- assets ----------

        private static TileBase EnsureWallTile()
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(WallTilePath);
            if (tile != null && tile.sprite != null) return tile;

            if (!File.Exists(WallTexturePath))
            {
                const int size = 16;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    pixels[y * size + x] = edge ? new Color32(95, 100, 115, 255) : new Color32(150, 155, 170, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(WallTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(WallTexturePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(WallTexturePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(WallTexturePath);
            if (sprite == null)
                Debug.LogError("[Reflective Audio 2D] Could not load the wall sprite from " + WallTexturePath +
                               ". Select it and set Texture Type to Sprite (2D and UI).");

            // Repairs a tile left without a sprite by an earlier attempt.
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, WallTilePath);
            }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Grid;
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Tile>(WallTilePath);
        }

        // One Console line on what was built, so an empty-looking scene says why.
        private static void ReportWalls(Tilemap tilemap, TileBase wallTile, Material material)
        {
            int cells = 0;
            foreach (TileBase cell in tilemap.GetTilesBlock(tilemap.cellBounds))
                if (cell != null) cells++;

            Sprite sprite = wallTile is Tile tile ? tile.sprite : null;
            var composite = tilemap.GetComponent<CompositeCollider2D>();
            int paths = composite != null ? composite.pathCount : 0;
            if (cells > 0 && paths == 0)
                Debug.LogError("[Reflective Audio 2D] The walls were drawn but have no collision, so the player " +
                               "will fall through. Update AcousticsGalleryBuilder.cs, or right-click the builder > " +
                               "Build Rooms, wait a moment and save the scene.");
            if (cells == 0)
            {
                Debug.LogError("[Reflective Audio 2D] No wall tiles were drawn (wall tile: " +
                               (wallTile != null ? wallTile.name : "missing") +
                               "). Look for an AcousticsGalleryBuilder error above this line.");
                return;
            }

            Debug.Log($"[Reflective Audio 2D] Walls: {cells} tiles, {paths} collision outline(s). Sprite: " +
                      (sprite != null ? sprite.name : "MISSING, so the walls are invisible but still solid") +
                      ". Material: " +
                      (material != null ? material.shader.name : "project default (under URP 2D this is lit and draws black without a 2D light)") + ".");
        }

        // Under URP's 2D renderer, the default sprite material is lit and draws black
        // without a 2D light. An unlit material keeps the demo visible either way.
        private static Material EnsureUnlitMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) return null;   // built-in pipeline: the default sprite material is already unlit

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, UnlitMaterialPath);
            return material;
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
            return -1;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------- serialized field helpers (fields are private on the components) ----------

        private static void Edit(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetVector2(SerializedObject so, string name, Vector2 value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.vector2Value = value;
        }

        private static void SetMask(SerializedObject so, string name, int mask)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = mask;
        }
    }
}