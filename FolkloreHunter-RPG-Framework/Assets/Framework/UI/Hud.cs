using System.Collections.Generic;
using System.Linq;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FH.UI
{
    /// <summary>In-play overlay: vitals, gold/rank/clock, hotbar and spell, interaction prompt, notifications,
    /// target and boss bars with part state, lock-on reticle and tracked contract objectives.</summary>
    public class Hud
    {
        readonly UiManager ui;
        readonly RectTransform root;
        Bar health, stamina, magic, target;
        Text nameText, goldText, clockText, rankText, promptText, flashText, areaText, trackerText, targetName, targetParts, spellText;
        Image staminaFill;
        RectTransform promptBox, targetBox, reticle, notices, areaBox;
        readonly List<(Text text, float until)> feed = new List<(Text, float)>();
        readonly Text[] hotbarCounts = new Text[4], hotbarNames = new Text[4];
        Image spellCooldown;
        float flashUntil, areaUntil, hintClock;
        Text controlsHint;
        public string LastFlash { get; private set; } = "";
        public string LastNotice { get; private set; } = "";
        public string PromptShown => promptBox && promptBox.gameObject.activeSelf ? promptText.text : "";
        public bool TargetShown => targetBox && targetBox.gameObject.activeSelf;

        GameSession S => ui.Session;

        public Hud(UiManager ui, RectTransform layer)
        {
            this.ui = ui;
            root = UiKit.Rect("HUD", layer).Stretch();
            Build();
        }

        public void SetVisible(bool on) => root.gameObject.SetActive(on);

        void Build()
        {
            // Vitals, top-left.
            var vit = UiKit.Panel("Vitals", root);
            vit.rectTransform.Box(new Vector2(0, 1), new Vector2(24, -22), new Vector2(430, 136));
            nameText = UiKit.Label("Name", vit.transform, "", 22, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            nameText.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -40), new Vector2(-18, -10));
            health = UiKit.Bar("Health", vit.transform, UiKit.Health, new Vector2(390, 20)); health.Root.Box(new Vector2(0, 1), new Vector2(18, -48), new Vector2(390, 20));
            stamina = UiKit.Bar("Stamina", vit.transform, UiKit.Stamina, new Vector2(390, 14)); stamina.Root.Box(new Vector2(0, 1), new Vector2(18, -78), new Vector2(390, 14));
            staminaFill = stamina.FillImage;
            magic = UiKit.Bar("Magic", vit.transform, UiKit.Magic, new Vector2(300, 10)); magic.Root.Box(new Vector2(0, 1), new Vector2(18, -102), new Vector2(300, 10));

            // Status, top-right.
            var st = UiKit.Panel("Status", root);
            st.rectTransform.Box(new Vector2(1, 1), new Vector2(-24, -22), new Vector2(360, 112));
            goldText = UiKit.Label("Gold", st.transform, "", 22, UiKit.Gold, TextAnchor.UpperRight);
            goldText.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -40), new Vector2(-18, -10));
            rankText = UiKit.Label("Rank", st.transform, "", 18, UiKit.Text, TextAnchor.UpperRight);
            rankText.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -68), new Vector2(-18, -42));
            clockText = UiKit.Label("Clock", st.transform, "", 18, UiKit.Muted, TextAnchor.UpperRight);
            clockText.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -96), new Vector2(-18, -70));

            // Tracked contracts, right side.
            trackerText = UiKit.Label("Tracker", root, "", 18, UiKit.Text, TextAnchor.UpperRight);
            trackerText.rectTransform.Box(new Vector2(1, 1), new Vector2(-28, -150), new Vector2(460, 300));

            // Area banner.
            areaBox = UiKit.Rect("Area banner", root).Box(new Vector2(.5f, 1), new Vector2(0, -120), new Vector2(800, 70));
            areaText = UiKit.Label("Area", areaBox, "", 40, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            areaText.rectTransform.Stretch();
            areaBox.gameObject.SetActive(false);

            // Target / boss bar, top-centre.
            targetBox = UiKit.Panel("Target", root).rectTransform.Box(new Vector2(.5f, 1), new Vector2(0, -22), new Vector2(640, 92));
            targetName = UiKit.Label("Target name", targetBox, "", 22, UiKit.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            targetName.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -34), new Vector2(-12, -6));
            target = UiKit.Bar("Target health", targetBox, new Color(.8f, .3f, .18f), new Vector2(600, 16)); target.Root.Box(new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(600, 16));
            targetParts = UiKit.Label("Parts", targetBox, "", 16, UiKit.Muted, TextAnchor.UpperCenter);
            targetParts.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -86), new Vector2(-12, -60));
            targetBox.gameObject.SetActive(false);
            reticle = UiKit.Rect("Lock reticle", root);
            reticle.sizeDelta = new Vector2(46, 46);
            var ri = reticle.gameObject.AddComponent<Image>(); ri.sprite = UiKit.Rounded; ri.type = Image.Type.Sliced; ri.color = new Color(1, .82f, .3f, .0f);
            var ro = reticle.gameObject.AddComponent<Outline>(); ro.effectColor = new Color(1, .82f, .3f, .95f); ro.effectDistance = new Vector2(3, 3);
            var dot = UiKit.Rect("Dot", reticle).Box(new Vector2(.5f, .5f), Vector2.zero, new Vector2(8, 8));
            var di = dot.gameObject.AddComponent<Image>(); di.sprite = UiKit.Rounded; di.color = new Color(1, .82f, .3f, .95f);
            reticle.gameObject.SetActive(false);

            // Hotbar, bottom-centre.
            var bar = UiKit.Rect("Hotbar", root).Box(new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(560, 92));
            for (int i = 0; i < 5; i++)
            {
                var slot = UiKit.Panel("Slot " + (i + 1), bar, UiKit.PanelLight);
                slot.rectTransform.Box(new Vector2(0, .5f), new Vector2(i * 112, 0), new Vector2(100, 88));
                var key = UiKit.Label("Key", slot.transform, i < 4 ? (i + 1).ToString() : "Q", 16, UiKit.Gold, TextAnchor.UpperLeft);
                key.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-6, -4));
                var nm = UiKit.Label("Item", slot.transform, "", 15, UiKit.Text, TextAnchor.MiddleCenter);
                nm.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(4, 10), new Vector2(-4, -18));
                var cnt = UiKit.Label("Count", slot.transform, "", 17, UiKit.Text, TextAnchor.LowerRight);
                cnt.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(6, 4), new Vector2(-8, -4));
                if (i < 4) { hotbarNames[i] = nm; hotbarCounts[i] = cnt; }
                else
                {
                    spellText = nm;
                    var cd = UiKit.Rect("Cooldown", slot.transform).Stretch(3);
                    spellCooldown = cd.gameObject.AddComponent<Image>(); spellCooldown.color = new Color(0, 0, 0, .55f);
                    spellCooldown.sprite = UiKit.Solid; spellCooldown.type = Image.Type.Filled; spellCooldown.fillMethod = Image.FillMethod.Vertical; spellCooldown.fillOrigin = 1;
                }
            }

            // Interaction prompt above the hotbar.
            promptBox = UiKit.Panel("Prompt", root).rectTransform.Box(new Vector2(.5f, 0), new Vector2(0, 134), new Vector2(620, 50));
            promptText = UiKit.Label("Prompt text", promptBox, "", 22, UiKit.Text, TextAnchor.MiddleCenter);
            promptText.rectTransform.Stretch(6);
            promptBox.gameObject.SetActive(false);

            // Centre flash for refusals and quick feedback.
            flashText = UiKit.Label("Flash", root, "", 26, UiKit.Warn, TextAnchor.MiddleCenter, FontStyle.Bold);
            flashText.rectTransform.Box(new Vector2(.5f, .5f), new Vector2(0, -170), new Vector2(1000, 50));

            // Notification feed, bottom-left.
            notices = UiKit.Column("Notices", root, 6);
            notices.Box(new Vector2(0, 0), new Vector2(24, 24), new Vector2(680, 300));
            notices.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;

            controlsHint = UiKit.Label("Controls hint", root, "", 15, UiKit.Muted, TextAnchor.LowerRight);
            controlsHint.rectTransform.Box(new Vector2(1, 0), new Vector2(-24, 10), new Vector2(1300, 24));
        }

        public void Rebind() { feed.ForEach(f => Object.Destroy(f.text.gameObject)); feed.Clear(); }

        public void Notify(string text, NoticeKind kind = NoticeKind.Info)
        {
            LastNotice = text;
            var color = kind == NoticeKind.Reward ? UiKit.Gold : kind == NoticeKind.Warning ? UiKit.Warn : kind == NoticeKind.Story ? new Color(.8f, .88f, 1f) : kind == NoticeKind.Progress ? UiKit.Good : UiKit.Text;
            var t = UiKit.Paragraph(notices, text, 19, color);
            feed.Add((t, Time.unscaledTime + 7));
            while (feed.Count > 6) { Object.Destroy(feed[0].text.gameObject); feed.RemoveAt(0); }
        }

        public void Flash(string text) { LastFlash = text; flashText.text = text; flashUntil = Time.unscaledTime + 1.8f; }

        public void ShowArea(string name) { areaText.text = name.ToUpperInvariant(); areaUntil = Time.unscaledTime + 3; areaBox.gameObject.SetActive(true); }

        /// <summary>One-line reminder for the device in use (keyboard or Xbox controller) and the combat style.</summary>
        string ControlsHint()
        {
            bool pad = InputRouter.PadActive;
            if (S.Proving)
                return pad ? "Y light   B heavy (hold: charge)   Y+B shield bash   A roll   RB guard   RT sprint   X sheathe   R3 lock-on   LB item   D-pad item   Menu ledger"
                           : "LMB light   RMB heavy (hold: charge)   both: shield bash   Space roll   Ctrl guard   Shift sprint   X sheathe   Tab lock-on   1-4 items   E ledger   Esc menu";
            return pad ? "Left stick move   RT run   Y attack   A dodge   LT spell   R3 lock-on   X interact   LB item   View map   Menu"
                       : "WASD move   Shift run   LMB attack   Space dodge   Q spell   Tab lock-on   E interact   1-4 items   I pack   J hunts   K hunter   M map   Esc menu";
        }

        public void Tick(float dt)
        {
            if (!root.gameObject.activeSelf || S.Mode != SessionMode.Playing) return;
            var h = S.Hunter; var c = S.Core; var st = h.Stats;
            hintClock -= dt;
            if (hintClock <= 0) { hintClock = .5f; controlsHint.text = ControlsHint(); }
            nameText.text = c.State.player.name + "   <size=17><color=#c9c4b4>Level " + c.State.player.level + "</color></size>";
            health.Set(h.Health / Mathf.Max(1, st[Stat.MaxHealth]), dt);
            stamina.Set(h.Stamina.Current / Mathf.Max(1, h.Stamina.Max), dt);
            staminaFill.color = h.Stamina.Exhausted ? new Color(.45f, .45f, .3f) : UiKit.Stamina;
            magic.Set(h.Magic / Mathf.Max(1, st[Stat.MaxMagic]), dt);
            goldText.text = c.State.gold + " gold";
            rankText.text = c.Progression.Rank.name + "  ·  " + (c.Db.Areas.TryGetValue(S.CurrentArea, out var a) ? a.name : "");
            clockText.text = c.Clock.Label + (c.Clock.IsNight ? "  ·  Night" : "");

            var bar = c.State.player.hotbar;
            for (int i = 0; i < 4; i++)
            {
                string id = i < bar.Count ? bar[i] : "";
                hotbarNames[i].text = string.IsNullOrEmpty(id) ? "" : c.Db.ItemName(id);
                hotbarCounts[i].text = string.IsNullOrEmpty(id) ? "" : "×" + c.Inventory.Count(id);
            }
            if (c.Db.Spells.TryGetValue(h.SelectedSpell, out var spell))
            {
                spellText.text = spell.name;
                spellCooldown.fillAmount = Mathf.Clamp01((h.SpellReadyAt - Time.time) / Mathf.Max(.1f, spell.cooldown)) + (h.Magic < spell.magicCost ? .0f : 0);
                spellText.color = h.Magic < spell.magicCost ? UiKit.Muted : UiKit.Text;
            }

            var focus = S.Focus;
            bool showPrompt = focus && !ui.BlocksWorld;
            promptBox.gameObject.SetActive(showPrompt);
            if (showPrompt) promptText.text = "<color=#f5d68c>" + InputRouter.Label(GameAction.Interact, "E") + "</color>   " + focus.Prompt(S);

            flashText.gameObject.SetActive(Time.unscaledTime < flashUntil);
            if (Time.unscaledTime > areaUntil) areaBox.gameObject.SetActive(false);
            for (int i = feed.Count - 1; i >= 0; i--)
                if (Time.unscaledTime > feed[i].until) { Object.Destroy(feed[i].text.gameObject); feed.RemoveAt(i); }

            // Tracked contracts and the active story stage.
            var lines = new List<string>();
            foreach (var def in c.Contracts.Active.Concat(c.Db.Contracts.Values.Where(x => c.Contracts.Get(x.id).status == ContractStatus.Completed)).Distinct())
            {
                var cs = c.Contracts.Get(def.id);
                lines.Add("<b><color=#f5d68c>" + def.name + "</color></b>" + (cs.status == ContractStatus.Completed ? "  <color=#9be38a>complete</color>" : ""));
                if (cs.status == ContractStatus.Completed)
                {
                    string giver = c.Db.Npcs.TryGetValue(def.giverNpcId ?? "", out var n) ? n.name : "the hunt board";
                    lines.Add("Report to " + giver);
                }
                else foreach (var line in c.Contracts.ProgressText(def).Split('\n')) lines.Add(line);
            }
            var rise = c.Db.Quests.Values.FirstOrDefault();
            if (rise != null && c.State.Quest(rise.id).status == QuestStatus.Active && lines.Count < 6)
                lines.Add("<color=#c9d6ff>" + rise.stages[c.State.Quest(rise.id).stage].description + "</color>");
            trackerText.text = S.Proving ? "" : string.Join("\n", lines);   // no story tracker in the test arena

            // Target bar: locked target, else the nearest engaged boss.
            var t = h.LockTarget && h.LockTarget.Alive ? h.LockTarget : S.Monsters.FirstOrDefault(m => m && m.Alive && m.Def.boss && m.Brain.Engaged);
            targetBox.gameObject.SetActive(t);
            if (t)
            {
                string mode = t.Brain.Mode == MonsterMode.Stagger ? "  <color=#9be38a>STAGGERED</color>" : t.Brain.Mode == MonsterMode.Telegraph ? "  <color=#ff7a55>ATTACKING</color>" : t.Brain.Mode == MonsterMode.Return ? "  <color=#c9c4b4>retreating</color>" : "";
                targetName.text = t.DisplayName + mode;
                target.Set(t.Brain.Health / t.Def.maxHealth, dt);
                targetParts.text = string.Join("    ", t.Def.parts.Where(p => p.breakHealth > 0 || p.weakPoint).Select(p => PartLabel(t, p)));
            }
            bool lockShown = h.LockTarget && h.LockTarget.Alive;
            reticle.gameObject.SetActive(lockShown);
            if (lockShown)
            {
                var cam = S.CameraRig.Camera;
                Vector3 sp = cam.WorldToScreenPoint(h.LockTarget.AimPoint.position);
                if (sp.z < 0) reticle.gameObject.SetActive(false);
                else
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var local);
                    reticle.anchorMin = reticle.anchorMax = new Vector2(.5f, .5f);
                    reticle.anchoredPosition = local;
                    reticle.localRotation = Quaternion.Euler(0, 0, Time.unscaledTime * 60);
                }
            }
        }

        static string PartLabel(MonsterActor m, MonsterPartDef p)
        {
            if (p.breakHealth > 0)
                return m.Parts.Broken.Contains(p.id) ? p.name + " <color=#9be38a>broken</color>" : p.name + " " + Mathf.RoundToInt(m.Parts.Progress(p.id) * 100) + "%";
            bool exposed = string.IsNullOrEmpty(p.requiresBrokenPart) || m.Parts.Broken.Contains(p.requiresBrokenPart);
            return exposed ? "<color=#ffd27a>" + p.name + ": weak point</color>" : p.name + ": covered";
        }
    }
}
