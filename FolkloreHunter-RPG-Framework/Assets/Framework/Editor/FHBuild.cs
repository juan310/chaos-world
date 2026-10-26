using System.Collections.Generic;
using System.IO;
using System.Linq;
using FH.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Generates the framework scene from code and builds the Windows player into Build-RPG
/// (the sample's Build and Build-Werewolf outputs are never touched).</summary>
public static class FHBuild
{
    public const string ScenePath = "Assets/Scenes/FolkloreHunterHunts.unity";
    public const string Output = "Build-Proving/FolkloreHunterProving.exe";
    const string Generated = "Assets/Generated/RPG";

    [MenuItem("Folklore Hunter/Generate scene")]
    public static void GenerateScene()
    {
        AssetDatabase.Refresh();
        if (AssetDatabase.IsValidFolder(Generated)) AssetDatabase.DeleteAsset(Generated);
        Directory.CreateDirectory(Generated);
        Directory.CreateDirectory("Assets/Prefabs");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        WorldKit.Materials.Clear();

        var world = new GameObject("Folklore Hunter - world");
        var refs = WorldBuilder.Build(world.transform);

        var spawn = refs.Anchor("spawn_home");
        var hunterGo = CharacterFactory.Actor("Hunter - rigged 3D character", spawn.position, 1.85f, .34f);
        hunterGo.transform.rotation = spawn.rotation;
        var hunter = hunterGo.AddComponent<HunterController>();
        hunter.Visual = CharacterFactory.Create("Hunter", hunterGo.transform, 1.85f);

        var camGo = new GameObject("Player camera");
        var cam = camGo.AddComponent<Camera>();
        cam.tag = "MainCamera"; cam.nearClipPlane = .12f; cam.farClipPlane = 220; cam.fieldOfView = 48;
        camGo.AddComponent<AudioListener>();
        var rig = camGo.AddComponent<CameraRig>();
        rig.Camera = cam;

        var sessionGo = new GameObject("Game session");
        var ui = new GameObject("Interface").AddComponent<FH.UI.UiManager>();
        ui.transform.SetParent(sessionGo.transform);
        var session = sessionGo.AddComponent<GameSession>();
        session.World = refs; session.Hunter = hunter; session.CameraRig = rig; session.Ui = ui;

        QualitySettings.skinWeights = SkinWeights.Unlimited;
        QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.shadowDistance = 65; QualitySettings.antiAliasing = 4; QualitySettings.vSyncCount = 1;

        int count = 0;
        var saved = new HashSet<Object>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            if (mf.sharedMesh && !AssetDatabase.Contains(mf.sharedMesh) && saved.Add(mf.sharedMesh)) AssetDatabase.CreateAsset(mf.sharedMesh, Generated + "/Mesh_" + (count++) + ".asset");
        foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
            if (mc.sharedMesh && !AssetDatabase.Contains(mc.sharedMesh) && saved.Add(mc.sharedMesh)) AssetDatabase.CreateAsset(mc.sharedMesh, Generated + "/Mesh_" + (count++) + ".asset");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            foreach (var mat in r.sharedMaterials)
                if (mat && !AssetDatabase.Contains(mat) && saved.Add(mat)) AssetDatabase.CreateAsset(mat, Generated + "/Material_" + (count++) + ".mat");
        foreach (var lib in Object.FindObjectsByType<MaterialLibrary>(FindObjectsInactive.Include))
            foreach (var mat in lib.Materials) if (mat && !AssetDatabase.Contains(mat) && saved.Add(mat)) AssetDatabase.CreateAsset(mat, Generated + "/Material_" + (count++) + ".mat");
        if (RenderSettings.skybox && !AssetDatabase.Contains(RenderSettings.skybox)) AssetDatabase.CreateAsset(RenderSettings.skybox, Generated + "/Sky.mat");

        PrefabUtility.SaveAsPrefabAsset(refs.Cottage.gameObject, "Assets/Prefabs/StartingCottage.prefab");
        int parts = refs.Cottage.GetComponentsInChildren<MeshFilter>(true).Length;
        if (parts < 80) throw new System.Exception("Cottage parts were merged: " + parts);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("FH_SCENE generated: " + count + " generated assets, cottage parts " + parts);
    }

    [MenuItem("Folklore Hunter/Build Windows player")]
    public static void Build()
    {
        ReferenceMonsterAssets.PrepareCompanion();
        GenerateScene();
        PlayerSettings.companyName = "Folklore Hunter";
        PlayerSettings.productName = "Folklore Hunter - Rise of the Estate";
        PlayerSettings.bundleVersion = Version;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        AssetDatabase.SaveAssets();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = Output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        string result = "Version " + Version + "\nResult " + report.summary.result + "\nErrors " + report.summary.totalErrors + "\nWarnings " + report.summary.totalWarnings + "\nSize " + report.summary.totalSize + "\nOutput " + Path.GetFullPath(Output);
        Directory.CreateDirectory("Docs");
        File.WriteAllText("Docs/build-result.txt", result);
        Debug.Log("FH_BUILD " + result);
        if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Player build failed");
    }

    public static string Version => "0.8.0-katana-range";
}
