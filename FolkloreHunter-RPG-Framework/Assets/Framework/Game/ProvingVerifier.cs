using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FH.Core;
using FH.UI;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Automated check of the Proving Grounds in the real Windows player (--verify-proving). The hunter is
    /// driven through an injected Xbox controller state, which goes through the same XInput mapping, InputRouter and
    /// HuntFighter as a real pad. The only fixtures are named in the checks (monster health resets, one attack set to
    /// knock down). Writes Captures/Proving/verification.txt and screenshots.</summary>
    public class ProvingVerifier : MonoBehaviour
    {
        GameSession S;
        string root;
        readonly List<string> lines = new List<string>();
        int failures;
        HunterController H => S.Hunter;
        HuntFighter F => S.Hunter.Hunt;

        IEnumerator Start()
        {
            S = GetComponent<GameSession>();
            root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "Proving"));
            Directory.CreateDirectory(root);
            Application.runInBackground = true;
            yield return Frames(5);
            float started = Time.realtimeSinceStartup;
            IEnumerator run = Run();
            while (true)
            {
                object current;
                try { if (!run.MoveNext()) break; current = run.Current; }
                catch (Exception e) { Check("no exception during verification", false, e.ToString()); break; }
                yield return current;
                if (Time.realtimeSinceStartup - started > 600) { Check("verification finished in time", false, "timed out"); break; }
            }
            GamePad.Inject(null);
            File.WriteAllText(Path.Combine(root, "verification.txt"), string.Join("\n", lines) + "\nFAILURES " + failures + "\nVersion " + Application.version + "  ·  " + DateTime.Now.ToString("u"));
            Debug.Log("FH_PROVING " + lines.Count(l => !l.StartsWith("NOTE")) + " checks; failures " + failures);
            yield return Frames(2);
            Application.Quit(failures > 0 ? 1 : 0);
        }

        // ---------------- helpers ----------------
        void Check(string name, bool ok, string detail = "")
        {
            lines.Add((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
            if (!ok) { failures++; Debug.LogWarning("FH_PROVING FAIL " + name + " " + detail); }
        }
        void Note(string t) => lines.Add("NOTE " + t);
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Snap(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(root, name + ".png")); yield return null; }

        /// <summary>Presses pad buttons for a few frames, then releases (pad stays connected with nothing held).</summary>
        IEnumerator Tap(PadButton b, int frames = 3) { GamePad.Inject(b); yield return Frames(frames); GamePad.Inject(PadButton.None); yield return Frames(2); }
        void Hold(PadButton b, Vector2 stick = default) => GamePad.Inject(b, stick);
        void Release() => GamePad.Inject(PadButton.None);
        IEnumerator UntilNeutral(float max = 4) { float t = 0; while (t < max && (H.Attack.Running || H.Dodging || F.Locked)) { t += Time.unscaledDeltaTime; yield return null; } yield return Frames(3); }
        IEnumerator Until(Func<bool> cond, float max) { float t = 0; while (t < max && !cond()) { t += Time.unscaledDeltaTime; yield return null; } }
        string CurrentId => F.Current != null ? F.Current.id : "-";

        void Face(MonsterActor m, float distance)
        {
            var o = m.transform.position + m.transform.forward * distance;
            var d = m.transform.position - o; d.y = 0;
            H.Teleport(S.SafeGround(o), Quaternion.LookRotation(d).eulerAngles.y);
            LookAlong(H.transform.eulerAngles.y);
        }

        MonsterActor Target()
        {
            var m = S.ProvingRig.Spawned.FirstOrDefault(x => x && x.Alive && x.MonsterId == "gravebound");
            if (!m) m = S.ProvingRig.Spawn("gravebound", true);
            m.Passive = true; m.Brain.Health = m.Def.maxHealth;
            return m;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        /// <summary>The camera behind something facing Unity yaw <paramref name="yaw"/> (the rig's yaw runs the other way).</summary>
        void LookAlong(float yaw) { S.CameraRig.Yaw = -yaw; S.CameraRig.Snap(); }
        static float Median(List<float> v) { if (v.Count == 0) return 0; var o = v.OrderBy(x => x).ToList(); return o[o.Count / 2]; }
        static float Percentile(List<float> v, float p) { if (v.Count == 0) return 0; var o = v.OrderBy(x => x).ToList(); return o[Mathf.Clamp((int)(o.Count * p), 0, o.Count - 1)]; }

        /// <summary>Pose frames of every hound clip (and of the hunter riding a gallop) for the SightForge / Claude Eyes
        /// inspector: side view, orthographic, transparent background, 16 frames per clip, rendered by the real player.</summary>
        IEnumerator CaptureHoundPoses(HoundCompanion hound)
        {
            string folder = Path.Combine(root, "HoundPoses"); Directory.CreateDirectory(folder);
            hound.enabled = false;
            var hide = new List<(GameObject go, int layer)>();
            void ToReview(Transform t) { foreach (var x in t.GetComponentsInChildren<Transform>(true)) { hide.Add((x.gameObject, x.gameObject.layer)); x.gameObject.layer = Layers.Review; } }
            ToReview(hound.Visual);
            var go = new GameObject("Hound review camera"); var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.cullingMask = 1 << Layers.Review; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.orthographic = true; cam.orthographicSize = 1.45f * HoundCompanion.Size;
            var rt = new RenderTexture(512, 400, 24); cam.targetTexture = rt;
            Vector3 c = hound.transform.position + Vector3.up * 1.05f * HoundCompanion.Size;
            cam.transform.position = c + hound.transform.right * 6f; cam.transform.LookAt(c);
            var anim = hound.Anim;
            var states = new[] { ("Idle", "idle"), ("Walk", "walk"), ("Gallop", "run"), ("Bite", "attack"), ("Slash_R", "slash"), ("Spin", "spin"), ("Pounce", "pounce"), ("Hop", "hop"), ("Sit", "sit"), ("Howl", "howl"), ("Hit", "hit") };
            foreach (var (clip, label) in states)
            {
                var st = anim.State(clip); if (st == null) { Check("hound has the " + clip + " clip", false); continue; }
                anim.Anim.Stop(); st.enabled = true; st.weight = 1;
                st.time = 0; anim.Anim.Sample(); yield return null;   // warm-up: the first frame must not show the previous clip
                for (int i = 0; i < 16; i++) { st.time = st.length * i / 16f; anim.Anim.Sample(); yield return Render(cam, rt, Path.Combine(folder, HoundCompanion.Model.ToLowerInvariant() + "_" + label + "_E_" + i.ToString("00") + ".png")); }
                st.enabled = false;
            }
            // Rider and mount together through one gallop cycle.
            if (!H.Riding) hound.Mount();
            ToReview(H.Visual);
            cam.orthographicSize = 1.75f * HoundCompanion.Size; c = hound.transform.position + Vector3.up * 1.4f * HoundCompanion.Size;
            cam.transform.position = c + hound.transform.right * 6f; cam.transform.LookAt(c);
            var g = anim.State("Gallop"); var ride = H.Anim.State("Ride_Idle");
            anim.Anim.Stop(); H.Anim.Anim.Stop(); g.enabled = true; g.weight = 1; ride.enabled = true; ride.weight = 1;
            g.time = 0; ride.time = 0; anim.Anim.Sample(); H.Anim.Anim.Sample(); yield return null;
            for (int i = 0; i < 16; i++)
            {
                g.time = g.length * i / 16f; ride.time = 0; anim.Anim.Sample(); H.Anim.Anim.Sample();
                yield return Render(cam, rt, Path.Combine(folder, "riding_run_E_" + i.ToString("00") + ".png"));
            }
            g.enabled = false; ride.enabled = false;
            hound.Dismount();
            foreach (var (o, layer) in hide) if (o) o.layer = layer;
            cam.targetTexture = null; rt.Release(); Destroy(rt); Destroy(go);
            anim.Current = ""; anim.Play("Idle"); H.Anim.Current = ""; H.Anim.Play("Idle");
            hound.enabled = true;
            Note("hound pose frames written to Captures/Proving/HoundPoses for SightForge review");
        }

        /// <summary>Pose frames of the hunter's new clips with the weapon in hand (bow, katana) and of the evasions, for the
        /// Claude Eyes inspector: side view, orthographic, transparent background, 16 frames per clip, rendered by the player.</summary>
        IEnumerator CaptureHunterPoses(string prefix = "hunter")
        {
            string folder = Path.Combine(root, "HunterPoses"); Directory.CreateDirectory(folder);
            H.enabled = false;                                   // nothing drives the pose while it is sampled
            var hide = new List<(GameObject go, int layer)>();
            foreach (var x in H.Visual.GetComponentsInChildren<Transform>(true)) { hide.Add((x.gameObject, x.gameObject.layer)); x.gameObject.layer = Layers.Review; }
            var go = new GameObject("Hunter review camera"); var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.cullingMask = 1 << Layers.Review; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.orthographic = true; cam.orthographicSize = 2.4f;
            var rt = new RenderTexture(640, 480, 24); cam.targetTexture = rt;     // wide enough for the katana's full reach
            var anim = H.Anim;
            var sets = new[]
            {
                ("bow", new[] { ("Bow_Draw", "bow_draw"), ("Bow_Shoot", "bow_shoot"), ("Bow_Power_Shoot", "bow_power"), ("Bow_Arc_Shoot", "bow_arc"), ("Bow_Piercer", "bow_piercer"), ("Bow_Step_R", "bow_step"), ("Bow_Aim_Walk", "walk") }),
                ("katana", new[] { ("2H_Melee_Attack_Chop", "katana_overhead"), ("2H_Melee_Attack_Slice", "katana_slice"), ("2H_Melee_Attack_Stab", "katana_thrust"), ("2H_Melee_Attack_Spin", "katana_roundslash"), ("2H_Melee_Attack_Spinning", "katana_spirit3"), ("Katana_Iai", "katana_iai") }),
                ("sword_shield", new[] { ("Hunt_Jog", "run"), ("Hunt_Sprint", "sprint"), ("Dodge_Right", "slide"), ("Dodge_Backward", "backstep"), ("Dodge_Forward", "roll"), ("Jump_Full_Short", "dive"), ("Lie_StandUp", "dive_landing") }),
            };
            int written = 0;
            foreach (var (weapon, clips) in sets)
            {
                S.ProvingRig.SetWeapon(weapon);
                F.SetSheathed(false);
                yield return Frames(2);
                Vector3 c = H.transform.position + Vector3.up * 1.05f;
                cam.transform.position = c + H.transform.right * 6f; cam.transform.LookAt(c);
                foreach (var (clip, label) in clips)
                {
                    var st = anim.State(clip); if (st == null) { Check("hunter has the " + clip + " clip", false); continue; }
                    anim.Anim.Stop(); st.enabled = true; st.weight = 1;
                    st.time = 0; anim.Anim.Sample(); yield return null;   // warm-up: the first frame must not show the previous clip
                    float from = label == "dive_landing" ? .3f : 0;      // the game starts the get-up past its sunk lying frames
                    float lowest = float.PositiveInfinity;
                    for (int i = 0; i < 16; i++)
                    {
                        st.time = st.length * (from + (1 - from) * i / 16f); anim.Anim.Sample();
                        F.KeepBladeOffGround();                          // as LateUpdate does in play
                        lowest = Mathf.Min(lowest, F.BladeTipClearance);
                        yield return Render(cam, rt, Path.Combine(folder, prefix + "_" + label + "_E_" + i.ToString("00") + ".png")); written++;
                    }
                    st.enabled = false;
                    if (label == "katana_overhead")
                        Check("katana overhead: the blade tip stays out of the ground (Claude Eyes found it 0.27 m under the soles)", lowest > -.03f, "lowest tip " + lowest.ToString("0.00") + " m over the ground, lifted " + F.BladeLifts + " times");
                }
            }
            foreach (var (o, layer) in hide) if (o) o.layer = layer;
            cam.targetTexture = null; rt.Release(); Destroy(rt); Destroy(go);
            anim.Current = ""; anim.Play("Idle");
            H.enabled = true;
            S.ProvingRig.SetWeapon("sword_shield");
            Check(prefix + " pose frames rendered for Claude Eyes (bow, katana, jog and sprint, evasions)", written == 20 * 16, written + " frames");
        }

        // ---------------- 0.9: the hunters from the main character sheet ----------------
        // ---------------- 0.9 battle: wounds, Sight and Rend, Resolve, Unravel, Thorn Rush, power clash, Thorn Tracer ----------------
        /// <summary>A wound opened on the monster's chest (or first) hurtbox without the blows (fixture for the Rend checks
        /// of the other weapons: opening a wound by blows is checked with the sword).</summary>
        Hurtbox OpenWoundFixture(MonsterActor m, int index = 0)
        {
            var boxes = m.GetComponentsInChildren<Hurtbox>().Where(b => !m.Wounds.IsOpen(b) && !m.Wounds.Tracker.IsScarred(MonsterWounds.Key(b), Time.time)).ToList();
            var box = boxes.FirstOrDefault(b => (b.Bone ?? b.name).ToLower().Contains("chest")) ?? boxes.FirstOrDefault(b => (b.Bone ?? b.name).ToLower().Contains("spine")) ?? boxes.ElementAtOrDefault(index) ?? boxes.FirstOrDefault();
            if (box) m.Wounds.Landed(box, box.transform.position, m.Wounds.Tracker.Threshold + 1, false);
            return box;
        }

        /// <summary>LT held (Hunter's Sight), then Y: the weapon's Rend. Returns once the move has run.</summary>
        IEnumerator SightRend()
        {
            Hold(PadButton.LT); yield return Wait(.35f);
            Pad(PadButton.LT | PadButton.Y); yield return Frames(3);
            Hold(PadButton.LT); yield return Frames(2);
            yield return Until(() => F.Current != null && F.Current.rend, 1f);
        }

        IEnumerator BattleChecks()
        {
            S.ProvingRig.ClearMonsters();
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(3);
            H.Health = H.Stats[Stat.MaxHealth];
            var m = Fresh(Target());
            Face(m, 2.1f); yield return Frames(3);
            Check("hunt monsters carry wounds", m.Wounds != null, m.DisplayName);
            if (m.Wounds == null) yield break;

            // Blows on one spot open a wound.
            for (int i = 0; i < 14 && m.Wounds.Tracker.OpenCount(Time.time) == 0; i++) { yield return Tap(PadButton.Y, 2); yield return Wait(.42f); if (i % 3 == 2) { yield return UntilNeutral(); Face(m, 2.1f); } }
            yield return UntilNeutral();
            var open = m.Wounds.OpenWounds().ToList();
            Check("blows that keep landing on one spot open a wound (a red glow on that bone)", open.Count > 0 && m.GetComponentsInChildren<Renderer>().Any(r => r.name.StartsWith("Wound ") && r.enabled),
                  "open " + open.Count + ", threshold " + m.Wounds.Tracker.Threshold.ToString("0"));

            // Hunter's Sight.
            Face(m, 2.1f); yield return Frames(2);
            Hold(PadButton.LT); yield return Wait(.4f);
            Check("holding LT with the weapon out is Hunter's Sight (the camera closes in)", F.Sight && S.CameraRig.Sight, F.LastEvent);
            yield return Wait(.3f);
            yield return Snap("w01-sight");
            // Y in Sight: the Rend tears the wound.
            float hp0 = m.Brain.Health; int torn0 = m.Wounds.Torn;
            Pad(PadButton.LT | PadButton.Y); yield return Frames(3); Hold(PadButton.LT); yield return Frames(2);
            yield return Until(() => F.Current != null && F.Current.rend, 1f);
            Check("Y in Sight is the sword's Rend (Rending Leap)", F.LastMove == "Rending Leap", F.LastMove + " / " + F.LastEvent);
            yield return Until(() => m.Wounds.Torn > torn0, 1.6f);
            yield return Wait(.1f);
            yield return Snap("w02-rend");
            float dealt = hp0 - m.Brain.Health;
            Check("the Rend tears the wound: heavy damage, the monster flinches, Resolve rises", m.Wounds.Torn > torn0 && F.Resolve.Stacks(Time.time) >= 1 && (m.Brain.Mode == MonsterMode.Stagger || m.Brain.Mode == MonsterMode.Downed),
                  "torn " + m.Wounds.Torn + ", damage " + dealt.ToString("0") + ", resolve " + F.Resolve.Stacks(Time.time) + ", " + m.Brain.Mode);
            Release(); yield return UntilNeutral();
            Check("a torn spot scars over and takes no new wound for a while", open.Count > 0 && m.Wounds.Tracker.IsScarred(MonsterWounds.Key(open[0].box), Time.time), F.LastEvent);
            // Tap LT with the weapon out: still the spell.
            float ready = H.SpellReadyAt; H.Magic = H.Stats[Stat.MaxMagic];
            yield return Wait(.3f);
            Pad(PadButton.LT); yield return Frames(3); Release(); yield return Frames(4);
            Check("a tap of LT with the weapon out still casts the spell (no Sight)", H.SpellReadyAt > ready && !F.Sight, "ready " + ready.ToString("0.0") + " -> " + H.SpellReadyAt.ToString("0.0"));
            yield return UntilNeutral();

            // Unravel: the third wound torn in the fight topples it (two more wounds opened by fixture, torn with the controls).
            Note("fixture: two more wounds opened directly on the monster for the Unravel check");
            for (int k = 0; k < 2; k++)
            {
                m.Brain.Settle(); Face(m, 2.1f); yield return Frames(2);
                var box = OpenWoundFixture(m, k + 1);
                int t0 = m.Wounds.Torn;
                yield return SightRend();
                yield return Until(() => m.Wounds.Torn > t0, 1.6f);
                Release(); yield return UntilNeutral();
            }
            Check("every third wound torn topples the monster (Unravel) and Resolve stacks to three", m.Wounds.Torn >= 3 && m.Brain.Mode == MonsterMode.Downed && F.Resolve.Stacks(Time.time) == HunterResolve.Max,
                  "torn " + m.Wounds.Torn + ", " + m.Brain.Mode + ", resolve " + F.Resolve.Stacks(Time.time));
            yield return Snap("w03-unravel");
            yield return Wait(1f);

            // Thorn Rush: back-step, Y (Leaping Slash), then Y as each glint shows: perfect links to the Shield Crash.
            m = Fresh(m); m.Brain.Settle();
            yield return Wait(3.5f);
            Face(m, 3.2f); yield return Frames(3);
            var seq = new List<string>();
            Pad(PadButton.A, new Vector2(0, -1)); yield return Frames(2); Release(); yield return Frames(2);
            Check("stick back and A with the sword out is a back-step", F.LastEvade == EvadeKind.Backstep, F.LastEvent);
            yield return Wait(.12f);
            int links0 = F.PerfectLinks, glints0 = F.Glints; float ko0 = m.Reactions.StunBuild; int topples0 = m.Reactions.Topples;
            yield return Tap(PadButton.Y, 2);
            for (float t = 0; t < 5f && seq.Count < 5; t += Time.unscaledDeltaTime)
            {
                if (CurrentId != "-" && seq.LastOrDefault() != CurrentId.Replace("_perfect", "")) seq.Add(CurrentId.Replace("_perfect", ""));
                if (F.Glinting && seq.Count < 5) { string before = CurrentId; yield return Tap(PadButton.Y, 2); yield return Until(() => CurrentId != before, .8f); continue; }
                yield return null;
            }
            yield return Snap("w04-shield-crash");
            yield return UntilNeutral();
            Check("back-step then Y: Leaping Slash, Thorn Rush I, II, III, Shield Crash", string.Join(",", seq) == "leaping_slash,rush_1,rush_2,rush_3,shield_crash", string.Join(",", seq));
            Check("each rush move glints, and Y in the glint is a perfect link (quicker, stronger)", F.Glints - glints0 >= 4 && F.PerfectLinks - links0 >= 4, "glints " + (F.Glints - glints0) + ", perfect links " + (F.PerfectLinks - links0));
            Check("the Shield Crash stuns hard", m.Reactions.StunBuild > ko0 || m.Reactions.Topples > topples0, "stun " + ko0.ToString("0") + " -> " + m.Reactions.StunBuild.ToString("0") + ", topples " + m.Reactions.Topples);
            // Pressed before the glint: a plain link.
            m = Fresh(m); m.Brain.Settle(); Face(m, 3.2f); yield return Frames(3);
            Pad(PadButton.A, new Vector2(0, -1)); yield return Frames(2); Release(); yield return Wait(.12f);
            yield return Tap(PadButton.Y, 2);
            yield return Until(() => F.Current != null && F.Current.id.StartsWith("leaping_slash") && HuntRoutes.CanChain(F.Current, H.Attack.Normalized), 1.5f);
            yield return Tap(PadButton.Y, 2);
            yield return Until(() => CurrentId.StartsWith("rush_1"), 1f);
            Check("Y before the glint is a plain link", CurrentId == "rush_1" && F.LastLink == RushTiming.Link.Plain, CurrentId + " " + F.LastLink);
            yield return UntilNeutral();

            // Power clash against a live Grave Warden: guard its blow with the meter full, mash Y.
            S.ProvingRig.ClearMonsters();
            var warden = S.ProvingRig.Spawn("grave_warden", true);
            warden.Passive = false; warden.Calm(1);
            warden.Brain.AttackFilter = a => a.Shape == AoeShape.None;
            Note("fixture: the clash meter filled directly (perfect guards filling it are checked with the guard)");
            for (int i = 0; i < 2; i++)
            {
                bool win = i == 0;
                while (!F.ClashGauge.Ready) F.ClashGauge.PerfectGuard();
                H.Health = H.Stats[Stat.MaxHealth];
                Face(warden, 2.0f);
                Hold(PadButton.RB);
                yield return Until(() => F.Clashing || F.LastEvent == "guarded" || H.Hurt, 9);
                bool clashed = F.Clashing;
                Release();
                if (i == 0) { yield return Wait(.2f); yield return Snap("w05-clash"); }
                float hpc = H.Health;
                int won0 = F.ClashesWon, lost0 = F.ClashesLost;
                for (float t = 0; t < 2.2f && F.Clashing; t += Time.unscaledDeltaTime)
                {
                    if (win) { yield return Tap(PadButton.Y, 2); yield return Frames(1); }
                    else yield return null;
                }
                if (win)
                {
                    Check("with the clash meter full the guarded blow is a power clash", clashed, F.LastEvent);
                    Check("mashing Y wins the clash: the Warden is thrown down and the Standoff Bash lands", F.ClashesWon > won0 && warden.Brain.Mode == MonsterMode.Downed && F.LastMove == "Standoff Bash",
                          "won " + F.ClashesWon + ", " + warden.Brain.Mode + ", " + F.LastMove);
                    yield return Wait(.3f);
                    yield return Snap("w06-clash-won");
                }
                else Check("not pressing loses the clash: the hunter is pushed back, unhurt", F.ClashesLost > lost0 && F.LastReaction == HitReaction.Knockback && H.Health >= hpc - .01f, "lost " + F.ClashesLost + ", " + F.LastReaction + ", hp " + hpc.ToString("0") + " -> " + H.Health.ToString("0"));
                yield return UntilNeutral(4);
                yield return Until(() => warden.Brain.Mode != MonsterMode.Downed, 5);
            }
            Check("each clash asks one more perfect guard", F.ClashGauge.Need == ClashMeter.BaseNeed + 2, "need " + F.ClashGauge.Need);
            S.ProvingRig.ClearMonsters();
            yield return Frames(2);

            // Lance and katana Rends (wound by fixture), the bow's Thorn Tracer.
            foreach (var (weapon, rend) in new[] { ("lance", "Piercing Rend"), ("katana", "Ember Rend") })
            {
                S.ProvingRig.SetWeapon(weapon); yield return Frames(3);
                m = Fresh(Target()); Face(m, 2.2f); yield return Frames(2);
                yield return Tap(PadButton.Y, 2); yield return UntilNeutral();          // draw
                Face(m, 2.2f); yield return Frames(2);
                OpenWoundFixture(m);
                int t0 = m.Wounds.Torn; float spirit0 = F.Spirit != null ? F.Spirit.Value : 0;
                yield return SightRend();
                bool right = F.LastMove == rend;
                yield return Until(() => m.Wounds.Torn > t0, 1.6f);
                yield return Wait(.1f);
                yield return Snap("w07-rend-" + weapon);
                Release(); yield return UntilNeutral();
                Check(weapon + ": Y in Sight is the " + rend + " and it tears the wound" + (weapon == "katana" ? " (the spirit gauge fills by half)" : ""),
                      right && m.Wounds.Torn > t0 && (weapon != "katana" || F.Spirit.Value >= Mathf.Min(100, spirit0 + 40)), F.LastMove + ", torn " + m.Wounds.Torn + (F.Spirit != null ? ", gauge " + spirit0.ToString("0") + " -> " + F.Spirit.Value.ToString("0") : ""));
            }
            S.ProvingRig.SetWeapon("bow"); yield return Frames(3);
            m = Fresh(Target()); Face(m, 9f); yield return Frames(2);
            yield return Tap(PadButton.Y, 2); yield return UntilNeutral();
            Face(m, 9f); yield return Frames(2);
            OpenWoundFixture(m);
            int bt0 = m.Wounds.Torn, planted0 = F.TracersPlanted, bursts0 = F.Tracer.Bursts;
            yield return SightRend();
            Check("bow: Y in Sight looses the Seeker Thorn", F.LastMove == "Seeker Thorn", F.LastMove);
            yield return Until(() => F.TracersPlanted > planted0, 2f);
            Release(); yield return UntilNeutral();
            Check("the tracer arrow tears the wound and stays in the monster", F.TracersPlanted > planted0 && m.Wounds.Torn > bt0 && F.Tracer.Active(Time.time), "planted " + F.TracersPlanted + ", torn " + m.Wounds.Torn);
            yield return Snap("w08-tracer");
            float hpb = m.Brain.Health;
            for (int i = 0; i < 14 && F.Tracer.Bursts == bursts0; i++) { yield return Tap(PadButton.Y, 2); yield return Wait(.5f); }
            Check("the tracer's arrows follow it and the eighth bursts it", F.Tracer.Bursts > bursts0, "hits " + F.Tracer.Hits + ", bursts " + F.Tracer.Bursts);
            yield return UntilNeutral();
            S.ProvingRig.ClearMonsters();
            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(3);
        }

        IEnumerator NewLookChecks()
        {
            var before = H.Look;
            foreach (var body in new[] { Appearance.Male, Appearance.Female })
            {
                var models = Appearance.Models(body, 1);
                string want = models.FirstOrDefault(CharacterFactory.Exists);
                if (want == null || want == "Hunter") { Note("the " + body + " hunter model is not in this build"); continue; }
                S.ProvingRig.ClearMonsters();
                if (H.LockTarget) yield return Tap(PadButton.RightStick);
                H.ApplyLook(models);
                S.ProvingRig.SetWeapon("sword_shield");
                F.SetSheathed(true);
                yield return Frames(4);
                var rends = H.Visual.GetComponentsInChildren<SkinnedMeshRenderer>();
                var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
                float ground = S.GroundHeight(H.transform.position);
                Check(body + " hunter: the character sheet's model is worn, standing on the ground at full height", H.Look == want && Mathf.Abs(b.min.y - ground) < .15f && b.size.y > 1.55f,
                      H.Look + ", height " + b.size.y.ToString("0.00") + " m, lowest point " + (b.min.y - ground).ToString("0.00") + " m from the ground");
                var clips = new[] { "Hunt_Jog", "Hunt_Sprint", "Bow_Draw", "Katana_Iai", "Lance_Charge", "Dodge_Forward", "Dodge_Right", "Ride_Idle", "2H_Melee_Attack_Chop", "1H_Melee_Attack_Chop" };
                Check(body + " hunter: every clip of the hunt moveset plays on the new body", clips.All(H.Anim.Has), string.Join(", ", clips.Where(c => !H.Anim.Has(c))));
                Check(body + " hunter: sword, shield, lance, bow and katana sit in the new hands", new[] { "1H_Sword", "Round_Shield", "Hunt_Lance", "Hunt_Bow", "Hunt_Katana" }.All(n => WorldKit.Find(H.Visual, n)));
                yield return Snap("n01-" + body + "-look");
                // a real fight: the draw slash and the combo land on a monster
                var m = Fresh(S.ProvingRig.Spawn("gravebound", true)); m.Passive = true;
                Face(m, 2.2f); yield return Frames(3);
                int landed = H.SwingsLanded;
                for (int i = 0; i < 3; i++) { yield return Tap(PadButton.Y); yield return Wait(.45f); }
                yield return UntilNeutral();
                Check(body + " hunter: the sword combo lands on a monster", H.SwingsLanded > landed, (H.SwingsLanded - landed) + " hits");
                yield return Snap("n02-" + body + "-combo");
                S.ProvingRig.ClearMonsters(); F.SetSheathed(true); yield return Frames(2);
                // jog: full stick for a second and a half
                var p0 = Flat(H.transform.position);
                Hold(PadButton.None, new Vector2(0, 1)); yield return Wait(1.5f);
                float jog = Vector3.Distance(p0, Flat(H.transform.position)) / 1.5f;
                Release(); yield return Wait(.4f);
                Check(body + " hunter: full stick jogs at the hunt pace", jog > 2.8f, jog.ToString("0.00") + " m/s");
                yield return CaptureHunterPoses(body);
            }
            H.ApplyLook(Appearance.Models(Appearance.Classic, 1));
            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(3);
            Check("the classic knight can be put back on", H.Look == "Hunter", H.Look + " (was " + before + ")");
        }

        // ---------------- 0.9: the skeletons and the Grave Warden from the supplied sheets ----------------
        IEnumerator SkeletonUpgradeChecks()
        {
            foreach (var id in new[] { "gravebound", "gravebound_knight", "gravebound_mage", "grave_warden" })
            {
                S.ProvingRig.ClearMonsters();
                if (H.LockTarget) yield return Tap(PadButton.RightStick);
                yield return Frames(2);
                var m = S.ProvingRig.Spawn(id, true);
                if (!m) { Check(id + " can be spawned", false); continue; }
                m.Passive = true;
                Face(m, id == "grave_warden" ? 6 : 4.5f); yield return Frames(4);
                var props = m.Visual.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith(m.Model + "_") && !r.name.EndsWith("_Body")).Select(r => r.name.Substring(m.Model.Length + 1)).ToList();
                bool fresh = m.Model == m.Def.model;
                if (fresh) Check(m.Def.name + ": the model from the skeleton sheet, holding its own weapons", props.Count > 0, m.Model + "; weapons " + string.Join("/", props));
                else Note(m.Def.name + ": its new model is not in this build (art paused); shown with the older " + m.Model + " model and its sword");
                Check(m.Def.name + " spawns and stands ready with its model and clips", m.Alive && m.Anim.Has("Idle") && m.Anim.Has("1H_Melee_Attack_Chop"), m.Model);
                yield return Snap("s01-" + id);
                if (id == "gravebound_mage")
                {
                    m.Passive = false; m.Brain.AttackFilter = null;
                    int bolts = MonsterVenom.GraveBolts;
                    S.ProvingRig.Invulnerable = true;
                    Face(m, 7f); yield return Frames(2);
                    yield return Until(() => MonsterVenom.GraveBolts > bolts, 8);
                    yield return Wait(.25f);
                    yield return Snap("s02-grave-bolt");
                    Check("the Gravebound Mage casts grave bolts at the hunter from range", MonsterVenom.GraveBolts > bolts, (MonsterVenom.GraveBolts - bolts) + " bolts");
                    S.ProvingRig.Invulnerable = false;
                }
            }
            S.ProvingRig.ClearMonsters();
        }

        static IEnumerator Render(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex); RenderTexture.active = old;
            yield return null;
        }

        int SaveFiles() => Directory.Exists(S.Store.Root) ? Directory.GetFiles(S.Store.Root, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith("settings.json")) : 0;

        Renderer R(string name) { var tr = WorldKit.Find(H.Visual, name); return tr ? tr.GetComponent<Renderer>() : null; }
        void Pad(PadButton b, Vector2 stick = default) => GamePad.Inject(b, stick);
        IEnumerator Combo(PadButton held, PadButton extra, int frames = 3, Vector2 stick = default) { Pad(held | extra, stick); yield return Frames(frames); Pad(held, stick); yield return Frames(2); }
        MonsterActor Fresh(MonsterActor m) { m = m && m.Alive ? m : Target(); m.Brain.Health = m.Def.maxHealth; return m; }

        // ---------------- the Briarhound fighting and riding like a Palamute ----------------
        static int SeqIndex(List<string> seq, params string[] run)
        {
            for (int i = 0; i + run.Length <= seq.Count; i++) if (seq.Skip(i).Take(run.Length).SequenceEqual(run)) return i;
            return -1;
        }

        IEnumerator HoundFightChecks(HoundCompanion hound, MonsterActor m)
        {
            // Combo: bite, return slash, spinning slash on the locked target.
            m = Fresh(m); m.Passive = true;
            int seq0 = hound.Sequence.Count, hits0 = hound.Bites;
            yield return Until(() => SeqIndex(hound.Sequence.Skip(seq0).ToList(), "Bite", "Slash_R", "Spin") >= 0, 10);
            yield return Wait(.6f);                      // the spin strikes 0.42 s after it starts
            var seq = hound.Sequence.Skip(seq0).ToList();
            Check("the hound fights in a combo: bite, return slash, spinning slash", SeqIndex(seq, "Bite", "Slash_R", "Spin") >= 0 && hound.Bites - hits0 >= 3,
                  string.Join(">", seq.Take(6)) + ", " + (hound.Bites - hits0) + " hits: " + string.Join("; ", hound.StrikeLog.Skip(Math.Max(0, hound.StrikeLog.Count - 4))));
            yield return Wait(.3f);
            yield return Snap("p10b-hound-spin");
            // Between combos it circles to the flank, across the monster from the hunter (the Palamute's pincer).
            float bestPincer = 0;
            for (float t = 0; t < 7; t += Time.unscaledDeltaTime) { bestPincer = Mathf.Max(bestPincer, hound.PincerAngle); if (bestPincer > 110) break; yield return null; }
            Check("between combos the hound takes the flank across the monster from the hunter", bestPincer > 110, "pincer angle " + bestPincer.ToString("0") + " degrees; " + string.Join(" | ", hound.DecisionLog.Skip(Math.Max(0, hound.DecisionLog.Count - 8))));
            // Openings: when the monster is toppled right after a combo, the hound goes straight back in (no 2.4 s pause).
            m = Fresh(m);
            yield return Until(() => hound.Sequence.Count > 0 && hound.Sequence[hound.Sequence.Count - 1] == "Spin" && !hound.Acting, 8);
            int pun0 = hound.Punishes, sq0 = hound.Sequence.Count;
            m.Brain.Topple(3.5f);                       // fixture: the monster is knocked down, as a topple from stun does
            float toppledAt = Time.time;
            yield return Until(() => hound.Sequence.Count > sq0, 3);
            float react = Time.time - toppledAt;
            Check("a toppled monster is punished at once (the hound skips its pause between combos)", hound.Punishes > pun0 && react < 1.2f, "toppled at " + toppledAt.ToString("0.00") + ", next strike after " + react.ToString("0.00") + " s (pause " + HoundTactics.ComboCooldown + " s), " + hound.LastEvent + "; "
                  + string.Join(" | ", hound.DecisionLog.Where(e => float.TryParse(e.Split(' ')[0], out float et) && et >= toppledAt - 1.5f).Take(24)));
            yield return Wait(3.2f);
            Check("the hound cuts the softest part in reach, not just the nearest", hound.WorthChosen >= hound.WorthNearest && hound.WorthChosen > 0,
                  "worth chosen " + hound.WorthChosen.ToString("0.0") + " vs nearest " + hound.WorthNearest.ToString("0.0") + ", last part " + hound.LastPartStruck);
            // Target choice: with no lock, it joins whichever monster the hunter is hitting.
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            var other = S.ProvingRig.Spawn("gravebound");
            other.Passive = true; other.Brain.Health = other.Def.maxHealth;
            other.transform.position = S.SafeGround(m.transform.position + Vector3.right * 9);
            yield return Frames(3);
            Face(other, 1.8f); yield return Frames(2);
            F.SetSheathed(false);
            for (int i = 0; i < 3 && hound.Target != other; i++) { yield return Tap(PadButton.Y, 2); yield return Wait(.5f); }
            yield return Until(() => hound.Target == other, 2);
            Check("with no lock, the hound joins the monster the hunter is hitting", hound.Target == other, (hound.Target ? hound.Target.DisplayName : "none") + " (" + hound.TargetReason + ")");
            yield return UntilNeutral();
            S.ProvingRig.Spawned.Remove(other); S.DespawnMonster(other);
            yield return Wait(.5f);
            // Steering: sent round the stone pillar to reach the hunter, without teleporting.
            var o = S.World.ProvingOrigin;
            H.Teleport(S.SafeGround(o + new Vector3(0, 0, -9)), 180); S.CameraRig.Yaw = 180; S.CameraRig.Snap();
            hound.PlaceAt(S.SafeGround(o + new Vector3(0, 0, -19.5f)), 0);
            int det0 = hound.Detours, warp0 = hound.Warps;
            var heel = H.transform.position - (H.transform.forward * 2.6f - H.transform.right * 1.8f) * HoundCompanion.Size;
            yield return Until(() => Vector3.Distance(Flat(hound.transform.position), Flat(heel)) < 2.5f, 8);
            float arrived = Vector3.Distance(Flat(hound.transform.position), Flat(heel));
            Check("the hound steers round a pillar to the hunter (no teleport)", arrived < 2.5f && hound.Detours > det0 && hound.Warps == warp0,
                  arrived.ToString("0.0") + " m from heel, detours " + (hound.Detours - det0) + ", warps " + (hound.Warps - warp0));
            yield return Snap("p10f-hound-detour");
            m = Fresh(m); Face(m, 3f); yield return Frames(2);
            if (H.LockTarget != m) { if (H.LockTarget) yield return Tap(PadButton.RightStick); yield return Tap(PadButton.RightStick); }
            Check("locked on to the training Gravebound again for the next hound checks", H.LockTarget == m);
            // Pounce: from mid range it leaps in and strikes.
            m = Fresh(m);
            yield return Until(() => hound.PounceReady, 7);   // the earlier pounce's cooldown
            var away = Flat(hound.transform.position - m.transform.position).normalized; if (away.sqrMagnitude < .01f) away = m.transform.right;
            hound.PlaceAt(S.SafeGround(m.transform.position + away * (m.Def.bodyRadius + 6.5f)), Quaternion.LookRotation(-away).eulerAngles.y);
            int p0 = hound.Pounces, b0 = hound.Bites; float gap0 = Vector3.Distance(Flat(hound.transform.position), Flat(m.transform.position));
            yield return Until(() => hound.Pounces > p0, 6);
            yield return Wait(1f);
            float gap1 = Vector3.Distance(Flat(hound.transform.position), Flat(m.transform.position));
            Check("from mid range the hound pounces: leaps in and strikes", hound.Pounces > p0 && hound.Bites > b0 && gap1 < gap0 - 2.5f, "gap " + gap0.ToString("0.0") + " -> " + gap1.ToString("0.0") + " m, " + string.Join("; ", hound.StrikeLog.Skip(Math.Max(0, hound.StrikeLog.Count - 2))));
            // Evade: a monster winding up with the hound inside its reach: it hops clear.
            m = Fresh(m);
            S.ProvingRig.Invulnerable = true;
            m.Passive = false; m.Calm(0);
            int e0 = hound.Evades; float closest = 99;
            for (float t = 0; t < 14 && hound.Evades == e0; t += Time.unscaledDeltaTime)
            {
                if (m.Brain.Mode == MonsterMode.Telegraph) closest = Mathf.Min(closest, Vector3.Distance(Flat(hound.transform.position), Flat(m.transform.position)));
                yield return null;
            }
            yield return Wait(.25f);
            float after = Vector3.Distance(Flat(hound.transform.position), Flat(m.transform.position));
            yield return Snap("p10c-hound-evade");
            Check("when the monster winds up near it, the hound hops clear", hound.Evades > e0 && after > closest, "evades " + (hound.Evades - e0) + ", " + closest.ToString("0.0") + " -> " + after.ToString("0.0") + " m, " + hound.LastEvent);
            m.Passive = true; m.Calm(3); S.ProvingRig.Invulnerable = false; H.Health = H.Stats[Stat.MaxHealth];
            yield return Wait(.6f);

            // Ridden: Y attacks (chained), A sidesteps, RB while dashing drifts.
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            m = Fresh(m);
            var front = Flat(m.transform.forward).normalized;
            hound.PlaceAt(S.SafeGround(m.transform.position + front * (m.Def.bodyRadius + 2f * HoundCompanion.Size)), Quaternion.LookRotation(-front).eulerAngles.y);
            yield return Frames(3);
            H.Teleport(S.SafeGround(hound.transform.position + hound.transform.right * 1.4f * HoundCompanion.Size), hound.transform.eulerAngles.y);
            F.SetSheathed(true);
            yield return Frames(3);
            yield return Tap(PadButton.X);
            yield return Frames(3);
            Check("mounted again next to the monster", H.Riding, hound.LastEvent);
            hound.PlaceAt(hound.transform.position, Quaternion.LookRotation(Flat(m.transform.position - hound.transform.position)).eulerAngles.y);
            LookAlong(hound.transform.eulerAngles.y);
            int r0 = hound.RideAttacks, rb0 = hound.Bites, rs0 = hound.Sequence.Count;
            for (int i = 0; i < 3; i++) { yield return Tap(PadButton.Y, 2); yield return Wait(.38f); }
            yield return Until(() => !hound.Acting, 2);
            var rseq = hound.Sequence.Skip(rs0).ToList();
            Check("riding, Y makes the hound attack; pressed again it chains bite, slash, spin", hound.RideAttacks - r0 == 3 && SeqIndex(rseq, "Bite", "Slash_R", "Spin") == 0 && hound.Bites > rb0,
                  string.Join(">", rseq) + ", hits " + (hound.Bites - rb0));
            yield return Snap("p10d-ridden-attack");
            Vector3 h0 = hound.transform.position; float f0 = hound.transform.eulerAngles.y; int hop0 = hound.RideHops; bool inv = false;
            Pad(PadButton.A); yield return Frames(2); Release();
            for (float t = 0; t < .5f; t += Time.unscaledDeltaTime) { inv |= H.Invulnerable; yield return null; }
            float hopDist = Vector3.Distance(Flat(h0), Flat(hound.transform.position));
            Check("riding, A sidesteps the hound (rider protected, facing kept)", hound.RideHops == hop0 + 1 && hopDist > 1.6f && inv && Mathf.Abs(Mathf.DeltaAngle(f0, hound.transform.eulerAngles.y)) < 10,
                  hopDist.ToString("0.00") + " m, rider invulnerable " + inv);
            // Locked on from the saddle with the stick let go: the hound runs the rider to the monster and stops at striking range.
            m = Fresh(m);
            var off = Flat(hound.transform.position - m.transform.position).normalized; if (off.sqrMagnitude < .01f) off = Vector3.back;
            hound.PlaceAt(S.SafeGround(m.transform.position + off * (m.Def.bodyRadius + 12)), Quaternion.LookRotation(Vector3.Cross(Vector3.up, off)).eulerAngles.y);
            LookAlong(Quaternion.LookRotation(-off).eulerAngles.y);
            yield return Frames(3);
            yield return Tap(PadButton.RightStick);
            bool locked = H.LockTarget == m, ran = false;
            for (float t = 0; t < 6; t += Time.unscaledDeltaTime) { ran |= hound.AutoRunning; if (ran && !hound.AutoRunning) break; yield return null; }
            float standOff = Vector3.Distance(Flat(hound.transform.position), Flat(m.transform.position)) - m.Def.bodyRadius;
            Check("riding, lock on and let go of the stick: the hound runs to the monster and stops in striking range", locked && ran && standOff < HoundTactics.AutoRunStandOff * HoundCompanion.Size + 1 && standOff > 1,
                  "locked " + locked + ", ran " + ran + ", stopped " + standOff.ToString("0.0") + " m off its body");
            // A wind-up aimed at the rider: the hound sidesteps by itself and the rider stays on.
            S.ProvingRig.Invulnerable = true;
            m.Passive = false; m.Calm(0);
            int ae0 = hound.AutoEvades;
            for (float t = 0; t < 14 && hound.AutoEvades == ae0 && H.Riding; t += Time.unscaledDeltaTime) yield return null;
            yield return Wait(.6f);
            Check("riding, a monster's wind-up makes the hound sidestep on its own; the rider stays on", hound.AutoEvades > ae0 && H.Riding, "auto sidesteps " + (hound.AutoEvades - ae0) + ", riding " + H.Riding + ", " + hound.LastEvent);
            m.Passive = true; m.Calm(3); S.ProvingRig.Invulnerable = false; H.Health = H.Stats[Stat.MaxHealth];
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            if (!H.Riding)
            {
                H.Teleport(S.SafeGround(hound.transform.position + hound.transform.right * 1.4f * HoundCompanion.Size), hound.transform.eulerAngles.y);
                F.SetSheathed(true); yield return Frames(3); yield return Tap(PadButton.X); yield return Frames(3);
            }
            // Dash on a straight, then the same turn dashing and drifting: the drift turns much tighter and leans in.
            hound.PlaceAt(S.SafeGround(S.World.ProvingOrigin + new Vector3(0, 0, -24)), 90);
            LookAlong(90);
            float stamina0 = H.Stamina.Current;
            Hold(PadButton.RT, new Vector2(0, 1)); yield return Wait(.8f);
            float y0 = hound.transform.eulerAngles.y;
            Hold(PadButton.RT, new Vector2(-.7f, -.7f)); yield return Wait(.2f);
            float dashTurn = Mathf.Abs(Mathf.DeltaAngle(y0, hound.transform.eulerAngles.y));
            Hold(PadButton.RT, new Vector2(0, 1)); yield return Wait(.8f);
            float y1 = hound.transform.eulerAngles.y, lean = 0; bool drifted = false;
            Hold(PadButton.RT | PadButton.RB, new Vector2(-.7f, -.7f));
            for (float t = 0; t < .2f; t += Time.unscaledDeltaTime)
            {
                drifted |= hound.Drifting;
                lean = Mathf.Max(lean, Mathf.Abs(Mathf.DeltaAngle(0, hound.Visual.localEulerAngles.z)));
                yield return null;
            }
            float driftTurn = Mathf.Abs(Mathf.DeltaAngle(y1, hound.transform.eulerAngles.y));
            yield return Snap("p10e-drift");
            Release(); yield return Wait(.6f);
            Check("riding, RB while dashing drifts: a much tighter turn, leaning into it", drifted && driftTurn > dashTurn * 1.5f && lean > 6,
                  "in 0.2 s toward 135 degrees: dash turn " + dashTurn.ToString("0") + ", drift turn " + driftTurn.ToString("0") + " degrees, lean " + lean.ToString("0"));
            Check("riding costs the hunter no stamina", H.Stamina.Current >= stamina0 - .5f, stamina0.ToString("0") + " -> " + H.Stamina.Current.ToString("0"));
            yield return Tap(PadButton.X);
            yield return Wait(.4f);
            Note("hound: strikes landed " + hound.Bites + ", pounces " + hound.Pounces + ", evades " + hound.Evades + ", punishes " + hound.Punishes + ", detours " + hound.Detours + ", warps " + hound.Warps
                 + ", ridden attacks " + hound.RideAttacks + ", sidesteps " + hound.RideHops + " (+" + hound.AutoEvades + " on its own), last part " + hound.LastPartStruck);
        }

        // ---------------- boss monsters: tougher, area attacks, phases ----------------
        MonsterAoe NewArea(string id) => MonsterAoe.Active.LastOrDefault(a => a && a.Attack.id == id);

        IEnumerator Force(MonsterActor m, string id)
        {
            yield return Until(() => m.Brain.Mode == MonsterMode.Chase || m.Brain.Mode == MonsterMode.Idle || m.Brain.Mode == MonsterMode.Alert, 4);   // the previous move finished
            yield return Until(() => m.Brain.Grace <= 0, 4);              // and its grace is over (a forced move waits for it)
            int started = m.AttacksStarted;
            m.Brain.ForceNext(m.Def.attacks.First(a => a.id == id));   // fixture: this move now, to check it deterministically
            yield return Until(() => m.AttacksStarted > started && m.Brain.CurrentAttack != null && m.Brain.CurrentAttack.id == id, 3);   // a new wind-up, not an earlier one
            yield return null;
        }

        IEnumerator BossChecks()
        {
            S.ProvingRig.ClearMonsters();
            S.ProvingRig.SetWeapon("sword_shield");
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            yield return Frames(2);
            var w = S.ProvingRig.Spawn("grave_warden", true);
            var valley = S.Db.Monsters["grave_warden"];
            Check("hunt rules make the Grave Warden a boss: 2.5x health and its area attacks", w.Def.maxHealth >= valley.maxHealth * 2.4f && w.Def.attacks.Count(a => a.Shape != AoeShape.None) >= 4 && valley.attacks.All(a => a.Shape == AoeShape.None),
                  w.Def.maxHealth.ToString("0") + " vs " + valley.maxHealth.ToString("0") + " health, " + string.Join("/", w.Def.attacks.Select(a => a.id)));
            w.Passive = false;
            w.Brain.AttackFilter = a => false;           // fixture: it makes only the moves under test (forced one at a time)
            S.ProvingRig.Invulnerable = true;            // health refilled; blows still land (counted by the areas)
            // The Briarhound sits these out: its bites flinch the boss, and a flinch cancels a wind-up (by design).
            if (S.Hound) { S.Hound.PlaceAt(S.SafeGround(S.World.ProvingOrigin + new Vector3(-30, 0, -20)), 0); S.Hound.enabled = false; }
            F.SetSheathed(true);
            // Roar: ears covered, no damage.
            Face(w, 5); yield return Frames(2);
            yield return Force(w, "warden_roar");
            var roar = NewArea("warden_roar");
            Check("the roar telegraphs on the ground around the boss", roar && !roar.Fired);
            yield return Until(() => F.LastReaction == HitReaction.Roar && F.LastEvent.Contains("roar"), 2.5f);
            yield return Snap("b01-roar");
            Check("caught in the roar the hunter covers their ears (no damage, rooted a moment)", F.LastReaction == HitReaction.Roar && roar && roar.HunterHits > 0, F.LastEvent);
            yield return UntilNeutral(3);
            // Shockwave: the ring's front reaches a hunter 5 m away...
            Face(w, 5); yield return Frames(2);
            yield return Force(w, "ground_slam");
            var slam = NewArea("ground_slam");
            bool had = slam; int hits = 0;
            yield return Until(() => slam == null || slam.Fired, 2);
            yield return Wait(.25f);
            if (slam) hits = slam.HunterHits;
            yield return Snap("b02-shockwave");
            for (float t0 = Time.time; slam && Time.time - t0 < .6f; ) { hits = slam.HunterHits; yield return null; }   // a single wave is gone in 0.8 s
            Check("ground slam: the shockwave ring travels out and strikes the hunter 5 m away", had && hits > 0, had ? hits + " hits" : "no area");
            yield return UntilNeutral(4);
            // ... and a roll through the wave front evades it.
            Face(w, 5); yield return Frames(2);
            yield return Force(w, "ground_slam");
            slam = NewArea("ground_slam");
            float dist = Vector3.Distance(Flat(H.transform.position), Flat(w.transform.position));
            yield return Until(() => slam == null || (slam.Fired && slam.WaveRadius >= dist - 1.5f), 3);
            float evadedBefore = H.LastDodgeEvade;
            had = slam; hits = 0;
            yield return Tap(PadButton.A, 2);
            int dodged = 0;
            for (float t0 = Time.time; Time.time - t0 < .8f; ) { if (slam) { hits = slam.HunterHits; dodged = slam.HunterEvaded; } yield return null; }
            Check("rolling through the shockwave's front evades it", had && hits == 0 && dodged > 0 && H.LastDodgeEvade > evadedBefore, had ? hits + " hits, " + dodged + " evaded" : "no area");
            yield return UntilNeutral(4);
            // Eruptions: three circles, one under the hunter; moving away in time avoids them.
            Face(w, 7); yield return Frames(2);
            yield return Force(w, "grave_burst");
            var bursts = MonsterAoe.Active.Where(a => a && a.Attack.id == "grave_burst").ToList();
            bool under = bursts.Any(b => Vector3.Distance(Flat(b.Origin), Flat(H.transform.position)) < .4f);
            yield return Snap("b03-eruptions-telegraph");
            Hold(PadButton.None, new Vector2(1, 0));
            yield return Wait(1.4f);
            Release();
            yield return Wait(.5f);
            Check("grave burst: three eruptions telegraph, one under the hunter; moving away in time avoids them", bursts.Count == 3 && under && bursts.All(b => b == null || b.HunterHits == 0),
                  bursts.Count + " eruptions, under the hunter " + under + ", hits " + bursts.Sum(b => b ? b.HunterHits : 0));
            yield return UntilNeutral(4);
            // Leap: from 9 m the boss jumps onto where the hunter stood.
            Face(w, 9); yield return Frames(2);
            Vector3 w0 = w.transform.position, stood = H.transform.position;
            yield return Force(w, "leap_smash");
            var leap = NewArea("leap_smash");
            yield return Wait(.6f);
            yield return Snap("b04-leap");
            yield return Until(() => leap == null || leap.Fired, 2);
            yield return Wait(.3f);
            float jumped = Vector3.Distance(Flat(w0), Flat(w.transform.position)), landedOff = Vector3.Distance(Flat(w.transform.position), Flat(stood));
            Check("leap smash: from 9 m it jumps onto where the hunter stood and the landing strikes", jumped > 5 && landedOff < 3f && leap && leap.HunterHits > 0, jumped.ToString("0.0") + " m jumped, landed " + landedOff.ToString("0.0") + " m off, hits " + (leap ? leap.HunterHits : 0));
            yield return UntilNeutral(4);
            // Phases: enraged below half health (a roar opens its enraged moves); desperate below 30 % (another roar).
            foreach (var (frac, phase) in new[] { (.47f, 2), (.28f, 3) })
            {
                w.Brain.Health = w.Def.maxHealth * frac;    // fixture: health set just above the threshold
                Face(w, 1.8f); yield return Frames(2);
                F.SetSheathed(false);
                for (int i = 0; i < 6 && w.MaxPhase < phase; i++) { yield return Tap(PadButton.Y, 2); yield return Wait(.45f); }
                yield return Until(() => w.Brain.CurrentAttack != null && w.Brain.CurrentAttack.id == "warden_roar", 3);
                Check((phase == 2 ? "below half health it enrages" : "below 30 % it turns desperate") + " and roars (phase " + phase + ")", w.MaxPhase >= phase && w.LastAttackId == "warden_roar", "phase " + w.Phase + " (max " + w.MaxPhase + "), last attack " + w.LastAttackId);
                yield return UntilNeutral(4);
            }
            Check("desperate: the cataclysm (three shockwaves) is in its choice", HuntBoss.Usable(w.Def.attacks.First(a => a.id == "cataclysm"), new BossSituation { Distance = 3, Angle = 0, Phase = w.Phase }));
            yield return Force(w, "cataclysm");
            var cata = NewArea("cataclysm");
            yield return Until(() => cata == null || cata.Fired, 2);
            yield return Wait(.7f);
            yield return Snap("b05-cataclysm");
            yield return Wait(1.2f);
            Check("the cataclysm sends three shockwaves in a row", cata && cata.HunterHits >= 2, cata ? cata.HunterHits + " waves struck" : "no area");
            yield return UntilNeutral(4);
            // The Briarhound beside the Warden when it slams: it hops clear, or the wave knocks it back a step.
            if (S.Hound)
            {
                yield return UntilNeutral(4);
                w.Brain.Health = w.Def.maxHealth; Face(w, 6); yield return Frames(2);
                S.Hound.enabled = true;
                S.Hound.PlaceAt(S.SafeGround(w.transform.position + w.transform.right * 3.5f), w.transform.eulerAngles.y);
                int houndBefore = S.Hound.BlowsTaken + S.Hound.Evades;
                yield return Force(w, "ground_slam");
                var hs = NewArea("ground_slam");
                yield return Until(() => hs == null || hs.Fired, 2);
                yield return Wait(1f);
                Check("the Briarhound beside the slam hops clear of it or is knocked back a step", S.Hound.BlowsTaken + S.Hound.Evades > houndBefore || (hs && hs.HoundHits > 0), S.Hound.LastEvent);
                yield return UntilNeutral(4);
            }
            // Thornback: a potion at mid range draws a charge.
            S.ProvingRig.ClearMonsters(); yield return Frames(2);
            var t = S.ProvingRig.Spawn("thornback", true); t.Passive = false;   // (its own choice here: that is what is checked)
            F.SetSheathed(true);
            S.ProvingRig.Invulnerable = false; H.Health = H.Stats[Stat.MaxHealth] * .6f;   // hurt, so the potion is drunk
            yield return Until(() => t.Brain.Mode == MonsterMode.Chase && t.Brain.Grace <= 0, 6);
            Face(t, 8f); yield return Frames(2);         // at range, the moment it is free to attack
            yield return Tap(PadButton.LB, 2);           // drink the potion in the selected slot
            yield return Until(() => t.Brain.Mode == MonsterMode.Telegraph, 4);
            var answer = t.Brain.CurrentAttack;
            Check("drinking a potion in front of the Thornback draws a gap closer (its charge)", H.LastItemAt > 0 && answer != null && answer.gapCloser, answer != null ? answer.id : "none");
            yield return UntilNeutral(4);
            // Mirejaw: the tail spin hits all round, even a hunter behind it.
            S.ProvingRig.ClearMonsters(); yield return Frames(2);
            var mj = S.ProvingRig.Spawn("mirejaw", true); mj.Passive = false;
            mj.Brain.AttackFilter = a => false;          // fixture: only the forced move
            var behind = mj.transform.position - mj.transform.forward * 3.2f;
            H.Teleport(S.SafeGround(behind), Quaternion.LookRotation(Flat(mj.transform.position - behind)).eulerAngles.y); LookAlong(H.transform.eulerAngles.y);
            yield return Frames(2);
            float yaw0 = mj.transform.eulerAngles.y;
            yield return Force(mj, "tail_spin");
            var spin = NewArea("tail_spin");
            yield return Until(() => spin == null || spin.Fired, 2);
            yield return Wait(.5f);
            Check("Mirejaw's tail spin strikes all round (a hunter behind it too)", spin && spin.HunterHits > 0, spin ? spin.HunterHits + " hits" : "no area");
            yield return UntilNeutral(4);
            S.ProvingRig.ClearMonsters();
            S.ProvingRig.Invulnerable = false; H.Health = H.Stats[Stat.MaxHealth];
            F.SetSheathed(true);
            if (S.Hound) S.Hound.enabled = true;
            yield return Frames(3);
        }

        // ---------------- movement feel (Monster Hunter pace) ----------------
        IEnumerator MovementChecks()
        {
            S.ProvingRig.SetWeapon("sword_shield");
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            var o = S.World.ProvingOrigin + new Vector3(-20, 0, -20);
            H.Teleport(S.SafeGround(o), 45); LookAlong(45);
            yield return Frames(3);
            var footL = WorldKit.Find(H.Visual, "foot.l"); var footR = WorldKit.Find(H.Visual, "foot.r");
            foreach (var (label, button, want, clip) in new[] { ("full stick jogs", PadButton.None, HunterController.JogSpeed, "Hunt_Jog"), ("RT sprints", PadButton.RT, HunterController.SprintSpeed, "Hunt_Sprint") })
            {
                H.Stamina.Current = H.Stamina.Max;
                Hold(button, new Vector2(0, 1));
                yield return Wait(.5f);
                var planted = new List<float>(); var body = new List<float>(); var sweep = new List<float>();
                Vector3 pl = footL.position, pr = footR.position, pb = H.transform.position; float t0 = Time.time; Vector3 start = H.transform.position;
                for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    float dt = Time.deltaTime; if (dt <= 0) continue;
                    float vl = Vector3.Distance(Flat(footL.position), Flat(pl)) / dt, vr = Vector3.Distance(Flat(footR.position), Flat(pr)) / dt;
                    // The slower foot is the one on the ground: how fast does it slide? (Height is no guide on this rig:
                    // the swinging foot's ankle often rides lower than the planted one.)
                    planted.Add(Mathf.Min(vl, vr));
                    // Diagnostic: the lower foot's sweep relative to the hunter (what the clip itself does at this rate).
                    var lowF = footL.position.y < footR.position.y ? footL : footR;
                    sweep.Add(Vector3.Dot(Flat(lowF.position - H.transform.position), H.transform.forward));
                    body.Add(Vector3.Distance(Flat(H.transform.position), Flat(pb)) / dt);
                    pl = footL.position; pr = footR.position; pb = H.transform.position;
                }
                float speed = Vector3.Distance(Flat(start), Flat(H.transform.position)) / Mathf.Max(.01f, Time.time - t0);
                string playing = H.Anim.Current;
                Release(); yield return Wait(.3f);
                Check(label + " at Monster Hunter pace (" + want + " m/s) with its stride clip", Mathf.Abs(speed - want) < want * .12f && playing.Contains(clip), speed.ToString("0.00") + " m/s, " + playing);
                float slide = Median(planted), bodyV = Median(body);
                var st = H.Anim.State(clip);
                Note(label + ": clip " + clip + " playing at " + (st != null ? st.speed.ToString("0.00") : "?") + "x, cycle " + (st != null ? (st.length / Mathf.Max(.01f, st.speed)).ToString("0.00") : "?") + " s; lower foot ranges "
                     + (sweep.Count > 0 ? sweep.Min().ToString("0.00") + " to " + sweep.Max().ToString("0.00") : "-") + " m along the facing; slower-foot speed P25 " + Percentile(planted, .25f).ToString("0.00") + ", median " + slide.ToString("0.00") + " m/s");
                // Known limit, reported rather than gated: the stock run's brief foot contact cannot carry these speeds
                // without slide (a run cycle authored for them is the fix). Gate only against it getting worse.
                Note(label + ": KNOWN LIMIT foot slide: the slower foot moves at " + slide.ToString("0.00") + " m/s while the body moves " + bodyV.ToString("0.00") + " m/s (" + (slide / Mathf.Max(.01f, bodyV) * 100).ToString("0") + " %)");
                Check(label + ": foot slide no worse than measured for 0.8.0 (under 75 % of body speed)", slide < bodyV * .75f, slide.ToString("0.00") + " vs " + bodyV.ToString("0.00") + " m/s");
                H.Teleport(S.SafeGround(o), 45); LookAlong(45); yield return Frames(2);
            }
            // Drawn weapon: a slower jog, not the old shuffle.
            F.SetSheathed(false); yield return Frames(2);
            Vector3 d0 = H.transform.position;
            Hold(PadButton.None, new Vector2(0, 1)); yield return Wait(1f); Release();
            float drawn = Vector3.Distance(Flat(d0), Flat(H.transform.position)) / 1f;
            Check("weapon drawn, full stick still jogs (about 85 % pace for the sword)", drawn > HunterController.JogSpeed * .7f, drawn.ToString("0.00") + " m/s");
            yield return Wait(.3f);
            // Attacks start on the press: no 70 ms wait to see whether the other button follows.
            var m = Fresh(Target()); Face(m, 1.8f); yield return Frames(3);
            GamePad.Inject(PadButton.Y); yield return null; yield return null;
            string onPress = CurrentId;
            GamePad.Inject(PadButton.None); yield return UntilNeutral();
            Check("Y starts the attack on the press (within two frames)", onPress != "-", onPress);
            // ... and B just after Y (inside 70 ms) still makes the Y+B move.
            Face(m, 1.8f); yield return Frames(3);
            GamePad.Inject(PadButton.Y); yield return null;
            GamePad.Inject(PadButton.Y | PadButton.B); yield return Frames(2);
            GamePad.Inject(PadButton.None); yield return Until(() => CurrentId == "shield_bash", .3f);
            Check("Y then B a moment later still becomes Y+B (shield bash)", CurrentId == "shield_bash", CurrentId + ", " + F.LastEvent);
            yield return UntilNeutral();
        }

        // ---------------- lance ----------------
        IEnumerator LanceChecks()
        {
            S.ProvingRig.ClearMonsters();
            yield return Frames(2);
            S.ProvingRig.SetWeapon("lance");
            yield return Frames(3);
            var lance = WorldKit.Find(H.Visual, "Hunt_Lance");
            Check("the ledger puts the lance and tower shield in hand (sheathed on the back)", F.Class == WeaponClass.Lance && F.Sheathed && R("Hunt_Lance").enabled && R("Hunt_TowerShield").enabled
                  && !H.SwordTransform.GetComponent<Renderer>().enabled && !R("Round_Shield").enabled && lance.parent.name == "chest", lance.parent.name);
            var m = Fresh(Target());
            Face(m, 2.3f);
            Hold(PadButton.RB); yield return Wait(.2f); Release(); yield return Wait(.15f);    // raising the guard draws
            Check("raising the guard draws the lance into the hand", !F.Sheathed && lance.parent.name != "chest", lance.parent.name);
            yield return Snap("p11-lance-drawn");
            int landed = H.SwingsLanded;
            var seen = new List<string>();
            var presses = new Queue<PadButton>(new[] { PadButton.Y, PadButton.B, PadButton.Y });
            yield return Tap(presses.Dequeue());
            for (float t = 0; t < 2.5f; t += Time.unscaledDeltaTime)
            {
                if (seen.LastOrDefault() != CurrentId && CurrentId != "-") seen.Add(CurrentId);
                if (F.Current != null && HuntRoutes.CanChain(F.Current, H.Attack.Normalized) && presses.Count > 0)
                {
                    string before = CurrentId;
                    yield return Tap(presses.Dequeue(), 2);
                    yield return Until(() => CurrentId != before, .5f);       // one press per thrust (two within 70 ms are Y+B)
                    continue;
                }
                yield return null;
            }
            Check("Y, B, Y: mid thrust, high thrust, mid thrust (the triple thrust)", seen.Take(3).SequenceEqual(new[] { "mid_1", "high_2", "mid_3" }), string.Join(">", seen));
            Check("the lance's thrusts land from 2.3 m (longer reach than the sword)", H.SwingsLanded - landed >= 2, (H.SwingsLanded - landed) + " hits");
            yield return UntilNeutral();
            yield return Tap(PadButton.Y | PadButton.B);
            yield return Until(() => F.Current != null, .5f);
            Check("Y+B is the wide sweep", CurrentId == "wide_sweep", CurrentId);
            yield return UntilNeutral();

            // Hops: up to three in a row, the last one long; no roll while drawn.
            Face(m, 3f); yield return Frames(3);
            var dists = new List<float>();
            Vector3 facing0 = H.transform.forward;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = H.transform.position;
                yield return Tap(PadButton.A, 2);
                yield return Until(() => !H.Dodging, .8f);
                dists.Add(Vector3.Distance(Flat(p0), Flat(H.transform.position)));
                yield return Frames(2);
            }
            Check("A hops instead of rolling: short (under 2.2 m), three in a row", dists[0] > .9f && dists[0] < 2.2f && dists[1] > .9f, string.Join(", ", dists.Select(d => d.ToString("0.00"))));
            Check("the third hop is the long one, and a fourth is refused until a pause", dists[2] > dists[0] * 1.3f && dists[3] < .3f, string.Join(", ", dists.Select(d => d.ToString("0.00"))));
            Check("hopping back keeps the lance facing the monster", Vector3.Angle(facing0, H.transform.forward) < 10, Vector3.Angle(facing0, H.transform.forward).ToString("0") + " degrees turned");
            yield return Wait(.6f);

            // Guard: the tower shield against the Warden.
            S.ProvingRig.ClearMonsters();
            var warden = S.ProvingRig.Spawn("grave_warden", true);
            warden.Passive = false; warden.Calm(1);
            warden.Brain.AttackFilter = a => a.Shape == AoeShape.None;   // fixture: melee swings only, for the guard checks (the boss moves have their own section)
            foreach (var a in warden.Def.attacks) a.reaction = "Knockback";
            H.Health = H.Stats[Stat.MaxHealth];
            Face(warden, 2.0f);
            Hold(PadButton.RB);
            float hpBefore = H.Health;
            yield return Until(() => F.LastEvent == "guarded" || F.LastEvent == "perfect guard", 9);
            var atk = warden.Brain.CurrentAttack;
            Check("the tower shield blocks the Warden with very little chip", (F.LastEvent == "guarded" || F.LastEvent == "perfect guard") && hpBefore - H.Health < (atk != null ? atk.damage * .1f : 99), F.LastEvent + ", lost " + (hpBefore - H.Health).ToString("0.0"));
            yield return Snap("p12-lance-guard");
            // Guard thrust (RB+Y) and guard dash (RB+Y with the stick).
            yield return Combo(PadButton.RB, PadButton.Y);
            yield return Until(() => F.Current != null, .4f);
            Check("RB+Y: guard thrust from behind the shield", CurrentId == "guard_thrust", CurrentId);
            yield return UntilNeutral();
            Face(warden, 3.6f); yield return Frames(2);    // room to close in (from 2 m the Warden's body stops the dash at once)
            Vector3 g0 = H.transform.position;
            Pad(PadButton.RB, new Vector2(0, 1)); yield return Frames(4);
            yield return Combo(PadButton.RB, PadButton.Y, 3, new Vector2(0, 1));
            yield return Until(() => F.Current != null, .4f);
            string dashId = CurrentId;
            yield return Until(() => F.Current == null, 1.5f);
            Check("RB+Y with the stick: guard dash, shield up while closing in", dashId == "guard_dash" && Vector3.Distance(Flat(g0), Flat(H.transform.position)) > 1.2f, dashId + ", " + Vector3.Distance(Flat(g0), Flat(H.transform.position)).ToString("0.0") + " m");
            Release();
            yield return Wait(.3f);
            // Counter stance: hold RB then B as the Warden winds up; the blocked blow becomes a Retribution Thrust.
            bool countered = false; int retributions = F.Retributions;
            for (int tries = 0; tries < 4 && !countered; tries++)
            {
                H.Health = H.Stats[Stat.MaxHealth];
                Face(warden, 2.0f);
                yield return Until(() => warden.Brain.Mode == MonsterMode.Telegraph && warden.Brain.CurrentAttack.telegraph - warden.Brain.ModeTime < .55f, 8);
                Hold(PadButton.RB); yield return Frames(2);
                Hold(PadButton.RB | PadButton.B);
                yield return Until(() => F.Retributions > retributions || H.Hurt, 1.4f);
                countered = F.Retributions > retributions;
                Release();
                yield return Until(() => F.Current != null, .3f);
                if (countered) { Check("RB+B counter stance: the blocked blow is answered by the Retribution Thrust", CurrentId == "retribution" || F.LastMove == "Retribution Thrust", CurrentId + " / " + F.LastMove); yield return Snap("p13-lance-retribution"); }
                yield return UntilNeutral();
            }
            Check("the counter stance catches a real Warden blow", countered, F.LastEvent);
            // Released without a blow: the counter thrust.
            Hold(PadButton.RB); yield return Frames(3);
            yield return Combo(PadButton.RB, PadButton.B, 3);
            yield return Wait(.15f);                     // the shield stays up past the 70 ms two-button window
            Release();
            yield return Until(() => F.Current != null, .5f);
            Check("a quick RB+B tap releases the counter thrust", CurrentId == "counter_thrust", CurrentId);
            yield return UntilNeutral();

            // Dash attack (RB+Y+B): charges forward striking repeatedly, Y finishes.
            S.ProvingRig.ClearMonsters();
            yield return Frames(2);
            var spot = S.World.ProvingOrigin + new Vector3(0, 0, -14);
            H.Teleport(S.SafeGround(spot), 0); S.CameraRig.Yaw = 0; S.CameraRig.Snap();     // facing the empty middle of the ring
            yield return Frames(3);
            H.Stamina.Current = H.Stamina.Max;
            Vector3 d0 = H.transform.position; float st0 = H.Stamina.Current;
            Hold(PadButton.RB); yield return Frames(3);
            yield return Combo(PadButton.RB, PadButton.Y | PadButton.B, 3);
            Release();
            yield return Until(() => F.Current != null, .4f);
            string dash = CurrentId;
            yield return Wait(1.3f);
            float travelled = Vector3.Distance(Flat(d0), Flat(H.transform.position));
            Check("RB+Y+B: dash attack charges forward (over 4 m in 1.3 s) and costs stamina", dash == "dash_attack" && travelled > 4f && H.Stamina.Current < st0 - 8, dash + ", " + travelled.ToString("0.0") + " m, stamina " + st0.ToString("0") + " -> " + H.Stamina.Current.ToString("0"));
            yield return UntilNeutral();
            S.ProvingRig.ClearMonsters();               // the Grave Warden from the counter test would draw the charge
            yield return Frames(2);
            m = Fresh(Target());
            Face(m, 3.5f);
            yield return Frames(3);
            int lane0 = S.Hound ? S.Hound.LaneClears : 0;
            if (S.Hound) S.Hound.PlaceAt(S.SafeGround(H.transform.position + H.transform.forward * 1.6f + H.transform.right * .5f), H.transform.eulerAngles.y);   // in the way of the charge
            if (H.LockTarget != m) { if (H.LockTarget) yield return Tap(PadButton.RightStick); yield return Tap(PadButton.RightStick); }   // a player charging this monster locks on to it
            string scene = "lock " + (H.LockTarget ? H.LockTarget.DisplayName : "none") + ", live " + string.Join("/", S.Monsters.Where(x => x && x.Alive).Select(x => x.MonsterId + "@" + Vector3.Distance(x.transform.position, H.transform.position).ToString("0.0")))
                + ", facing " + Vector3.Angle(H.transform.forward, Flat(m.transform.position - H.transform.position)).ToString("0");
            H.Stamina.Current = H.Stamina.Max; landed = H.SwingsLanded;
            Hold(PadButton.RB); yield return Frames(3);
            yield return Combo(PadButton.RB, PadButton.Y | PadButton.B, 3);
            Release();
            var path = new List<string>();
            for (float t = 0; t < 1.6f; t += Time.unscaledDeltaTime)
            {
                if (path.Count < 9 && (path.Count == 0 || t > path.Count * .18f))
                    path.Add(Vector3.Distance(Flat(H.transform.position), Flat(m.transform.position)).ToString("0.0") + (S.Hound ? "/h" + Vector3.Distance(Flat(H.transform.position), Flat(S.Hound.transform.position)).ToString("0.0") : "") + ":" + CurrentId + ":" + m.Brain.Mode);
                yield return null;
            }
            scene += "; distance to it / to the hound over the charge: " + string.Join(" ", path);
            yield return Snap("p14-lance-dash");
            Check("charging through a monster strikes it more than once on one pass", H.SwingsLanded - landed >= 2, (H.SwingsLanded - landed) + " hits in 1.6 s, " + Vector3.Distance(H.transform.position, m.transform.position).ToString("0.0") + " m from it; before: " + scene);
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            if (S.Hound) Check("the Briarhound, standing in the charge's path, steps out of the hunter's way", S.Hound.LaneClears > lane0, S.Hound.LastEvent);
            yield return Tap(PadButton.Y);
            yield return Until(() => CurrentId == "dash_finisher", .6f);
            Check("Y during the charge: finishing thrust", CurrentId == "dash_finisher", CurrentId);
            yield return UntilNeutral();
            // A toppled monster lies on the ground: the charge must still strike it (in Monster Hunter every attack lands on a downed monster).
            m = Fresh(Target()); m.Brain.Topple(3f);
            yield return Wait(.8f);                      // lying down
            Face(m, 3.5f); yield return Frames(3);
            if (H.LockTarget != m) { if (H.LockTarget) yield return Tap(PadButton.RightStick); yield return Tap(PadButton.RightStick); }
            H.Stamina.Current = H.Stamina.Max; landed = H.SwingsLanded;
            Hold(PadButton.RB); yield return Frames(3);
            yield return Combo(PadButton.RB, PadButton.Y | PadButton.B, 3);
            Release();
            yield return Wait(1.4f);
            Check("charging into a toppled monster strikes it lying down", H.SwingsLanded - landed >= 1, (H.SwingsLanded - landed) + " hits, monster " + m.Brain.Mode);
            if (H.LockTarget) yield return Tap(PadButton.RightStick);
            yield return UntilNeutral();
            Note("lance: hops " + string.Join(", ", dists.Select(d => d.ToString("0.00"))) + " m; dash " + travelled.ToString("0.0") + " m");
        }

        // ---------------- bow ----------------
        IEnumerator BowChecks()
        {
            S.ProvingRig.ClearMonsters();
            yield return Frames(2);
            S.ProvingRig.SetWeapon("bow");
            yield return Frames(3);
            Check("the ledger puts the bow in hand and the quiver on the back", F.Class == WeaponClass.Bow && R("Hunt_Bow").enabled && R("Hunt_Quiver").enabled && !R("Hunt_Lance").enabled && !H.SwordTransform.GetComponent<Renderer>().enabled);
            var m = Fresh(Target());
            Face(m, 10f);
            yield return Frames(3);
            int fired = ArrowShot.Fired, hits = ArrowShot.Landed;
            yield return Tap(PadButton.Y);
            yield return Until(() => ArrowShot.Fired > fired, 1f);
            yield return Wait(.5f);
            Check("Y from sheathed: draw shot, an arrow flies and strikes the monster 10 m away", ArrowShot.Fired == fired + 1 && ArrowShot.Landed > hits, (ArrowShot.Fired - fired) + " fired, " + (ArrowShot.Landed - hits) + " hit");
            // Quick follow-up shots climb the levels: 1 -> 2 -> 3 arrows.
            var levels = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                yield return Until(() => F.Current == null || HuntRoutes.CanChain(F.Current, H.Attack.Normalized), 1);
                int before = ArrowShot.Fired;
                yield return Tap(PadButton.Y, 2);
                yield return Until(() => ArrowShot.Fired > before, 1f);
                levels.Add("L" + F.LastShotLevel + "x" + F.LastShotArrows);
            }
            Check("quick follow-up shots rise a level each: 2 arrows, then 3", levels.SequenceEqual(new[] { "L2x2", "L3x3" }), string.Join(", ", levels));
            yield return Wait(1.4f);
            Check("after a pause the bow is back at level 1", F.BowLevel == 1, F.BowLevel.ToString());
            // Holding Y draws: levels climb with time and the draw costs stamina; release shoots.
            H.Stamina.Current = H.Stamina.Max;
            Hold(PadButton.Y);
            yield return Until(() => F.Drawing, .5f);
            yield return Wait(1.05f);
            int heldLevel = F.BowLevel; float drawStamina = H.Stamina.Current;
            yield return Snap("p15-bow-draw");
            Release();
            yield return Until(() => F.Current != null, .5f);
            yield return Wait(.3f);
            Check("holding Y draws through the charge levels (level 3 after a second)", heldLevel == 3 && F.LastShotLevel == 3 && F.LastShotArrows == 3, "held level " + heldLevel + ", shot L" + F.LastShotLevel + "x" + F.LastShotArrows);
            Check("drawing costs stamina", drawStamina < H.Stamina.Max - 5, drawStamina.ToString("0"));
            yield return Wait(1.4f);
            // Charging sidestep: +1 level and a quick side step.
            Vector3 s0 = H.transform.position, sf = H.transform.forward;
            yield return Tap(PadButton.A, 2);
            yield return Until(() => !H.Dodging, .8f);
            float step = Vector3.Distance(Flat(s0), Flat(H.transform.position));
            Check("the sidestep keeps the archer facing the target", Vector3.Angle(sf, H.transform.forward) < 10, Vector3.Angle(sf, H.transform.forward).ToString("0") + " degrees turned");
            Check("A with the bow drawn: charging sidestep (+1 level, about 2.4 m)", F.LastEvent.StartsWith("charging sidestep") && F.BowLevel == 2 && step > 1.5f && step < 3.2f, F.LastEvent + ", level " + F.BowLevel + ", " + step.ToString("0.00") + " m");
            int f2 = ArrowShot.Fired;
            yield return Tap(PadButton.Y, 2);
            yield return Until(() => ArrowShot.Fired > f2, 1f);
            Check("the shot after a sidestep fires at the raised level", F.LastShotLevel == 2, "L" + F.LastShotLevel);
            // Power shot: B right after a shot: a wider fan at the current level, no level gained.
            yield return Until(() => F.Current == null || HuntRoutes.CanChain(F.Current, H.Attack.Normalized), 1);
            int f3 = ArrowShot.Fired; int lv = F.BowLevel;
            yield return Tap(PadButton.B, 2);
            yield return Until(() => ArrowShot.Fired > f3, 1f);
            Check("B right after a shot: power shot (two extra arrows in a wider fan)", F.LastMove == "Fan Volley" && F.LastShotArrows == Set().Level(lv).arrows + 2, F.LastMove + " x" + F.LastShotArrows + " at level " + lv);
            yield return Wait(1.4f);
            // Arc shot: B on its own: arrows rain on the locked target after a moment and build stun.
            m = Fresh(m);
            Face(m, 9f); yield return Frames(3);
            yield return Tap(PadButton.RightStick);
            Check("R3 locks on to the monster for the arc shot", H.LockTarget == m);
            float stun = m.Reactions.StunBuild; int h4 = ArrowShot.Landed;
            yield return Tap(PadButton.B, 2);
            yield return Until(() => F.Current != null, .4f);
            string arc = CurrentId;
            yield return Wait(1.0f);
            yield return Snap("p16-arc-shot");
            yield return Wait(1.0f);
            Check("B alone: arc shot, arrows rain down on the monster", arc == "arc_shot" && ArrowShot.Landed - h4 >= 2, arc + ", " + (ArrowShot.Landed - h4) + " hits");
            Check("the arc shot builds stun toward a topple", m.Reactions.StunBuild > stun || m.Reactions.Topples > 0, stun.ToString("0") + " -> " + m.Reactions.StunBuild.ToString("0"));
            yield return UntilNeutral();
            // Dragon Piercer: Y+B: a long windup, then one bolt that strikes again and again as it passes through.
            m = Fresh(m);
            Face(m, 8f); yield return Frames(3);
            int h5 = ArrowShot.Landed;
            yield return Tap(PadButton.Y | PadButton.B);
            yield return Until(() => F.Current != null, .4f);
            string dp = CurrentId;
            yield return Wait(.9f);
            yield return Snap("p17-dragon-piercer");
            yield return Wait(1.2f);
            Check("Y+B: Dragon Piercer pierces and hits several times with one bolt", dp == "dragon_piercer" && ArrowShot.Landed - h5 >= 3, dp + ", " + (ArrowShot.Landed - h5) + " hits");
            yield return UntilNeutral();
            if (H.LockTarget) yield return Tap(PadButton.RightStick);     // free aim next
            // Aim: RB brings the camera over the shoulder; arrows go to the centre of the screen.
            m = Fresh(m);
            Face(m, 9f); yield return Frames(3);
            Hold(PadButton.RB);
            yield return Wait(.15f);
            // Put the crosshair on the monster the way a player nudges the stick: from where the camera actually is.
            Vector3 toM = Vector3.forward;
            for (int i = 0; i < 40; i++)
            {
                var cp = S.CameraRig.Camera.transform.position;
                toM = ((m.AimPoint ? m.AimPoint.position : m.transform.position + Vector3.up) - cp).normalized;
                S.CameraRig.Yaw = Mathf.Atan2(-toM.x, toM.z) * Mathf.Rad2Deg; S.CameraRig.Pitch = Mathf.Asin(-toM.y) * Mathf.Rad2Deg;
                yield return null;
            }
            yield return Wait(.2f);
            float camDist = Vector3.Distance(S.CameraRig.Camera.transform.position, H.transform.position + Vector3.up * 1.5f);
            Check("RB aims: the camera comes in over the shoulder", F.Aiming && S.CameraRig.Aiming && camDist < 4f, camDist.ToString("0.0") + " m");
            yield return Snap("p18-bow-aim");
            int h6 = ArrowShot.Landed;
            yield return Combo(PadButton.RB, PadButton.Y, 2);
            yield return Wait(.6f);
            var camT = S.CameraRig.Camera.transform;
            Check("a shot while aiming flies to the reticle and hits what it shows", ArrowShot.Landed > h6, "aim point " + F.LastAimPoint.ToString("0.0") + ", monster at " + m.transform.position.ToString("0.0")
                  + ", hunter " + H.transform.position.ToString("0.0") + ", camera " + camT.position.ToString("0.0") + " fwd " + camT.forward.ToString("0.00") + " yaw " + S.CameraRig.Yaw.ToString("0") + " pitch " + S.CameraRig.Pitch.ToString("0") + " wanted " + toM.ToString("0.00"));
            Release();
            yield return Wait(.3f);
            Check("letting go of RB ends the aim", !F.Aiming && !S.CameraRig.Aiming);
            Note("bow: arrows fired " + ArrowShot.Fired + ", hits " + ArrowShot.Landed);
            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(3);
        }

        // ---------------- Training Range: evasions, bow clips and archery, katana, hazards, dummies ----------------
        void Place(string anchor, float yaw, Vector3 offset = default)
        {
            var a = S.World.Anchor(anchor);
            H.Teleport(S.SafeGround(a.position + offset), yaw);
            S.CameraRig.Pitch = 20; LookAlong(yaw);
        }

        /// <summary>Evade with the stick held the same frame as A (as a player flicks the stick and presses).</summary>
        IEnumerator Evade(Vector2 stick, PadButton extra = PadButton.None)
        {
            Pad(PadButton.A | extra, stick); yield return Frames(2);
            Pad(extra, Vector2.zero); yield return Frames(1);
            yield return Until(() => !H.Dodging, 1.2f);
        }

        RangeHazard Hazard(string name) => RangeHazard.All.FirstOrDefault(h => h && h.name == name);

        /// <summary>Points the camera (and so the crosshair) at a world point from where the camera actually is.</summary>
        IEnumerator AimAt(Vector3 point)
        {
            for (int i = 0; i < 40; i++)
            {
                var d = (point - S.CameraRig.Camera.transform.position).normalized;
                S.CameraRig.Yaw = Mathf.Atan2(-d.x, d.z) * Mathf.Rad2Deg; S.CameraRig.Pitch = Mathf.Asin(-d.y) * Mathf.Rad2Deg;
                yield return null;
            }
        }

        IEnumerator RangeChecks()
        {
            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(2);
            S.SwitchTestArea(true);
            yield return Wait(1.6f);
            Check("the ledger takes the hunter to the Training Range (still unsaved test rules)", S.InRange && S.Proving && S.AreaAt(H.transform.position) == "range" && S.World.IsInRange(H.transform.position), S.AreaAt(H.transform.position));
            var dummies = S.ProvingRig.Dummies.Where(d => d && d.Alive).ToList();
            Check("the range has four archery targets, three hazards and two passive dummies", RangeTarget.All.Count == 4 && RangeHazard.All.Count == 3 && dummies.Count == 2 && dummies.All(d => d.Passive),
                  RangeTarget.All.Count + " targets, " + RangeHazard.All.Count + " hazards, " + dummies.Count + " dummies");
            yield return Snap("r01-training-range");

            // Evasions with the sword and shield drawn, on the measured dodge lane (facing north, the camera behind).
            Place("range_lane", 0);
            Hold(PadButton.RB); yield return Wait(.2f); Release(); yield return Wait(.3f);
            var evades = new List<string>();
            foreach (var (stick, want, label) in new[] { (new Vector2(1, 0), EvadeKind.SideSlide, "right"), (new Vector2(-1, 0), EvadeKind.SideSlide, "left"), (new Vector2(0, -1), EvadeKind.Backstep, "back") })
            {
                H.Stamina.Current = H.Stamina.Max;
                Vector3 p0 = H.transform.position, f0 = H.transform.forward; int before = F.Evades;
                bool sawClip = false, sawFrames = false;
                Pad(PadButton.A, stick); yield return Frames(2);
                sawClip = H.Anim.Current.Contains(want == EvadeKind.SideSlide ? (stick.x > 0 ? "Dodge_Right" : "Dodge_Left") : "Dodge_Backward");
                Pad(PadButton.None); yield return Frames(1);
                while (H.Dodging) { sawFrames |= H.Invulnerable; yield return null; }
                Vector3 d = H.transform.position - p0; d.y = 0;
                float along = want == EvadeKind.SideSlide ? Vector3.Dot(d, Vector3.right * Mathf.Sign(stick.x)) : Vector3.Dot(d, Vector3.back);
                float turned = Vector3.Angle(f0, H.transform.forward);
                evades.Add(label + " " + along.ToString("0.00") + " m");
                Check("weapon out, stick " + label + " + A: " + (want == EvadeKind.SideSlide ? "side slide" : "back-step") + " that keeps facing forward",
                      F.Evades == before + 1 && F.LastEvade == want && sawClip && turned < 8 && along > (want == EvadeKind.Backstep ? 1.2f : 1.7f) && along < 3f && sawFrames,
                      F.LastEvade + ", " + along.ToString("0.00") + " m, turned " + turned.ToString("0") + ", clip " + H.Anim.Current + ", i-frames " + sawFrames);
                yield return Wait(.25f);
            }
            {
                H.Stamina.Current = H.Stamina.Max;
                Vector3 p0 = H.transform.position;
                yield return Evade(new Vector2(0, 1));
                float dist = Vector3.Distance(Flat(p0), Flat(H.transform.position));
                Check("stick forward + A: the full roll (about 3 m)", F.LastEvade == EvadeKind.Roll && dist > 2.4f && dist < 3.6f, F.LastEvade + ", " + dist.ToString("0.00") + " m");
                evades.Add("roll " + dist.ToString("0.00") + " m");
            }
            yield return Snap("r02-dodge-lane");
            // Dive: sprinting away from a monster close by.
            var small = dummies.FirstOrDefault(x => x.MonsterId == "gravebound") ?? dummies[0];
            H.Teleport(S.SafeGround(small.transform.position - Vector3.forward * 3.5f), 180);
            S.CameraRig.Yaw = 180; S.CameraRig.Snap();
            H.Stamina.Current = H.Stamina.Max;
            Pad(PadButton.RT, new Vector2(0, 1));       // run south, away from the dummy (the camera looks south)
            yield return Wait(1.0f);
            bool sprint = F.Sprinting && F.Sheathed;
            Vector3 dv0 = H.transform.position;
            Pad(PadButton.RT | PadButton.A, new Vector2(0, 1)); yield return Frames(2);
            Pad(PadButton.None); yield return Frames(1);
            float diveIframes = 0;
            while (H.Dodging) { if (H.Invulnerable) diveIframes += Time.deltaTime; yield return null; }
            float dive = Vector3.Distance(Flat(dv0), Flat(H.transform.position));
            bool landing = F.Recovering;
            yield return Snap("r03-dive-landing");
            Check("sprinting away from a monster, A dives: long (over 3.8 m) with a long invulnerable window, then a landing", sprint && F.LastEvade == EvadeKind.Dive && dive > 3.8f && diveIframes > .4f && landing,
                  "sprinting " + sprint + ", " + F.LastEvade + ", " + dive.ToString("0.00") + " m, i-frames " + diveIframes.ToString("0.00") + " s, landing " + landing);
            yield return Until(() => !F.Recovering, 1);
            Note("evasions: " + string.Join(", ", evades) + ", dive " + dive.ToString("0.00") + " m");

            // Hazards: standing in a slam pad is a hit; rolling through the blow evades it (the i-frames).
            var quick = Hazard("Quick slam pad");
            H.Health = H.Stats[Stat.MaxHealth];
            H.Teleport(S.SafeGround(quick.transform.position), 0); S.CameraRig.Yaw = 0; S.CameraRig.Snap();
            int struck = quick.Struck, evaded = quick.Evaded; float hp = H.Health;
            yield return Until(() => quick.Struck > struck, quick.Period + .5f);
            Check("a slam pad strikes the hunter standing in it (a flinch, health lost)", quick.Struck == struck + 1 && H.Health < hp, (hp - H.Health).ToString("0.0") + " damage");
            yield return UntilNeutral();
            // Early: the roll starts a quarter second before the blow: evaded by the i-frames, no perfect-evade bonus.
            H.Teleport(S.SafeGround(quick.transform.position), 0);
            yield return Until(() => quick.UntilBlow < .26f && quick.UntilBlow > .2f, quick.Period + .5f);
            hp = H.Health; int perfect0 = H.PerfectEvades;
            yield return Tap(PadButton.A, 2);
            yield return Wait(.4f);
            Check("rolling early through the slam evades it (no damage), but it is not a perfect evade", quick.Evaded == evaded + 1 && H.Health >= hp - .01f && H.PerfectEvades == perfect0,
                  quick.Evaded - evaded + " evaded, " + (hp - H.Health).ToString("0.0") + " damage, perfect " + (H.PerfectEvades - perfect0));
            yield return UntilNeutral();
            // Last moment: the blow lands right as the roll starts: perfect evade, slow motion and stamina back.
            H.Teleport(S.SafeGround(quick.transform.position), 0);
            H.Stamina.Current = 60;
            yield return Until(() => quick.UntilBlow < .07f, quick.Period + .5f);
            hp = H.Health;
            yield return Tap(PadButton.A, 2);
            float slowest = Time.timeScale, staminaAfter = 0;
            for (float t = 0; t < .5f; t += Time.unscaledDeltaTime)
            {
                slowest = Mathf.Min(slowest, Time.timeScale);
                if (H.PerfectEvades > perfect0 && staminaAfter == 0) staminaAfter = H.Stamina.Current;
                yield return null;
            }
            yield return Snap("r02b-perfect-evade");
            Check("rolling at the last moment is a perfect evade: no damage, time slows, stamina comes back", H.PerfectEvades == perfect0 + 1 && H.Health >= hp - .01f && slowest < .5f && staminaAfter > 60 - 25 + 15,
                  "perfect " + (H.PerfectEvades - perfect0) + ", time scale down to " + slowest.ToString("0.00") + ", stamina 60 -> roll -25 -> " + staminaAfter.ToString("0"));
            yield return Wait(.6f);
            Check("the slow motion ends by itself", Time.timeScale == 1 && !TimeWarp.Active, Time.timeScale.ToString("0.00"));
            yield return UntilNeutral();
            var log = Hazard("Sweeping log");
            int logBlows = log.Blows;
            yield return Until(() => log.Blows > logBlows, log.Period + .5f);
            Check("the sweeping log telegraphs and sweeps its lane on a timer", log.Blows > logBlows);

            // Bow: held in the left hand, drawn to the cheek with an arrow nocked; archery targets.
            S.ProvingRig.SetWeapon("bow");
            yield return Frames(3);
            var bowT = WorldKit.Find(H.Visual, "Hunt_Bow");
            Check("the bow rides on the back when sheathed", F.Sheathed && bowT.parent.name == "chest", bowT.parent.name);
            Place("range_archery", 0);
            yield return Frames(3);
            yield return Tap(PadButton.Y);                 // from sheathed: the draw shot
            yield return Wait(1.3f);
            Hold(PadButton.Y);
            yield return Until(() => F.Drawing, .5f);
            yield return Wait(.5f);
            var nockR = R("Hunt_NockedArrow");
            Check("drawing: the bow in the left hand, the Bow_Draw clip, an arrow nocked", F.Drawing && bowT.parent.name.Contains(".l") && H.Anim.Current.Contains("Bow_Draw") && nockR && nockR.enabled,
                  bowT.parent.name + ", " + H.Anim.Current + ", nocked " + (nockR && nockR.enabled));
            yield return Snap("r04-bow-draw-range");
            Release();
            yield return Until(() => F.Current != null, .5f);
            Check("the release plays the bow's shot clip", H.Anim.Current.Contains("Bow_Shoot"), H.Anim.Current);
            yield return Wait(1.2f);
            Pad(PadButton.A, new Vector2(1, 0)); yield return Frames(2);
            string stepClip = H.Anim.Current;
            Pad(PadButton.None); yield return Until(() => !H.Dodging, 1);
            Check("the charging sidestep plays the bow's own step clip", F.LastEvade == EvadeKind.Sidestep && stepClip.Contains("Bow_Step_R"), F.LastEvade + ", " + stepClip);
            yield return Wait(1.0f);
            foreach (var dist in new[] { 10f, 30f })
            {
                var target = RangeTarget.All.First(t => Mathf.Approximately(t.Distance, dist));
                Place("range_archery", 0);
                yield return Frames(2);
                Hold(PadButton.RB);
                yield return Wait(.2f);
                yield return AimAt(target.Face.position + Vector3.up * (dist > 20 ? .35f : .1f));
                int hits = target.Hits;
                Hold(PadButton.RB | PadButton.Y); yield return Wait(1.0f);   // a full draw
                Hold(PadButton.RB); yield return Wait(.9f);
                Release(); yield return Wait(.3f);
                Check("aimed full-draw shot hits the " + dist + " m target", target.Hits > hits, target.Hits - hits + " hits, gold " + target.Golds);
                if (dist == 30) yield return Snap("r05-archery-30m");
            }

            // Katana: spirit gauge, spirit combo to the roundslash level-up, foresight and iai counters on the hazards.
            S.ProvingRig.SetWeapon("katana");
            yield return Frames(3);
            var kat = WorldKit.Find(H.Visual, "Hunt_Katana");
            Check("the ledger puts the two-handed katana on the back (spirit gauge empty)", F.Class == WeaponClass.Katana && F.Sheathed && kat.parent.name == "chest" && R("Hunt_Katana").enabled
                  && !H.SwordTransform.GetComponent<Renderer>().enabled && !R("Round_Shield").enabled && F.Spirit.Value == 0 && F.SpiritLevel == 0, kat.parent.name);
            Place("range_lane", 0);
            yield return Wait(.3f);
            yield return Snap("r05b-katana-sheathed");
            var dm = small; dm.Brain.Health = dm.Def.maxHealth;
            Face(dm, 2.2f);
            yield return Tap(PadButton.Y);
            yield return Until(() => F.Current != null, .5f);
            Check("Y from sheathed: draw slash, the katana in both hands", CurrentId == "draw_slash" && kat.parent.name != "chest", CurrentId + ", " + kat.parent.name);
            yield return Snap("r06-katana-draw");
            yield return UntilNeutral();
            // Fill the gauge with normal slashes (the spirit route costs about 60).
            var seenK = new List<string>();
            for (float t = 0; t < 9 && F.Spirit.Value < 70; t += Time.unscaledDeltaTime)
            {
                if (F.Current != null && seenK.LastOrDefault() != CurrentId) seenK.Add(CurrentId);
                if (F.Current == null || HuntRoutes.CanChain(F.Current, H.Attack.Normalized)) { string b = CurrentId; yield return Tap(F.Current != null && F.Current.id == "overhead" ? PadButton.B : PadButton.Y, 2); yield return Until(() => CurrentId != b, .6f); continue; }
                yield return null;
            }
            Check("katana slashes fill the spirit gauge", F.Spirit.Value >= 60, F.Spirit.Value.ToString("0") + " after " + string.Join(">", seenK.Take(8)));
            yield return UntilNeutral();
            // RB x4: Spirit Blade I, II, III, Spirit Roundslash; the landed roundslash raises the level.
            dm.Brain.Health = dm.Def.maxHealth;
            Face(dm, 2.2f); yield return Frames(2);
            var spiritSeen = new List<string>();
            int rbPresses = 0;
            yield return Tap(PadButton.RB, 2); rbPresses++;
            for (float t = 0; t < 5 && F.SpiritLevel == 0; t += Time.unscaledDeltaTime)
            {
                if (F.Current != null && spiritSeen.LastOrDefault() != CurrentId) spiritSeen.Add(CurrentId);
                if (rbPresses < 4 && F.Current != null && HuntRoutes.CanChain(F.Current, H.Attack.Normalized)) { string b = CurrentId; yield return Tap(PadButton.RB, 2); rbPresses++; yield return Until(() => CurrentId != b, .6f); continue; }
                yield return null;
            }
            yield return Wait(.3f);
            var glow = R("Hunt_KatanaGlow");
            Check("RB, RB, RB, RB: Spirit Blade I, II, III, then the Spirit Roundslash", spiritSeen.Take(4).SequenceEqual(new[] { "spirit_1", "spirit_2", "spirit_3", "roundslash" }), string.Join(">", spiritSeen));
            Check("the landed roundslash raises the spirit level and the edge glows", F.SpiritLevel == 1 && glow && glow.enabled, "level " + F.SpiritLevel + ", glow " + (glow && glow.enabled));
            yield return Snap("r07-spirit-level");
            yield return UntilNeutral();
            // Foresight Slash: RB+B just before the slow slam lands: step back through it, then the counter.
            var slow = Hazard("Slow slam pad");
            H.Teleport(S.SafeGround(slow.transform.position), 180); S.CameraRig.Yaw = 180; S.CameraRig.Snap();
            F.Spirit.Spend(Time.time, 100);
            int fc = F.ForesightCounters, slowEv = slow.Evaded;
            yield return Until(() => slow.UntilBlow < .12f && slow.UntilBlow > .04f, slow.Period + .5f);
            yield return Tap(PadButton.RB | PadButton.B, 2);
            yield return Until(() => F.Current != null, 1f);
            string fs = CurrentId;
            yield return Snap("r08-foresight-counter");
            Check("RB+B as the slam lands: Foresight step, the blow passes through, Foresight Counter, gauge full", F.ForesightCounters == fc + 1 && fs == "foresight_counter" && slow.Evaded == slowEv + 1 && F.Spirit.Value > 90,
                  fs + ", counters " + (F.ForesightCounters - fc) + ", evaded " + (slow.Evaded - slowEv) + ", gauge " + F.Spirit.Value.ToString("0"));
            yield return UntilNeutral();
            H.Teleport(S.SafeGround(slow.transform.position + Vector3.forward * 5), 180);   // off the pad: no blow coming
            yield return Wait(.3f);
            yield return Tap(PadButton.RB | PadButton.B, 2);
            yield return Until(() => F.Current != null, 1f);
            Check("RB+B with no blow coming: the plain Foresight Slash", CurrentId == "foresight_slash", CurrentId);
            yield return UntilNeutral();
            // Iai stance: RB+A; Y: Iai Slash (the gauge refills itself after it lands).
            dm.Brain.Health = dm.Def.maxHealth;
            Face(dm, 2.2f); yield return Frames(2);
            yield return Tap(PadButton.RB | PadButton.A, 2);
            yield return Frames(3);
            bool stance = F.IaiStance && H.Anim.Current.Contains("Katana_Iai"), noDodge = !H.Dodging;
            yield return Snap("r09-iai-stance");
            Check("RB+A: the iai stance (no roll)", stance && noDodge, H.Anim.Current);
            int landedK = H.SwingsLanded;
            yield return Tap(PadButton.Y, 2);
            yield return Until(() => F.Current != null, .5f);
            string iai = CurrentId;
            yield return Wait(.5f);
            Check("Y in the stance: Iai Slash; once it lands the spirit gauge refills by itself", iai == "iai_slash" && H.SwingsLanded > landedK && F.Spirit.IaiActive(Time.time), iai + ", hits " + (H.SwingsLanded - landedK) + ", iai " + F.Spirit.IaiActive(Time.time));
            yield return UntilNeutral();
            // Iai Spirit Slash: in the stance, RB as the slam comes: the blow is caught in the windup and countered.
            H.Teleport(S.SafeGround(slow.transform.position), 180); S.CameraRig.Yaw = 180; S.CameraRig.Snap();
            yield return Until(() => slow.UntilBlow < 1.6f && slow.UntilBlow > 1.2f, slow.Period + .5f);
            yield return Tap(PadButton.RB | PadButton.A, 2);
            yield return Until(() => slow.UntilBlow < .2f, 2.5f);
            int sc = F.SpiritCounters, lvl = F.SpiritLevel; hp = H.Health;
            yield return Tap(PadButton.RB, 2);
            yield return Until(() => F.SpiritCounters > sc, .6f);
            string isl = CurrentId;
            yield return Snap("r10-iai-spirit-counter");
            Check("RB in the stance as the slam lands: Iai Spirit Slash counters it (no damage, level up)", F.SpiritCounters == sc + 1 && isl == "iai_spirit" && F.SpiritLevel == Mathf.Min(3, lvl + 1) && H.Health >= hp - .01f,
                  isl + ", counters " + (F.SpiritCounters - sc) + ", level " + lvl + " -> " + F.SpiritLevel + ", damage " + (hp - H.Health).ToString("0.0"));
            yield return UntilNeutral();
            // Lateral Fade Slash: Y+B with the stick to the side.
            Place("range_lane", 0);
            yield return Frames(2);
            Vector3 l0 = H.transform.position;
            Pad(PadButton.Y | PadButton.B, new Vector2(1, 0)); yield return Frames(2); Pad(PadButton.None); yield return Wait(.5f);
            float lat = Vector3.Dot(Flat(H.transform.position - l0), Vector3.right);
            Check("Y+B with the stick to the side: Lateral Fade Slash slides about 2.5 m sideways", F.LastMove == "Sidelong Cut" && lat > 1.6f, F.LastEvent + ", " + lat.ToString("0.00") + " m");
            yield return UntilNeutral();

            // Dummies never fall.
            dm.Brain.Health = dm.Def.maxHealth * .2f;
            yield return Frames(3);
            // (Healed to full; the hound may already have bitten it again by the time this is read.)
            Check("the training dummies never fall (healed when low, and before any blow that would fell them)", dm.Alive && dm.Brain.Health >= dm.Def.maxHealth * .6f && dummies.All(d => d && d.Alive), dm.Brain.Health.ToString("0") + " / " + dm.Def.maxHealth.ToString("0"));
            Note("range: targets " + string.Join(", ", RangeTarget.All.OrderBy(t => t.Distance).Select(t => t.Distance + " m " + t.Hits)) + "; hazards " + string.Join(", ", RangeHazard.All.Select(h => h.name + " " + h.Blows + " blows, " + h.Evaded + " evaded, " + h.Struck + " struck")));

            S.ProvingRig.SetWeapon("sword_shield");
            yield return Frames(2);
            S.SwitchTestArea(false);
            yield return Wait(1.6f);
            Check("back to the Proving Grounds from the ledger (Gravebound at the centre again)", !S.InRange && S.World.IsInProving(H.transform.position) && S.ProvingRig.Spawned.Any(x => x && x.MonsterId == "gravebound"));
        }

        HuntMovesetDef Set() => F.Set;

        // ---------------- run ----------------
        IEnumerator Run()
        {
            int savesBefore = SaveFiles();
            InputRouter.UseVirtual = false;
            GamePad.Inject(PadButton.None);
            Check("title offers the Proving Grounds", S.Ui.Top is TitleScreen && S.Ui.Top.Root.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("Proving Grounds")));
            S.EnterProvingGrounds();
            yield return Wait(1.2f);
            Check("enters the Proving Grounds with hunt combat", S.Proving && H.HuntStyle && S.CurrentArea == "proving" && InputRouter.MouseLook, S.CurrentArea);
            Check("a passive training Gravebound stands in the ring", S.ProvingRig.Spawned.Any(m => m && m.Passive && m.HuntRules));
            Check("world monsters are not spawned here", S.Monsters.All(m => S.World.IsInProving(m.transform.position)), S.Monsters.Count.ToString());
            Check("starts sheathed with sword and shield on the back", F.Sheathed && H.SwordTransform.parent.name == "chest", H.SwordTransform.parent.name);
            Check("controller reports connected", GamePad.Connected);
            yield return Snap("p01-sheathed-arena");

            // Draw attack, then the light chain.
            var m = Target();
            Face(m, 1.9f);
            yield return Frames(5);
            var seen = new List<string>();
            bool hitstopSeen = false, monsterFrozen = false;
            yield return Tap(PadButton.Y);
            for (float t = 0; t < 3.2f; t += Time.unscaledDeltaTime)
            {
                if (seen.LastOrDefault() != CurrentId && CurrentId != "-") seen.Add(CurrentId);
                if (Time.time < H.HitstopUntil) hitstopSeen = true;
                if (m && m.Anim.Frozen) monsterFrozen = true;
                if (F.Current != null && HuntRoutes.CanChain(F.Current, H.Attack.Normalized) && seen.Count < 3) { yield return Tap(PadButton.Y, 2); continue; }
                yield return null;
            }
            Check("Y from sheathed draws with the draw slash", seen.FirstOrDefault() == "draw_slash", string.Join(">", seen));
            Check("light chain continues horizontal then overhead", seen.Count >= 3 && seen[1] == "slash_2" && seen[2] == "chop_3", string.Join(">", seen));
            Check("a landed blow freezes the hunter (hitstop)", hitstopSeen);
            Check("and freezes the monster for the same beat", monsterFrozen);
            Check("the moves cost no stamina", H.Stamina.Current >= H.Stamina.Max - .5f, H.Stamina.Current.ToString("0"));
            yield return UntilNeutral();

            // Charge: hold B through two levels, release a charged spin.
            m = Target(); Face(m, 2.0f);
            Hold(PadButton.B);
            yield return Wait(.35f);
            Check("holding B starts charging", F.Charging, F.LastEvent);
            yield return Wait(1.0f);
            Check("charge reaches level 2", F.ChargeLevel == 2, F.ChargeLevel.ToString());
            yield return Snap("p02-charging");
            Release();
            yield return Frames(4);
            Check("full charge releases the charged spinning slash", CurrentId == "spin_charged2" && Mathf.Abs(F.Current.damageMultiplier - 2.0f * 1.9f) < .01f, CurrentId);
            yield return UntilNeutral();

            // Y + B together: shield bash.
            m = Target(); Face(m, 1.6f);
            yield return Tap(PadButton.Y | PadButton.B);
            yield return Frames(3);
            Check("Y and B together is the shield bash", CurrentId == "shield_bash", CurrentId);
            yield return Snap("p03-shield-bash");
            yield return UntilNeutral();

            // Roll cancel windows: early in a heavy overhead the roll is refused; late it cancels.
            yield return Tap(PadButton.B, 2);
            yield return Until(() => CurrentId == "heavy_chop", .5f);
            Check("a tapped B is the uncharged heavy overhead", CurrentId == "heavy_chop", CurrentId);
            yield return Tap(PadButton.A, 2);
            Check("rolling early in a heavy attack is refused (committed)", !H.Dodging && CurrentId == "heavy_chop", CurrentId);
            yield return Until(() => F.Current == null || H.Attack.Normalized > .64f, 2);
            yield return Tap(PadButton.A, 2);
            Check("late in the swing the roll cancels it", H.Dodging, CurrentId);
            yield return Until(() => !H.Dodging, 1);
            yield return Tap(PadButton.Y, 2);
            yield return Until(() => F.Current != null, .4f);
            Check("Y right after a roll is the roll slash", CurrentId == "roll_slash", CurrentId);
            yield return UntilNeutral();

            // Lock-on and strafing: the feet travel the way the body moves.
            m = Target(); Face(m, 4f);
            yield return Tap(PadButton.RightStick);
            Check("R3 locks on", H.LockTarget == m);
            Hold(PadButton.None, new Vector2(1, 0));
            yield return Wait(.7f);
            var toM = m.transform.position - H.transform.position; toM.y = 0;
            Check("locked on, moving sideways plays the strafe", H.Anim.Current.Contains("Strafe"), H.Anim.Current);
            Check("while strafing the hunter keeps facing the target", Vector3.Angle(H.transform.forward, toM) < 30, Vector3.Angle(H.transform.forward, toM).ToString("0"));
            yield return Snap("p04-strafe-lock");
            Release();
            yield return Tap(PadButton.RightStick);

            // Sheathe and sprint.
            yield return Tap(PadButton.X);
            yield return Wait(.6f);
            Check("X sheathes: sword back on the back", F.Sheathed && H.SwordTransform.parent.name == "chest");
            Hold(PadButton.RT, new Vector2(0, 1));
            yield return Wait(.5f);
            Check("RT sprints while sheathed", F.Sprinting && (H.Anim.Current.Contains("Hunt_Sprint") || H.Anim.Current.Contains("Running")), H.Anim.Current);
            Release();
            yield return Wait(.3f);

            // Item: LB drinks the selected potion with the drink animation.
            H.Health = H.Stats[Stat.MaxHealth] * .5f;
            int potions = S.Core.Inventory.Count("potion_mooncap");
            float hp = H.Health;
            yield return Tap(PadButton.LB);
            yield return Frames(3);
            Check("LB drinks the selected potion (Use_Item)", S.Core.Inventory.Count("potion_mooncap") == potions - 1 && H.Health > hp && H.Anim.Current.Contains("Use_Item"), H.Anim.Current);
            yield return Wait(1.1f);

            // Guard against a live Grave Warden, then a perfect guard and the counter.
            S.ProvingRig.ClearMonsters();
            var warden = S.ProvingRig.Spawn("grave_warden", true);
            warden.Passive = false; warden.Calm(1);
            warden.Brain.AttackFilter = a => a.Shape == AoeShape.None;   // fixture: melee swings only, for the guard checks (the boss moves have their own section)
            H.Health = H.Stats[Stat.MaxHealth];
            Face(warden, 2.0f);
            Hold(PadButton.RB);
            float hpBefore = H.Health;
            yield return Until(() => F.LastEvent == "guarded" || F.LastEvent == "perfect guard", 9);
            bool guarded = F.LastEvent == "guarded" || F.LastEvent == "perfect guard";
            var atk = warden.Brain.CurrentAttack;
            Check("RB raises the shield and blocks the Warden's blow", guarded, F.LastEvent);
            Check("a guarded blow costs a little health, not the full hit", hpBefore - H.Health < (atk != null ? atk.damage * .4f : 99), (hpBefore - H.Health).ToString("0.0"));
            yield return Snap("p05-guard");
            Release();
            // Perfect guard: raise the shield in the last moment before the blow.
            bool perfect = false;
            for (int tries = 0; tries < 3 && !perfect; tries++)
            {
                Face(warden, 2.0f);
                yield return Until(() => warden.Brain.Mode == MonsterMode.Telegraph && warden.Brain.CurrentAttack.telegraph - warden.Brain.ModeTime < .07f, 8);
                Hold(PadButton.RB);
                yield return Until(() => F.LastEvent == "perfect guard" || F.LastEvent == "guarded" || F.LastReaction != HitReaction.None && H.Hurt, 1.5f);
                perfect = F.LastEvent == "perfect guard";
                Release();
                if (perfect) { yield return Tap(PadButton.Y, 2); yield return Until(() => F.Current != null, .4f); }
                else yield return Wait(.8f);
            }
            Check("raising the guard just before the blow is a perfect guard", perfect, F.LastEvent);
            Check("Y after a perfect guard is the guard counter", perfect && (CurrentId == "counter" || F.LastMove == "Guard Counter"), CurrentId + " / " + F.LastMove);
            Check("a perfect guard fills the power clash meter", !perfect || F.ClashGauge.Perfects >= 1 || F.ClashGauge.Clashes > 0, F.ClashGauge.Perfects + "/" + F.ClashGauge.Need);
            yield return UntilNeutral();

            // Knockdown and get-up (fixture: the Warden's attacks set to knock down).
            foreach (var a in warden.Def.attacks) a.reaction = "Knockdown";
            Note("fixture: Grave Warden attacks set to Knockdown for the reaction check");
            H.Health = H.Stats[Stat.MaxHealth];
            Face(warden, 1.8f);
            yield return Until(() => F.Downed, 10);
            Check("an unguarded heavy blow knocks the hunter down", F.Downed && F.LastReaction == HitReaction.Knockdown, F.LastReaction.ToString());
            yield return Snap("p06-knockdown");
            yield return Until(() => F.LastEvent == "getting up", 2);
            Check("the hunter gets up with invulnerability", F.LastEvent == "getting up" && H.Invulnerable, F.LastEvent);
            foreach (var a in warden.Def.attacks) a.reaction = null;
            warden.Passive = true;
            yield return Wait(1.0f);

            // Monster reactions: flinch from damage, topple from stun, enrage below half health.
            S.ProvingRig.ClearMonsters();
            m = Target();
            Face(m, 1.3f);
            int flinches = m.Reactions.Flinches;
            for (int i = 0; i < 4 && m.Reactions.Flinches == flinches; i++) { yield return Tap(PadButton.Y); yield return UntilNeutral(); m.Brain.Health = m.Def.maxHealth; Face(m, 1.3f); }
            Check("enough damage makes the monster flinch", m.Reactions.Flinches > flinches, m.Reactions.Flinches.ToString());
            bool downed = false;
            for (int i = 0; i < 4 && !downed; i++)
            {
                yield return Tap(PadButton.Y | PadButton.B);
                for (float t = 0; t < 1.2f && !downed; t += Time.unscaledDeltaTime) { downed = m.Brain.Mode == MonsterMode.Downed; yield return null; }
                m.Brain.Health = m.Def.maxHealth; Face(m, 1.6f);
            }
            Check("shield bashes build stun until the monster topples", downed, m.Reactions.StunBuild.ToString("0"));
            yield return Snap("p07-topple");
            yield return Until(() => m.Brain.Mode != MonsterMode.Downed, 5);
            m.Brain.Health = m.Def.maxHealth * .45f;
            int landed = H.SwingsLanded;
            for (int i = 0; i < 3 && !m.Reactions.Enraged; i++)
            {
                yield return UntilNeutral();
                Face(m, 1.3f);
                yield return Tap(PadButton.Y);
                yield return Until(() => F.Current != null, .5f);
                yield return UntilNeutral();
                if (m.Brain.Health <= 0) { m = Target(); m.Brain.Health = m.Def.maxHealth * .45f; }
            }
            Check("below half health the monster enrages (attacks sooner)", m.Reactions.Enraged && m.Brain.CooldownScale < 1,
                "hp " + m.Brain.Health.ToString("0") + ", blows landed " + (H.SwingsLanded - landed) + ", mode " + m.Brain.Mode + ", last move " + F.LastMove);

            // The Briarhound: follows, comes to the whistle, can be ridden at a gallop, bites the hunted monster.
            var hound = S.Hound;
            Check("the Briarhound is in the ring", hound, (S.Hound ? S.Hound.name + " (" + HoundCompanion.Model + ")" : "none"));
            if (hound)
            {
                // Release the lock from the enrage test: a locked target sends the hound to assist instead of heeling.
                if (H.LockTarget) yield return Tap(PadButton.RightStick);
                var spot = S.World.ProvingOrigin + new Vector3(-14, 0, -14);
                H.Teleport(S.SafeGround(spot), 45);
                yield return Until(() => Vector3.Distance(hound.transform.position, H.transform.position) < 5 * HoundCompanion.Size, 8);
                Check("the hound follows the hunter", Vector3.Distance(hound.transform.position, H.transform.position) < 5 * HoundCompanion.Size, Vector3.Distance(hound.transform.position, H.transform.position).ToString("0.0") + " m");
                yield return Tap(PadButton.DPadUp);
                Check("D-pad up whistles the hound", hound.LastEvent == "called" || hound.State == HoundState.Called, hound.LastEvent);
                yield return Wait(1.6f);
                // Mount: stand beside it and press X. Drawn, X sheathes first; sheathed, X mounts.
                var beside = hound.transform.position + hound.transform.right * 1.4f * HoundCompanion.Size;
                H.Teleport(S.SafeGround(beside), hound.transform.eulerAngles.y);
                F.SetSheathed(false);
                yield return Frames(4);
                Check("with the weapon drawn, X next to the hound does not offer to ride", !(S.Focus is HoundMount), S.Focus ? S.Focus.name : "none");
                yield return Tap(PadButton.X);
                yield return Wait(.6f);
                Check("so X sheathes there instead of mounting", F.Sheathed && !H.Riding);
                Check("standing beside the hound offers to ride it", S.Focus is HoundMount, S.Focus ? S.Focus.name : "none");
                yield return Tap(PadButton.X);
                yield return Frames(3);
                Check("X mounts: the hunter sits on the saddle", H.Riding && H.transform.parent == hound.Saddle && H.Anim.Current.Contains("Ride_Idle"), H.Anim.Current);
                Check("weapons go on the back to ride", F.Sheathed);
                yield return Snap("p08-riding");
                Vector3 start = hound.transform.position;
                Hold(PadButton.None, new Vector2(0, 1));
                yield return Wait(1.2f);
                Check("the stick walks the hound with its rider", Vector3.Distance(start, hound.transform.position) > .8f && H.transform.parent == hound.Saddle, Vector3.Distance(start, hound.transform.position).ToString("0.0") + " m");
                float top = 0;
                Hold(PadButton.RT, new Vector2(.3f, 1));
                var pawSamples = new List<(float height, float speed)>(); var bodySpeeds = new List<float>(); var seatGaps = new List<float>();
                var pawL = WorldKit.Find(hound.Visual, "fore_paw.l"); var pawR = WorldKit.Find(hound.Visual, "fore_paw.r");
                var hips = WorldKit.Find(H.Visual, "hips");
                Vector3 prevL = pawL.position, prevR = pawR.position, prevBody = hound.transform.position;
                for (float t = 0; t < 2.2f; t += Time.unscaledDeltaTime)
                {
                    top = Mathf.Max(top, hound.Speed);
                    yield return null;
                    float dt = Time.deltaTime;
                    if (dt <= 0 || t < .6f) { prevL = pawL.position; prevR = pawR.position; prevBody = hound.transform.position; continue; }
                    // The lower fore paw is the planted one; its ground speed should be near zero while the body travels.
                    var low = pawL.position.y < pawR.position.y ? pawL : pawR;
                    var prev = low == pawL ? prevL : prevR;
                    pawSamples.Add((low.position.y - S.GroundHeight(low.position), Vector3.Distance(Flat(low.position), Flat(prev)) / dt));
                    bodySpeeds.Add(Vector3.Distance(Flat(hound.transform.position), Flat(prevBody)) / dt);
                    if (hips && hound.Saddle) seatGaps.Add(Vector3.Distance(hips.position, hound.Saddle.position));
                    prevL = pawL.position; prevR = pawR.position; prevBody = hound.transform.position;
                }
                // Planted = within 3 cm of the lowest the paw gets during the gallop (the paw bone sits above the sole).
                float lowest = pawSamples.Count > 0 ? pawSamples.Min(p => p.height) : 0;
                var planted = pawSamples.Where(p => p.height <= lowest + .03f).Select(p => p.speed).ToList();
                float plantedMedian = Median(planted), bodyMedian = Median(bodySpeeds);
                Note("hound gallop: planted fore paw " + plantedMedian.ToString("0.00") + " m/s vs body " + bodyMedian.ToString("0.00") + " m/s (" + planted.Count + " planted samples)");
                Check("galloping, the planted paw does not skate (under 35% of body speed)", planted.Count > 5 && plantedMedian < bodyMedian * .35f, plantedMedian.ToString("0.00") + " vs " + bodyMedian.ToString("0.00"));
                float gapMin = seatGaps.Count > 0 ? seatGaps.Min() : 0, gapMax = seatGaps.Count > 0 ? seatGaps.Max() : 0;
                Note("rider hips to saddle bone: " + gapMin.ToString("0.000") + "-" + gapMax.ToString("0.000") + " m through the gallop");
                Check("the rider stays seated through the gallop (hips within 5 cm of a fixed seat)", seatGaps.Count > 0 && gapMax - gapMin < .05f && gapMax < .45f, gapMin.ToString("0.00") + "-" + gapMax.ToString("0.00"));
                yield return Snap("p09-gallop");
                Check("RT gallops: faster than the hunter can sprint (1.25x or more)", top > HunterController.SprintSpeed * 1.25f && hound.Anim.Current.Contains("Gallop"), top.ToString("0.0") + " m/s vs hunter sprint " + HunterController.SprintSpeed.ToString("0.0"));
                Release();
                yield return Wait(.6f);
                yield return Tap(PadButton.X);
                yield return Frames(3);
                Check("X dismounts beside the hound, back on foot", !H.Riding && H.Body.enabled && H.transform.parent != hound.Saddle, hound.LastEvent);
                // Assist: lock on to the training target and let the hound bite it.
                m = Target();
                Face(m, 3f);
                yield return Tap(PadButton.RightStick);
                int bites = hound.Bites;
                yield return Until(() => hound.Bites > bites, 10);
                Check("the hound bites the monster the hunter is locked on to", hound.Bites > bites, hound.LastEvent);
                yield return Snap("p10-hound-assist");
                yield return HoundFightChecks(hound, m);
                if (H.LockTarget) yield return Tap(PadButton.RightStick);
                yield return CaptureHoundPoses(hound);
            }

            yield return MovementChecks();
            yield return LanceChecks();
            yield return BowChecks();
            yield return BossChecks();
            yield return RangeChecks();
            yield return CaptureHunterPoses();
            yield return BattleChecks();
            yield return NewLookChecks();
            yield return SkeletonUpgradeChecks();

            // Menus on the pad: Menu opens pause, B backs out.
            yield return Tap(PadButton.Start);
            Check("Menu button opens the pause menu", S.Ui.Top is PauseScreen, S.Ui.Describe());
            yield return Tap(PadButton.B);
            Check("B closes it", S.Ui.Top == null, S.Ui.Describe());

            // Leave: nothing saved, classic controls restored.
            S.LeaveProvingGrounds();
            yield return Wait(.5f);
            Check("leaving returns to the title with classic controls", S.Mode == SessionMode.Title && !H.HuntStyle && !InputRouter.MouseLook && Cursor.lockState == CursorLockMode.None);
            Check("the Proving Grounds wrote no save files", SaveFiles() == savesBefore, savesBefore + " -> " + SaveFiles());
            var lanceR = WorldKit.Find(H.Visual, "Hunt_Lance").GetComponent<Renderer>(); var bowR = WorldKit.Find(H.Visual, "Hunt_Bow").GetComponent<Renderer>();
            var katR = WorldKit.Find(H.Visual, "Hunt_Katana").GetComponent<Renderer>();
            Check("back on the title the hunter holds sword and shield again (no lance, bow or katana)", H.SwordTransform.GetComponent<Renderer>().enabled && !lanceR.enabled && !bowR.enabled && !katR.enabled && H.SwordTransform.parent.name != "chest", H.SwordTransform.parent.name);
        }
    }
}
