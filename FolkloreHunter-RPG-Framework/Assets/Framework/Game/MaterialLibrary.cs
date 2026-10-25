using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Holds runtime-used materials so their shader variants are included in the player build.</summary>
    public class MaterialLibrary : MonoBehaviour
    {
        public List<Material> Materials = new List<Material>();
        public static void Register()
        {
            foreach (var lib in FindObjectsByType<MaterialLibrary>(FindObjectsInactive.Include))
                foreach (var m in lib.Materials) if (m && !WorldKit.Materials.ContainsKey(m.name)) WorldKit.Materials[m.name] = m;
        }
    }
}
