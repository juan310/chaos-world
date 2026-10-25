using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Writes a SightForge / Claude Eyes actor track (one JSON line per frame, the format `eyes_inspect.py track`
    /// reads) from the running 3D game, so its temporal checks run on real play: facing against travel, stride per walk
    /// cycle, jitter, dithering, pops, pose flicker and "moving while the picture does not change".
    /// Mapping to the inspector's top-down 2D model: x = world X, y = world Z (north = +Z); the facing is the rendered
    /// model's forward quantised to S/E/N/W; the "sprite" is the playing clip plus its cycle frame (16 per loop).
    /// Idle poses carry no frame number, so a body that glides while idling reads as a stuck picture.</summary>
    public class MotionTrack : MonoBehaviour
    {
        public string Path;
        const float Range = 40f;
        // Perspective camera ~6 m behind the hunter with a 60 degree field of view shows ~6.9 m of height:
        // reported as an equivalent orthographic size so the inspector's pixel thresholds mean roughly screen pixels.
        const float EquivalentOrtho = 3.46f;
        StreamWriter w;
        int frame;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void Start()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            w = new StreamWriter(Path, false, new UTF8Encoding(false));
        }

        void LateUpdate()
        {
            var s = GameSession.I;
            if (w == null || !s || s.Mode != SessionMode.Playing) return;
            var hero = s.Hunter.transform.position;
            var sb = new StringBuilder(512);
            sb.Append("{\"t\":").Append(F(Time.time)).Append(",\"frame\":").Append(frame++)
              .Append(",\"map\":\"").Append(s.CurrentArea).Append('"')
              .Append(",\"ui\":").Append(s.Ui.Top != null ? "\"" + s.Ui.Top.GetType().Name + "\"" : "null")
              .Append(",\"camera\":[").Append(F(hero.x)).Append(',').Append(F(hero.z)).Append(',').Append(F(EquivalentOrtho)).Append("]")
              .Append(",\"actors\":[");
            bool first = true;
            Actor(sb, ref first, "hunter", "Hunter", "player", "hunter", s.Hunter.transform, s.Hunter.Visual, s.Hunter.Anim, 1.8f);
            foreach (var m in s.Monsters)
                if (m && m.gameObject.activeInHierarchy && Vector3.Distance(m.transform.position, hero) < Range)
                    Actor(sb, ref first, m.InstanceKey, m.DisplayName, "monster", m.MonsterId, m.transform, m.Visual, m.Anim, m.Def.height);
            foreach (var n in s.World.Npcs)
                if (n && n.gameObject.activeInHierarchy && Vector3.Distance(n.transform.position, hero) < Range)
                    Actor(sb, ref first, "npc_" + n.NpcId, n.Def(s).name, "npc", n.NpcId, n.transform, n.Visual, n.Anim, 1.8f);
            sb.Append("]}");
            w.WriteLine(sb.ToString());
            if (frame % 30 == 0) w.Flush();   // survives a killed process (the real-input session is stopped, not quit)
        }

        static void Actor(StringBuilder sb, ref bool first, string id, string name, string role, string species, Transform body, Transform visual, AnimationDriver anim, float height)
        {
            if (!first) sb.Append(',');
            first = false;
            var p = body.position;
            var f = (visual ? visual : body).forward;
            string dir = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? (f.x > 0 ? "E" : "W") : (f.z > 0 ? "N" : "S");
            sb.Append("{\"id\":\"").Append(id).Append("\",\"name\":\"").Append(name.Replace("\"", "'")).Append("\",\"role\":\"").Append(role)
              .Append("\",\"species\":\"").Append(species).Append("\",\"x\":").Append(F(p.x)).Append(",\"y\":").Append(F(p.z))
              .Append(",\"h\":").Append(F(height)).Append(",\"w\":0.6,\"sprite\":\"").Append(Sprite(species, dir, anim)).Append("\"}");
        }

        /// <summary>walk / run / idle use the inspector's directional names; every other clip is named but carries no facing.</summary>
        static string Sprite(string who, string dir, AnimationDriver anim)
        {
            string clip = anim?.Current ?? "";
            var st = anim?.State(clip);
            int k = st == null ? 0 : Mathf.FloorToInt(Mathf.Repeat(st.normalizedTime, 1f) * 16) % 16;
            if (clip.EndsWith("Walking_A")) return who + "_walk_" + dir + "_" + k.ToString("00");
            if (clip.EndsWith("Running_A")) return who + "_run_" + dir + "_" + k.ToString("00");
            if (clip.EndsWith("Idle") || clip.EndsWith("Idle_Combat")) return who + "_idle_" + dir;
            int cut = clip.LastIndexOf('|');
            return who + "_" + (cut >= 0 ? clip.Substring(cut + 1) : clip).ToLowerInvariant() + "_" + k.ToString("00");
        }

        static string F(float v) => v.ToString("0.###", Inv);

        void OnDestroy() { w?.Flush(); w?.Dispose(); w = null; }
        void OnApplicationQuit() => OnDestroy();
    }
}
