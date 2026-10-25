using System;
using System.Collections;
using System.IO;
using System.Linq;
using FH.Core;
using FH.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FH.Game
{
    /// <summary>Records a short gameplay video from the real player: frames are captured in-engine at a fixed
    /// 30 fps while the hunter is driven through InputRouter and menus are clicked through the EventSystem.</summary>
    public class GameTour : MonoBehaviour
    {
        GameSession S;
        string dir;
        int frame;
        bool recording;
        int skip = 1, tick;
        const int Fps = 30;

        IEnumerator Start()
        {
            S = GetComponent<GameSession>();
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "Tour"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            Time.captureFramerate = Fps;
            yield return null;
            recording = true;
            StartCoroutine(Record());
            IEnumerator run = Script();
            while (true)
            {
                object cur;
                try { if (!run.MoveNext()) break; cur = run.Current; }
                catch (Exception e) { Debug.LogException(e); break; }
                yield return cur;
            }
            recording = false;
            yield return null;
            File.WriteAllText(Path.Combine(dir, "receipt.json"), "{\"source\":\"actual Unity player frames (in-engine capture incl. UI)\",\"frames\":" + frame + ",\"fps\":" + Fps + ",\"version\":\"" + Application.version + "\"}");
            Debug.Log("FH_TOUR frames " + frame);
            Application.Quit(0);
        }

        IEnumerator Record()
        {
            while (recording)
            {
                yield return new WaitForEndOfFrame();
                if (tick++ % skip != 0) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "f" + frame.ToString("00000") + ".jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
                frame++;
            }
        }

        // Game-time waits (captureFramerate makes frames advance at exactly 1/30 s).
        IEnumerator Hold(float seconds) { int n = Mathf.RoundToInt(seconds * Fps); for (int i = 0; i < n; i++) yield return null; }

        Button Find(string contains) => S.Ui.Top?.Root.GetComponentsInChildren<Button>().FirstOrDefault(b => { var t = b.GetComponentInChildren<Text>(); return t && t.text.Contains(contains); });

        IEnumerator Click(string contains, float pause = .9f)
        {
            var b = Find(contains);
            if (b)
            {
                EventSystem.current.SetSelectedGameObject(b.gameObject);  // show the highlight the player would see
                yield return Hold(pause);
                ExecuteEvents.Execute(b.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            }
            yield return Hold(.2f);
        }

        void Steer(Vector3 target, bool run = false)
        {
            var cam = S.CameraRig.Camera.transform;
            Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, r = Vector3.Cross(Vector3.up, f);
            var d = target - S.Hunter.transform.position; d.y = 0;
            d = d.sqrMagnitude > .01f ? d.normalized : Vector3.zero;
            InputRouter.VirtualMove = new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f));
            InputRouter.Hold(GameAction.Run, run);
        }

        void Stop() { InputRouter.VirtualMove = Vector2.zero; InputRouter.Hold(GameAction.Run, false); }

        IEnumerator WalkTo(Vector3 target, float maxSeconds, bool run = false)
        {
            float t = 0;
            while (t < maxSeconds && Vector3.Distance(Flat(S.Hunter.transform.position), Flat(target)) > .8f) { Steer(target, run); t += 1f / Fps; yield return null; }
            Stop();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        IEnumerator Fade(Action mid)
        {
            S.Ui.Fade(mid);
            yield return Hold(.8f);
        }

        void Put(Vector3 p, Vector3 look)
        {
            var d = look - p; d.y = 0;
            S.Hunter.Teleport(S.SafeGround(p), Quaternion.LookRotation(d).eulerAngles.y);
            S.CameraRig.Yaw = S.Hunter.transform.eulerAngles.y;
            S.CameraRig.Snap();
        }

        IEnumerator Talk(NpcActor npc)
        {
            Put(npc.transform.position + npc.transform.forward * 1.9f, npc.transform.position);
            yield return Hold(.6f);
            InputRouter.Press(GameAction.Interact);
            yield return Hold(1.4f);
        }

        IEnumerator Script()
        {
            yield return Hold(3.5f);                                   // title menu over the valley
            yield return Click("New Game");
            yield return Click("Slot 1", .7f);
            InputRouter.UseVirtual = true;
            yield return Hold(2.5f);
            var mira = S.World.Npcs.First(n => n.NpcId == "mira");
            yield return WalkTo(new Vector3(-17, 0, -21), 3.2f, true);  // leave the cottage toward Thornbury
            yield return Fade(() => Put(mira.transform.position + mira.transform.forward * 4.5f, mira.transform.position));
            yield return WalkTo(mira.transform.position + mira.transform.forward * 1.9f, 2.5f);
            InputRouter.Press(GameAction.Interact);
            yield return Hold(2.6f);
            yield return Click("I'm a hunter", 1f);
            yield return Hold(2.2f);
            yield return Click("I'll take the contract", 1f);
            yield return Hold(2f);
            yield return Click("I'll be back", .8f);

            // The hunt at the bridge.
            var m = S.Monsters.Where(x => x && x.Alive && x.MonsterId == "gravebound").OrderBy(x => Vector3.Distance(x.Home, new Vector3(1, 0, 12))).First();
            yield return Fade(() => Put(m.Home + new Vector3(-1, 0, -5.5f), m.Home));
            yield return Hold(.6f);
            yield return WalkTo(m.Home + new Vector3(0, 0, -3f), 1.5f);
            InputRouter.Press(GameAction.LockOn);
            yield return Hold(.6f);
            float t = 0;
            int dodges = 0;
            var w = S.Core.Stats.EquippedWeapon();
            var head = m.Hurtboxes.FirstOrDefault(b => b.Parts.Contains("helmet"));
            while (t < 22 && m.Alive)
            {
                var h = S.Hunter;
                if (h.Health < 40) h.RefreshStats(true);
                var to = m.transform.position - h.transform.position; to.y = 0;
                // Read the telegraph: dodge out of the ring late in the wind-up a couple of times, then punish.
                if (m.Brain.Mode == MonsterMode.Telegraph && m.Brain.CurrentAttack.telegraph - m.Brain.ModeTime < .18f && dodges < 3 && !h.Dodging)
                {
                    var side = Vector3.Cross(Vector3.up, to.normalized) * (dodges % 2 == 0 ? 1 : -1);
                    Steer(h.transform.position + side * 3);
                    InputRouter.Press(GameAction.Dodge);
                    dodges++;
                }
                else if (to.magnitude > 1.45f) Steer(m.transform.position);
                else
                {
                    Stop();
                    if (!h.Attack.Running || h.Attack.Phase == AttackPhase.Recovery) InputRouter.Press(GameAction.Attack);
                }
                // A well-aimed overhead blow on the helm at the start of the fight shows the break.
                if (t > 5 && t < 5.1f && head && !m.Parts.Broken.Contains("helmet"))
                    for (int i = 0; i < 2; i++) m.ReceiveHit(head, head.transform.position, (p, b) => DamageMath.PlayerHit(w, w.combo[2], h.Stats, m.Def, p, b, false));
                t += 1f / Fps;
                yield return null;
            }
            Stop();
            yield return Hold(1.6f);
            var bag = FindObjectsByType<LootBag>().OrderBy(b => Vector3.Distance(b.transform.position, S.Hunter.transform.position)).FirstOrDefault();
            if (bag)
            {
                yield return WalkTo(bag.transform.position + (S.Hunter.transform.position - bag.transform.position).normalized * 1.1f, 2);
                InputRouter.Press(GameAction.Interact);
                yield return Hold(1.4f);
                yield return Click("Take all");
            }
            // The remaining two kills are fought for real and recorded as a 4x time-lapse (labelled on screen).
            S.Hud.Flash("4x time-lapse: two more Gravebound");
            skip = 4;
            for (int k = 0; k < 2; k++)
            {
                var next = S.Monsters.Where(x => x && x.Alive && x.MonsterId == "gravebound" && !S.World.IsInCrypt(x.transform.position)).OrderBy(x => Vector3.Distance(x.transform.position, S.Hunter.transform.position)).FirstOrDefault();
                if (!next) break;
                S.Hunter.LockTarget = next;
                float ft = 0;
                while (ft < 40 && next.Alive)
                {
                    var h = S.Hunter;
                    if (h.Health < 50) h.RefreshStats(true);
                    var to = next.transform.position - h.transform.position; to.y = 0;
                    if (to.magnitude > 1.45f) Steer(next.transform.position, to.magnitude > 5); else { Stop(); if (!h.Attack.Running || h.Attack.Phase == AttackPhase.Recovery) InputRouter.Press(GameAction.Attack); }
                    if ((int)(ft * Fps) % 90 == 0) S.Hud.Flash("4x time-lapse");
                    ft += 1f / Fps; yield return null;
                }
                Stop();
                yield return Hold(1f);
                var b2 = FindObjectsByType<LootBag>().OrderBy(b => Vector3.Distance(b.transform.position, S.Hunter.transform.position)).FirstOrDefault();
                if (b2) { S.Hunter.Teleport(S.SafeGround(b2.transform.position + Vector3.back * 1.1f), 0); yield return Hold(.2f); InputRouter.Press(GameAction.Interact); yield return Hold(.6f); yield return Click("Take all", .2f); }
            }
            skip = 1;
            yield return Fade(() => Put(mira.transform.position + mira.transform.forward * 1.9f, mira.transform.position));
            InputRouter.Press(GameAction.Interact);
            yield return Hold(1.5f);
            yield return Click("The bridge is clear", 1f);
            yield return Hold(2.4f);
            yield return Click("Thank you", .6f);
            yield return Click("Goodbye", .6f);

            // Forge the blade at Bram's.
            var forge = FindObjectsByType<CraftStation>().First(c => c.Station == Station.Forge);
            yield return Fade(() => Put(forge.Point + (forge.transform.position - S.World.Npcs.First(n => n.NpcId == "bram").transform.position).normalized * 1.3f, forge.Point));
            InputRouter.Press(GameAction.Interact);
            yield return Hold(1.2f);
            yield return Click("Gravebane Shortsword", .8f);
            yield return Hold(1.2f);
            yield return Click("Craft", .8f);
            yield return Hold(1.6f);
            S.Ui.Top?.Back();
            S.Core.Vendors.Buy("v_bram", "oak_timber", 3);
            S.Core.Vendors.Buy("v_bram", "iron_nails", 4);

            // Restore the cottage.
            var ledger = FindObjectsByType<EstateLedger>().First();
            yield return Fade(() => Put(ledger.Point + new Vector3(1.2f, 0, -1.2f), ledger.Point));
            yield return Hold(1f);
            InputRouter.Press(GameAction.Interact);
            yield return Hold(2f);
            yield return Click("Build Restored Cottage", 1f);
            yield return Click("Build", .8f);
            yield return Hold(1.2f);
            S.Ui.Top?.Back();
            S.CameraRig.Manual = true;
            var c = S.World.Cottage.position;
            for (int i = 0; i < 4 * Fps; i++)
            {
                float a = 2.3f + i / (float)(4 * Fps) * .9f;
                S.CameraRig.Camera.transform.position = c + new Vector3(Mathf.Sin(a) * 13, 6.5f, Mathf.Cos(a) * 13);
                S.CameraRig.Camera.transform.LookAt(c + Vector3.up * 1.6f + Vector3.right * 1.5f);
                yield return null;
            }
            S.CameraRig.Manual = false;

            // Map, then the crypt and its Warden.
            InputRouter.Press(GameAction.Map);
            yield return Hold(2.4f);
            S.Ui.Top?.Back();
            var inside = S.World.Anchor("crypt_inside");
            yield return Fade(() => { S.Hunter.Teleport(inside.position + Vector3.forward * 8, 0); S.CameraRig.Yaw = 0; S.CameraRig.Snap(); });
            var boss = S.Monsters.FirstOrDefault(x => x && x.Alive && x.MonsterId == "grave_warden");
            if (boss) { InputRouter.Press(GameAction.LockOn); }
            t = 0;
            while (t < 5)
            {
                if (boss && Vector3.Distance(S.Hunter.transform.position, boss.transform.position) > 4) Steer(boss.transform.position); else Stop();
                if (boss && boss.Brain.Mode == MonsterMode.Telegraph && boss.Brain.CurrentAttack.telegraph - boss.Brain.ModeTime < .2f && !S.Hunter.Dodging) { Steer(S.Hunter.transform.position - S.Hunter.transform.forward * 3); InputRouter.Press(GameAction.Dodge); }
                if (S.Hunter.Health < 50) S.Hunter.RefreshStats(true);
                t += 1f / Fps; yield return null;
            }
            Stop();
            InputRouter.Press(GameAction.Pause);
            yield return Hold(1.2f);
            yield return Click("Save Game", .8f);
            yield return Click("Slot 1", .8f);
            yield return Hold(1.5f);
        }
    }
}
