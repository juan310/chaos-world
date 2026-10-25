using System;
using FH.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FH.UI
{
    /// <summary>Reusable UI building blocks with one visual language: parchment text on dark oak panels.</summary>
    public static class UiKit
    {
        public static readonly Color PanelBg = new Color(.07f, .085f, .075f, .93f);
        public static readonly Color PanelLight = new Color(.13f, .15f, .13f, .95f);
        public static readonly Color Border = new Color(.55f, .45f, .28f, 1);
        public static readonly Color Text = new Color(.93f, .91f, .84f);
        public static readonly Color Muted = new Color(.66f, .66f, .6f);
        public static readonly Color Gold = new Color(.96f, .84f, .55f);
        public static readonly Color Warn = new Color(1f, .7f, .38f);
        public static readonly Color Good = new Color(.6f, .9f, .55f);
        public static readonly Color Health = new Color(.72f, .2f, .15f);
        public static readonly Color Stamina = new Color(.52f, .7f, .3f);
        public static readonly Color Magic = new Color(.3f, .5f, .9f);
        public static readonly Color ButtonIdle = new Color(.18f, .2f, .17f, 1);
        public static readonly Color ButtonHot = new Color(.46f, .36f, .18f, 1);
        public static readonly Color ButtonAccent = new Color(.30f, .26f, .16f, 1);
        public static readonly Color ButtonDisabled = new Color(.11f, .11f, .10f, .92f);

        static Font font;
        static Sprite rounded, solid;

        public static Font Font => font ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Sprite Rounded
        {
            get
            {
                if (rounded) return rounded;
                const int s = 32, r = 7;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = "Rounded panel", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = Mathf.Max(0, Mathf.Max(r - x - .5f, x + .5f - (s - r))), dy = Mathf.Max(0, Mathf.Max(r - y - .5f, y + .5f - (s - r)));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d + .5f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                tex.Apply();
                return rounded = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
            }
        }

        public static Sprite Solid
        {
            get
            {
                if (solid) return solid;
                var tex = new Texture2D(4, 4) { name = "Solid" };
                for (int i = 0; i < 16; i++) tex.SetPixel(i % 4, i / 4, Color.white);
                tex.Apply();
                return solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offsetMin; r.offsetMax = offsetMax;
            return r;
        }

        public static RectTransform Stretch(this RectTransform r, float inset = 0) => r.Place(Vector2.zero, Vector2.one, Vector2.one * inset, -Vector2.one * inset);

        /// <summary>Anchored box of a fixed size relative to a pivot point on the parent.</summary>
        public static RectTransform Box(this RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        public static Image Panel(string name, Transform parent, Color? color = null, bool border = true)
        {
            var r = Rect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = color ?? PanelBg;
            if (border)
            {
                var o = r.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(Border.r, Border.g, Border.b, .55f); o.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size = 22, Color? color = null, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var r = Rect(name, parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.color = color ?? Text; t.alignment = align; t.fontStyle = style;
            t.text = text; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = r.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, .6f); sh.effectDistance = new Vector2(1, -1);
            return t;
        }

        public static UiButton Button(Transform parent, string label, Action onClick, float height = 48, int fontSize = 22)
        {
            var r = Rect("Button " + label, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = Color.white;
            var b = r.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            img.color = ButtonIdle;
            r.gameObject.AddComponent<ButtonVisual>();
            var outline = r.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(Border.r, Border.g, Border.b, .45f);
            var t = Label("Text", r, label, fontSize, Text, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch(6);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height; le.flexibleWidth = 1;
            var ub = new UiButton { Button = b, Text = t, Layout = le };
            if (onClick != null) b.onClick.AddListener(() => { if (b.interactable) onClick(); });
            return ub;
        }

        /// <summary>Vertical stack that sizes to its content; optionally scrollable.</summary>
        public static RectTransform Column(string name, Transform parent, float spacing = 8, int padding = 0, bool expandWidth = true)
        {
            var r = Rect(name, parent);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = expandWidth; v.childForceExpandHeight = false;
            return r;
        }

        public static RectTransform Row(string name, Transform parent, float spacing = 8, float height = 48)
        {
            var r = Rect(name, parent);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height;
            return r;
        }

        public static RectTransform Scroll(string name, Transform parent, out RectTransform content)
        {
            var r = Rect(name, parent);
            var bg = r.gameObject.AddComponent<Image>(); bg.color = new Color(0, 0, 0, .18f); bg.sprite = Rounded; bg.type = Image.Type.Sliced;
            var sr = r.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", r).Stretch(4);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Column("Content", viewport, 6, 4);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport; sr.content = content; sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            return r;
        }

        public static Text Paragraph(Transform parent, string text, int size = 20, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var t = Label("Paragraph", parent, text, size, color, TextAnchor.UpperLeft, style);
            var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        public static Bar Bar(string name, Transform parent, Color color, Vector2 size)
        {
            var r = Rect(name, parent); r.sizeDelta = size;
            var bg = r.gameObject.AddComponent<Image>(); bg.sprite = Rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.05f, .05f, .05f, .85f);
            var ghost = Rect("Recent", r).Stretch(2); var gi = ghost.gameObject.AddComponent<Image>(); gi.sprite = Rounded; gi.type = Image.Type.Sliced; gi.color = new Color(1, .9f, .7f, .5f);
            var fill = Rect("Fill", r).Stretch(2); var fi = fill.gameObject.AddComponent<Image>(); fi.sprite = Rounded; fi.type = Image.Type.Sliced; fi.color = color;
            return new Bar { Root = r, Fill = fill, Ghost = ghost, FillImage = fi };
        }

        public static string RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return "#8fdc7a";
                case Rarity.Rare: return "#7fb2ff";
                case Rarity.Epic: return "#c48cff";
                case Rarity.Legendary: return "#ffb347";
                default: return "#e9e4d4";
            }
        }

        public static void Select(GameObject go)
        {
            if (EventSystem.current && go && go.activeInHierarchy) EventSystem.current.SetSelectedGameObject(go);
        }
    }

    public class UiButton
    {
        public Button Button;
        public Text Text;
        public LayoutElement Layout;
        Text reason;

        public UiButton Active(bool on = true) { Button.GetComponent<ButtonVisual>().Accent = on; return this; }

        public UiButton Enabled(bool on, string why = null)
        {
            Button.interactable = on;
            if (!on && !string.IsNullOrEmpty(why))
            {
                if (reason == null)
                {
                    reason = UiKit.Label("Reason", Button.transform.parent, "", 17, UiKit.Warn);
                    reason.transform.SetSiblingIndex(Button.transform.GetSiblingIndex() + 1);
                    var fit = reason.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
                reason.text = "   " + why;
                reason.gameObject.SetActive(true);
            }
            else if (reason != null) reason.gameObject.SetActive(false);
            Text.color = on ? UiKit.Text : UiKit.Muted;
            return this;
        }
    }

    public class Bar
    {
        public RectTransform Root, Fill, Ghost;
        public Image FillImage;
        float ghostValue = 1;
        public void Set(float t, float dt)
        {
            t = Mathf.Clamp01(t);
            Fill.anchorMax = new Vector2(t, 1);
            ghostValue = ghostValue > t ? Mathf.MoveTowards(ghostValue, t, dt * .5f) : t;
            Ghost.anchorMax = new Vector2(ghostValue, 1);
        }
    }
}
