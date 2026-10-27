using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>Batch entry points (Unity -batchmode -executeMethod ...):
    ///   FH.WorldForge.WFBuild.BuildPreview -wfRecipe &lt;recipe.json&gt; [-wfOut &lt;folder&gt;]
    ///       the preview scene (camera + WFPreview) saved as Assets/WorldForge/Preview.unity, the recipe copied to
    ///       StreamingAssets/WorldForge/recipe.json (the player reads it at start; edit it there and press F5), and a
    ///       Windows player built to &lt;folder&gt;/WorldForgePreview.exe (default Build/)
    ///   FH.WorldForge.WFBuild.GenerateScene -wfRecipe &lt;recipe.json&gt;
    ///       a scene with the world generated in the editor (Assets/WorldForge/Generated.unity) for editing</summary>
    public static class WFBuild
    {
        static string Arg(string key, string def = null)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        public static void BuildPreview()
        {
            var recipe = Arg("-wfRecipe"); var outDir = Arg("-wfOut", Path.GetFullPath("Build"));
            if (string.IsNullOrEmpty(recipe) || !File.Exists(recipe)) { Debug.LogError("WF_BUILD no recipe (-wfRecipe)"); EditorApplication.Exit(2); return; }
            Directory.CreateDirectory("Assets/WorldForge"); Directory.CreateDirectory("Assets/StreamingAssets/WorldForge");
            File.Copy(recipe, "Assets/StreamingAssets/WorldForge/recipe.json", true);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera", typeof(Camera), typeof(AudioListener)); cam.tag = "MainCamera";
            new GameObject("WorldForge Preview").AddComponent<WFPreview>();
            const string path = "Assets/WorldForge/Preview.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            PlayerSettings.productName = "WorldForge Preview"; PlayerSettings.companyName = "Folklore Hunter";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true; PlayerSettings.colorSpace = ColorSpace.Linear;
            Directory.CreateDirectory(outDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { path }, locationPathName = Path.Combine(outDir, "WorldForgePreview.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Debug.Log("WF_BUILD " + report.summary.result + " " + report.summary.totalErrors + " errors, " + report.summary.outputPath);
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        public static void GenerateScene()
        {
            var recipe = Arg("-wfRecipe");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>()) UnityEngine.Object.DestroyImmediate(l.gameObject);
            var w = new GameObject("WorldForge").AddComponent<WFWorld>();
            w.Recipe = WFRecipe.FromJson(File.ReadAllText(recipe)); w.RecipeFile = Path.GetFullPath(recipe);
            w.BuildNow(w.Recipe);
            Directory.CreateDirectory("Assets/WorldForge");
            EditorSceneManager.SaveScene(scene, "Assets/WorldForge/Generated.unity");
            Debug.Log("WF_SCENE " + w.Notes.LastOrDefault());
            EditorApplication.Exit(0);
        }
    }
}
