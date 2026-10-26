using System.Collections.Generic;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The Grave Warden's crypt (0.9), built from the supplied hallway and lair pictures
    /// (Docs/Concepts/Characters-0.9/warden-hallway.png, warden-lair.png):
    /// the entrance stairs; a hallway of wet cobbles between skull pedestals with fire braziers, crimson banners, hanging
    /// chains, candles, barred side arches and two knight statues, ending at the great horned-skull door under the
    /// inscription "THE DEAD OBEY THE WARDEN" (the door and the inscription are the picture's own, see
    /// Tools/make_lair_textures.py); steps down into the round lair: a rune circle of ring paving, pillars with banners
    /// and chains, braziers, barred alcoves with coffins, ground fog, and at the far end stairs up to the altar between
    /// two giant knight statues. Crypt-local coordinates: x across, z from the entrance (south) to the altar (north).
    /// The 3D props made from the art (Models/Lair/*) are used when present, simple shapes otherwise.</summary>
    public static class WardenLair
    {
        public const float HallFloor = .6f;                   // the hallway stands above the lair; steps go down at the door
        public static readonly Vector3 LairCentre = new Vector3(0, 0, 6);
        public const float LairRadius = 12.5f, WallRadius = 14f, HallHalfWidth = 4f, HallStart = -28f, DoorZ = -10f;
        const string Tex = "Environment/WardenLair/";

        static Transform g;
        static Vector3 o;
        static readonly Dictionary<string, Mesh> tiled = new Dictionary<string, Mesh>();

        public static void Build(Transform root, WorldRefs refs)
        {
            o = refs.CryptOrigin; tiled.Clear();
            g = new GameObject("Warden's Crypt - instanced dungeon").transform; g.SetParent(root); g.position = o;
            Hallway();
            Door();
            Lair();
            Altar();
            Fog();
        }

        // ---------------- materials ----------------
        static Material Textured(string name, string tex, float gloss, bool cutout = false, Color? tint = null)
        {
            if (WorldKit.Materials.TryGetValue(name, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { name = name, color = tint ?? Color.white };
            m.mainTexture = Resources.Load<Texture2D>(Tex + tex);
            m.SetFloat("_Glossiness", gloss);
            if (cutout)
            {
                m.SetFloat("_Mode", 1); m.SetFloat("_Cutoff", .5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450;
                m.SetInt("_Cull", 0);
            }
            WorldKit.Materials[name] = m;
            return m;
        }

        static Material Floor => Textured("Crypt cobbles", "cobble_floor", .55f);          // wet: the braziers glint in it
        static Material Wall => Textured("Crypt wall stone", "wall_stone", .12f);
        static Material Stone => Textured("Crypt dressed stone", "wall_stone", .2f, false, new Color(.82f, .78f, .74f));
        static Material Iron => WorldKit.Mat("Crypt iron", new Color(.16f, .15f, .15f), .7f);
        static Material Bronze => WorldKit.Mat("Crypt bronze", new Color(.45f, .3f, .15f), .8f);
        static Material Wax => WorldKit.Mat("Candle wax", new Color(.86f, .8f, .66f));
        static Material Bone => WorldKit.Mat("Ossuary bone", new Color(.66f, .6f, .5f));
        static Material Dark => WorldKit.Mat("Crypt dark", new Color(.04f, .04f, .05f));

        /// <summary>A box whose stone texture keeps its size in the world (tileMeters per repeat) whatever the box's size.</summary>
        static GameObject TiledBox(string name, Vector3 local, Vector3 size, Material mat, float tileMeters, bool solid = true, Quaternion? rot = null)
        {
            var b = WorldKit.Box(name, o + local, size, mat, g, solid);
            if (rot.HasValue) b.transform.rotation = rot.Value;
            string key = size.ToString("F2") + "/" + tileMeters;
            if (!tiled.TryGetValue(key, out var mesh))
            {
                var src = b.GetComponent<MeshFilter>().sharedMesh;
                mesh = Object.Instantiate(src); mesh.name = "Tiled box " + key;
                var v = mesh.vertices; var n = mesh.normals; var uv = new Vector2[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var p = Vector3.Scale(v[i], size);
                    var a = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                    uv[i] = (a.x > .5f ? new Vector2(p.z, p.y) : a.y > .5f ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y)) / tileMeters;
                }
                mesh.uv = uv;
                tiled[key] = mesh;
            }
            b.GetComponent<MeshFilter>().sharedMesh = mesh;
            return b;
        }

        static GameObject Quad(string name, Vector3 local, Vector2 size, Material mat, Quaternion rot)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name; q.transform.SetParent(g, false);
            q.transform.position = o + local; q.transform.rotation = rot; q.transform.localScale = new Vector3(size.x, size.y, 1);
            q.GetComponent<Renderer>().sharedMaterial = mat;
            WorldKit.RemoveCollider(q);
            return q;
        }

        /// <summary>A 3D prop made from the concept art (Models/Lair/&lt;name&gt;) at a height, or null when it was not made.</summary>
        static Transform Prop(string name, Vector3 local, float height, float yaw)
        {
            if (!CharacterFactory.Exists("Lair/" + name)) return null;
            var holder = new GameObject(name).transform; holder.SetParent(g, false);
            holder.position = o + local; holder.rotation = Quaternion.Euler(0, yaw, 0);
            var v = CharacterFactory.Create("Lair/" + name, holder, height);
            foreach (var a in v.GetComponentsInChildren<Animation>()) a.enabled = false;
            return holder;
        }

        // ---------------- fire ----------------
        static void Fire(Vector3 local, float size, float light, Transform parent = null)
        {
            var f = new GameObject("Brazier fire").transform; f.SetParent(parent ? parent : g, false); f.position = o + local;
            var ps = f.gameObject.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = .7f; main.startSpeed = .9f * size; main.startSize = .55f * size; main.maxParticles = 60;
            main.startColor = new Color(1f, .62f, .25f, .9f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -.15f;
            var em = ps.emission; em.rateOverTime = 34;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .22f * size; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1, .85f, .5f), 0), new GradientColorKey(new Color(1, .45f, .12f), .45f), new GradientColorKey(new Color(.5f, .15f, .05f), 1) },
                         new[] { new GradientAlphaKey(.0f, 0), new GradientAlphaKey(.95f, .12f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .3f));
            var r = f.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FireMat;
            var l = new GameObject("Brazier light").AddComponent<Light>(); l.transform.SetParent(f, false); l.transform.localPosition = Vector3.up * .5f;
            l.type = LightType.Point; l.color = new Color(1f, .55f, .22f); l.range = 9 * light; l.intensity = 2.4f * light; l.shadows = LightShadows.None;
            f.gameObject.AddComponent<BrazierFlame>().Light = l;
        }

        static Material FireMat
        {
            get
            {
                const string n = "Brazier flame";
                if (WorldKit.Materials.TryGetValue(n, out var m) && m) return m;
                m = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = n, mainTexture = Resources.Load<Texture2D>(Tex + "flame") };
                WorldKit.Materials[n] = m; return m;
            }
        }

        static void Brazier(Vector3 local, float yaw)
        {
            var p = Prop("SkullPedestal", local, 1.9f, yaw);
            if (!p)
            {
                TiledBox("Skull pedestal", local + Vector3.up * .55f, new Vector3(.8f, 1.1f, .8f), Stone, 1.2f, true, Quaternion.Euler(0, yaw, 0));
                TiledBox("Pedestal cap", local + Vector3.up * 1.16f, new Vector3(.95f, .12f, .95f), Stone, 1.2f, true, Quaternion.Euler(0, yaw, 0));
                var skull = WorldKit.Orb("Carved skull", local + Vector3.up * .62f + Quaternion.Euler(0, yaw, 0) * Vector3.forward * .4f, .17f, Bone, g);
                skull.transform.localScale = new Vector3(.3f, .34f, .16f);
                WorldKit.Cylinder("Brazier bowl", local + Vector3.up * 1.32f, .42f, .22f, Iron, g);
                WorldKit.Cylinder("Brazier coals", local + Vector3.up * 1.43f, .34f, .04f, WorldKit.Glow(new Color(1f, .35f, .08f), 2.5f), g);
            }
            Fire(local + Vector3.up * 1.48f, 1f, 1f);
        }

        static void Candles(Vector3 local, int count, float spread)
        {
            var rng = new System.Random((int)(local.x * 31 + local.z * 7));
            for (int i = 0; i < count; i++)
            {
                var p = local + new Vector3((float)rng.NextDouble() * 2 - 1, 0, (float)rng.NextDouble() * 2 - 1) * spread;
                float h = .12f + (float)rng.NextDouble() * .25f;
                WorldKit.Cylinder("Candle", p + Vector3.up * h * .5f, .045f, h, Wax, g);
                WorldKit.Orb("Candle flame", p + Vector3.up * (h + .05f), .035f, WorldKit.Glow(new Color(1f, .7f, .3f), 4), g);
            }
            var l = new GameObject("Candle light").AddComponent<Light>(); l.transform.SetParent(g, false); l.transform.position = o + local + Vector3.up * .5f;
            l.type = LightType.Point; l.color = new Color(1f, .65f, .3f); l.range = 3.5f; l.intensity = 1.2f;
        }

        static void Banner(Vector3 local, float yaw, float height = 3.4f)
        {
            var mat = Textured("Crimson banner", "banner", .05f, true);
            Quad("Banner", local, new Vector2(height * .5f, height), mat, Quaternion.Euler(0, yaw, 0));
            TiledBox("Banner rod", local + Vector3.up * (height * .5f + .05f), new Vector3(height * .58f, .06f, .06f), Iron, 1, false, Quaternion.Euler(0, yaw, 0));
        }

        /// <summary>A chain hung between two points, sagging.</summary>
        static void Chain(Vector3 a, Vector3 b, float sag)
        {
            var go = new GameObject("Hanging chain"); go.transform.SetParent(g, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 16; lr.widthMultiplier = .14f; lr.textureMode = LineTextureMode.Tile;
            lr.sharedMaterial = Textured("Chain links", "chain", .4f, true);
            for (int i = 0; i < 16; i++)
            {
                float t = i / 15f;
                lr.SetPosition(i, o + Vector3.Lerp(a, b, t) + Vector3.down * sag * 4 * t * (1 - t));
            }
        }

        /// <summary>A barred arch in a wall: a dark recess, iron bars and, behind them, a coffin or a pile of bones.</summary>
        static void BarredAlcove(Vector3 local, float yaw, bool coffin)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            var back = rot * Vector3.forward;
            WorldKit.Box("Alcove darkness", o + local + back * .55f + Vector3.up * 1.6f, new Vector3(2.2f, 3.2f, .1f), Dark, g).transform.rotation = rot;
            for (int i = 0; i < 7; i++)
            {
                var bar = WorldKit.Cylinder("Iron bar", o + local + rot * new Vector3(-.9f + i * .3f, 1.6f, 0), .03f, 3.1f, Iron, g);
            }
            TiledBox("Bar rail", local + Vector3.up * 3.1f, new Vector3(2.1f, .08f, .08f), Iron, 1, false, rot);
            if (coffin)
            {
                var c = Prop("Sarcophagus", local + back * 1.1f, .9f, yaw + 90);
                if (!c) TiledBox("Coffin", local + back * 1.1f + Vector3.up * .4f, new Vector3(1.9f, .8f, .9f), Stone, 1, false, rot);
            }
            else for (int i = 0; i < 9; i++)
                    WorldKit.Orb("Skull", o + local + back * 1.1f + rot * new Vector3(-.6f + (i % 3) * .55f, .2f + (i / 3) * .3f, 0), .14f, Bone, g);
        }

        // ---------------- the hallway ----------------
        static void Hallway()
        {
            float len = DoorZ - HallStart, mid = (DoorZ + HallStart) / 2;
            TiledBox("Hallway floor", new Vector3(0, HallFloor - .25f, mid), new Vector3(HallHalfWidth * 2 + 2, .5f, len + 2), Floor, 2.4f);
            TiledBox("Hallway ceiling", new Vector3(0, HallFloor + 7.5f, mid), new Vector3(HallHalfWidth * 2 + 2, .5f, len + 2), Wall, 3);
            foreach (int s in new[] { -1, 1 })
            {
                TiledBox("Hallway wall", new Vector3(s * (HallHalfWidth + .4f), HallFloor + 3.5f, mid), new Vector3(.8f, 7.5f, len + 2), Wall, 2.5f);
                for (int i = 0; i < 4; i++)
                {
                    float z = HallStart + 3 + i * 4.6f;
                    TiledBox("Hallway pillar", new Vector3(s * (HallHalfWidth - .2f), HallFloor + 3.5f, z), new Vector3(.9f, 7f, .9f), Stone, 1.5f);
                    if (i < 3)
                    {
                        Brazier(new Vector3(s * (HallHalfWidth - 1.1f), HallFloor, z + 2.3f), s > 0 ? -90 : 90);
                        Banner(new Vector3(s * (HallHalfWidth - .02f), HallFloor + 4.6f, z + 2.3f), s > 0 ? -90 : 90);
                        Candles(new Vector3(s * (HallHalfWidth - .6f), HallFloor, z + 1.2f), 5, .25f);
                    }
                    if (i < 3) Chain(new Vector3(s * (HallHalfWidth - .2f), HallFloor + 6.6f, z), new Vector3(s * (HallHalfWidth - .2f), HallFloor + 6.6f, z + 4.6f), .9f);
                }
                BarredAlcove(new Vector3(s * (HallHalfWidth - .05f), HallFloor, HallStart + 5.3f), s > 0 ? 90 : -90, false);
                // knight statues flanking the door, facing the hallway
                KnightStatue(new Vector3(s * 2.9f, HallFloor, DoorZ - 1.4f), 180, 3.4f);
            }
            TiledBox("Entrance wall", new Vector3(0, HallFloor + 3.5f, HallStart - 1), new Vector3(HallHalfWidth * 2 + 2, 7.5f, .8f), Wall, 2.5f);
            for (int i = 0; i < 3; i++)                      // stairs up to the graveyard, against the entrance wall
            {
                float top = .3f * (i + 1);
                TiledBox("Entrance step", new Vector3(0, HallFloor + top / 2, HallStart + .9f - i * .45f), new Vector3(3f, top, .45f), Stone, 1, false);
            }
        }

        static Transform KnightStatue(Vector3 local, float yaw, float height)
        {
            var t = Prop("KnightStatue", local + Vector3.up * .45f, height, yaw);
            TiledBox("Statue plinth", local + Vector3.up * .22f, new Vector3(1.4f, .45f, 1.4f), Stone, 1.2f);
            if (t) return t;
            // stand-in: a hooded figure with a greatsword point-down
            var rot = Quaternion.Euler(0, yaw, 0);
            TiledBox("Statue body", local + Vector3.up * (.45f + height * .4f), new Vector3(.9f, height * .8f, .6f), Stone, 1, true, rot);
            WorldKit.Orb("Statue head", o + local + Vector3.up * (.45f + height * .88f), .26f, Stone, g);
            TiledBox("Statue sword", local + Vector3.up * (.45f + height * .35f) + rot * Vector3.forward * .42f, new Vector3(.12f, height * .7f, .05f), Stone, 1, false, rot);
            TiledBox("Statue guard", local + Vector3.up * (.45f + height * .66f) + rot * Vector3.forward * .42f, new Vector3(.6f, .1f, .1f), Stone, 1, false, rot);
            return null;
        }

        // ---------------- the great door ----------------
        static void Door()
        {
            float w = 4.4f, h = 6f, y0 = HallFloor;
            // the wall the door is set in, with the doorway left open between the jambs
            foreach (int s in new[] { -1, 1 })
                TiledBox("Door wall", new Vector3(s * (w / 2 + (HallHalfWidth + 1 - w / 2) / 2 + .4f), y0 + 3.75f, DoorZ), new Vector3(HallHalfWidth + 1 - w / 2 + .8f, 7.5f, .8f), Wall, 2.5f);
            TiledBox("Door lintel wall", new Vector3(0, y0 + h + .75f, DoorZ), new Vector3(w + .2f, 1.5f, .8f), Wall, 2.5f);
            var inscription = Textured("The dead obey the Warden", "inscription", .3f);
            Quad("Inscription: THE DEAD OBEY THE WARDEN", new Vector3(0, y0 + h + .55f, DoorZ - .42f), new Vector2(5.6f, .53f), inscription, Quaternion.Euler(0, 0, 0));
            var door = new GameObject("Great door").AddComponent<GreatDoor>(); door.transform.SetParent(g, false);
            door.transform.position = o + new Vector3(0, y0 + 1.2f, DoorZ - .8f);
            door.Id = "warden_door"; door.Range = 3.2f;
            foreach (int s in new[] { -1, 1 })
            {
                var hinge = new GameObject(s < 0 ? "Left leaf hinge" : "Right leaf hinge").transform; hinge.SetParent(g, false);
                hinge.position = o + new Vector3(s * w / 2, y0, DoorZ);
                var leaf = WorldKit.Box(s < 0 ? "Left door leaf" : "Right door leaf", hinge.position + new Vector3(-s * w / 4, h / 2, 0), new Vector3(w / 2, h, .25f), Iron, hinge, true);
                var face = s < 0 ? LeafMat("Great door left", 0) : LeafMat("Great door right", .5f);
                var q = Quad("Door face", leaf.transform.position - o + new Vector3(0, 0, -.13f), new Vector2(w / 2, h), face, Quaternion.identity);
                q.transform.SetParent(leaf.transform, true);
                var back = Quad("Door back", leaf.transform.position - o + new Vector3(0, 0, .13f), new Vector2(w / 2, h), face, Quaternion.Euler(0, 180, 0));
                back.transform.SetParent(leaf.transform, true);
                if (s < 0) door.LeftHinge = hinge; else door.RightHinge = hinge;
            }
        }

        static Material LeafMat(string name, float offset)
        {
            if (WorldKit.Materials.TryGetValue(name, out var m) && m) return m;
            m = Object.Instantiate(Textured("Great door", "great_door", .35f)); m.name = name;
            m.mainTextureScale = new Vector2(.5f, 1); m.mainTextureOffset = new Vector2(offset, 0);
            WorldKit.Materials[name] = m; return m;
        }

        // ---------------- the lair ----------------
        static void Lair()
        {
            var c = LairCentre;
            TiledBox("Lair floor", c + new Vector3(0, -.25f, 0), new Vector3(WallRadius * 2 + 2, .5f, WallRadius * 2 + 2), Floor, 2.6f);
            TiledBox("Lair ceiling", c + new Vector3(0, 11f, 0), new Vector3(WallRadius * 2 + 2, .5f, WallRadius * 2 + 2), Wall, 3);
            var rune = Quad("Rune circle", c + Vector3.up * .015f, new Vector2(LairRadius * 1.8f, LairRadius * 1.8f), Textured("Rune circle", "rune_circle", .5f, true), Quaternion.Euler(90, 0, 0));
            // steps down from the door into the lair
            TiledBox("Door landing", new Vector3(0, HallFloor * .5f, DoorZ + .2f), new Vector3(7f, HallFloor, .8f), Stone, 1.2f);
            for (int i = 0; i < 2; i++)
            {
                float top = HallFloor - .2f * (i + 1);
                TiledBox("Lair step", new Vector3(0, top / 2, DoorZ + 1f + i * .8f), new Vector3(7f, top, .8f), Stone, 1.2f);
            }
            int n = 16;
            for (int k = 0; k < n; k++)
            {
                float a0 = k * Mathf.PI * 2 / n, a1 = (k + 1) * Mathf.PI * 2 / n;
                Vector3 p0 = c + new Vector3(Mathf.Sin(a0), 0, -Mathf.Cos(a0)) * WallRadius, p1 = c + new Vector3(Mathf.Sin(a1), 0, -Mathf.Cos(a1)) * WallRadius;
                Vector3 mid = (p0 + p1) / 2; float seg = Vector3.Distance(p0, p1);
                var rot = Quaternion.LookRotation(c - mid);
                bool doorway = k == 0 || k == n - 1;            // the south wall opens onto the steps from the door
                bool altar = k == 7 || k == 8;                  // the north wall opens onto the altar
                if (!doorway && !altar) TiledBox("Lair wall", mid + Vector3.up * 5.5f + (mid - c).normalized * .5f, new Vector3(seg + .4f, 11f, .9f), Wall, 2.5f, true, rot);
                var pil = p0 + (c - p0).normalized * .6f;
                TiledBox("Lair pillar", pil + Vector3.up * 5f, new Vector3(1.3f, 10f, 1.3f), Stone, 1.5f, true, rot);
                if (k % 2 == 1 && !altar) Banner(pil + (c - pil).normalized * .67f + Vector3.up * 5.4f, Quaternion.LookRotation(pil - c).eulerAngles.y + 180, 4.2f);
                var pNext = p1 + (c - p1).normalized * .6f;
                if (!doorway && !altar) Chain(pil + Vector3.up * 8.6f, pNext + Vector3.up * 8.6f, 1.4f);
                if (!doorway && !altar && k % 2 == 0) BarredAlcove(mid + (c - mid).normalized * .1f, rot.eulerAngles.y + 180, k % 4 == 0);
                if (!doorway && !altar && k % 2 == 1)
                {
                    Brazier(c + (pil - c).normalized * (WallRadius - 2.2f), rot.eulerAngles.y);
                    Candles(c + (mid - c).normalized * (WallRadius - 1.4f), 6, .35f);
                }
                if (!doorway && !altar && k % 3 == 0)            // rubble along the wall foot
                    for (int r = 0; r < 4; r++)
                        TiledBox("Rubble", mid + (c - mid).normalized * (1 + r * .35f) + new Vector3(r * .3f - .4f, .15f, r % 2 * .4f), new Vector3(.5f, .3f, .4f) * (1 - r * .15f), Stone, 1, true, Quaternion.Euler(r * 17, r * 41, r * 9));
            }
            // the south wall either side of the steps
            foreach (int s in new[] { -1, 1 })
                TiledBox("Lair south wall", new Vector3(s * (3.5f + (WallRadius - 3.5f) / 2), 5.5f, DoorZ - .2f), new Vector3(WallRadius - 3.5f, 11f, .8f), Wall, 2.5f);
            TiledBox("Lair south wall over the door", new Vector3(0, HallFloor + 6f + 2.5f, DoorZ - .2f), new Vector3(7f, 5f, .8f), Wall, 2.5f);
        }

        static void Altar()
        {
            var c = LairCentre;
            float z0 = c.z + 9.2f;                               // foot of the altar stairs
            for (int i = 0; i < 5; i++)
            {
                float stepTop = .3f * (i + 1);
                TiledBox("Altar step", new Vector3(0, stepTop / 2, z0 + i * .7f), new Vector3(8f - i * .4f, stepTop, .7f), Stone, 1.2f);
            }
            float top = 1.5f, zd = z0 + 5.25f;
            TiledBox("Altar dais", new Vector3(0, top / 2, zd), new Vector3(10f, top, 4.2f), Stone, 1.5f);
            var sar = Prop("Sarcophagus", new Vector3(0, top, zd), 1.2f, 0);
            if (!sar)
            {
                TiledBox("Altar sarcophagus", new Vector3(0, top + .55f, zd), new Vector3(2.8f, 1.1f, 1.5f), Stone, 1);
                TiledBox("Sarcophagus lid", new Vector3(0, top + 1.18f, zd), new Vector3(3f, .16f, 1.7f), Stone, 1);
            }
            Candles(new Vector3(-2.2f, top, zd - 1.2f), 7, .4f);
            Candles(new Vector3(2.2f, top, zd - 1.2f), 7, .4f);
            foreach (int s in new[] { -1, 1 })
            {
                KnightStatue(new Vector3(s * 4.1f, top, zd + .6f), 180, 6.2f);
                Brazier(new Vector3(s * 2.9f, top, zd - 1.5f), 180);
                Banner(new Vector3(s * 6.4f, top + 4.4f, zd + 1.95f), 180, 4.6f);
            }
            TiledBox("Altar back wall", new Vector3(0, 5.5f, zd + 2.4f), new Vector3(16f, 11f, .8f), Wall, 2.5f);
            // a gothic arch over the sarcophagus, the window behind it glowing faintly
            TiledBox("Arch left", new Vector3(-1.9f, top + 3f, zd + 1.9f), new Vector3(.5f, 6f, .5f), Stone, 1.2f);
            TiledBox("Arch right", new Vector3(1.9f, top + 3f, zd + 1.9f), new Vector3(.5f, 6f, .5f), Stone, 1.2f);
            TiledBox("Arch top", new Vector3(0, top + 6.3f, zd + 1.9f), new Vector3(4.4f, .6f, .5f), Stone, 1.2f);
            WorldKit.Box("Arch window", o + new Vector3(0, top + 3.6f, zd + 2.0f), new Vector3(3.2f, 5.2f, .05f), WorldKit.Glow(new Color(.35f, .4f, .55f), .6f), g);
            foreach (int s in new[] { -1, 1 })
                TiledBox("Altar side wall", new Vector3(s * 8f, 5.5f, zd + .15f), new Vector3(.8f, 11f, 4.5f), Wall, 2.5f);
        }

        // ---------------- ground fog ----------------
        static void Fog()
        {
            foreach (var (centre, size) in new[] { (LairCentre, new Vector3(LairRadius * 2, .1f, LairRadius * 2)), (new Vector3(0, HallFloor, (DoorZ + HallStart) / 2), new Vector3(HallHalfWidth * 2, .1f, DoorZ - HallStart)) })
            {
                var f = new GameObject("Ground fog"); f.transform.SetParent(g, false); f.transform.position = o + centre + Vector3.up * .25f;
                var ps = f.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = 9; main.startSpeed = .08f; main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 4f); main.maxParticles = 140;
                main.startColor = new Color(.55f, .5f, .48f, .16f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
                var em = ps.emission; em.rateOverTime = 14;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .3f), new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) });
                col.color = grad;
                var r = f.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = FogMat; r.sortingFudge = 10;
            }
        }

        static Material FogMat
        {
            get
            {
                const string n = "Crypt ground fog";
                if (WorldKit.Materials.TryGetValue(n, out var m) && m) return m;
                m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { name = n, mainTexture = Resources.Load<Texture2D>(Tex + "fog_puff") };
                WorldKit.Materials[n] = m; return m;
            }
        }
    }
}
