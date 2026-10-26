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
                if (Time.realtimeSinceStartup - started > 300) { Check("verification finished in time", false, "timed out"); break; }
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
            S.CameraRig.Yaw = H.transform.eulerAngles.y; S.CameraRig.Snap();
        }

        MonsterActor Target()
        {
            var m = S.ProvingRig.Spawned.FirstOrDefault(x => x && x.Alive && x.MonsterId == "gravebound");
            if (!m) m = S.ProvingRig.Spawn("gravebound", true);
            m.Passive = true; m.Brain.Health = m.Def.maxHealth;
            return m;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static float Median(List<float> v) { if (v.Count == 0) return 0; var o = v.OrderBy(x => x).ToList(); return o[o.Count / 2]; }

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
            var states = new[] { ("Idle", "idle"), ("Walk", "walk"), ("Gallop", "run"), ("Bite", "attack"), ("Sit", "sit"), ("Howl", "howl"), ("Hit", "hit") };
            foreach (var (clip, label) in states)
            {
                var st = anim.State(clip); if (st == null) { Check("hound has the " + clip + " clip", false); continue; }
                anim.Anim.Stop(); st.enabled = true; st.weight = 1;
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
            Check("RT sprints while sheathed", F.Sprinting && H.Anim.Current.Contains("Running"), H.Anim.Current);
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
                Check("RT gallops: much faster than the hunter can run (1.7x or more)", top > H.RunSpeed * 1.7f && hound.Anim.Current.Contains("Gallop"), top.ToString("0.0") + " m/s vs run " + H.RunSpeed.ToString("0.0"));
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
                if (H.LockTarget) yield return Tap(PadButton.RightStick);
                yield return CaptureHoundPoses(hound);
            }

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
        }
    }
}
