using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class BedRest : Interactable
    {
        public override string Prompt(GameSession s) => "Sleep until morning and save";
        public override void Use(GameSession s) => s.Ui.Confirm("Sleep until morning?", "Resting restores you fully, advances to the next day and saves your game.", "Sleep", () => s.Sleep());
    }
}
