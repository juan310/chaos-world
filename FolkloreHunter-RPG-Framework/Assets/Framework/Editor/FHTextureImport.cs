using UnityEditor;

/// <summary>The carved crypt kit's 4K atlases keep their size; stone relief maps (<c>*_n.png</c> beside the crypt's textures, and beside the lair props) import as normal
/// maps, so the Standard shader reads them as surface relief. Sprite frames (Resources/Sprites) keep their alpha and size.</summary>
public class FHTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        // The carved crypt kit's atlases are 4K: keep them sharp (the default import halves them).
        if (assetPath.Contains("/Models/Lair/Kit/")) ((TextureImporter)assetImporter).maxTextureSize = 4096;
        // A drawn monster's frames (Resources/Sprites/<name>/<clip>/NN.png, SpriteActor): cut out on their alpha, kept at
        // their own size (not stretched to a power of two), clamped so no edge bleeds round the card.
        if (assetPath.Contains("/Resources/Sprites/"))
        {
            var t = (TextureImporter)assetImporter;
            t.alphaIsTransparency = true; t.npotScale = TextureImporterNPOTScale.None; t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.mipmapEnabled = true; t.maxTextureSize = 1024;
            return;
        }
        if (!assetPath.EndsWith("_n.png") || !(assetPath.Contains("/Environment/") || assetPath.Contains("/Models/Lair/"))) return;
        var t = (TextureImporter)assetImporter;
        t.textureType = TextureImporterType.NormalMap;
    }
}
