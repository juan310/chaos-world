using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FH.Game
{
    [Flags]
    public enum PadButton
    {
        None = 0, DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Start = 0x0010, Back = 0x0020,
        LeftStick = 0x0040, RightStick = 0x0080, LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
        LT = 0x10000, RT = 0x20000     // triggers past their threshold, folded into the same mask
    }

    /// <summary>Xbox controller through Windows XInput (xinput1_4.dll, present on every Windows 10/11 machine): exact
    /// buttons, both triggers, both sticks with the standard deadzones, and rumble. No Unity package is needed.
    /// Automated checks can inject a pad state through <see cref="Inject"/>, which drives the same mapping.</summary>
    public static class GamePad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
        [StructLayout(LayoutKind.Sequential)]
        struct XVibration { public ushort left, right; }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("xinput1_4.dll")] static extern int XInputGetState(int index, out XState state);
        [DllImport("xinput1_4.dll")] static extern int XInputSetState(int index, ref XVibration vibration);
#else
        static int XInputGetState(int index, out XState state) { state = default; return 1167; }
        static int XInputSetState(int index, ref XVibration vibration) => 1167;
#endif

        const int LeftDeadzone = 7849, RightDeadzone = 8689, TriggerThreshold = 30;
        static int frame = -1, pad = -1, searchCooldown;
        static PadButton now, before;
        static Vector2 leftStick, rightStick;
        static float lt, rt;
        static bool available = true;
        static float rumbleLow, rumbleHigh, rumbleUntil;
        static PadButton? injected;
        static Vector2 injectedLeft, injectedRight;

        public static bool Connected { get { Poll(); return pad >= 0 || injected.HasValue; } }
        /// <summary>Last frame (Time.frameCount) on which the pad was touched: drives keyboard vs controller prompts.</summary>
        public static int LastUsedFrame { get; private set; } = -1;

        /// <summary>Replaces the real pad with a scripted state (null returns control to the hardware).</summary>
        public static void Inject(PadButton? buttons, Vector2 left = default, Vector2 right = default)
        {
            injected = buttons; injectedLeft = left; injectedRight = right;
        }

        static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            before = now;
            if (injected.HasValue)
            {
                now = injected.Value; leftStick = injectedLeft; rightStick = injectedRight;
                lt = (now & PadButton.LT) != 0 ? 1 : 0; rt = (now & PadButton.RT) != 0 ? 1 : 0;
            }
            else ReadHardware();
            if (now != PadButton.None || leftStick.sqrMagnitude > 0 || rightStick.sqrMagnitude > 0) LastUsedFrame = frame;
            if (rumbleUntil > 0 && Time.unscaledTime > rumbleUntil) { rumbleUntil = 0; SetMotors(0, 0); }
        }

        static void ReadHardware()
        {
            now = PadButton.None; leftStick = rightStick = Vector2.zero; lt = rt = 0;
            if (!available) return;
            try
            {
                // An unplugged slot costs a driver call; look for a new pad only every 60 frames.
                if (pad < 0)
                {
                    if (searchCooldown-- > 0) return;
                    searchCooldown = 60;
                    for (int i = 0; i < 4 && pad < 0; i++) if (XInputGetState(i, out _) == 0) pad = i;
                    if (pad < 0) return;
                }
                if (XInputGetState(pad, out var s) != 0) { pad = -1; return; }
                now = (PadButton)s.buttons;
                lt = s.lt > TriggerThreshold ? s.lt / 255f : 0; rt = s.rt > TriggerThreshold ? s.rt / 255f : 0;
                if (lt > 0) now |= PadButton.LT;
                if (rt > 0) now |= PadButton.RT;
                leftStick = Stick(s.lx, s.ly, LeftDeadzone); rightStick = Stick(s.rx, s.ry, RightDeadzone);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                available = false;
                Debug.Log("FH_PAD XInput unavailable: " + e.Message);
            }
        }

        /// <summary>Radial deadzone, rescaled so the stick still reaches full speed at the rim.</summary>
        static Vector2 Stick(short x, short y, int deadzone)
        {
            var v = new Vector2(x, y);
            float m = v.magnitude;
            if (m <= deadzone) return Vector2.zero;
            return v / m * Mathf.Clamp01((m - deadzone) / (32767f - deadzone));
        }

        public static bool Held(PadButton b) { Poll(); return (now & b) == b && b != PadButton.None; }
        public static bool Down(PadButton b) { Poll(); return (now & b) == b && (before & b) != b && b != PadButton.None; }
        public static bool Up(PadButton b) { Poll(); return (now & b) != b && (before & b) == b && b != PadButton.None; }
        public static Vector2 LeftStick { get { Poll(); return leftStick; } }
        public static Vector2 RightStick { get { Poll(); return rightStick; } }
        public static float LeftTrigger { get { Poll(); return lt; } }
        public static float RightTrigger { get { Poll(); return rt; } }

        /// <summary>Rumble: low = heavy left motor, high = light right motor (0..1), for a duration in real seconds.</summary>
        public static void Rumble(float low, float high, float seconds)
        {
            if (!InputRouter.RumbleEnabled || injected.HasValue) return;
            rumbleLow = Mathf.Max(rumbleUntil > Time.unscaledTime ? rumbleLow : 0, low);
            rumbleHigh = Mathf.Max(rumbleUntil > Time.unscaledTime ? rumbleHigh : 0, high);
            rumbleUntil = Mathf.Max(rumbleUntil, Time.unscaledTime + seconds);
            SetMotors(rumbleLow, rumbleHigh);
        }

        public static void StopRumble() { rumbleUntil = 0; SetMotors(0, 0); }

        static void SetMotors(float low, float high)
        {
            if (pad < 0 || !available) return;
            var v = new XVibration { left = (ushort)(Mathf.Clamp01(low) * 65535), right = (ushort)(Mathf.Clamp01(high) * 65535) };
            try { XInputSetState(pad, ref v); } catch (Exception) { available = false; }
        }
    }
}
