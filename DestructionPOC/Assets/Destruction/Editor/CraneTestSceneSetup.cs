using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DestructionLab.EditorTools
{
    /// <summary>
    /// Creates (or rebuilds) Assets/Scenes/CraneTest.unity: the wrecking crane, the warehouse and a first-person
    /// player on an open plane. Run from the menu or headless:
    /// Unity -batchmode -quit -projectPath DestructionPOC -executeMethod DestructionLab.EditorTools.CraneTestSceneSetup.Build
    /// </summary>
    public static class CraneTestSceneSetup
    {
        const string ScenePath = "Assets/Scenes/CraneTest.unity";
        const string CraneModelPath = "Assets/Destruction/Models/WreckingCrane/WreckingCrane.fbx";
        const string ExcavatorModelPath = "Assets/Destruction/Models/Excavator/Excavator.fbx";
        const string WheelLoaderModelPath = "Assets/Destruction/Models/WheelLoader/WheelLoader.fbx";
        const string SkidSteerModelPath = "Assets/Destruction/Models/SkidSteer/SkidSteer.fbx";
        const string DozerModelPath = "Assets/Destruction/Models/LandfillDozer/LandfillDozer.fbx";
        const string MaterialDir = "Assets/Destruction/Materials/Crane";

        // name, Blender linear base colour, metallic, roughness (mirrors Tools/blender/build_wrecking_crane.py)
        static readonly (string name, Color linear, float metallic, float roughness)[] CraneMaterials =
        {
            ("CraneYellow", new Color(0.95f, 0.62f, 0.04f), 0f, 0.6f),
            ("CraneWornYellow", new Color(0.66f, 0.38f, 0.04f), 0f, 0.7f),
            ("CraneCharcoal", new Color(0.075f, 0.08f, 0.09f), 0.2f, 0.65f),
            ("CraneCabGlass", new Color(0.30f, 0.40f, 0.50f), 0f, 0.25f),
            ("CraneDarkSteel", new Color(0.13f, 0.14f, 0.16f), 0.85f, 0.4f),
        };

        [MenuItem("Destruction Lab/Build Crane Test Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<DestructionSettings>(LabSceneSetup.SettingsAssetPath) == null)
                LabSceneSetup.Build(); // creates settings + materials first
            var settings = AssetDatabase.LoadAssetAtPath<DestructionSettings>(LabSceneSetup.SettingsAssetPath);

            var crane = ConfigureCraneModel();
            var warehouse = LabSceneSetup.ConfigureAuthoredModel(LabSceneSetup.WarehouseModelPath);
            if (crane == null || warehouse == null)
            {
                Debug.LogError("[DestructionLab] crane or warehouse FBX missing; crane test scene not built.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var light = Object.FindAnyObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
                light.intensity = 1.6f;
                light.shadows = LightShadows.Soft;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.64f, 0.74f);
                // Fixed three-quarter overhead view; tune on the component. Field defaults match the class.
                cam.gameObject.AddComponent<CraneOverheadCamera>();
                cam.orthographic = false;
                cam.fieldOfView = 35f;
                cam.orthographicSize = 13f;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 250f;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

            var go = new GameObject("Crane Test");
            var boot = go.AddComponent<CraneTestBootstrap>();
            boot.settings = settings;
            boot.craneModel = crane;
            boot.warehouseModel = warehouse;
            // Excavators share the crane's materials and import settings.
            boot.excavatorModel = ConfigurePropModel(ExcavatorModelPath);
            if (boot.excavatorModel == null) Debug.LogWarning("[DestructionLab] excavator FBX missing; CraneTest built without excavators.");
            boot.wheelLoaderModel = ConfigurePropModel(WheelLoaderModelPath);
            boot.skidSteerModel = ConfigurePropModel(SkidSteerModelPath);
            if (boot.wheelLoaderModel == null || boot.skidSteerModel == null) Debug.LogWarning("[DestructionLab] loader FBX missing; CraneTest built without it.");

            boot.dozerModel = ConfigurePropModel(DozerModelPath);
            if (boot.dozerModel == null) Debug.LogWarning("[DestructionLab] landfill dozer FBX missing; CraneTest built without it.");

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DestructionLab] Crane test scene rebuilt.");
        }

        static GameObject ConfigureCraneModel() => ConfigurePropModel(CraneModelPath);

        static GameObject ConfigurePropModel(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return null;
            Directory.CreateDirectory(MaterialDir);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var m in CraneMaterials)
            {
                string path = $"{MaterialDir}/{m.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = lit;
                mat.SetColor("_BaseColor", m.linear.gamma);
                mat.SetFloat("_Metallic", m.metallic);
                mat.SetFloat("_Smoothness", 1f - m.roughness);
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        }
    }
}
