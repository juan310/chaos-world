using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class StorageChest : Interactable
    {
        public override bool Available(GameSession s) => gameObject.activeInHierarchy && s.Core.Estate.Storage.Capacity > 0;
        public override string Prompt(GameSession s) => "Open storage";
        public override void Use(GameSession s) => s.Ui.OpenStorage();
    }
}
