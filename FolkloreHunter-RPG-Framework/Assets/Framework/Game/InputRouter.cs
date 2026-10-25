using System.Collections.Generic;
using UnityEngine;

namespace FH.Game
{
    public enum GameAction { Attack, Spell, Dodge, Interact, LockOn, Run, Hotbar1, Hotbar2, Hotbar3, Hotbar4, Pause, Inventory, Journal, Character, Map, CycleSpell, Inspect }

    /// <summary>One place that turns devices into game actions. Menus set GameplayBlocked so no key or click
    /// leaks into combat; automated verification injects actions through the same path the keyboard uses.</summary>
    public static class InputRouter
    {
        public static bool GameplayBlocked;
        public static float MouseSensitivity = 1;
        public static bool InvertY;
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
                case GameAction.Attack: return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.JoystickButton2);
                case GameAction.Spell: return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton3);
                case GameAction.Dodge: return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton1);
                case GameAction.Interact: return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton0);
                case GameAction.LockOn: return Input.GetKeyDown(KeyCode.Tab) || Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.JoystickButton9);
                case GameAction.Hotbar1: return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.R);
                case GameAction.Hotbar2: return Input.GetKeyDown(KeyCode.Alpha2);
                case GameAction.Hotbar3: return Input.GetKeyDown(KeyCode.Alpha3);
                case GameAction.Hotbar4: return Input.GetKeyDown(KeyCode.Alpha4);
                case GameAction.Pause: return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7);
                case GameAction.Inventory: return Input.GetKeyDown(KeyCode.I);
                case GameAction.Journal: return Input.GetKeyDown(KeyCode.J);
                case GameAction.Character: return Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.C);
                case GameAction.Map: return Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.JoystickButton6);
                case GameAction.CycleSpell: return Input.GetKeyDown(KeyCode.F);
                case GameAction.Inspect: return Input.GetKeyDown(KeyCode.F2);
                default: return false;
            }
        }

        /// <summary>Gameplay action pressed this frame (false while a menu has focus).</summary>
        public static bool Down(GameAction a)
        {
            if (Suppressed) return false;
            if (virtualDown.Contains(a)) return true;
            return !UseVirtual && KeyDown(a);
        }

        /// <summary>Menu-level shortcut (Escape, I, J...) that works whether or not gameplay is blocked.</summary>
        public static bool MenuDown(GameAction a) => virtualDown.Contains(a) || (!UseVirtual && KeyDown(a));

        public static bool Held(GameAction a)
        {
            if (Suppressed) return false;
            if (virtualHeld.Contains(a)) return true;
            if (UseVirtual) return false;
            if (a == GameAction.Run) return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton8);
            return false;
        }

        public static Vector2 Move()
        {
            if (Suppressed) return Vector2.zero;
            if (UseVirtual) return Vector2.ClampMagnitude(VirtualMove, 1);
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1);
        }

        /// <summary>Camera orbit delta: right mouse drag, or the right stick when mapped.</summary>
        public static Vector2 Look()
        {
            if (Suppressed || UseVirtual) return Vector2.zero;
            var d = Vector2.zero;
            if (Input.GetMouseButton(1)) d += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
            d *= MouseSensitivity;
            if (InvertY) d.y = -d.y;
            return d;
        }

        public static float Zoom() => Suppressed || UseVirtual ? 0 : Input.mouseScrollDelta.y;
    }
}
