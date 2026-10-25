using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>--input-log: records real device input as Unity's Input sees it, with the game's response, so a
    /// session driven by OS-level key/mouse messages can be audited afterwards.</summary>
    public class InputLogger : MonoBehaviour
    {
        StreamWriter log;
        static readonly KeyCode[] keys = { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.LeftShift, KeyCode.Space, KeyCode.E, KeyCode.Q, KeyCode.Tab, KeyCode.Escape, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.M, KeyCode.Return, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.Alpha1 };

        void Start()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "input-log.txt"));
            log = new StreamWriter(path, false) { AutoFlush = true };
            log.WriteLine("Real input log  " + DateTime.Now.ToString("u") + "  focused=" + Application.isFocused);
        }

        void Update()
        {
            var s = GameSession.I;
            foreach (var k in keys)
            {
                if (Input.GetKeyDown(k)) Write("down " + k, s);
                if (Input.GetKeyUp(k)) Write("up   " + k, s);
            }
            if (Input.GetMouseButtonDown(0)) Write("mouse left down at " + Input.mousePosition, s);
            if (Input.GetMouseButtonDown(1)) Write("mouse right down at " + Input.mousePosition, s);
        }

        void Write(string what, GameSession s)
        {
            if (log == null || s == null) return;
            var h = s.Hunter;
            log.WriteLine(Time.frameCount + "  " + what.PadRight(34) + " | mode " + s.Mode + " | ui " + (s.Ui.Describe() == "" ? "-" : s.Ui.Describe())
                + " | pos " + h.transform.position.ToString("F1") + " | swing " + h.Attack.SwingId + " " + h.Attack.Phase + (h.Dodging ? " DODGE" : ""));
        }

        void OnDestroy() => log?.Dispose();
    }
}
