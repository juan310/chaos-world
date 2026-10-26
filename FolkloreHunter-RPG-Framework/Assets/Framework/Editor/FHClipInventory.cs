using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Lists every animation clip each character model ships with (name, length, loop), so movesets are
/// designed from the authored clips that actually exist. Writes Docs/clip-inventory.txt.</summary>
public static class FHClipInventory
{
    public static void Run()
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var path in Directory.GetFiles("Assets/Resources/Models", "*.fbx").OrderBy(p => p))
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path.Replace('\\', '/')).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            lines.Add("== " + Path.GetFileNameWithoutExtension(path) + " (" + clips.Count + " clips)");
            foreach (var c in clips.OrderBy(c => c.name)) lines.Add("  " + c.name + "  " + c.length.ToString("F2") + " s" + (c.isLooping ? "  loop" : ""));
        }
        File.WriteAllLines("Docs/clip-inventory.txt", lines);
        Debug.Log("FH_CLIPS " + lines.Count);
    }
}
