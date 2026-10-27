using System.IO;
using UnityEditor;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>WorldForge in the editor: the recipe's every number in the inspector, Load / Save its JSON, Regenerate;
    /// in the Scene view each feature's points as handles to drag (a path, tunnel, river or wall is redrawn through them)
    /// and its outline drawn. Generated objects are not saved into the scene (Regenerate after opening it).</summary>
    [CustomEditor(typeof(WFWorld))]
    public class WFWorldEditor : Editor
    {
        int feature = -1;

        public override void OnInspectorGUI()
        {
            var w = (WFWorld)target;
            EditorGUILayout.HelpBox("Edit the recipe below (or drag a feature's points in the Scene view), then Regenerate. Stats: " +
                (w.Result != null ? $"{w.Result.Triangles:N0} triangles, {w.Result.Meshes.Count} chunks, {w.BuildSeconds:0.0} s" : "not generated"), MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load recipe...")) Load(w);
                if (GUILayout.Button("Save recipe")) Save(w, false);
                if (GUILayout.Button("Save as...")) Save(w, true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.color = new Color(.7f, 1f, .7f);
                if (GUILayout.Button("Regenerate", GUILayout.Height(28))) Regenerate(w);
                GUI.color = Color.white;
                if (GUILayout.Button("Clear", GUILayout.Height(28))) w.Clear();
            }
            if (w.Recipe != null && w.Recipe.features.Count > 0)
            {
                var names = new string[w.Recipe.features.Count + 1]; names[0] = "(no handles)";
                for (int i = 0; i < w.Recipe.features.Count; i++) names[i + 1] = w.Recipe.features[i].name + " (" + w.Recipe.features[i].type + ")";
                feature = EditorGUILayout.Popup("Handles for", feature + 1, names) - 1;
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        static void Regenerate(WFWorld w)
        {
            if (w.Recipe == null) return;
            EditorUtility.DisplayProgressBar("WorldForge", "Generating " + w.Recipe.name + "...", .5f);
            try { w.BuildNow(w.Recipe); }
            finally { EditorUtility.ClearProgressBar(); }
            SceneView.RepaintAll();
        }

        static void Load(WFWorld w)
        {
            var p = EditorUtility.OpenFilePanel("WorldForge recipe", string.IsNullOrEmpty(w.RecipeFile) ? Application.dataPath : Path.GetDirectoryName(w.RecipeFile), "json");
            if (string.IsNullOrEmpty(p)) return;
            Undo.RecordObject(w, "Load recipe");
            w.Recipe = WFRecipe.FromJson(File.ReadAllText(p)); w.RecipeFile = p;
            EditorUtility.SetDirty(w);
        }

        static void Save(WFWorld w, bool ask)
        {
            if (w.Recipe == null) return;
            var p = w.RecipeFile;
            if (ask || string.IsNullOrEmpty(p)) p = EditorUtility.SaveFilePanel("WorldForge recipe", Application.dataPath, w.Recipe.name, "json");
            if (string.IsNullOrEmpty(p)) return;
            File.WriteAllText(p, w.Recipe.ToJson()); w.RecipeFile = p;
            Debug.Log("WorldForge: saved " + p);
        }

        void OnSceneGUI()
        {
            var w = (WFWorld)target;
            if (w.Recipe == null) return;
            for (int i = 0; i < w.Recipe.features.Count; i++)
            {
                var f = w.Recipe.features[i];
                Handles.color = i == feature ? Color.yellow : new Color(1, 1, 1, .35f);
                for (int k = 0; k + 1 < f.points.Count; k++) Handles.DrawLine(f.points[k], f.points[k + 1], i == feature ? 3 : 1);
                if (f.points.Count > 0) Handles.Label(f.points[0] + Vector3.up * 1.5f, f.name);
                if (i != feature) continue;
                for (int k = 0; k < f.points.Count; k++)
                {
                    EditorGUI.BeginChangeCheck();
                    var p = Handles.PositionHandle(f.points[k], Quaternion.identity);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(w, "Move " + f.name + " point"); f.points[k] = p; EditorUtility.SetDirty(w); }
                }
            }
        }

        [MenuItem("GameObject/WorldForge/New world from recipe...", false, 10)]
        static void NewWorld()
        {
            var p = EditorUtility.OpenFilePanel("WorldForge recipe", Application.dataPath, "json");
            var go = new GameObject("WorldForge"); var w = go.AddComponent<WFWorld>();
            w.Recipe = string.IsNullOrEmpty(p) ? new WFRecipe() : WFRecipe.FromJson(File.ReadAllText(p)); w.RecipeFile = p ?? "";
            Undo.RegisterCreatedObjectUndo(go, "New WorldForge world");
            Selection.activeGameObject = go;
        }
    }
}
