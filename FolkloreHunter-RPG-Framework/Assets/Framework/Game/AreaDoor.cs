using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Entrance or exit of an instanced area such as the crypt.</summary>
    public class AreaDoor : Interactable
    {
        public string TargetArea;
        public Transform Destination;
        public string Label;
        public override string Prompt(GameSession s) => Label;
        public override void Use(GameSession s) => s.Travel(this);
    }
}
