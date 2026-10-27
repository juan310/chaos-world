using UnityEditor;

namespace FH.WorldForge
{
    /// <summary>Test characters for WorldForge (Assets/Resources/WorldForgeTest/) import with legacy animation, as the
    /// Folklore Hunter game's own models do, so the walker can play their clips by name.</summary>
    public class WFModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/WorldForgeTest/")) return;
            var m = (ModelImporter)assetImporter;
            m.animationType = ModelImporterAnimationType.Legacy; m.importAnimation = true;
            m.materialImportMode = ModelImporterMaterialImportMode.None; m.isReadable = false;
        }
    }
}
