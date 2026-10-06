using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DestructionLab.EditorTools
{
    /// <summary>
    /// Creates (or rebuilds) Assets/Scenes/Title.unity, the title menu that leads into ConvenienceStore.unity, and
    /// makes it the first scene in the build. Run from the menu or headless:
    /// Unity -batchmode -quit -projectPath DestructionPOC -executeMethod DestructionLab.EditorTools.TitleSceneSetup.Build
    /// </summary>
    public static class TitleSceneSetup
    {
        const string ScenePath = "Assets/Scenes/Title.unity";

        [MenuItem("Destruction Lab/Rebuild Title Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The menu is drawn with IMGUI over the camera's clear colour.
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f);
            cam.cullingMask = 0;

            new GameObject("Title Menu").AddComponent<TitleMenu>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Title first, so the built game opens on it.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            if (!scenes.Any(s => s.path == "Assets/Scenes/ConvenienceStore.unity"))
                scenes.Add(new EditorBuildSettingsScene("Assets/Scenes/ConvenienceStore.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DestructionLab] Title scene rebuilt.");
        }
    }
}
