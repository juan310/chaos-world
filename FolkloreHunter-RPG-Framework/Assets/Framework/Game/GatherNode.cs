using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Herbs and flowers regrow each in-game day; supply chests open once per save.</summary>
    public class GatherNode : Interactable
    {
        public string ItemId;
        public int Amount = 1;
        public bool Regrows = true;
        public string Key => (Regrows ? "regrow:" : "once:") + Id;
        public override bool Available(GameSession s) => !s.Core.State.world.usedPickups.Contains(Key);
        public override string Prompt(GameSession s) => "Gather " + s.Core.Db.ItemName(ItemId);
        public override void Use(GameSession s)
        {
            var r = s.Core.Inventory.Add(ItemId, Amount, ItemSource.Gather);
            if (!r.Ok) { s.Hud.Flash(r.Reason); return; }
            s.Core.State.world.usedPickups.Add(Key);
            s.Hud.Notify("+" + Amount + " " + s.Core.Db.ItemName(ItemId), NoticeKind.Reward);
            WorldKit.Burst(transform.position + Vector3.up * .3f, new Color(.85f, .75f, .2f));
            s.Hunter.Anim.Play("Interact", false, 1.8f, true);
            Refresh(s);
        }
        public void Refresh(GameSession s) { foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = Available(s); }
    }
}
