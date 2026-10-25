using System;
using System.Collections.Generic;
using System.Linq;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FH.UI
{
    /// <summary>Base for full screens and modal windows. Screens are rebuilt from state on Refresh.</summary>
    public abstract class UiScreen
    {
        public UiManager Ui;
        public GameSession S => Ui.Session;
        public GameCore C => Ui.Session.Core;
        public RectTransform Root, Body;
        public virtual bool PausesWorld => true;
        public virtual bool Dim => true;
        public virtual Vector2 Size => new Vector2(1500, 860);
        protected int selectIndex;
        public abstract string Title { get; }
        public abstract void Build();
        public virtual void Tick() { }
        /// <summary>Escape / controller B. Returning false keeps the screen open.</summary>
        public virtual bool Back() { Ui.Close(this); return true; }
        public void Refresh()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var buttons = Body.GetComponentsInChildren<Button>().ToList();
            int idx = selected ? buttons.FindIndex(b => b.gameObject == selected) : -1;
            for (int i = Body.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(Body.GetChild(i).gameObject);
            Body.DetachChildren();
            Build();
            Ui.FocusFirst(this, idx);
        }
    }

    /// <summary>Owns the canvas, the screen stack, focus and Escape/back. While any screen is open, gameplay input is
    /// blocked and the world is paused, so nothing typed or clicked in a menu reaches combat.</summary>
    public class UiManager : MonoBehaviour
    {
        public GameSession Session;
        public Hud Hud { get; private set; }
        public Canvas Canvas { get; private set; }
        public IReadOnlyList<UiScreen> Stack => stack;
        public UiScreen Top => stack.Count > 0 ? stack[stack.Count - 1] : null;
        public bool BlocksWorld => stack.Count > 0 || fading || titleOpen;
        public float LastBlockedToPlay { get; private set; }

        readonly List<UiScreen> stack = new List<UiScreen>();
        CanvasScaler scaler;
        RectTransform hudLayer, screenLayer, overlayLayer;
        Image fade;
        bool fading, titleOpen;
        float fadeAlpha;
        Action fadeMid;
        int fadeStage;

        public void Initialize(GameSession s)
        {
            Session = s;
            var go = new GameObject("Game UI", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.layer = 5;
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            go.AddComponent<GraphicRaycaster>();
            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("Event System");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                var input = es.AddComponent<StandaloneInputModule>();
                input.cancelButton = "Cancel"; input.submitButton = "Submit";
            }
            hudLayer = UiKit.Rect("HUD layer", go.transform).Stretch();
            screenLayer = UiKit.Rect("Screen layer", go.transform).Stretch();
            overlayLayer = UiKit.Rect("Overlay layer", go.transform).Stretch();
            fade = UiKit.Rect("Fade", overlayLayer).Stretch().gameObject.AddComponent<Image>();
            fade.color = new Color(0, 0, 0, 0); fade.raycastTarget = false;
            Hud = new Hud(this, hudLayer);
            SetScale(s.Settings.uiScale);
        }

        public void SetScale(float scale)
        {
            if (scaler) scaler.referenceResolution = new Vector2(1920, 1080) / Mathf.Clamp(scale, .75f, 1.5f);
        }

        // ---------- stack ----------
        public T Push<T>(T screen) where T : UiScreen
        {
            Session.Hunter.InterruptForMenu();
            screen.Ui = this;
            var root = UiKit.Rect(screen.GetType().Name, screenLayer).Stretch();
            if (screen.Dim)
            {
                var dim = root.gameObject.AddComponent<Image>();
                dim.color = new Color(0, 0, 0, .55f);   // also swallows clicks behind the window
            }
            var panel = UiKit.Panel("Window", root);
            panel.rectTransform.Box(new Vector2(.5f, .5f), Vector2.zero, screen.Size);
            var title = UiKit.Label("Title", panel.transform, screen.Title, 34, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -64), new Vector2(-32, -18));
            var hint = UiKit.Label("Back hint", panel.transform, "Esc  Back", 18, UiKit.Muted, TextAnchor.UpperRight);
            hint.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -56), new Vector2(-32, -24));
            screen.Root = root;
            screen.Body = UiKit.Rect("Body", panel.transform).Place(Vector2.zero, Vector2.one, new Vector2(32, 28), new Vector2(-32, -78));
            stack.Add(screen);
            screen.Build();
            if (!stack.Contains(screen)) return screen;   // the screen closed itself while building
            FocusFirst(screen, -1);
            UpdateBlocking();
            return screen;
        }

        public void Close(UiScreen screen)
        {
            if (!stack.Remove(screen)) return;
            if (screen.Root) Destroy(screen.Root.gameObject);
            if (Top != null) { Top.Refresh(); }
            UpdateBlocking();
        }

        public void CloseAll()
        {
            foreach (var s in stack.ToArray()) if (s.Root) Destroy(s.Root.gameObject);
            stack.Clear();
            UpdateBlocking();
        }

        void UpdateBlocking()
        {
            bool block = BlocksWorld;
            if (block) InputRouter.GameplayBlocked = true;
            else if (InputRouter.GameplayBlocked) { InputRouter.Unblock(); LastBlockedToPlay = Time.unscaledTime; }
            Time.timeScale = stack.Any(s => s.PausesWorld) || titleOpen ? 0 : 1;
            Hud.SetVisible(!titleOpen && Session.Mode == SessionMode.Playing);
        }

        public void FocusFirst(UiScreen screen, int preferIndex)
        {
            var buttons = screen.Root.GetComponentsInChildren<Button>().Where(b => b.interactable).ToList();
            var all = screen.Root.GetComponentsInChildren<Button>().ToList();
            GameObject pick = null;
            if (preferIndex >= 0 && preferIndex < all.Count && all[preferIndex].interactable) pick = all[preferIndex].gameObject;
            if (!pick && buttons.Count > 0) pick = buttons[0].gameObject;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(pick);
        }

        // ---------- per-frame ----------
        void Update()
        {
            if (Session == null) return;
            Hud.Tick(Time.unscaledDeltaTime);
            UpdateFade();
            if (fading) return;
            if (titleOpen && stack.Count == 0) return;
            Top?.Tick();
            if (InputRouter.MenuDown(GameAction.Pause))
            {
                if (Top != null) Top.Back();
                else if (Session.Mode == SessionMode.Playing) OpenPause();
                return;
            }
            if (Session.Mode != SessionMode.Playing || titleOpen) return;
            if (stack.Count == 0 || Top is JournalScreen)
            {
                var j = Top as JournalScreen;
                if (InputRouter.MenuDown(GameAction.Inventory)) OpenJournal(JournalTab.Pack, j);
                else if (InputRouter.MenuDown(GameAction.Journal)) OpenJournal(JournalTab.Hunts, j);
                else if (InputRouter.MenuDown(GameAction.Character)) OpenJournal(JournalTab.Hunter, j);
                else if (InputRouter.MenuDown(GameAction.Map)) OpenJournal(JournalTab.Map, j);
            }
        }

        void OpenJournal(JournalTab tab, JournalScreen open)
        {
            if (open != null) { if (open.Tab == tab) Close(open); else open.Show(tab); return; }
            Push(new JournalScreen(tab));
        }

        // ---------- top-level flows ----------
        public void ShowTitle()
        {
            CloseAll();
            titleOpen = true;
            Push(new TitleScreen());
        }

        public void TitleClosed() { titleOpen = false; UpdateBlocking(); }

        public void ShowGameplay()
        {
            titleOpen = false;
            CloseAll();
            Hud.Rebind();
            UpdateBlocking();
        }

        public void OpenPause() => Push(new PauseScreen());
        public void OpenEstate() => Push(new JournalScreen(JournalTab.Estate));
        public void OpenStorage() => Push(new StorageScreen());
        public void OpenCrafting(Station station, string label) => Push(new CraftScreen(station, label));
        public void OpenContractBoard() => Push(new ContractBoardScreen());
        public void OpenLoot(LootBag bag) => Push(new LootScreen(bag));
        public void OpenDialogue(NpcActor npc) => Push(new DialogueScreen(npc));
        public void Confirm(string title, string body, string ok, Action onOk) => Push(new ConfirmScreen(title, body, ok, onOk));
        public void ShowRecovery(int goldLost) => Push(new MessageScreen("Carried home", "You collapsed on the hunt. Villagers carried you back to the cottage." + (goldLost > 0 ? "\n\nYou lost " + goldLost + " gold along the way." : "") + "\n\nThe monsters you fought have regained their strength.", "Continue"));

        public void Fade(Action atBlack)
        {
            fading = true; fadeStage = 0; fadeMid = atBlack;
            UpdateBlocking();
        }

        void UpdateFade()
        {
            if (!fading) { fade.color = new Color(0, 0, 0, 0); return; }
            float dt = Time.unscaledDeltaTime * 3.2f;
            if (fadeStage == 0) { fadeAlpha = Mathf.Min(1, fadeAlpha + dt); if (fadeAlpha >= 1) { fadeStage = 1; try { fadeMid?.Invoke(); } catch (Exception e) { Debug.LogException(e); } } }
            else { fadeAlpha = Mathf.Max(0, fadeAlpha - dt); if (fadeAlpha <= 0) { fading = false; UpdateBlocking(); } }
            fade.color = new Color(0, 0, 0, fadeAlpha);
        }

        /// <summary>For verification: which screen types are open, top last.</summary>
        public string Describe() => string.Join(" > ", stack.Select(s => s.GetType().Name));
    }
}
