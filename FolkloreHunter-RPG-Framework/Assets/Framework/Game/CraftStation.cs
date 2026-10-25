using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class CraftStation : Interactable
    {
        public Station Station;
        public string Label = "Workbench";
        public override bool Available(GameSession s) => gameObject.activeInHierarchy;
        public override string Prompt(GameSession s) => "Use " + Label;
        public override void Use(GameSession s) => s.Ui.OpenCrafting(Station, Label);
    }
}
