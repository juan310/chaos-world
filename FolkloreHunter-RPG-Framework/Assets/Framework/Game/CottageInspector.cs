using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Review tool kept from the sample: F2 lifts the separate roof meshes (in world units) to show the
    /// cottage is built from independent parts. Not part of the player-facing controls list.</summary>
    public class CottageInspector : MonoBehaviour
    {
        public bool Lifted { get; private set; }
        readonly List<(Transform part, Vector3 origin)> roof = new List<(Transform, Vector3)>();

        void Update()
        {
            if (InputRouter.MenuDown(GameAction.Inspect) && GameSession.I && GameSession.I.Simulating) Toggle();
        }

        public void Toggle()
        {
            if (!Lifted)
            {
                roof.Clear();
                foreach (var t in GetComponentsInChildren<MeshRenderer>().Select(r => r.transform).Where(t => t.name.StartsWith("Roof_")))
                    roof.Add((t, t.position));
                foreach (var (t, o) in roof) t.position = o + Vector3.up * 1.8f;
            }
            else foreach (var (t, o) in roof) t.position = o;
            Lifted = !Lifted;
        }

        public int RoofPieces => GetComponentsInChildren<MeshRenderer>().Count(r => r.name.StartsWith("Roof_"));
    }
}
