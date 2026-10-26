using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class BedRest : Interactable
    {
        public override string Prompt(GameSession s) => s.Core.Clock.CanSleepThroughNight ? "Sleep until morning and save" : "Rest a while and save";
        public override void Use(GameSession s)
        {
            if (s.Core.Clock.CanSleepThroughNight) s.Ui.Confirm("Sleep until morning?", "Resting restores you fully, advances to the next day and saves your game.", "Sleep", () => s.Sleep());
            else s.Ui.Confirm("Rest a while?", "It is too early to sleep through the night (after 18:00). A short rest restores you fully, passes two hours and saves your game.", "Sleep", () => s.Sleep());
        }
    }
}
