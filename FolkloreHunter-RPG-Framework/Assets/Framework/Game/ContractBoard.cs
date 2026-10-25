using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class ContractBoard : Interactable
    {
        public override string Prompt(GameSession s) => "Read the hunt board";
        public override void Use(GameSession s) => s.Ui.OpenContractBoard();
    }
}
