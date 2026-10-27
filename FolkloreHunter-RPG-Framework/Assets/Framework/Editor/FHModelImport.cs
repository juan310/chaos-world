using UnityEditor;
using UnityEngine;

/// <summary>Import settings carried over unchanged from the 3D sample (FH3ModelImport): legacy animation for the
/// KayKit characters exported with FBX_SCALE_ALL, readable meshes, and per-state foot pivots for the werewolf sprites.</summary>
public class FHModelImport : AssetPostprocessor
{
    // bumped when the import rules change, so Unity re-imports what they apply to
    public override uint GetVersion() => 2;

    void OnPreprocessModel()
    {
        var m = (ModelImporter)assetImporter;
        m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; m.importCameras = false; m.importLights = false; m.isReadable = true;
        string p = assetPath;
        if (p.EndsWith("Briarhound.fbx") || p.EndsWith("CuSith.fbx") || p.EndsWith("Thornback.fbx") || p.EndsWith("Mirejaw.fbx") || p.EndsWith("Hunter.fbx") || p.Contains("/Hunter_") || p.Contains("/Gravebound_") || p.Contains("/Skel_") || p.EndsWith("GraveWarden.fbx") || p.EndsWith("Gravebound.fbx") || p.EndsWith("Herbalist.fbx") || p.EndsWith("Smith.fbx") || p.EndsWith("Noble.fbx"))
        {
            m.animationType = ModelImporterAnimationType.Legacy; m.importAnimation = true; m.animationCompression = ModelImporterAnimationCompression.Off;
            if (p.EndsWith("Thornback.fbx") || p.EndsWith("Mirejaw.fbx") || p.EndsWith("Briarhound.fbx")) { m.skinWeights = ModelImporterSkinWeights.Custom; m.maxBonesPerVertex = 8; m.minBoneWeight = .001f; }
        }
        else m.importAnimation = false;
    }

    void OnPreprocessTexture()
    {
        var t = (TextureImporter)assetImporter;
        t.textureType = TextureImporterType.Default; t.mipmapEnabled = true; t.filterMode = FilterMode.Bilinear; t.maxTextureSize = 1024;
        // The characters' own atlases at full size (the Codex bodies' 2K-4K textures, also as copied beside the slim game
        // models: Hunter_*_Slim, Skel_*, Villager_*, the Warden's helmet), compressed at high quality, sharp at an angle.
        var file = System.IO.Path.GetFileName(assetPath);
        if (assetPath.Contains("/Models/Codex/") || (assetPath.Contains("/Models/") && (file.StartsWith("Skel_") || file.StartsWith("Villager_") || file.Contains("_Slim_"))))
        {
            t.maxTextureSize = 4096; t.textureCompression = TextureImporterCompression.CompressedHQ; t.anisoLevel = 4;
        }
        if (assetPath.Contains("/Models/Weapons/")) { t.maxTextureSize = 2048; t.anisoLevel = 4; }   // the starting weapon set
        if (assetPath.Contains("/Monsters/")) {
            t.maxTextureSize = 4096;
            if (assetPath.Contains("_normal")) t.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("_metallic")) t.sRGBTexture = false;
        }
        if (assetPath.Contains("/Werewolf/"))
        {
            t.textureType = TextureImporterType.Sprite; t.spriteImportMode = SpriteImportMode.Single; t.spritePixelsPerUnit = 95;
            var settings = new TextureImporterSettings(); t.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = assetPath.Contains("/attack/") ? new Vector2(37f / 408f, 16f / 316f) : assetPath.Contains("/hurt/") ? new Vector2(66f / 352f, 16f / 316f) : new Vector2(38f / 329f, 16f / 320f);
            t.SetTextureSettings(settings); t.alphaIsTransparency = true; t.mipmapEnabled = false; t.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
