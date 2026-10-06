using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DestructionLab.EditorTools
{
    /// <summary>
    /// Creates (or rebuilds) Assets/Scenes/ConvenienceStore.unity: the convenience-store lot blockout in the lab
    /// (same bootstrap, controller, HUD and tools as DestructionLab). Run from the menu or headless:
    /// Unity -batchmode -quit -projectPath DestructionPOC -executeMethod DestructionLab.EditorTools.ConvenienceStoreSceneSetup.Build
    /// </summary>
    public static class ConvenienceStoreSceneSetup
    {
        const string ScenePath = "Assets/Scenes/ConvenienceStore.unity";
        internal const string ModelPath = "Assets/Destruction/Models/ConvenienceStore.fbx";

        // The model's lot is 50 x 40 m centred on the origin; the pad adds a margin around it.
        static readonly Vector2 LotSize = new Vector2(60f, 50f);

        [MenuItem("Destruction Lab/Rebuild Convenience Store Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<DestructionSettings>(LabSceneSetup.SettingsAssetPath) == null)
                LabSceneSetup.Build(); // creates settings + materials first
            var settings = AssetDatabase.LoadAssetAtPath<DestructionSettings>(LabSceneSetup.SettingsAssetPath);

            var model = LabSceneSetup.ConfigureAuthoredModel(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[DestructionLab] {ModelPath} missing; convenience store scene not built.");
                return;
            }

            var scene = LabSceneSetup.NewLabScene();
            var go = new GameObject("Destruction Lab");
            var boot = go.AddComponent<LabBootstrap>();
            boot.settings = settings;
            boot.convenienceStoreModel = model;
            boot.startScenarioId = "store";
            boot.lotCenter = Vector2.zero;
            boot.lotSize = LotSize;

            // The drivable yard: crane, skid steer, dozer, roll-off container, player and the cleanup gauge. The rigs
            // are the CraneTest ones, so the models get the same material/import setup.
            var yard = go.AddComponent<StoreYard>();
            yard.craneModel = CraneTestSceneSetup.ConfigurePropModel(CraneTestSceneSetup.CraneModel);
            yard.skidSteerModel = CraneTestSceneSetup.ConfigurePropModel(CraneTestSceneSetup.SkidSteerModel);
            yard.dozerModel = CraneTestSceneSetup.ConfigurePropModel(CraneTestSceneSetup.DozerModel);
            if (yard.craneModel == null)
            {
                Debug.LogWarning("[DestructionLab] crane FBX missing; convenience store built without the drivable yard.");
                Object.DestroyImmediate(yard);
            }
            else
            {
                if (yard.skidSteerModel == null) Debug.LogWarning("[DestructionLab] skid steer FBX missing; store yard built without it.");
                if (yard.dozerModel == null) Debug.LogWarning("[DestructionLab] landfill dozer FBX missing; store yard built without it.");
                boot.yard = yard;
            }

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DestructionLab] Convenience store scene rebuilt.");
        }
    }
}
