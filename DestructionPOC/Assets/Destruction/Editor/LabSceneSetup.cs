using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DestructionLab.EditorTools
{
    /// <summary>
    /// Creates (or rebuilds) the lab scene, settings asset and materials. Run from the menu or headless:
    /// Unity -batchmode -quit -projectPath DestructionPOC -executeMethod DestructionLab.EditorTools.LabSceneSetup.Build
    /// </summary>
    public static class LabSceneSetup
    {
        const string Root = "Assets/Destruction";
        const string ScenePath = "Assets/Scenes/DestructionLab.unity";
        const string SettingsPath = Root + "/Settings/DestructionSettings.asset";
        const string MaterialDir = Root + "/Materials";

        [MenuItem("Destruction Lab/Reset Settings To Defaults")]
        public static void ResetSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DestructionSettings>(SettingsPath);
            if (settings == null) { Build(); return; }
            var fresh = ScriptableObject.CreateInstance<DestructionSettings>();
            fresh.name = settings.name;
            fresh.pieceMaterial = settings.pieceMaterial;
            fresh.groundMaterial = settings.groundMaterial;
            fresh.overlayMaterial = settings.overlayMaterial;
            EditorUtility.CopySerialized(fresh, settings);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[DestructionLab] Settings reset to code defaults.");
        }

        [MenuItem("Destruction Lab/Rebuild Lab Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Settings");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory("Assets/Scenes");

            var settings = AssetDatabase.LoadAssetAtPath<DestructionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DestructionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            settings.pieceMaterial = MaterialAsset("Piece", lit, Color.white, 0.25f);
            settings.groundMaterial = MaterialAsset("Ground", lit, new Color(0.34f, 0.38f, 0.35f), 0.1f);
            settings.overlayMaterial = OverlayMaterial();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var light = Object.FindAnyObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(50f, 150f, 0f); // lights the +z faces most scenarios present
                light.intensity = 1.6f;
                light.shadows = LightShadows.Soft;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.64f, 0.74f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

            var go = new GameObject("Destruction Lab");
            var boot = go.AddComponent<LabBootstrap>();
            boot.settings = settings;
            boot.authoredModel = ConfigureAuthoredModel(AuthoredModelPath);
            boot.warehouseModel = ConfigureAuthoredModel(WarehouseModelPath);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[DestructionLab] Lab scene, settings and materials rebuilt.");
        }

        const string AuthoredModelPath = Root + "/Models/SampleBuilding.fbx";
        internal const string WarehouseModelPath = Root + "/Models/Warehouse.fbx";
        internal const string SettingsAssetPath = SettingsPath;

        /// <summary>
        /// Imports an authored blockout with settings the structure importer relies on: real metres, no
        /// rescaling, readable meshes and no generated colliders (the lab makes its own).
        /// </summary>
        [MenuItem("Destruction Lab/Reimport Authored Model")]
        public static void ReimportAuthoredModel()
        {
            ConfigureAuthoredModel(AuthoredModelPath);
            ConfigureAuthoredModel(WarehouseModelPath);
        }

        internal static GameObject ConfigureAuthoredModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[DestructionLab] no authored model at {path}; the lab will run without scenario 8.");
                return null;
            }
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material MaterialAsset(string name, Shader shader, Color color, float smoothness)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material OverlayMaterial()
        {
            string path = $"{MaterialDir}/DiagnosticsOverlay.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = 4000;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
