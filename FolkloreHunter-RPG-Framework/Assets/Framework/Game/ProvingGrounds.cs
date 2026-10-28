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
    /// hunting-style mouse look (cursor locked while playing, released whenever a menu is open). Also runs the
    /// Training Range (archery targets, dummies that never die, dodge hazards).</summary>
    public class ProvingGrounds : MonoBehaviour
    {
        public bool InfiniteStamina, Invulnerable, MonstersPassive = true, ShowVolumes, ShowOverlay = true;
        public readonly List<MonsterActor> Spawned = new List<MonsterActor>();
        /// <summary>Training Range dummies: passive, and healed before they can fall.</summary>
        public readonly HashSet<MonsterActor> Dummies = new HashSet<MonsterActor>();
        public string LastHitText { get; private set; } = "-";
        GameSession S;
        RectTransform panel;
        Text overlay, reticle;
        LineRenderer blade;
        readonly List<GameObject> markers = new List<GameObject>();
        float overlayClock;

        public static readonly (string id, string label)[] Weapons = { ("sword_shield", "Sword and Shield"), ("lance", "Lance"), ("bow", "Bow"), ("katana", "Katana (two-handed)") };

        /// <summary>Puts another weapon in the hunter's hands (sheathed): its moveset, its pieces, its controls.</summary>
        public void SetWeapon(string id)
        {
            if (!S.Db.HuntMovesets.TryGetValue(id, out var set) || !S.Hunter.Hunt) return;
            S.Hunter.CancelActions();
            S.Hunter.Hunt.Equip(set);
            S.Hud?.Flash(set.name + " in hand");
            S.Hud?.Notify(set.name + ": " + (set.Class == WeaponClass.Lance ? "thrust, guard, counter and charge; hop instead of rolling." :
                set.Class == WeaponClass.Bow ? "draw and charge shots, step to charge, fan volley, rain volley, Skewer Shot; RB aims." :
                set.Class == WeaponClass.Katana ? "Y/B slashes fill the ember gauge; RB Ember Cuts ending in the Ember Wheel (levels up); RB+B Shadowstep; RB+A sheath stance." :
                "fast slashes, charged heavy, shield bash, guard."), NoticeKind.Story);
        }

        /// <summary>The test body: the owner's Tripo male (all its own Tripo animations kept) built with the newest clips
        /// (the MotionBricks moves, crouch, sneak and the jumps), so they are tried here before they go on the hunters.</summary>
        public const string TestBody = "Hunter_Test_Slim";
        public bool TestBodyOn => S.Hunter.Look == TestBody;
        public bool TestBodyAvailable => CharacterFactory.Exists(TestBody);

        /// <summary>Wears the test body, or goes back to the hunter chosen at the title (the weapon in hand is kept).</summary>
        public void UseTestBody(bool on)
        {
            if (on && !TestBodyAvailable) return;
            string weapon = S.Hunter.Hunt && S.Hunter.Hunt.Set != null ? S.Hunter.Hunt.Set.id : null;
            S.Hunter.CancelActions();
            S.Hunter.ApplyLook(on ? new[] { TestBody } : Appearance.Models(S.Settings.lookBody, S.Settings.lookHair));
            if (weapon != null) SetWeapon(weapon);
            S.Hud?.Flash(on ? "Test body: the newest moves (Z crouch, V jump)" : "Back in your hunter");
        }

        public static readonly (string id, string label)[] Monsters =
            { ("gravebound", "Gravebound"), ("gravebound_knight", "Gravebound Knight"), ("gravebound_mage", "Gravebound Mage"), ("grave_warden", "Grave Warden"), ("thornback", "Thornback"), ("mirejaw", "Mirejaw"), ("barrow_troll", "Barrow Troll") };

        /// <summary>A monster can be spawned when its data and its 3D model are both present in this build.</summary>
        public static bool Available(GameSession s, string id) => s.Db.Monsters.TryGetValue(id, out var def) && CharacterFactory.Exists(CharacterFactory.MonsterModel(def));

        public void Begin(GameSession s)
        {
            S = s;
            BuildOverlay();
            var lineGo = new GameObject("Blade volume");
            blade = lineGo.AddComponent<LineRenderer>();
            blade.material = WorldKit.Transparent("Blade volume", new Color(1, .2f, .1f, .5f));
            blade.positionCount = 2; blade.enabled = false;
            S.Hunter.BladeHit += RecordHit;
            SpawnDefaults();
            if (Resources.Load<GameObject>("Models/Briarhound") || Resources.Load<GameObject>("Models/CuSith"))
            {
                var h = S.Hunter.transform;
                S.Hound = HoundCompanion.Spawn(S, h.position - (h.forward * 2.6f - h.right * 1.8f) * HoundCompanion.Size, h.eulerAngles.y);
            }
            S.Hud?.Notify(S.InRange
                ? "Training Range: archery targets to the west (10 to 40 m), dummies to the east, dodge hazards and a measured dodge lane in the middle. The ledger by the entrance sets weapons and options."
                : "Proving Grounds: a passive Gravebound waits at the centre. The ledger by the south wall spawns monsters and sets options.", NoticeKind.Story);
        }

        /// <summary>What each area starts with: the Proving Grounds a Gravebound at the centre; the Training Range a
        /// large and a small dummy in the pen. Target and hazard counts start from zero.</summary>
        public void SpawnDefaults()
        {
            Dummies.Clear();
            if (!S.InRange) { Spawn("gravebound", true); return; }
            foreach (var t in RangeTarget.All) t.ResetCount();
            foreach (var h in RangeHazard.All) h.ResetCount();
            foreach (var (id, anchor) in new[] { ("thornback", "range_dummy_large"), ("gravebound", "range_dummy_small") })
            {
                var a = S.World.Anchor(anchor);
                var m = SpawnAt(id, a.position, a.eulerAngles.y);
                if (m) { m.Passive = true; m.Unkillable = true; Dummies.Add(m); }
            }
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
            Vector3 pos = atTarget && !S.InRange ? S.World.Anchor("proving_target").position : h.position + h.forward * 8;
            var look = h.position - pos; look.y = 0;
            return SpawnAt(id, pos, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0);
        }

        MonsterActor SpawnAt(string id, Vector3 pos, float yaw)
        {
            if (!Available(S, id)) { S.Hud?.Flash("No 3D model for " + id + " in this build."); return null; }
            if (S.InRange)
            {
                var r = S.World.RangeOrigin; var half = S.World.RangeHalfSize - new Vector2(4, 4);
                pos.x = Mathf.Clamp(pos.x, r.x - half.x, r.x + half.x); pos.z = Mathf.Clamp(pos.z, r.z - half.y, r.z + half.y);
            }
            else
            {
                var o = S.World.ProvingOrigin;
                var flat = new Vector2(pos.x - o.x, pos.z - o.z);
                if (flat.magnitude > S.World.ProvingRadius - 4) { flat = flat.normalized * (S.World.ProvingRadius - 4); pos = new Vector3(o.x + flat.x, pos.y, o.z + flat.y); }
            }
            var m = S.SpawnForTest(id, pos);
            m.EnableHuntRules();         // flinch, topple, enrage, and the boss version: tougher, area attacks, phases
            m.Passive = MonstersPassive;
            m.transform.rotation = Quaternion.Euler(0, yaw, 0);
            m.Calm(2.5f);
            Spawned.Add(m);
            return m;
        }

        public void ClearMonsters()
        {
            foreach (var m in Spawned) if (m) S.DespawnMonster(m);
            Spawned.Clear(); Dummies.Clear();
        }

        public void SetPassive(bool on)
        {
            MonstersPassive = on;
            foreach (var m in Spawned) if (m) { m.Passive = on || Dummies.Contains(m); m.Calm(2); }
        }

        public void ResetHunter()
        {
            var spawn = S.World.Anchor(S.TestSpawnAnchor);
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
            // Dummies never fall: healed once they drop under a third of their health.
            foreach (var m in Dummies) if (m && m.Alive && m.Brain.Health < m.Def.maxHealth * .33f) { m.Brain.Health = m.Def.maxHealth; WorldKit.Burst(m.transform.position + Vector3.up * 2, new Color(.6f, 1f, .6f), 10); }
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
            sb.Append("<b><color=#f5d68c>").Append(S.TestAreaName.ToUpperInvariant()).Append("</color></b>   ").Append(InputRouter.PadActive ? "Xbox controller" : GamePad.Connected ? "Keyboard + mouse (controller connected)" : "Keyboard + mouse").Append('\n');
            if (f)
            {
                sb.Append("Weapon ").Append(f.Set.name).Append(f.Sheathed ? "  sheathed" : "  drawn").Append(f.Guarding ? "  GUARD" : "")
                  .Append(f.Charging ? "  CHARGING " + f.ChargeHeld.ToString("0.0") + " s  level " + f.ChargeLevel : "")
                  .Append(f.CounterStance ? "  <color=#9cf>COUNTER STANCE</color>" : "")
                  .Append(f.Class == WeaponClass.Bow ? "  charge level " + f.BowLevel + (f.Drawing ? " (drawing)" : "") + (f.Aiming ? "  AIMING" : "") + "   last shot L" + f.LastShotLevel + " x" + f.LastShotArrows + "   arrows fired " + ArrowShot.Fired + ", hits " + ArrowShot.Landed : "")
                  .Append(f.Class == WeaponClass.Lance ? "  retributions " + f.Retributions + (f.ChainedDodges > 0 ? "  hops " + f.ChainedDodges : "") : "")
                  .Append(f.Class == WeaponClass.Katana ? "  ember " + f.Spirit.Value.ToString("0") + "/100  level " + f.SpiritLevel + (f.Spirit.IaiActive(Time.time) ? " (sheath refill)" : "") + (f.IaiStance ? "  <color=#9cf>SHEATH STANCE</color>" : "") + (f.Foresight ? "  <color=#9cf>SHADOWSTEP</color>" : "") + "   shadowstep counters " + f.ForesightCounters + ", ember draw counters " + f.SpiritCounters : "").Append('\n');
                sb.Append("Evade ").Append(f.Evades > 0 ? f.LastEvade.ToString() : "-").Append(" (").Append(f.Evades).Append(" so far, perfect ").Append(h.PerfectEvades).Append(")")
                  .Append(TimeWarp.Active ? "  <color=#9cf>SLOW MOTION</color>" : "").Append(f.Sprinting ? "   sprinting: evade away from a monster to dive" : "").Append('\n');
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
                  .Append(r.Enraged ? "  <color=#f66>ENRAGED</color>" : "").Append(r.Topples > 0 ? "  toppled x" + r.Topples : "")
                  .Append(m.HuntRules ? "  phase " + m.Phase + (m.Phase == 3 ? " <color=#f66>DESPERATE</color>" : "") + "  last attack " + m.LastAttackId + "  areas live " + MonsterAoe.Active.Count : "").Append('\n');
            }
            var hf = S.Hunter.Hunt;
            if (hf && hf.enabled)
            {
                sb.Append("Wounds ");
                if (m && m.Wounds) sb.Append("open ").Append(m.Wounds.Tracker.OpenCount(Time.time)).Append(" (opened ").Append(m.Wounds.Opened).Append(", torn ").Append(m.Wounds.Torn).Append(")  ");
                sb.Append(hf.Sight ? "<color=#f66>SIGHT</color>  " : "").Append("rends ").Append(hf.Rends).Append("  torn by you ").Append(hf.WoundsTorn)
                  .Append("  Resolve ").Append(hf.Resolve.Stacks(Time.time))
                  .Append("  rush: glints ").Append(hf.Glints).Append(", perfect links ").Append(hf.PerfectLinks).Append(" (last ").Append(hf.LastLink).Append(')')
                  .Append("  standoff ").Append(hf.ClashGauge.Perfects).Append('/').Append(hf.ClashGauge.Need).Append(hf.Clashing ? " <color=#ffd257>STANDOFF " + hf.Clash.Presses + "/" + hf.Clash.Target + "</color>" : "")
                  .Append(" won ").Append(hf.ClashesWon).Append(" lost ").Append(hf.ClashesLost)
                  .Append("  seeker ").Append(hf.Tracer.Active(Time.time) ? hf.Tracer.Hits + "/" + TracerMark.Needed : "-").Append(" bursts ").Append(hf.Tracer.Bursts).Append('\n');
            }
            if (S.InRange)
            {
                sb.Append("Targets ");
                foreach (var t in RangeTarget.All.OrderBy(x => x.Distance)) sb.Append(t.Distance.ToString("0")).Append(" m: ").Append(t.Hits).Append(" (gold ").Append(t.Golds).Append(")  ");
                sb.Append("  Hazards ").Append(RangeHazard.Enabled ? "on" : "off");
                foreach (var hz in RangeHazard.All) sb.Append("  ").Append(hz.name.Replace(" pad", "")).Append(": evaded ").Append(hz.Evaded).Append(", struck ").Append(hz.Struck);
                sb.Append('\n');
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
