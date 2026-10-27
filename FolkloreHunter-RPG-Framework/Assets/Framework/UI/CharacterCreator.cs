using System;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FH.UI
{
    /// <summary>New game: choose the male or female hunter (the main character sheet) and one of their ten hairstyles,
    /// or the classic knight, and name the hunter. The real 3D hunter stands beside the menu in the chosen look.</summary>
    public class CharacterCreatorScreen : UiScreen
    {
        public override string Title => "Your hunter";
        public override bool Dim => false;
        public override bool PausesWorld => false;      // the hunter beside the menu keeps breathing (the title world is not simulated)
        public override Vector2 Size => new Vector2(980, 980);
        readonly int slot;
        string body = Appearance.Male;
        int hair = 1;
        string hunterName;
        InputField nameField;
        Vector3 camPos; Quaternion camRot; Quaternion hunterRot; bool posed;
        static readonly System.Random dice = new System.Random();

        public CharacterCreatorScreen(int slot)
        {
            this.slot = slot;
            hunterName = Appearance.MaleNames[0];
        }

        /// <summary>Lets the verifier and the tour pick a look as the controls would.</summary>
        public void Choose(string newBody, int newHair, string newName = null)
        {
            body = newBody; hair = Appearance.ClampHair(newHair);
            if (newName != null) hunterName = newName;
            Refresh();
        }

        public string ChosenBody => body;
        public int ChosenHair => hair;

        public override void Build()
        {
            Root.GetComponentInChildren<Image>().rectTransform.Box(new Vector2(0, .5f), new Vector2(60, 0), Size);
            PoseCamera();
            S.Hunter.ApplyLook(Appearance.Models(body, hair));
            var col = UiKit.Column("Creator", Body, 10);
            col.Stretch();
            var bodies = UiKit.Row("Bodies", col, 10, 54);
            Pick(bodies, "Male hunter", body == Appearance.Male, () => SetBody(Appearance.Male));
            Pick(bodies, "Female hunter", body == Appearance.Female, () => SetBody(Appearance.Female));
            UiKit.Paragraph(col, "Hairstyle:  <color=#f5d68c>" + Appearance.Hairstyles(body)[hair - 1] + "</color>", 22);
            for (int row = 0; row < 2; row++)
            {
                var r = UiKit.Row("Hair row " + row, col, 8, 250);
                for (int i = 1; i <= 5; i++) Portrait(r, row * 5 + i);
            }
            UiKit.Paragraph(col, "Name", 20, UiKit.Muted);
            var nameRow = UiKit.Row("Name row", col, 10, 54);
            nameField = NameField(nameRow);
            var roll = UiKit.Button(nameRow, "Random name", RandomName, 54, 20); roll.Layout.flexibleWidth = .5f;
            var go = UiKit.Button(col, "Begin the hunt", Begin, 60, 24);
            go.Active();
            UiKit.Button(col, "Back", () => Back(), 48, 20);
        }

        void Pick(RectTransform row, string label, bool on, Action click)
        {
            var b = UiKit.Button(row, label, click, 54, 20);
            if (on) b.Active();
        }

        void Portrait(RectTransform row, int n)
        {
            var b = UiKit.Button(row, "", () => { hair = n; Refresh(); }, 250, 16);
            if (n == hair) b.Active();
            var tex = Resources.Load<Texture2D>("UI/Portraits/" + body + "_" + n.ToString("00"));
            if (tex)
            {
                var img = UiKit.Rect("Portrait", b.Button.transform).gameObject.AddComponent<Image>();
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
                img.preserveAspect = true; img.raycastTarget = false;
                img.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(6, 30), new Vector2(-6, -6));
            }
            b.Text.text = n.ToString("00") + " " + Appearance.Hairstyles(body)[n - 1];
            b.Text.fontSize = 14;
            b.Text.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(2, 2), new Vector2(-2, 28));
        }

        InputField NameField(RectTransform row)
        {
            var r = UiKit.Rect("Name field", row);
            var bg = r.gameObject.AddComponent<Image>(); bg.sprite = UiKit.Rounded; bg.type = Image.Type.Sliced; bg.color = UiKit.PanelLight;
            var text = UiKit.Label("Text", r, "", 24, UiKit.Gold, TextAnchor.MiddleLeft);
            text.rectTransform.Stretch(12); text.supportRichText = false;
            var f = r.gameObject.AddComponent<InputField>();
            f.textComponent = text; f.characterLimit = 18; f.text = hunterName;
            f.shouldActivateOnSelect = false;                  // the pad moves past it; A (or a click) starts typing
            f.onEndEdit.AddListener(v => hunterName = Appearance.CleanName(v));
            f.onValueChanged.AddListener(v => hunterName = v);
            r.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.5f;
            return f;
        }

        void SetBody(string b)
        {
            bool keepName = Array.IndexOf(Appearance.MaleNames, hunterName) < 0 && Array.IndexOf(Appearance.FemaleNames, hunterName) < 0;
            body = b;
            if (!keepName) hunterName = (b == Appearance.Female ? Appearance.FemaleNames : Appearance.MaleNames)[0];
            Refresh();
        }

        void RandomName()
        {
            var names = body == Appearance.Female ? Appearance.FemaleNames : Appearance.MaleNames;
            hunterName = names[dice.Next(names.Length)];
            if (nameField) nameField.text = hunterName;
        }

        /// <summary>The camera looks at the hunter from the front, the hunter standing to the right of the menu.</summary>
        void PoseCamera()
        {
            var h = S.Hunter.transform; var cam = S.CameraRig.Camera.transform;
            if (!posed) { camPos = cam.position; camRot = cam.rotation; hunterRot = h.rotation; posed = true; }
            S.CameraRig.Manual = true;
            Vector3 f = h.forward; f.y = 0; f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            cam.position = h.position + f * 3.4f + Vector3.up * 1.25f + right * 1.15f;
            cam.LookAt(h.position + Vector3.up * 1.05f + right * 1.15f);
        }

        void Restore()
        {
            if (!posed) return;
            var cam = S.CameraRig.Camera.transform;
            cam.position = camPos; cam.rotation = camRot; S.Hunter.transform.rotation = hunterRot;
        }

        public override bool Back()
        {
            if (nameField && nameField.isFocused) { hunterName = nameField.text; nameField.DeactivateInputField(); return false; }   // B leaves the name, not the screen
            Restore();
            S.Hunter.ApplyLook(Appearance.Models(S.Settings.lookBody, S.Settings.lookHair));
            Ui.Close(this);
            return true;
        }

        void Begin()
        {
            if (nameField) hunterName = nameField.text;
            Restore();
            S.Store.Delete(slot);
            S.NewGame(slot, Appearance.CleanName(hunterName), body, hair);
        }
    }
}
