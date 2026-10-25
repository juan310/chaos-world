using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class LootBag : Interactable
    {
        public LootBagState State;
        public override string Prompt(GameSession s) => "Collect " + (string.IsNullOrEmpty(State?.label) ? "loot" : State.label);
        public override void Use(GameSession s) => s.Ui.OpenLoot(this);
        void Update() { transform.Rotate(0, 40 * Time.deltaTime, 0, Space.World); }
    }
}
