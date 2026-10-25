using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Activates the modular pieces belonging to the current estate tier.</summary>
    public class EstateVisuals : MonoBehaviour
    {
        [System.Serializable] public class Group { public string id; public int minTier, maxTier = 99; public GameObject root; }
        public List<Group> Groups = new List<Group>();
        public void Apply(int tier)
        {
            foreach (var g in Groups) if (g.root) g.root.SetActive(tier >= g.minTier && tier <= g.maxTier);
        }
        public int ActivePieces => Groups.Where(g => g.root && g.root.activeSelf).Sum(g => g.root.GetComponentsInChildren<MeshRenderer>().Length);
    }
}
