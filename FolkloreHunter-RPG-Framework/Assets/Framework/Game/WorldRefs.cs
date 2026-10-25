using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Scene references produced by the world builder and saved with the scene.</summary>
    public class WorldRefs : MonoBehaviour
    {
        public Light Sun;
        public List<Light> Lanterns = new List<Light>();
        public EstateVisuals Estate;
        public List<NpcActor> Npcs = new List<NpcActor>();
        public List<WorldAnchor> Anchors = new List<WorldAnchor>();
        public Transform Cottage;
        public List<Transform> CottageParts = new List<Transform>();
        public Vector3 CryptOrigin = new Vector3(200, 0, 0);
        public Vector2 CryptHalfSize = new Vector2(9, 15);
        public float CryptFloor => CryptOrigin.y;
        public Rect MapBounds = new Rect(-50, -50, 100, 100);

        public Transform Anchor(string id)
        {
            var a = Anchors.FirstOrDefault(x => x && x.Id == id);
            if (!a) throw new System.Exception("Missing world anchor " + id);
            return a.transform;
        }

        public bool IsInCrypt(Vector3 p) => Mathf.Abs(p.x - CryptOrigin.x) < CryptHalfSize.x + 3 && Mathf.Abs(p.z - CryptOrigin.z) < CryptHalfSize.y + 3;

        public Vector3 SpawnPosition(SpawnDef sp)
        {
            if (sp.areaId == "crypt") return CryptOrigin + new Vector3(sp.x, .1f, sp.z);
            return new Vector3(sp.x, WorldBuilder.Height(sp.x, sp.z) + .1f, sp.z);
        }
    }

}
