using System.Collections.Generic;
using UnityEngine;

namespace FH.Game
{
    public enum GameAction { Attack, Spell, Dodge, Interact, LockOn, Run, Hotbar1, Hotbar2, Hotbar3, Hotbar4, Pause, Inventory, Journal, Character, Map, CycleSpell, Inspect,
        HeavyAttack, Guard, Sheathe, Whistle, UseItem, ItemPrev, ItemNext, Sight }

    /// <summary>One place that turns devices into game actions. Menus set GameplayBlocked so no key or click
    /// leaks into combat; automated verification injects actions through the same path the keyboard uses.
    /// The Xbox controller is read through XInput (<see cref="GamePad"/>) with a Monster Hunter-style layout:
    /// Y light, B heavy (hold to charge), A roll, RB guard, RT sprint, X interact / sheathe, R3 lock on,
    /// LB use item, D-pad left/right choose item, D-pad up whistle, D-pad down change spell, LT spell (hunt combat,
    /// weapon drawn: tap LT for the spell, hold it for Hunter's Sight; Left Alt is Sight on the keyboard),
    /// Start pause, View map.</summary>
    public static class InputRouter
    {
        public static bool GameplayBlocked;
        public static float MouseSensitivity = 1;
        public static bool InvertY;
        public static bool RumbleEnabled = true;
        /// <summary>Hunting style on PC: the mouse turns the camera without a button (cursor locked) and the
        /// right button is the heavy attack. Off in the classic world until the hunt combat is integrated.</summary>
        public static bool MouseLook;
        public static float PadLookSpeed = 170;    // degrees per second at full right-stick deflection
        /// <summary>True when the controller was used more recently than the keyboard/mouse: prompts show pad buttons.</summary>
        public static bool PadActive => GamePad.LastUsedFrame >= 0 && GamePad.LastUsedFrame >= lastKeyboardFrame;
        static int lastKeyboardFrame = -1;
        static int unblockFrame = -1;
        static HashSet<GameAction> virtualDown = new HashSet<GameAction>(), pendingDown = new HashSet<GameAction>();
        static readonly HashSet<GameAction> virtualHeld = new HashSet<GameAction>();
        public static Vector2 VirtualMove;
        public static bool UseVirtual;

        /// <summary>Called when a menu closes: the click or key that closed it must not also swing the sword.</summary>
        public static void Unblock()
        {
            GameplayBlocked = false;
            unblockFrame = Time.frameCount;
        }

        static bool Suppressed => GameplayBlocked || Time.frameCount <= unblockFrame + 1;

        /// <summary>Queues a virtual press; it is seen as "pressed" for exactly one following frame.</summary>
        public static void Press(GameAction a) => pendingDown.Add(a);
        public static void Hold(GameAction a, bool held) { if (held) virtualHeld.Add(a); else virtualHeld.Remove(a); }
        /// <summary>Called once per frame (late): presses queued this frame become active for the next one.</summary>
        public static void EndFrame()
        {
            var t = virtualDown; virtualDown = pendingDown; pendingDown = t; pendingDown.Clear();
        }

        static bool KeyDown(GameAction a)
        {
            switch (a)
            {
                case GameAction.Attack: return Input.GetMouseButtonDown(0);
                case GameAction.HeavyAttack: return Input.GetMouseButtonDown(1) && MouseLook;
                case GameAction.Spell: return Input.GetKeyDown(KeyCode.Q);
                case GameAction.Dodge: return Input.GetKeyDown(KeyCode.Space);
                case GameAction.Interact: return Input.GetKeyDown(KeyCode.E);
                case GameAction.Sheathe: return Input.GetKeyDown(KeyCode.X);
                case GameAction.Guard: return Input.GetKeyDown(KeyCode.LeftControl);
                case GameAction.Whistle: return Input.GetKeyDown(KeyCode.G);
                case GameAction.LockOn: return Input.GetKeyDown(KeyCode.Tab) || Input.GetMouseButtonDown(2);
                case GameAction.Hotbar1: return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.R);
                case GameAction.Hotbar2: return Input.GetKeyDown(KeyCode.Alpha2);
                case GameAction.Hotbar3: return Input.GetKeyDown(KeyCode.Alpha3);
                case GameAction.Hotbar4: return Input.GetKeyDown(KeyCode.Alpha4);
                case GameAction.Pause: return Input.GetKeyDown(KeyCode.Escape);
                case GameAction.Inventory: return Input.GetKeyDown(KeyCode.I);
                case GameAction.Journal: return Input.GetKeyDown(KeyCode.J);
                case GameAction.Character: return Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.C);
                case GameAction.Map: return Input.GetKeyDown(KeyCode.M);
                case GameAction.CycleSpell: return Input.GetKeyDown(KeyCode.F);
                case GameAction.Inspect: return Input.GetKeyDown(KeyCode.F2);
                default: return false;
            }
        }

        static PadButton PadFor(GameAction a)
        {
            switch (a)
            {
                case GameAction.Attack: return PadButton.Y;
                case GameAction.HeavyAttack: return PadButton.B;
                case GameAction.Dodge: return PadButton.A;
                case GameAction.Interact: case GameAction.Sheathe: return PadButton.X;
                case GameAction.Guard: return PadButton.RB;
                case GameAction.Run: return PadButton.RT;
                case GameAction.LockOn: return PadButton.RightStick;
                case GameAction.UseItem: return PadButton.LB;
                case GameAction.ItemPrev: return PadButton.DPadLeft;
                case GameAction.ItemNext: return PadButton.DPadRight;
                case GameAction.Whistle: return PadButton.DPadUp;
                case GameAction.CycleSpell: return PadButton.DPadDown;
                case GameAction.Spell: return PadButton.LT;
                case GameAction.Pause: return PadButton.Start;
                case GameAction.Map: return PadButton.Back;
                default: return PadButton.None;
            }
        }

        static bool DeviceDown(GameAction a)
        {
            if (KeyDown(a)) { lastKeyboardFrame = Time.frameCount; return true; }
            return GamePad.Down(PadFor(a));
        }

        /// <summary>The pad button bound to an action, for on-screen prompts.</summary>
        public static string PadLabel(GameAction a)
        {
            var b = PadFor(a);
            switch (b)
            {
                case PadButton.RightStick: return "R3";
                case PadButton.DPadUp: return "D-pad Up";
                case PadButton.DPadDown: return "D-pad Down";
                case PadButton.DPadLeft: return "D-pad Left";
                case PadButton.DPadRight: return "D-pad Right";
                case PadButton.Back: return "View";
                case PadButton.Start: return "Menu";
                default: return b.ToString();
            }
        }

        /// <summary>Keyboard or controller label for an action, whichever device the player is using now.</summary>
        public static string Label(GameAction a, string keyboard) => PadActive ? PadLabel(a) : keyboard;

        /// <summary>Gameplay action pressed this frame (false while a menu has focus).</summary>
        public static bool Down(GameAction a)
        {
            if (Suppressed) return false;
            if (virtualDown.Contains(a)) return true;
            return !UseVirtual && DeviceDown(a);
        }

        /// <summary>Menu-level shortcut (Escape, I, J...) that works whether or not gameplay is blocked.</summary>
        public static bool MenuDown(GameAction a) => virtualDown.Contains(a) || (!UseVirtual && DeviceDown(a));

        public static bool Held(GameAction a)
        {
            if (Suppressed) return false;
            if (virtualHeld.Contains(a)) return true;
            if (UseVirtual) return false;
            if (GamePad.Held(PadFor(a))) return true;
            switch (a)
            {
                case GameAction.Run: return Input.GetKey(KeyCode.LeftShift);
                case GameAction.Guard: return Input.GetKey(KeyCode.LeftControl);
                case GameAction.Attack: return Input.GetMouseButton(0);
                case GameAction.HeavyAttack: return MouseLook && Input.GetMouseButton(1);
                case GameAction.Spell: return Input.GetKey(KeyCode.Q);
                case GameAction.Sight: return Input.GetKey(KeyCode.LeftAlt);
                default: return false;
            }
        }

        /// <summary>Movement from keys or the left stick, whichever is pushed further (never summed).</summary>
        public static Vector2 Move()
        {
            if (Suppressed) return Vector2.zero;
            if (UseVirtual) return Vector2.ClampMagnitude(VirtualMove, 1);
            var keys = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1);
            var stick = GamePad.LeftStick;
            if (keys.sqrMagnitude > 0 && Input.anyKey) lastKeyboardFrame = Time.frameCount;
            return stick.sqrMagnitude > keys.sqrMagnitude ? stick : keys;
        }

        /// <summary>Camera orbit delta in degrees: right stick, mouse movement in hunting style, or right mouse drag.</summary>
        public static Vector2 Look()
        {
            if (Suppressed || UseVirtual) return Vector2.zero;
            var d = Vector2.zero;
            var mouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            if (MouseLook ? Cursor.lockState == CursorLockMode.Locked : Input.GetMouseButton(1)) d += mouse * 3f * MouseSensitivity;
            if (d.sqrMagnitude > 0) lastKeyboardFrame = Time.frameCount;
            d += GamePad.RightStick * PadLookSpeed * Time.unscaledDeltaTime;
            if (InvertY) d.y = -d.y;
            return d;
        }

        public static float Zoom() => Suppressed || UseVirtual ? 0 : Input.mouseScrollDelta.y;
    }
}
