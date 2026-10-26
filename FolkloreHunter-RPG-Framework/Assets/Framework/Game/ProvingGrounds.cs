using System.Collections.Generic;
using System.Linq;
using System.Text;
using FH.Core;
using FH.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FH.Game
{
    /// <summary>Runs the Proving Grounds: test monsters under hunt rules, training toggles, the frame-data overlay
    /// (move, phase, windows, hitstop, hitzone, monster build-ups, live input), optional hit-volume display, and the
    /// hunting-style mouse look (cursor locked while playing, released whenever a menu is open).</summary>
    public class ProvingGrounds : MonoBehaviour
    {
        public bool InfiniteStamina, Invulnerable, MonstersPassive = true, ShowVolumes, ShowOverlay = true;
        public readonly List<MonsterActor> Spawned = new List<MonsterActor>();
        public string LastHitText { get; private set; } = "-";
        GameSession S;
        RectTransform panel;
        Text overlay, reticle;
        LineRenderer blade;
        readonly List<GameObject> markers = new List<GameObject>();
        float overlayClock;

        public static readonly (string id, string label)[] Weapons = { ("sword_shield", "Sword and Shield"), ("lance", "Lance"), ("bow", "Bow") };

        /// <summary>Puts another weapon in the hunter's hands (sheathed): its moveset, its pieces, its controls.</summary>
        public void SetWeapon(string id)
        {
            if (!S.Db.HuntMovesets.TryGetValue(id, out var set) || !S.Hunter.Hunt) return;
            S.Hunter.CancelActions();
            S.Hunter.Hunt.Equip(set);
            S.Hud?.Flash(set.name + " in hand");
            S.Hud?.Notify(set.name + ": " + (set.Class == WeaponClass.Lance ? "thrust, guard, counter and charge; hop instead of rolling." :
                set.Class == WeaponClass.Bow ? "draw and charge shots, sidestep to charge, power shot, arc shot, Dragon Piercer; RB aims." : "fast slashes, charged heavy, shield bash, guard."), NoticeKind.Story);
        }

        public static readonly (string id, string label)[] Monsters =
            { ("gravebound", "Gravebound"), ("grave_warden", "Grave Warden"), ("thornback", "Thornback"), ("mirejaw", "Mirejaw") };

        /// <summary>A monster can be spawned when its data and its 3D model are both present in this build.</summary>
        public static bool Available(GameSession s, string id) => s.Db.Monsters.TryGetValue(id, out var def) && Resources.Load<GameObject>("Models/" + def.model);

        public void Begin(GameSession s)
        {
            S = s;
            BuildOverlay();
            var lineGo = new GameObject("Blade volume");
            blade = lineGo.AddComponent<LineRenderer>();
            blade.material = WorldKit.Transparent("Blade volume", new Color(1, .2f, .1f, .5f));
            blade.positionCount = 2; blade.enabled = false;
            S.Hunter.BladeHit += RecordHit;
            Spawn("gravebound", true);
            if (Resources.Load<GameObject>("Models/Briarhound") || Resources.Load<GameObject>("Models/CuSith"))
            {
                var h = S.Hunter.transform;
                S.Hound = HoundCompanion.Spawn(S, h.position - (h.forward * 2.6f - h.right * 1.8f) * HoundCompanion.Size, h.eulerAngles.y);
            }
            S.Hud?.Notify("Proving Grounds: a passive Gravebound waits at the centre. The ledger by the south wall spawns monsters and sets options.", NoticeKind.Story);
        }

        public void End()
        {
            if (S && S.Hunter) S.Hunter.BladeHit -= RecordHit;
            if (S && S.Hound) { Destroy(S.Hound.gameObject); S.Hound = null; }
            ClearMonsters();
            if (panel) Destroy(panel.gameObject);
            if (reticle) Destroy(reticle.gameObject);
            if (blade) Destroy(blade.gameObject);
            ClearMarkers();
        }

        void OnDestroy() => End();

        // ---------------- monsters ----------------
        public MonsterActor Spawn(string id, bool atTarget = false)
        {
            if (!Available(S, id)) { S.Hud?.Flash("No 3D model for " + id + " in this build."); return null; }
            var h = S.Hunter.transform;
            Vector3 pos = atTarget ? S.World.Anchor("proving_target").position : h.position + h.forward * 8;
            var o = S.World.ProvingOrigin;
            var flat = new Vector2(pos.x - o.x, pos.z - o.z);
            if (flat.magnitude > S.World.ProvingRadius - 4) { flat = flat.normalized * (S.World.ProvingRadius - 4); pos = new Vector3(o.x + flat.x, pos.y, o.z + flat.y); }
            var m = S.SpawnForTest(id, pos);
            m.HuntRules = true;
            m.Passive = MonstersPassive;
            var look = h.position - m.transform.position; look.y = 0;
            if (look.sqrMagnitude > .01f) m.transform.rotation = Quaternion.LookRotation(look);
            m.Calm(2.5f);
            Spawned.Add(m);
            return m;
        }

        public void ClearMonsters()
        {
            foreach (var m in Spawned) if (m) S.DespawnMonster(m);
            Spawned.Clear();
        }

        public void SetPassive(bool on)
        {
            MonstersPassive = on;
            foreach (var m in Spawned) if (m) { m.Passive = on; m.Calm(2); }
        }

        public void ResetHunter()
        {
            var spawn = S.World.Anchor("proving_spawn");
            S.Hunter.Revive(S.SafeGround(spawn.position), spawn.eulerAngles.y);
            S.Hunter.Hunt?.SetSheathed(true);
            S.CameraRig.Snap();
        }

        void RecordHit(MonsterActor m, HitInfo hit)
        {
            var move = S.Hunter.Hunt ? S.Hunter.Hunt.Current : null;
            LastHitText = hit.Damage + (hit.WeakPoint || m.LastHitZone >= 1.3f ? " (soft hitzone x" + m.LastHitZone.ToString("0.##") + ")" : m.LastHitZone < .8f ? " (hard hitzone x" + m.LastHitZone.ToString("0.##") + ")" : "")
                + (move != null ? "  by " + (move.name ?? move.id) + " mv " + move.damageMultiplier.ToString("0.##") : "");
        }

        // ---------------- per frame ----------------
        void Update()
        {
            if (!S || !S.Proving) return;
            var h = S.Hunter;
            if (InfiniteStamina) h.Stamina.Current = h.Stamina.Max;
            if (Invulnerable && !h.Dead) h.Health = h.Stats[Stat.MaxHealth];
            Spawned.RemoveAll(m => !m);
            bool play = S.Simulating && InputRouter.MouseLook;
            Cursor.lockState = play ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !play;
            UpdateVolumes();
            UpdateReticle();
            overlayClock -= Time.unscaledDeltaTime;
            if (overlayClock <= 0) { overlayClock = .1f; UpdateOverlay(); }
        }

        /// <summary>Bow: a crosshair while aiming or drawing, with one pip per charge level (gold at full).</summary>
        void UpdateReticle()
        {
            var f = S.Hunter.Hunt;
            bool show = f && f.Class == WeaponClass.Bow && (f.Aiming || f.Drawing) && S.Simulating;
            reticle.gameObject.SetActive(show);
            if (!show) return;
            int level = f.BowLevel, max = f.Set.MaxLevel;
            var pips = new StringBuilder();
            for (int i = 1; i <= max; i++) pips.Append(i <= level ? (level >= max ? "<color=#ffb347>●</color>" : "<color=#fff2c0>●</color>") : "<color=#ffffff55>○</color>");
            reticle.text = (f.Aiming ? "<size=40>+</size>\n" : "\n") + "<size=20>" + pips + "</size>";
        }

        void BuildOverlay()
        {
            panel = UiKit.Panel("Proving overlay", S.Ui.HudLayer, new Color(0, 0, 0, .55f), false).rectTransform;
            panel.Box(new Vector2(0, 1), new Vector2(24, -180), new Vector2(760, 250));
            overlay = UiKit.Label("Proving overlay text", panel, "", 16, UiKit.Text, TextAnchor.UpperLeft);
            overlay.rectTransform.Stretch(10);
            overlay.supportRichText = true;
            reticle = UiKit.Label("Bow reticle", S.Ui.HudLayer, "", 34, Color.white, TextAnchor.MiddleCenter);
            reticle.rectTransform.Box(new Vector2(.5f, .5f), Vector2.zero, new Vector2(320, 90));
            reticle.supportRichText = true;
        }

        void UpdateOverlay()
        {
            panel.gameObject.SetActive(ShowOverlay);
            if (!ShowOverlay) return;
            var h = S.Hunter; var f = h.Hunt;
            var sb = new StringBuilder();
            sb.Append("<b><color=#f5d68c>PROVING GROUNDS</color></b>   ").Append(InputRouter.PadActive ? "Xbox controller" : GamePad.Connected ? "Keyboard + mouse (controller connected)" : "Keyboard + mouse").Append('\n');
            if (f)
            {
                sb.Append("Weapon ").Append(f.Set.name).Append(f.Sheathed ? "  sheathed" : "  drawn").Append(f.Guarding ? "  GUARD" : "")
                  .Append(f.Charging ? "  CHARGING " + f.ChargeHeld.ToString("0.0") + " s  level " + f.ChargeLevel : "")
                  .Append(f.CounterStance ? "  <color=#9cf>COUNTER STANCE</color>" : "")
                  .Append(f.Class == WeaponClass.Bow ? "  charge level " + f.BowLevel + (f.Drawing ? " (drawing)" : "") + (f.Aiming ? "  AIMING" : "") + "   last shot L" + f.LastShotLevel + " x" + f.LastShotArrows + "   arrows fired " + ArrowShot.Fired + ", hits " + ArrowShot.Landed : "")
                  .Append(f.Class == WeaponClass.Lance ? "  retributions " + f.Retributions + (f.ChainedDodges > 0 ? "  hops " + f.ChainedDodges : "") : "").Append('\n');
                var cur = f.Current;
                if (cur != null)
                    sb.Append("Move <b>").Append(cur.name ?? cur.id).Append("</b>  ").Append(h.Attack.Phase).Append("  ").Append(h.Attack.Time.ToString("0.00")).Append(" / ").Append(cur.Total.ToString("0.00"))
                      .Append(" s   chain ").Append(HuntRoutes.CanChain(cur, h.Attack.Normalized) ? "<color=#8f8>open</color>" : "wait").Append("   roll ").Append(HuntRoutes.CanRollCancel(cur, h.Attack.Normalized) ? "<color=#8f8>ok</color>" : "locked").Append('\n');
                else sb.Append("Move -  (last: ").Append(f.LastMove).Append(")\n");
                sb.Append("Event ").Append(f.LastEvent).Append("   hitstop ").Append(f.LastHitstop.ToString("0.000")).Append(" s   last hit ").Append(LastHitText).Append('\n');
            }
            sb.Append("Hunter HP ").Append(h.Health.ToString("0")).Append('/').Append(h.Stats[Stat.MaxHealth].ToString("0")).Append("   stamina ").Append(h.Stamina.Current.ToString("0")).Append('/').Append(h.Stamina.Max.ToString("0"))
              .Append(h.Invulnerable ? "   <color=#8cf>i-frames</color>" : "").Append('\n');
            var m = h.LockTarget && h.LockTarget.Alive ? h.LockTarget : Spawned.Where(x => x && x.Alive).OrderBy(x => Vector3.Distance(x.transform.position, h.transform.position)).FirstOrDefault();
            if (m)
            {
                var r = m.Reactions;
                sb.Append("Target <b>").Append(m.DisplayName).Append("</b>  ").Append(m.Brain.Mode).Append(m.Passive ? " (passive)" : "").Append("  HP ").Append(m.Brain.Health.ToString("0")).Append('/').Append(m.Def.maxHealth.ToString("0"))
                  .Append("  flinch ").Append(r.FlinchBuild.ToString("0")).Append('/').Append(r.FlinchThreshold.ToString("0"))
                  .Append("  stun ").Append(r.StunBuild.ToString("0")).Append('/').Append(r.StunThreshold.ToString("0"))
                  .Append(r.Enraged ? "  <color=#f66>ENRAGED</color>" : "").Append(r.Topples > 0 ? "  toppled x" + r.Topples : "").Append('\n');
            }
            var ls = GamePad.LeftStick; var rs = GamePad.RightStick;
            sb.Append("Input  LS ").Append(ls.x.ToString("0.00")).Append(',').Append(ls.y.ToString("0.00")).Append("  RS ").Append(rs.x.ToString("0.00")).Append(',').Append(rs.y.ToString("0.00"))
              .Append("  LT ").Append(GamePad.LeftTrigger.ToString("0.0")).Append(" RT ").Append(GamePad.RightTrigger.ToString("0.0")).Append("  ");
            foreach (var b in new[] { PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.LB, PadButton.RB, PadButton.RightStick, PadButton.DPadUp, PadButton.Start })
                if (GamePad.Held(b)) sb.Append('[').Append(b).Append("] ");
            overlay.text = sb.ToString();
        }

        void UpdateVolumes()
        {
            var h = S.Hunter;
            blade.enabled = ShowVolumes && h.Attack.Running;
            if (blade.enabled)
            {
                float r = h.Attack.Def != null ? h.Attack.Def.radius : .2f;
                blade.startWidth = blade.endWidth = r * 2;
                var c = h.Attack.DamageWindowOpen ? new Color(1, .15f, .1f, .6f) : new Color(.8f, .8f, .8f, .25f);
                blade.startColor = blade.endColor = c;
                blade.SetPosition(0, h.BladeBase); blade.SetPosition(1, h.BladeTip);
            }
            bool want = ShowVolumes;
            if (want && markers.Count == 0)
                foreach (var m in Spawned.Where(x => x))
                    foreach (var hb in m.Hurtboxes)
                    {
                        var col = hb.GetComponent<SphereCollider>();
                        if (!col) continue;
                        float world = col.radius * Mathf.Max(hb.transform.lossyScale.x, hb.transform.lossyScale.y);
                        var part = m.Def.parts.FirstOrDefault(p => hb.Parts.Contains(p.id));
                        var color = part != null && (part.weakPoint || part.damageMultiplier >= 1.3f) ? new Color(1f, .6f, .1f, .35f) : new Color(.3f, .7f, 1f, .25f);
                        var orb = WorldKit.Orb("Hurtbox volume", hb.transform.position, world, WorldKit.Transparent("Hurtbox volume " + color, color), hb.transform);
                        Destroy(orb.GetComponent<Collider>());
                        markers.Add(orb);
                    }
            if (!want && markers.Count > 0) ClearMarkers();
            markers.RemoveAll(x => !x);
        }

        void ClearMarkers() { foreach (var m in markers) if (m) Destroy(m); markers.Clear(); }

        /// <summary>Volumes are rebuilt when monsters change.</summary>
        public void RefreshVolumes() => ClearMarkers();
    }
}
