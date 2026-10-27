using System;
using System.Linq;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FH.UI
{
    public static class Layout
    {
        /// <summary>Splits a body into a scrolling list on the left and a detail column on the right.</summary>
        public static void Split(RectTransform body, float leftFraction, out RectTransform list, out RectTransform detail)
        {
            var left = UiKit.Scroll("List", body, out list);
            left.Place(Vector2.zero, new Vector2(leftFraction, 1), Vector2.zero, new Vector2(-12, 0));
            var right = UiKit.Panel("Detail", body, UiKit.PanelLight, false).rectTransform.Place(new Vector2(leftFraction, 0), Vector2.one, new Vector2(12, 0), Vector2.zero);
            detail = UiKit.Column("Detail column", right, 10, 18);
            detail.Stretch();
            detail.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
        }

        public static UiButton Row(RectTransform list, string text, Action click, Action focus, bool selected = false)
        {
            var b = UiKit.Button(list, text, click, 52, 20);
            b.Text.alignment = TextAnchor.MiddleLeft;
            b.Text.rectTransform.Stretch(14);
            if (selected) b.Active();
            if (focus != null) UiHover.Attach(b, focus);
            return b;
        }

        public static void Clear(RectTransform r)
        {
            for (int i = r.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(r.GetChild(i).gameObject);
            r.DetachChildren();
        }

        public static Text Heading(Transform parent, string text) => UiKit.Paragraph(parent, text, 26, UiKit.Gold, FontStyle.Bold);
    }

    public class TitleScreen : UiScreen
    {
        public override string Title => "";
        public override bool Dim => false;
        public override bool ShowBackHint => false;
        public override Vector2 Size => new Vector2(560, 700);
        float t;

        public override void Build()
        {
            Root.GetComponentInChildren<Image>().rectTransform.Box(new Vector2(0, .5f), new Vector2(90, 0), Size);
            var col = UiKit.Column("Menu", Body, 12);
            col.Stretch();
            UiKit.Paragraph(col, "FOLKLORE HUNTER", 46, UiKit.Gold, FontStyle.Bold);
            UiKit.Paragraph(col, "Rise of the Estate", 26, UiKit.Text, FontStyle.Italic);
            UiKit.Paragraph(col, " ", 10);
            int recent = S.MostRecentSlot();
            var cont = UiKit.Button(col, "Continue", () => Continue(recent));
            cont.Enabled(recent > 0, recent > 0 ? null : "No saved game yet.");
            UiKit.Button(col, "New Game", () => Ui.Push(new SlotScreen(SlotMode.New)));
            var load = UiKit.Button(col, "Load Game", () => Ui.Push(new SlotScreen(SlotMode.Load)));
            load.Enabled(recent > 0, recent > 0 ? null : "No saved games.");
            UiKit.Button(col, "Proving Grounds  (combat test)", () => S.EnterProvingGrounds());
            UiKit.Button(col, "Training Range  (weapons and evasions)", () => S.EnterTrainingRange());
            UiKit.Button(col, "Settings", () => Ui.Push(new SettingsScreen()));
            UiKit.Button(col, "Controls", () => Ui.Push(new ControlsScreen()));
            UiKit.Button(col, "Quit", () => S.QuitGame(false));
            UiKit.Paragraph(col, "A low-poly folklore hunting RPG. Original content; KayKit and Kenney CC0 art.", 15, UiKit.Muted);
        }

        void Continue(int slot)
        {
            var r = S.LoadSlot(slot);
            if (!r.Ok) Ui.Push(new MessageScreen("Could not continue", r.Message, "Back"));
        }

        public override bool Back() => false;

        public override void Tick()
        {
            t += Time.unscaledDeltaTime;
            var cam = S.CameraRig.Camera.transform;
            var c = S.World.Cottage.position + Vector3.up * 2;
            float a = t * .05f + 2.6f;
            cam.position = c + new Vector3(Mathf.Sin(a) * 15, 7, Mathf.Cos(a) * 15);
            cam.LookAt(c);
        }
    }

    public enum SlotMode { New, Load, Save }

    public class SlotScreen : UiScreen
    {
        readonly SlotMode mode;
        public SlotScreen(SlotMode m) { mode = m; }
        public override string Title => mode == SlotMode.New ? "New Game - choose a slot" : mode == SlotMode.Load ? "Load Game" : "Save Game";
        public override Vector2 Size => new Vector2(980, 600);

        public override void Build()
        {
            var col = UiKit.Column("Slots", Body, 12);
            col.Stretch();
            for (int slot = 1; slot <= 3; slot++)
            {
                int s = slot;
                var sum = S.Store.Summary(slot, S.Db);
                string when = string.IsNullOrEmpty(sum.SavedAtUtc) ? "" : "   <color=#a9a59a>" + DateTime.Parse(sum.SavedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g") + "</color>";
                string label = "Slot " + slot + "   " + sum.Label + when;
                var b = UiKit.Button(col, label, () => Pick(s, sum), 64, 21);
                b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.Stretch(16);
                if (mode == SlotMode.Load) b.Enabled(sum.Exists && sum.Status != LoadStatus.Corrupt, !sum.Exists ? "Empty." : sum.Status == LoadStatus.Corrupt ? "This save is damaged and cannot be loaded." : null);
            }
            UiKit.Paragraph(col, mode == SlotMode.New ? "Starting a new game in a used slot will replace that save." : "Saves are written safely with a backup of the previous save.", 17, UiKit.Muted);
            UiKit.Button(col, "Back", () => Ui.Close(this));
        }

        void Pick(int slot, SlotSummary sum)
        {
            switch (mode)
            {
                case SlotMode.New:
                    if (sum.Exists) Ui.Confirm("Replace slot " + slot + "?", "The saved game in this slot will be replaced by a new game.", "Start new game", () => Start(slot));
                    else Start(slot);
                    break;
                case SlotMode.Load:
                    {
                        var r = S.LoadSlot(slot);
                        if (!r.Ok) Ui.Push(new MessageScreen("Could not load", r.Message, "Back"));
                        break;
                    }
                case SlotMode.Save:
                    if (sum.Exists && slot != S.Slot) Ui.Confirm("Overwrite slot " + slot + "?", "The saved game in this slot will be replaced.", "Overwrite", () => DoSave(slot));
                    else DoSave(slot);
                    break;
            }
        }

        void Start(int slot) => Ui.Push(new CharacterCreatorScreen(slot));

        void DoSave(int slot)
        {
            bool ok = S.Save(slot);
            Ui.Close(this);
            S.Hud.Notify(ok ? "Game saved to slot " + slot + "." : "Save failed.", ok ? NoticeKind.Info : NoticeKind.Warning);
        }
    }

    public class SettingsScreen : UiScreen
    {
        public override string Title => "Settings";
        public override Vector2 Size => new Vector2(900, 640);

        public override void Build()
        {
            var st = S.Settings;
            var col = UiKit.Column("Settings", Body, 10);
            col.Stretch();
            Stepper(col, "Master volume", Mathf.RoundToInt(st.masterVolume * 100) + "%", () => { st.masterVolume = Mathf.Clamp01(st.masterVolume - .1f); }, () => { st.masterVolume = Mathf.Clamp01(st.masterVolume + .1f); });
            Stepper(col, "Camera sensitivity", st.mouseSensitivity.ToString("0.0"), () => { st.mouseSensitivity = Mathf.Max(.2f, st.mouseSensitivity - .1f); }, () => { st.mouseSensitivity = Mathf.Min(3, st.mouseSensitivity + .1f); });
            Stepper(col, "Interface size", Mathf.RoundToInt(st.uiScale * 100) + "%", () => { st.uiScale = Mathf.Max(.75f, st.uiScale - .05f); }, () => { st.uiScale = Mathf.Min(1.5f, st.uiScale + .05f); });
            Toggle(col, "Invert camera vertical", st.invertY, () => st.invertY = !st.invertY);
            Toggle(col, "Damage numbers", st.showDamageNumbers, () => st.showDamageNumbers = !st.showDamageNumbers);
            Toggle(col, "Fullscreen", st.fullscreen, () => st.fullscreen = !st.fullscreen);
            UiKit.Button(col, "Done", () => Ui.Close(this));
        }

        void Stepper(RectTransform col, string label, string value, Action minus, Action plus)
        {
            var row = UiKit.Row(label, col, 10, 50);
            var l = UiKit.Label("Label", row, label, 22, UiKit.Text, TextAnchor.MiddleLeft);
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 3;
            UiKit.Button(row, "-", () => { minus(); S.SaveSettings(); Refresh(); }).Layout.flexibleWidth = .5f;
            var v = UiKit.Label("Value", row, value, 22, UiKit.Gold, TextAnchor.MiddleCenter);
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            UiKit.Button(row, "+", () => { plus(); S.SaveSettings(); Refresh(); }).Layout.flexibleWidth = .5f;
        }

        void Toggle(RectTransform col, string label, bool on, Action flip)
        {
            var b = UiKit.Button(col, label + ":  " + (on ? "On" : "Off"), () => { flip(); S.SaveSettings(); Refresh(); });
            b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.Stretch(16);
        }
    }

    public class PauseScreen : UiScreen
    {
        public override string Title => "Paused";
        public override Vector2 Size => new Vector2(560, 700);

        public override void Build()
        {
            var col = UiKit.Column("Pause", Body, 10);
            col.Stretch();
            UiKit.Button(col, "Resume", () => Ui.Close(this));
            if (S.Proving)
            {
                UiKit.Button(col, "Proving ledger", () => { Ui.Close(this); Ui.Push(new ProvingScreen()); });
                UiKit.Button(col, "Settings", () => Ui.Push(new SettingsScreen()));
                UiKit.Button(col, "Controls", () => Ui.Push(new ControlsScreen()));
                UiKit.Button(col, "Leave the " + S.TestAreaName, () => S.LeaveProvingGrounds());
                UiKit.Paragraph(col, "Nothing in the Proving Grounds or the Training Range is saved.", 16, UiKit.Muted);
                return;
            }
            bool fighting = S.InFight;
            UiKit.Button(col, "Save Game", () => Ui.Push(new SlotScreen(SlotMode.Save))).Enabled(!fighting, fighting ? "You cannot save in the middle of a fight." : null);
            UiKit.Button(col, "Load Game", () => Ui.Push(new SlotScreen(SlotMode.Load)));
            UiKit.Button(col, "Journal", () => { Ui.Close(this); Ui.Push(new JournalScreen(JournalTab.Hunts)); });
            UiKit.Button(col, "Settings", () => Ui.Push(new SettingsScreen()));
            UiKit.Button(col, "Controls", () => Ui.Push(new ControlsScreen()));
            UiKit.Button(col, "Save and quit to title", () => S.QuitToTitle(true)).Enabled(!fighting, fighting ? "Finish the fight first, or quit without saving." : null);
            UiKit.Button(col, "Quit to title without saving", () => Ui.Confirm("Quit without saving?", "Progress since your last save will be lost.", "Quit", () => S.QuitToTitle(false)));
            UiKit.Button(col, "Quit game", () => Ui.Confirm("Quit the game?", fighting ? "You are in a fight; progress since the last save will be lost." : "Your game will be saved first.", "Quit", () => S.QuitGame(!fighting)));
        }
    }

    public class ControlsScreen : UiScreen
    {
        public override string Title => "Controls";
        public override Vector2 Size => new Vector2(1240, 1000);
        public override void Build()
        {
            var col = UiKit.Column("Controls", Body, 8);
            col.Stretch();
            UiKit.Paragraph(col,
                "<color=#f5d68c>Valley (classic combat)</color>   keyboard  /  Xbox controller\n" +
                "<b>Move</b> WASD / left stick   <b>Run</b> Shift / RT   <b>Attack</b> Left mouse / Y (press in rhythm: 3-hit combo)\n" +
                "<b>Dodge</b> Space / A   <b>Spell</b> Q / LT   <b>Change spell</b> F / D-pad down   <b>Lock-on</b> Tab, middle mouse / R3\n" +
                "<b>Interact</b> E / X   <b>Items</b> 1-4, R / LB   <b>Camera</b> hold right mouse / right stick   <b>Zoom</b> wheel\n" +
                "<b>Pack</b> I   <b>Hunts</b> J   <b>Hunter</b> K   <b>Map</b> M / View   <b>Menu, back</b> Esc / Menu, B\n\n" +
                "<color=#f5d68c>Proving Grounds and Training Range (hunt combat)</color>   the mouse turns the camera; weapons chosen at the ledger\n" +
                "<b>All weapons</b>  Evade Space, A: roll; weapon out, stick to the side: side slide, stick back: back-step (both keep facing);\n" +
                "    sprinting away from a monster: dive (long, very safe, then a moment to get up)   Sprint Shift, RT (sheathes)   Sheathe X\n" +
                "    Evade at the last moment (the blow lands right as you press): perfect evade, time slows and stamina comes back\n" +
                "    Items 1-4, LB (D-pad picks)   Whistle G, D-pad up\n" +
                "    <b>Wounds</b>: keep striking one spot and a wound opens (red glow). Hold LT / Left Alt (weapon out): Hunter's Sight shows\n" +
                "    them bright; Y in Sight is your weapon's <b>Rend</b>: it leaps to the wound and tears it (big damage, the monster flinches,\n" +
                "    +1 Resolve: more damage, quicker stamina). Every third wound torn in a fight topples the monster. Tap LT for the spell.\n" +
                "<b>Sword and shield</b>  Light LMB, Y (slash, slash, overhead; forward: lunge)   Heavy RMB, B (hold: charge, level 2 spins)\n" +
                "    Y+B shield bash (stun)   Guard Ctrl, RB (perfect guard, then light: counter)   light right after a roll: roll slash\n" +
                "    Back-step then Y: Leaping Slash, then Y Thorn Rush I, II, III and the Shield Crash; press as the blade glints: perfect\n" +
                "    Perfect guards fill the standoff meter (sword and shield, lance): the next guarded blow is a STANDOFF, mash Y to win\n" +
                "<b>Lance</b>  Y mid thrust, B high thrust (mix up to three)   Y+B wide sweep   A hop (three in a row, the last is long)\n" +
                "    Guard RB (wide, little chip)  then Y guard thrust (stick: guard dash), B counter stance (a blocked blow: Retribution Thrust),\n" +
                "    Y+B dash attack (Y during it: finishing thrust)\n" +
                "<b>Bow</b>  Hold Y to draw (three charge levels), release to shoot; quick follow-up shots rise a level   A drawing step (+1 level)\n" +
                "    B after a shot: fan volley   B: rain volley (arrows rain on the target)   Y+B Skewer Shot   RB aim over the shoulder\n" +
                "    Its Rend is the Seeker Thorn: the arrow stays in; your arrows bend toward it and the eighth one bursts it\n" +
                "<b>Katana</b> (two-handed)  Y overhead, B thrust, then rising slash   Y+B fade slash (back; stick to the side: lateral fade)\n" +
                "    RB Ember Cut I, II, III, then the Ember Wheel: a landed Ember Wheel raises the ember level (white, yellow, red)\n" +
                "    Hits fill the ember gauge; ember moves spend it. RB+B Shadowstep Slash: step back, a blow caught in it is countered\n" +
                "    RB+A sheath stance: Y Sheath Draw (the gauge refills itself), RB Ember Draw (a blow in its windup is countered: level up)\n\n" +
                "<color=#f5d68c>Hunting tips</color>  Watch the raised weapon, red eyes and warning ring; roll through the blow, then punish.\n" +
                "Orange numbers mean a soft hitzone. Shield bashes and overheads build stun: a toppled monster is wide open.", 17);
            UiKit.Button(col, "Back", () => Ui.Close(this));
        }
    }

    public class ConfirmScreen : UiScreen
    {
        readonly string title, body, ok; readonly Action onOk;
        public ConfirmScreen(string t, string b, string o, Action a) { title = t; body = b; ok = o; onOk = a; }
        public override string Title => title;
        public override Vector2 Size => new Vector2(760, 380);
        public override void Build()
        {
            var col = UiKit.Column("Confirm", Body, 14);
            col.Stretch();
            UiKit.Paragraph(col, body, 22);
            var row = UiKit.Row("Buttons", col, 14, 54);
            UiKit.Button(row, "Cancel", () => Ui.Close(this));
            UiKit.Button(row, ok, () => { Ui.Close(this); onOk(); });
        }
    }

    public class MessageScreen : UiScreen
    {
        readonly string title, body, ok;
        public MessageScreen(string t, string b, string o) { title = t; body = b; ok = o; }
        public override string Title => title;
        public override Vector2 Size => new Vector2(800, 460);
        public override void Build()
        {
            var col = UiKit.Column("Message", Body, 14);
            col.Stretch();
            UiKit.Paragraph(col, body, 22);
            UiKit.Button(col, ok, () => Ui.Close(this));
        }
    }
}
