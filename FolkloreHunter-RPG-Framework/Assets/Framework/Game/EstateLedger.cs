using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class EstateLedger : Interactable
    {
        public override string Prompt(GameSession s) => "Estate ledger";
        public override void Use(GameSession s) => s.Ui.OpenEstate();
    }
}
