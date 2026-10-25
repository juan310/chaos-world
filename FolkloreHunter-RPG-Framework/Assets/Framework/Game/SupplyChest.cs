using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class SupplyChest : Interactable
    {
        public List<ItemAmount> Contents = new List<ItemAmount>();
        public int Gold;
        public Transform Lid;
        string Key => "once:" + Id;
        public override bool Available(GameSession s) => !s.Core.State.world.usedPickups.Contains(Key);
        public override string Prompt(GameSession s) => "Open chest";
        public override void Use(GameSession s)
        {
            var fit = s.Core.Inventory.CanAdd(Contents);
            if (!fit.Ok) { s.Hud.Flash(fit.Reason); return; }
            s.Core.State.world.usedPickups.Add(Key);
            s.Core.Inventory.AddAll(Contents, ItemSource.Loot);
            s.Core.Wallet.Earn(Gold);
            s.Hud.Notify("Chest: " + string.Join(", ", Contents.Select(c => c.count + "× " + s.Core.Db.ItemName(c.itemId))) + (Gold > 0 ? ", " + Gold + " gold" : ""), NoticeKind.Reward);
            Refresh(s);
        }
        public void Refresh(GameSession s) { if (Lid) Lid.localRotation = Available(s) ? Quaternion.identity : Quaternion.Euler(-70, 0, 0); }
    }
}
