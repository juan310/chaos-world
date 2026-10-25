using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Trigger volume on a monster bone. Several part ids may share one bone (helm over skull).</summary>
    public class Hurtbox : MonoBehaviour
    {
        public MonsterActor Owner;
        public List<string> Parts = new List<string>();
        public string Bone;
    }
}
