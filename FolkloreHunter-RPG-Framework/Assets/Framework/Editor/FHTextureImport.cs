using UnityEditor;

/// <summary>Stone relief maps (<c>*_n.png</c> beside the crypt's textures, and beside the lair props) import as normal
/// maps, so the Standard shader reads them as surface relief.</summary>
public class FHTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.EndsWith("_n.png") || !(assetPath.Contains("/Environment/") || assetPath.Contains("/Models/Lair/"))) return;
        var t = (TextureImporter)assetImporter;
        t.textureType = TextureImporterType.NormalMap;
    }
}
