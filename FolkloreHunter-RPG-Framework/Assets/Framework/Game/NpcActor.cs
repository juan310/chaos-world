using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public class NpcActor : Interactable
    {
        public string NpcId;
        public Transform Visual;
        public Vector3 SettlementSpot, HomeSpot;
        public float SettlementYaw, HomeYaw;
        AnimationDriver anim;
        public AnimationDriver Anim => anim;
        float idleSwap;

        public NpcDef Def(GameSession s) => s.Core.Db.Npcs[NpcId];
        public override string Prompt(GameSession s)
        {
            var c = s.Core;
            var cs = c.State.contracts.FirstOrDefault(x => x.status == ContractStatus.Completed && c.Db.Contracts[x.id].giverNpcId == NpcId);
            return (cs != null ? "Talk to " + Def(s).name + "  (report your hunt)" : "Talk to " + Def(s).name);
        }
        public override void Use(GameSession s) => s.Ui.OpenDialogue(this);

        public void Begin(string model)
        {
            if (Visual) { anim = new AnimationDriver(Visual, model); anim.Play("Idle"); }
        }

        /// <summary>Spouses move into the estate: a visible consequence of the relationship state.</summary>
        public void PlaceFor(GameSession s)
        {
            bool atHome = s.Core.Relationships.IsSpouse(NpcId) && HomeSpot != Vector3.zero;
            transform.SetPositionAndRotation(atHome ? HomeSpot : SettlementSpot, Quaternion.Euler(0, atHome ? HomeYaw : SettlementYaw, 0));
        }

        void Update()
        {
            if (anim == null || !GameSession.I || !GameSession.I.Simulating) return;
            var hero = GameSession.I.Hunter;
            var d = hero.transform.position - transform.position; d.y = 0;
            if (d.magnitude < 4 && d.sqrMagnitude > .01f)
                Visual.rotation = Quaternion.Slerp(Visual.rotation, Quaternion.LookRotation(d), Time.deltaTime * 3);
            idleSwap -= Time.deltaTime;
            if (idleSwap < 0) { idleSwap = Random.Range(6f, 11f); if (anim.Has("Interact") && Random.value < .3f) anim.Play("Interact", false, .8f, true); else anim.Play("Idle"); }
            if (anim.Finished("Interact")) anim.Play("Idle");
        }
    }
}
