using FH.Game;
using UnityEngine;

namespace FH.UI
{
    /// <summary>The Proving Grounds ledger: spawn test monsters, training toggles, reset, leave.</summary>
    public class ProvingScreen : UiScreen
    {
        public override string Title => S.TestAreaName;
        public override Vector2 Size => new Vector2(980, 1010);

        public override void Build()
        {
            var rig = S.ProvingRig;
            var row = UiKit.Row("Columns", Body, 24, 890);
            row.Stretch();
            var left = UiKit.Column("Monsters", row, 8);
            UiKit.Paragraph(left, "Spawn a monster", 22, UiKit.Gold);
            foreach (var (id, label) in ProvingGrounds.Monsters)
            {
                bool ok = ProvingGrounds.Available(S, id);
                UiKit.Button(left, label, () => { rig.Spawn(id); rig.RefreshVolumes(); Ui.Close(this); }).Enabled(ok, ok ? null : "Its 3D model is not in this build.");
            }
            UiKit.Button(left, "Clear monsters", () => { rig.ClearMonsters(); rig.RefreshVolumes(); Refresh(); });
            UiKit.Paragraph(left, rig.Spawned.Count + " monster(s) here. Monsters use the hunt rules: flinch, topple and enrage." + (S.InRange ? " The pen's dummies never fall." : ""), 16, UiKit.Muted);

            UiKit.Paragraph(left, "Weapon", 22, UiKit.Gold);
            foreach (var (id, label) in ProvingGrounds.Weapons)
            {
                bool on = S.Hunter.Hunt && S.Hunter.Hunt.Set != null && S.Hunter.Hunt.Set.id == id;
                UiKit.Button(left, label + (on ? "  (in hand)" : ""), () => { rig.SetWeapon(id); Ui.Close(this); }).Active(on);
            }

            var right = UiKit.Column("Options", row, 8);
            UiKit.Paragraph(right, "Options", 22, UiKit.Gold);
            Toggle(right, "Monsters passive (training targets)", rig.MonstersPassive, v => rig.SetPassive(v));
            Toggle(right, "Infinite stamina", rig.InfiniteStamina, v => rig.InfiniteStamina = v);
            Toggle(right, "Invulnerable hunter", rig.Invulnerable, v => rig.Invulnerable = v);
            Toggle(right, "Show hit volumes", rig.ShowVolumes, v => { rig.ShowVolumes = v; rig.RefreshVolumes(); });
            Toggle(right, "Frame-data overlay", rig.ShowOverlay, v => rig.ShowOverlay = v);
            Toggle(right, "Controller rumble", InputRouter.RumbleEnabled, v => { InputRouter.RumbleEnabled = v; if (!v) GamePad.StopRumble(); });
            if (S.InRange) Toggle(right, "Dodge hazards", RangeHazard.Enabled, v => RangeHazard.Enabled = v);
            UiKit.Button(right, "Heal and reset hunter", () => { rig.ResetHunter(); Ui.Close(this); });
            UiKit.Button(right, S.InRange ? "Go to the Proving Grounds" : "Go to the Training Range", () => { Ui.Close(this); S.SwitchTestArea(!S.InRange); });
            UiKit.Button(right, "Controls", () => Ui.Push(new ControlsScreen()));
            UiKit.Button(right, "Leave the " + S.TestAreaName, () => Ui.Confirm("Leave the " + S.TestAreaName + "?", "Nothing here is saved. You return to the title.", "Leave", () => S.LeaveProvingGrounds()));
            UiKit.Button(right, "Back", () => Ui.Close(this));
        }

        void Toggle(Transform parent, string label, bool on, System.Action<bool> set)
        {
            UiKit.Button(parent, label + ":  " + (on ? "ON" : "off"), () => { set(!on); Refresh(); }).Active(on);
        }
    }
}
