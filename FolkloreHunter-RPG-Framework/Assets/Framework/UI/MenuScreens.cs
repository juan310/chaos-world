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
            if (selected) b.Button.GetComponent<Image>().color = new Color(1.25f, 1.15f, .9f);
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
        public override Vector2 Size => new Vector2(560, 640);
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

        void Start(int slot) { S.Store.Delete(slot); S.NewGame(slot, "Hunter"); }

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
            bool fighting = S.Monsters.Any(m => m && m.Alive && m.Brain.Engaged);
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
        public override Vector2 Size => new Vector2(980, 760);
        public override void Build()
        {
            var col = UiKit.Column("Controls", Body, 8);
            col.Stretch();
            UiKit.Paragraph(col,
                "<b>Move</b>  WASD / left stick        <b>Run</b>  Shift (uses stamina)\n" +
                "<b>Attack</b>  Left mouse / X — press again in rhythm to chain a three-hit combo\n" +
                "<b>Dodge</b>  Space / B — brief invulnerability; can cancel an attack's recovery\n" +
                "<b>Spell</b>  Q / Y        <b>Change spell</b>  F\n" +
                "<b>Lock-on</b>  Tab / middle mouse — the camera frames your target\n" +
                "<b>Interact</b>  E / A        <b>Items</b>  1-4 (R drinks the first slot)\n" +
                "<b>Camera</b>  Hold right mouse and drag; mouse wheel zooms\n" +
                "<b>Pack</b>  I    <b>Hunts</b>  J    <b>Hunter</b>  K    <b>Map</b>  M    <b>Menu / back</b>  Esc / Start\n\n" +
                "<color=#f5d68c>Hunting tips</color>\n" +
                "Monsters raise their weapon and their eyes flare red before striking; a red ring marks where the blow lands.\n" +
                "Step out of the ring or dodge through the swing, then punish the recovery.\n" +
                "Aim high: helms crack under repeated blows and expose the skull, a weak point.", 21);
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
