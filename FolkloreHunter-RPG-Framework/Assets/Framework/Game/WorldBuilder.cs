using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FH.Game
{
    /// <summary>Builds the playable valley from real, separately editable 3D objects. Geography, cottage, props and
    /// palette come from the approved 3D sample (FH3World); Thornbury, estate tiers and the crypt are new.</summary>
    public static class WorldBuilder
    {
        public static readonly Vector3 HousePos = new Vector3(-11, 0, -14);
        public static readonly Vector3 TownPos = new Vector3(-26, 0, -27);
        public static readonly Vector3 CryptEntrance = new Vector3(26, 0, 27);

        public static float RiverZ(float x) => 2 + 1.5f * Mathf.Sin(x * .07f);

        public static float Height(float x, float z)
        {
            float h = .55f * Mathf.Sin(x * .10f) * Mathf.Cos(z * .09f) + .35f * (Mathf.PerlinNoise(x * .055f + 14, z * .055f + 20) - .5f);
            h += Mathf.Max(0, z - 15) * .028f;
            float flat = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5, 14, Vector2.Distance(new Vector2(x, z), new Vector2(HousePos.x, HousePos.z))));
            float town = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(7, 13, Vector2.Distance(new Vector2(x, z), new Vector2(TownPos.x, TownPos.z))));
            h *= 1 - Mathf.Max(flat, town);
            h -= 1.35f * Mathf.Exp(-Mathf.Pow((z - RiverZ(x)) / 2.15f, 2));
            return h;
        }

        public static Vector3 Ground(float x, float z, float offset = 0) => new Vector3(x, Height(x, z) + offset, z);

        public static readonly Vector2[] Road =
        {
            new Vector2(-26, -30), new Vector2(-24, -24), new Vector2(-17, -20), new Vector2(-11, -17), new Vector2(-7, -21),
            new Vector2(0, -12), new Vector2(3, 2), new Vector2(4, 12), new Vector2(13, 20), new Vector2(24, 24)
        };

        public static float PathDistance(Vector2 p)
        {
            float best = 999;
            for (int i = 1; i < Road.Length; i++)
            {
                var v = Road[i] - Road[i - 1];
                float t = Mathf.Clamp01(Vector2.Dot(p - Road[i - 1], v) / v.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, Road[i - 1] + v * t));
            }
            return best;
        }

        static Transform root;
        static WorldRefs refs;

        public static WorldRefs Build(Transform worldRoot)
        {
            root = worldRoot;
            refs = root.gameObject.AddComponent<WorldRefs>();
            Random.InitState(4319);
            Atmosphere();
            Terrain();
            River();
            Cottage();
            Estate();
            Thornbury();
            Populate();
            HuntGrounds();
            Crypt();
            Library();
            return refs;
        }

        static void Anchor(string id, Vector3 pos, float yaw, string kind = "spawn")
        {
            var go = new GameObject("Anchor - " + id);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var a = go.AddComponent<WorldAnchor>();
            a.Id = id; a.Kind = kind;
            refs.Anchors.Add(a);
        }

        static Material M(string n, Color c) => WorldKit.Mat(n, c);

        static void Atmosphere()
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = .013f; RenderSettings.fogColor = new Color(.63f, .70f, .62f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.62f, .69f, .70f); RenderSettings.ambientEquatorColor = new Color(.40f, .45f, .34f); RenderSettings.ambientGroundColor = new Color(.23f, .24f, .20f);
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Valley Sky" };
            sky.SetColor("_SkyTint", new Color(.5f, .5f, .5f)); sky.SetColor("_GroundColor", new Color(.34f, .40f, .30f));
            sky.SetFloat("_AtmosphereThickness", .85f); sky.SetFloat("_Exposure", .95f); sky.SetFloat("_SunSize", .035f);
            RenderSettings.skybox = sky;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional; sun.color = new Color(1, .88f, .69f); sun.intensity = 1.15f;
            sun.transform.rotation = Quaternion.Euler(43, -38, 0); sun.shadows = LightShadows.Soft; sun.shadowStrength = .8f;
            RenderSettings.sun = sun;
            refs.Sun = sun;
        }

        static void Terrain()
        {
            var go = new GameObject("Terrain - sculpted meadow and ashland");
            go.transform.SetParent(root);
            const int n = 128; const float size = 100;
            var verts = new Vector3[(n + 1) * (n + 1)]; var colors = new Color[verts.Length]; var tris = new int[n * n * 6];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    float xx = (x / (float)n - .5f) * size, zz = (z / (float)n - .5f) * size; int i = z * (n + 1) + x;
                    verts[i] = Ground(xx, zz);
                    float noise = Mathf.PerlinNoise(xx * .16f + 32, zz * .16f + 41);
                    Color c = Color.Lerp(new Color(.27f, .37f, .17f), new Color(.43f, .52f, .26f), noise);
                    float ash = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(12, 29, zz)); c = Color.Lerp(c, new Color(.34f, .32f, .26f), ash * .87f);
                    float d = PathDistance(new Vector2(xx, zz)); c = Color.Lerp(new Color(.53f, .43f, .29f), c, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.0f, 2.2f, d)));
                    float plaza = Vector2.Distance(new Vector2(xx, zz), new Vector2(TownPos.x, TownPos.z));
                    if (plaza < 6.5f) c = Color.Lerp(new Color(.50f, .42f, .30f), c, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4.5f, 6.5f, plaza)));
                    float bank = Mathf.Abs(zz - RiverZ(xx)); if (bank < 3) c = Color.Lerp(new Color(.42f, .40f, .30f), c, Mathf.InverseLerp(1.5f, 3, bank));
                    colors[i] = c;
                }
            int q = 0;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) { int a = z * (n + 1) + x; tris[q++] = a; tris[q++] = a + n + 1; tris[q++] = a + 1; tris[q++] = a + 1; tris[q++] = a + n + 1; tris[q++] = a + n + 2; }
            var mesh = new Mesh { name = "Valley ground mesh", vertices = verts, colors = colors, triangles = tris };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Folklore3D/Meadow")) { name = "Continuous meadow and road" };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static void River()
        {
            var g = new GameObject("River - flowing 3D surface"); g.transform.SetParent(root);
            int n = 100; var v = new Vector3[(n + 1) * 2]; var t = new int[n * 6];
            for (int i = 0; i <= n; i++) { float x = -50 + i; v[i * 2] = new Vector3(x, -.43f, RiverZ(x) - 1.35f); v[i * 2 + 1] = new Vector3(x, -.43f, RiverZ(x) + 1.35f); }
            for (int i = 0; i < n; i++) { int a = i * 2, j = i * 6; t[j] = a; t[j + 1] = a + 1; t[j + 2] = a + 2; t[j + 3] = a + 2; t[j + 4] = a + 1; t[j + 5] = a + 3; }
            var mesh = new Mesh { name = "River ribbon", vertices = v, triangles = t }; mesh.RecalculateNormals();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Folklore3D/River")) { name = "Flowing river" };
            var bridge = new GameObject("Footbridge - individual planks and rails"); bridge.transform.SetParent(root);
            var wood = M("Bridge oak", new Color(.29f, .19f, .09f));
            float z = RiverZ(3);
            for (int i = 0; i < 15; i++) WorldKit.Box("Bridge plank " + i, new Vector3(3, -.02f, z - 3 + i * .43f), new Vector3(2.8f, .18f, .39f), wood, bridge.transform, true);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 4; i++) WorldKit.Box("Bridge post", new Vector3(3 + side * 1.42f, .47f, z - 2.8f + i * 1.9f), new Vector3(.13f, 1.1f, .13f), wood, bridge.transform, true);
                WorldKit.Box("Bridge handrail", new Vector3(3 + side * 1.42f, .97f, z), new Vector3(.11f, .11f, 6.1f), wood, bridge.transform);
            }
        }

        static void Cottage()
        {
            var o = new GameObject("Starting Cottage - separate Blender parts"); o.transform.SetParent(root);
            o.transform.position = Ground(HousePos.x, HousePos.z);
            refs.Cottage = o.transform;
            var model = Object.Instantiate(Resources.Load<GameObject>("Models/Cottage"), o.transform);
            model.name = "Blender cottage parts";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
            foreach (var mr in o.GetComponentsInChildren<MeshRenderer>())
            {
                string n = mr.name;
                if (n.StartsWith("Wall_") || n.StartsWith("Floor_") || n.StartsWith("Porch_Step")) mr.gameObject.AddComponent<MeshCollider>();
                foreach (var mat in mr.sharedMaterials) if (mat && mat.name.Contains("Glow")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(1, .48f, .08f) * 1.6f); }
            }
            refs.CottageParts = o.GetComponentsInChildren<MeshRenderer>().Select(x => x.transform).ToList();
            var door = WorldKit.Find(o.transform, "DoorPivot");
            if (door) { var cd = o.AddComponent<CottageDoor>(); cd.Door = door; }
            o.AddComponent<CottageInspector>();
            var lantern = new GameObject("Lantern warm light").AddComponent<Light>();
            lantern.transform.SetParent(o.transform, false); lantern.transform.localPosition = new Vector3(-.87f, 2.2f, -2.7f);
            lantern.type = LightType.Point; lantern.color = new Color(1, .53f, .19f); lantern.intensity = 2.3f; lantern.range = 6;
            refs.Lanterns.Add(lantern);
            var smoke = new GameObject("Chimney smoke - particles"); smoke.transform.SetParent(o.transform, false); smoke.transform.localPosition = new Vector3(-1.62f, 5.75f, .75f);
            var ps = smoke.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = 3; main.startSpeed = .32f; main.startSize = .25f; main.startColor = new Color(.52f, .53f, .49f, .38f); main.maxParticles = 20;
            var emission = ps.emission; emission.rateOverTime = 3; var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 10; shape.radius = .08f;
            var ren = ps.GetComponent<ParticleSystemRenderer>(); ren.sharedMaterial = new Material(Shader.Find("Particles/Standard Unlit")) { name = "Smoke" }; ren.sharedMaterial.color = new Color(.6f, .6f, .55f, .3f);
            Nature("bed", Ground(HousePos.x - 1.4f, HousePos.z + .8f, .38f), 1.4f, 0, false, o.transform);
            WorldKit.Box("Interior table", Ground(HousePos.x + 1.2f, HousePos.z + .8f, 1.1f), new Vector3(1.1f, .12f, .8f), M("Table oak", new Color(.32f, .19f, .08f)), o.transform, true);

            var front = o.transform.position + new Vector3(0, 0, -3);
            var rest = new GameObject("Cottage door - rest").AddComponent<BedRest>();
            rest.transform.SetParent(o.transform); rest.transform.position = front; rest.Id = "cottage_rest"; rest.Range = 2.4f;
            Anchor("spawn_home", Ground(-9, -21, .1f), 20);
            Anchor("map_home", o.transform.position, 0, "marker");

            var chest = WorldKit.Box("Hunter supply chest", Ground(-16, -11, .3f), new Vector3(.9f, .55f, .55f), M("Chest wood", new Color(.38f, .20f, .065f)), root, true);
            var lid = WorldKit.Box("Chest lid", chest.transform.position + new Vector3(0, .32f, 0), new Vector3(.92f, .12f, .57f), M("Chest wood", new Color(.38f, .20f, .065f)), chest.transform);
            var pivot = new GameObject("Lid hinge").transform; pivot.SetParent(chest.transform, true); pivot.position = chest.transform.position + new Vector3(0, .3f, .27f); lid.transform.SetParent(pivot, true);
            var sc = chest.AddComponent<SupplyChest>(); sc.Id = "home_chest"; sc.Gold = 25; sc.Lid = pivot;
            sc.Contents.Add(new ItemAmount { itemId = "potion_mooncap", count = 1 });
            sc.Contents.Add(new ItemAmount { itemId = "iron_nails", count = 2 });
        }

        /// <summary>Tier pieces are real modular meshes toggled by EstateVisuals. Tier 0 shows the collapsed lean-to;
        /// tier 1 rebuilds porch, shed, workbench and planters. Higher tiers are data hooks with no art yet.</summary>
        static void Estate()
        {
            var c = refs.Cottage.position;
            var vis = refs.Cottage.gameObject.AddComponent<EstateVisuals>();
            refs.Estate = vis;
            var oak = M("Estate fresh oak", new Color(.42f, .26f, .10f)); var old = M("Weathered planks", new Color(.30f, .26f, .21f));
            var stone = M("Ruins stone", new Color(.37f, .37f, .32f));

            var t0 = new GameObject("Estate tier 0 - collapsed lean-to").transform; t0.SetParent(refs.Cottage, true);
            for (int i = 0; i < 6; i++)
            {
                var p = WorldKit.Box("Fallen plank " + i, Ground(c.x + 4 + Random.Range(-.8f, .8f), c.z + .5f + Random.Range(-1f, 1f), .12f + i * .05f), new Vector3(.25f, .07f, 2.2f), old, t0, false);
                p.transform.rotation = Quaternion.Euler(Random.Range(-12, 12), Random.Range(0, 180), Random.Range(-20, 20));
            }
            WorldKit.Box("Leaning post", Ground(c.x + 4.6f, c.z + 1.5f, .8f), new Vector3(.18f, 1.6f, .18f), old, t0, true).transform.rotation = Quaternion.Euler(0, 0, 24);
            Nature("stump_old", Ground(c.x + 3.6f, c.z - .8f), .5f, 30, false, t0);
            vis.Groups.Add(new EstateVisuals.Group { id = "tier0", minTier = 0, maxTier = 0, root = t0.gameObject });

            var t1 = new GameObject("Estate tier 1 - restored cottage").transform; t1.SetParent(refs.Cottage, true);
            for (int i = -1; i <= 1; i += 2) WorldKit.Box("Porch post", c + new Vector3(i * 1.4f, 1.3f, -3.1f), new Vector3(.18f, 2.6f, .18f), oak, t1, true);
            WorldKit.Box("Porch beam", c + new Vector3(0, 2.6f, -3.1f), new Vector3(3.1f, .16f, .2f), oak, t1);
            for (int i = -1; i <= 1; i += 2) WorldKit.Box("Porch rail", c + new Vector3(i * 2.1f, .75f, -3.1f), new Vector3(1.2f, .1f, .1f), oak, t1);
            // Storage shed: floor, three walls, pitched roof, door frame.
            Vector3 s = Ground(c.x + 4.3f, c.z + .5f);
            WorldKit.Box("Shed floor", s + Vector3.up * .08f, new Vector3(2.4f, .16f, 2.2f), oak, t1, true);
            WorldKit.Box("Shed back wall", s + new Vector3(0, 1.1f, 1.05f), new Vector3(2.4f, 2f, .12f), oak, t1, true);
            WorldKit.Box("Shed side wall", s + new Vector3(-1.15f, 1.1f, 0), new Vector3(.12f, 2f, 2.2f), oak, t1, true);
            WorldKit.Box("Shed side wall", s + new Vector3(1.15f, 1.1f, 0), new Vector3(.12f, 2f, 2.2f), oak, t1, true);
            for (int i = -1; i <= 1; i += 2) WorldKit.Box("Shed roof panel", s + new Vector3(i * .65f, 2.35f, 0), new Vector3(1.45f, .08f, 2.6f), M("Shed shingles", new Color(.33f, .18f, .12f)), t1).transform.rotation = Quaternion.Euler(0, 0, -i * 24);
            var storage = WorldKit.Box("Storage chest", s + new Vector3(0, .45f, .45f), new Vector3(1.1f, .55f, .55f), M("Chest wood", new Color(.38f, .20f, .065f)), t1, true);
            var sc = storage.AddComponent<StorageChest>(); sc.Id = "estate_storage"; sc.Range = 2.4f;
            // Workbench.
            Vector3 w = Ground(c.x - 4.2f, c.z - 2.2f);
            WorldKit.Box("Workbench top", w + Vector3.up * .9f, new Vector3(1.8f, .12f, .8f), oak, t1, true);
            for (int i = 0; i < 4; i++) WorldKit.Box("Workbench leg", w + new Vector3((i % 2 == 0 ? -.8f : .8f), .45f, (i < 2 ? -.32f : .32f)), new Vector3(.1f, .9f, .1f), oak, t1);
            WorldKit.Box("Bench vice", w + new Vector3(.7f, 1.05f, -.2f), new Vector3(.2f, .18f, .25f), M("Iron", new Color(.25f, .25f, .27f)), t1);
            WorldKit.Box("Hand plane", w + new Vector3(-.4f, 1.0f, .1f), new Vector3(.35f, .08f, .12f), old, t1);
            var bench = new GameObject("Workbench station").AddComponent<CraftStation>(); bench.transform.SetParent(t1); bench.transform.position = w + Vector3.up * .5f;
            bench.Station = Station.Workbench; bench.Label = "your workbench"; bench.Id = "estate_workbench";
            // Planters with flowers under the front windows.
            for (int i = -1; i <= 1; i += 2)
            {
                var box = WorldKit.Box("Window planter", c + new Vector3(i * 1.9f, .35f, -2.75f), new Vector3(.9f, .3f, .3f), oak, t1);
                Nature(i < 0 ? "flower_yellowA" : "flower_purpleA", box.transform.position + Vector3.up * .15f, .35f, 0, false, t1);
            }
            for (int i = 0; i < 7; i++) Nature("fence_simple", Ground(c.x - 3 + i * 1.25f, c.z - 5.2f), .9f, 0, false, t1);
            vis.Groups.Add(new EstateVisuals.Group { id = "tier1", minTier = 1, root = t1.gameObject });

            var ledger = WorldKit.Box("Estate ledger lectern", Ground(c.x + 2.2f, c.z - 3.9f, .55f), new Vector3(.45f, 1.1f, .4f), oak, refs.Cottage, true);
            WorldKit.Box("Ledger book", ledger.transform.position + new Vector3(0, .6f, 0), new Vector3(.5f, .06f, .38f), M("Ledger leather", new Color(.35f, .12f, .08f)), ledger.transform);
            var lg = ledger.AddComponent<EstateLedger>(); lg.Id = "estate_ledger"; lg.Range = 2f;
        }

        static void Thornbury()
        {
            var town = new GameObject("Thornbury Crossing - settlement").transform; town.SetParent(root);
            var oak = M("Sign oak", new Color(.25f, .13f, .055f)); var stone = M("Ruins stone", new Color(.37f, .37f, .32f));
            var cloth = M("Stall cloth", new Color(.30f, .42f, .26f)); var iron = M("Iron", new Color(.25f, .25f, .27f));
            Anchor("map_thornbury", TownPos, 0, "marker");

            // Well at the centre of the plaza.
            var well = Ground(TownPos.x, TownPos.z);
            for (int i = 0; i < 10; i++) { float a = i * Mathf.PI * 2 / 10; WorldKit.Box("Well stone", well + new Vector3(Mathf.Cos(a) * .9f, .35f, Mathf.Sin(a) * .9f), new Vector3(.55f, .7f, .35f), stone, town, true).transform.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0); }
            for (int i = -1; i <= 1; i += 2) WorldKit.Box("Well post", well + new Vector3(i * .95f, 1.3f, 0), new Vector3(.14f, 1.6f, .14f), oak, town);
            WorldKit.Box("Well roof", well + new Vector3(0, 2.15f, 0), new Vector3(2.4f, .1f, 1.4f), M("Shed shingles", new Color(.33f, .18f, .12f)), town);

            // Mira's herb stall.
            Vector3 st = Ground(TownPos.x + 3.5f, TownPos.z + 3.2f);
            for (int i = 0; i < 4; i++) WorldKit.Box("Stall post", st + new Vector3(i % 2 == 0 ? -1.3f : 1.3f, 1.1f, i < 2 ? -.8f : .8f), new Vector3(.12f, 2.2f, .12f), oak, town, true);
            WorldKit.Box("Stall canopy", st + new Vector3(0, 2.25f, 0), new Vector3(3, .08f, 2.1f), cloth, town).transform.rotation = Quaternion.Euler(-10, 0, 0);
            WorldKit.Box("Stall counter", st + new Vector3(0, .5f, -.55f), new Vector3(2.5f, 1f, .6f), oak, town, true);
            for (int i = 0; i < 5; i++) WorldKit.Cylinder("Herb jar", st + new Vector3(-1 + i * .5f, 1.12f, -.55f), .1f, .24f, WorldKit.Mat("Jar " + i, Color.Lerp(new Color(.5f, .7f, .4f), new Color(.7f, .45f, .6f), i / 4f)), town);
            var mortar = new GameObject("Mira's mortar and pestle").AddComponent<CraftStation>(); mortar.transform.SetParent(town); mortar.transform.position = st + new Vector3(1.0f, 1.1f, -.55f);
            mortar.Station = Station.Alchemy; mortar.Label = "Mira's mortar and pestle"; mortar.Id = "alchemy_mira"; mortar.Range = 1.6f;
            WorldKit.Cylinder("Mortar", mortar.transform.position, .14f, .16f, stone, town);
            Npc("mira", "Herbalist", 1.75f, null, st + new Vector3(0, 0, .35f), 180, HousePos + new Vector3(-2.4f, 0, -3.6f), 160, town);

            // Bram's forge.
            Vector3 f = Ground(TownPos.x - 4.2f, TownPos.z + 3.6f);
            WorldKit.Box("Forge hearth", f + new Vector3(0, .6f, .8f), new Vector3(1.6f, 1.2f, 1.1f), stone, town, true);
            WorldKit.Box("Forge coals", f + new Vector3(0, 1.22f, .75f), new Vector3(1.0f, .08f, .6f), WorldKit.Glow(new Color(1f, .35f, .06f), 2.5f), town);
            WorldKit.Box("Forge chimney", f + new Vector3(0, 2.4f, 1.1f), new Vector3(.7f, 2.4f, .6f), stone, town);
            for (int i = 0; i < 4; i++) WorldKit.Box("Forge roof post", f + new Vector3(i % 2 == 0 ? -1.6f : 1.6f, 1.25f, i < 2 ? -1.1f : 1.4f), new Vector3(.14f, 2.5f, .14f), oak, town, true);
            WorldKit.Box("Forge roof", f + new Vector3(0, 2.55f, .15f), new Vector3(3.6f, .1f, 3f), M("Shed shingles", new Color(.33f, .18f, .12f)), town);
            WorldKit.Box("Anvil base", f + new Vector3(.2f, .3f, -.6f), new Vector3(.35f, .6f, .35f), oak, town, true);
            WorldKit.Box("Anvil", f + new Vector3(.2f, .7f, -.6f), new Vector3(.8f, .22f, .3f), iron, town);
            WorldKit.Box("Quench trough", f + new Vector3(-1.2f, .3f, -.5f), new Vector3(.5f, .5f, 1.1f), oak, town, true);
            var forgeLight = new GameObject("Forge glow").AddComponent<Light>(); forgeLight.transform.SetParent(town); forgeLight.transform.position = f + new Vector3(0, 1.6f, .4f);
            forgeLight.type = LightType.Point; forgeLight.color = new Color(1f, .45f, .15f); forgeLight.range = 6; forgeLight.intensity = 2.4f; refs.Lanterns.Add(forgeLight);
            var anvil = new GameObject("Bram's forge").AddComponent<CraftStation>(); anvil.transform.SetParent(town); anvil.transform.position = f + new Vector3(.2f, .8f, -.6f);
            anvil.Station = Station.Forge; anvil.Label = "Bram's forge"; anvil.Id = "forge_bram"; anvil.Range = 1.8f;
            Npc("bram", "Smith", 1.9f, new Color(.78f, .62f, .5f), f + new Vector3(1.2f, 0, -1.2f), 200, Vector3.zero, 0, town);

            // Hunt board.
            var board = new GameObject("Hunt board"); board.transform.SetParent(town); board.transform.position = Ground(TownPos.x + .5f, TownPos.z - 3.8f);
            WorldKit.Box("Board post", board.transform.position + new Vector3(-.7f, .9f, 0), new Vector3(.15f, 1.8f, .15f), oak, board.transform, true);
            WorldKit.Box("Board post", board.transform.position + new Vector3(.7f, .9f, 0), new Vector3(.15f, 1.8f, .15f), oak, board.transform, true);
            WorldKit.Box("Board panel", board.transform.position + Vector3.up * 1.45f, new Vector3(1.7f, .9f, .1f), oak, board.transform);
            for (int i = 0; i < 4; i++) WorldKit.Box("Posted notice", board.transform.position + new Vector3(-.55f + i * .37f, 1.45f + (i % 2) * .12f, -.07f), new Vector3(.28f, .36f, .01f), M("Parchment", new Color(.86f, .8f, .64f)), board.transform);
            WorldKit.Label("HUNT BOARD", board.transform, new Vector3(0, 2.05f, -.08f), .045f, new Color(.94f, .83f, .58f)).transform.localRotation = Quaternion.identity;
            board.transform.rotation = Quaternion.Euler(0, 180, 0);
            var cb = board.AddComponent<ContractBoard>(); cb.Id = "hunt_board"; cb.Range = 2.4f;

            // Court visitor beside her banner.
            Vector3 e = Ground(TownPos.x + 5f, TownPos.z - 2f);
            WorldKit.Box("Court banner pole", e + new Vector3(.9f, 1.6f, .6f), new Vector3(.1f, 3.2f, .1f), oak, town, true);
            WorldKit.Box("Court banner", e + new Vector3(.9f, 2.5f, .5f), new Vector3(.05f, 1.3f, .7f), M("Court purple", new Color(.32f, .16f, .38f)), town);
            Npc("lady_elowen", "Noble", 1.78f, new Color(.78f, .7f, .95f), e, 250, Vector3.zero, 0, town);

            // Lanterns around the plaza.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + .4f;
                var lp = Ground(TownPos.x + Mathf.Cos(a) * 6.5f, TownPos.z + Mathf.Sin(a) * 6.5f);
                WorldKit.Box("Lantern post", lp + Vector3.up * 1.2f, new Vector3(.12f, 2.4f, .12f), oak, town, true);
                WorldKit.Box("Lantern", lp + Vector3.up * 2.45f, new Vector3(.25f, .3f, .25f), WorldKit.Glow(new Color(1f, .7f, .3f), 2), town);
                var l = new GameObject("Lantern light").AddComponent<Light>(); l.transform.SetParent(town); l.transform.position = lp + Vector3.up * 2.4f;
                l.type = LightType.Point; l.color = new Color(1f, .62f, .25f); l.range = 7; l.intensity = 1.2f; refs.Lanterns.Add(l);
            }
            for (int i = 0; i < 8; i++) Nature("fence_simple", Ground(TownPos.x - 7 + i * 1.25f, TownPos.z - 7), .9f, 0, false, town);
            Nature("log_stack", Ground(TownPos.x - 6.5f, TownPos.z + 1), .9f, 70, true, town);
            Nature("campfire_logs", Ground(TownPos.x - 2, TownPos.z - 1.5f), .3f, 0, false, town);
        }

        static void Npc(string id, string model, float height, Color? tint, Vector3 spot, float yaw, Vector3 home, float homeYaw, Transform parent)
        {
            string use = CharacterFactory.Exists(model) ? model : (id == "bram" ? "Hunter" : "Herbalist");
            var go = new GameObject("NPC - " + id);
            go.layer = Layers.Actor;
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0, yaw, 0));
            var col = go.AddComponent<CapsuleCollider>(); col.center = Vector3.up * .85f; col.height = 1.7f; col.radius = .3f;
            var npc = go.AddComponent<NpcActor>();
            npc.NpcId = id; npc.Id = "npc_" + id; npc.Range = 2.4f;
            npc.Visual = CharacterFactory.Create(use, go.transform, height, use == model ? (Color?)null : tint);
            npc.SettlementSpot = spot; npc.SettlementYaw = yaw;
            npc.HomeSpot = home == Vector3.zero ? Vector3.zero : Ground(home.x, home.z); npc.HomeYaw = homeYaw;
            refs.Npcs.Add(npc);
        }

        public static GameObject Nature(string name, Vector3 pos, float height, float yaw, bool solid, Transform parent = null)
        {
            var prefab = Resources.Load<GameObject>("Models/Nature/" + name);
            if (!prefab) return null;
            var o = Object.Instantiate(prefab, parent ? parent : root);
            o.name = name + " - separate prop";
            o.transform.position = pos;
            var rr = o.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) return o;
            var b = rr[0].bounds; foreach (var x in rr.Skip(1)) b.Encapsulate(x.bounds);
            float scale = height / Mathf.Max(.02f, b.size.y); float bottom = b.min.y - pos.y;
            o.transform.localScale *= scale; o.transform.position -= Vector3.up * bottom * scale; o.transform.Rotate(0, yaw, 0, Space.World);
            var texture = Resources.Load<Texture2D>("Models/Nature/colormap");
            if (texture) { var atlas = M("Nature atlas", Color.white); atlas.mainTexture = texture; foreach (var r in rr) r.sharedMaterials = Enumerable.Repeat(atlas, r.sharedMaterials.Length).ToArray(); }
            // Match props to the cottage's warm wood, olive foliage and neutral stone (sample palette).
            foreach (var renderer in rr)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    Color c = original ? original.color : Color.gray; Color.RGBToHSV(c, out float hue, out float sat, out float value);
                    Color target = c;
                    if (name.StartsWith("tree_")) target = (hue > .18f && hue < .8f && sat > .12f) ? new Color(.28f, .43f, .19f) : new Color(.32f, .21f, .12f);
                    else if (name.StartsWith("rock_")) target = new Color(.40f, .42f, .36f);
                    else if (name.StartsWith("fence_")) target = new Color(.37f, .25f, .13f);
                    else if (name.StartsWith("log") || name.StartsWith("stump") || name.StartsWith("campfire")) target = value > .6f ? new Color(.60f, .45f, .27f) : new Color(.29f, .19f, .10f);
                    else if (name.StartsWith("flower_purple")) target = (hue > .6f || hue < .1f) && sat > .2f ? new Color(.55f, .32f, .62f) : new Color(.34f, .49f, .22f);
                    else if (hue > .18f && hue < .8f && sat > .12f) target = new Color(.34f, .49f, .22f);
                    target *= Mathf.Lerp(.75f, 1.05f, value);
                    return M("Palette " + name + " " + (original ? original.name : "none"), target);
                }).ToArray();
            }
            if (solid) { var c2 = o.AddComponent<CapsuleCollider>(); c2.height = height * .6f / scale; c2.radius = Mathf.Min(.7f, height * .11f) / scale; c2.center = Vector3.up * c2.height * .5f; }
            return o;
        }

        static bool Clear(float x, float z, float pathClearance)
        {
            var p = new Vector2(x, z);
            return PathDistance(p) >= pathClearance && Vector2.Distance(p, new Vector2(HousePos.x, HousePos.z)) >= 8.5f
                && Vector2.Distance(p, new Vector2(TownPos.x, TownPos.z)) >= 11 && Mathf.Abs(z - RiverZ(x)) >= 4
                && Vector2.Distance(p, new Vector2(CryptEntrance.x, CryptEntrance.z)) >= 6.5f;
        }

        static void Populate()
        {
            for (int i = 0; i < 145; i++)
            {
                float x = Random.Range(-43f, 43f), z = Random.Range(-40f, 42f);
                if (!Clear(x, z, 3.8f)) continue;
                if (z > 15 && Random.value < .75f) { DeadTree(Ground(x, z), Random.Range(3, 5f)); continue; }
                string[] types = { "tree_oak", "tree_oak_dark", "tree_simple", "tree_pineDefaultA", "tree_pineDefaultB" };
                Nature(types[i % types.Length], Ground(x, z), Random.Range(4.8f, 8.0f), Random.Range(0, 360), true);
            }
            for (int i = 0; i < 320; i++)
            {
                float x = Random.Range(-38, 38f), z = Random.Range(-34, 36f);
                if (Mathf.Abs(z - RiverZ(x)) < 2.8f || Vector2.Distance(new Vector2(x, z), new Vector2(HousePos.x, HousePos.z)) < 4 || PathDistance(new Vector2(x, z)) < 1.3f || Vector2.Distance(new Vector2(x, z), new Vector2(TownPos.x, TownPos.z)) < 8) continue;
                bool rock = z > 15 || i % 5 == 0;
                string name = rock ? (i % 2 == 0 ? "rock_largeA" : "rock_smallB") : (i % 3 == 0 ? "plant_bush" : i % 3 == 1 ? "grass_large" : "flower_yellowA");
                Nature(name, Ground(x, z), rock ? Random.Range(.35f, 1.25f) : Random.Range(.25f, .7f), Random.Range(0, 360), rock);
            }
            for (int i = 0; i < 13; i++) { float x = -18 + i * 1.25f; Nature("fence_simple", Ground(x, -9), .9f, 0, false); }
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI * .25f; Nature("rock_tallA", Ground(Mathf.Cos(a) * 45, Mathf.Sin(a) * 45), Random.Range(9, 15), a * Mathf.Rad2Deg, false); }
            int n = 0;
            foreach (var h in new[] { new Vector2(-4, -10), new Vector2(-16, -22), new Vector2(0, -8), new Vector2(8, 11), new Vector2(15, 15), new Vector2(-20, -17) })
                Gather("mooncap_herb", "mushroom_redGroup", h.x, h.y, .5f, "mooncap_" + n++);
            n = 0;
            foreach (float x in new[] { -15f, -6f, 10f, 19f })
                Gather("moonpetal", "flower_purpleA", x, RiverZ(x) - 3.1f, .45f, "moonpetal_" + n++);
        }

        static void Gather(string itemId, string prop, float x, float z, float height, string id)
        {
            var o = Nature(prop, Ground(x, z), height, Random.Range(0, 360), false);
            if (!o) return;
            var g = o.AddComponent<GatherNode>();
            g.ItemId = itemId; g.Id = id; g.Regrows = true; g.Range = 1.8f;
        }

        static void DeadTree(Vector3 p, float h)
        {
            var wood = M("Ashwood", new Color(.22f, .21f, .18f));
            var go = new GameObject("Charred tree - separate trunk and branches"); go.transform.SetParent(root); go.transform.position = p;
            WorldKit.Box("Charred trunk", p + Vector3.up * h * .5f, new Vector3(.33f, h, .33f), wood, go.transform, true);
            for (int i = 0; i < 3; i++) { var b = WorldKit.Box("Bare branch", p + new Vector3((i % 2 == 0 ? 1 : -1) * .55f, h * (.5f + i * .12f), 0), new Vector3(1.5f, .14f, .15f), wood, go.transform); b.transform.rotation = Quaternion.Euler(0, i * 70, i % 2 == 0 ? 35 : -35); }
        }

        static void HuntGrounds()
        {
            var stone = M("Ruins stone", new Color(.37f, .37f, .32f));
            var g = new GameObject("Ashen Graveyard").transform; g.SetParent(root);
            for (int i = 0; i < 9; i++) { float x = 17 + (i % 3) * 2.7f, z = 17 + (i / 3) * 3; var grave = WorldKit.Box("Grave marker " + i, Ground(x, z, .6f), new Vector3(.65f, 1.1f, .24f), stone, g, true); grave.transform.Rotate(0, Random.Range(-15, 15), Random.Range(-7, 7)); }
            var c = CryptEntrance;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2 / 12;
                if (i == 9) continue; // gap facing the graves: the way in
                var p = Ground(c.x + Mathf.Cos(angle) * 4, c.z + Mathf.Sin(angle) * 4);
                WorldKit.Box("Ruined tower block " + i, p + Vector3.up * 1.5f, new Vector3(1.5f, Random.Range(2, 4f), .7f), stone, g, true).transform.rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
            }
            // Crypt stairway entrance at the tower's heart.
            var e = Ground(c.x, c.z);
            WorldKit.Box("Crypt arch pillar", e + new Vector3(-1, 1.3f, 0), new Vector3(.5f, 2.6f, .6f), stone, g, true);
            WorldKit.Box("Crypt arch pillar", e + new Vector3(1, 1.3f, 0), new Vector3(.5f, 2.6f, .6f), stone, g, true);
            WorldKit.Box("Crypt arch lintel", e + new Vector3(0, 2.75f, 0), new Vector3(2.6f, .4f, .7f), stone, g, true);
            WorldKit.Box("Crypt doorway darkness", e + new Vector3(0, 1.2f, .05f), new Vector3(1.5f, 2.4f, .1f), M("Crypt dark", new Color(.04f, .04f, .05f)), g);
            var door = new GameObject("Crypt entrance").AddComponent<AreaDoor>(); door.transform.SetParent(g); door.transform.position = e + new Vector3(0, .1f, -.6f);
            door.TargetArea = "crypt"; door.Label = "Descend into the Warden's Crypt"; door.Id = "crypt_enter"; door.Range = 2.2f;
            Anchor("crypt_outside", Ground(c.x, c.z - 2.4f, .1f), 180);
            Anchor("map_crypt", e, 0, "marker");
            Anchor("map_bridge", Ground(3, RiverZ(3)), 0, "marker");
            // door.Destination is assigned once the crypt interior exists (see Crypt()).
        }

        static void Crypt()
        {
            var o = refs.CryptOrigin;
            var g = new GameObject("Warden's Crypt - instanced dungeon").transform; g.SetParent(root); g.position = o;
            var stone = M("Crypt stone", new Color(.30f, .30f, .28f)); var dark = M("Crypt floor", new Color(.22f, .21f, .19f));
            float hx = refs.CryptHalfSize.x, hz = refs.CryptHalfSize.y;
            WorldKit.Box("Crypt floor", o + new Vector3(0, -.25f, 0), new Vector3(hx * 2 + 1, .5f, hz * 2 + 1), dark, g, true);
            WorldKit.Box("Crypt ceiling", o + new Vector3(0, 5.3f, 0), new Vector3(hx * 2 + 1, .5f, hz * 2 + 1), stone, g, true);
            WorldKit.Box("Crypt wall north", o + new Vector3(0, 2.5f, hz + .3f), new Vector3(hx * 2 + 1, 5.5f, .6f), stone, g, true);
            WorldKit.Box("Crypt wall south", o + new Vector3(0, 2.5f, -hz - .3f), new Vector3(hx * 2 + 1, 5.5f, .6f), stone, g, true);
            WorldKit.Box("Crypt wall west", o + new Vector3(-hx - .3f, 2.5f, 0), new Vector3(.6f, 5.5f, hz * 2 + 1), stone, g, true);
            WorldKit.Box("Crypt wall east", o + new Vector3(hx + .3f, 2.5f, 0), new Vector3(.6f, 5.5f, hz * 2 + 1), stone, g, true);
            for (int i = 0; i < 5; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    WorldKit.Box("Crypt pillar", o + new Vector3(s * 5.2f, 2.5f, -10 + i * 5), new Vector3(.8f, 5f, .8f), stone, g, true);
                    if (i % 2 == 0) WorldKit.Box("Sarcophagus", o + new Vector3(s * 7.4f, .45f, -8 + i * 4.5f), new Vector3(1.1f, .9f, 2.2f), stone, g, true);
                }
            WorldKit.Box("Warden's dais", o + new Vector3(0, .15f, 11), new Vector3(7, .3f, 5), stone, g, true);
            for (int i = 0; i < 6; i++)
            {
                var tp = o + new Vector3(i % 2 == 0 ? -hx + .5f : hx - .5f, 2.6f, -12 + (i / 2) * 11);
                WorldKit.Orb("Torch flame", tp, .14f, WorldKit.Glow(new Color(1f, .5f, .15f), 3), g);
                WorldKit.Box("Torch bracket", tp + Vector3.down * .3f, new Vector3(.1f, .5f, .1f), M("Iron", new Color(.25f, .25f, .27f)), g);
                var l = new GameObject("Torch light").AddComponent<Light>(); l.transform.SetParent(g); l.transform.position = tp;
                l.type = LightType.Point; l.color = new Color(1f, .55f, .2f); l.range = 9; l.intensity = 2.2f;
            }
            Anchor("crypt_inside", o + new Vector3(0, .1f, -12), 0);
            var exit = new GameObject("Crypt exit stairs").AddComponent<AreaDoor>(); exit.transform.SetParent(g); exit.transform.position = o + new Vector3(0, .1f, -14);
            exit.TargetArea = "ashen_fields"; exit.Label = "Climb back to the graveyard"; exit.Id = "crypt_exit"; exit.Range = 2.4f;
            exit.Destination = refs.Anchor("crypt_outside");
            WorldKit.Box("Stair step", o + new Vector3(0, .2f, -14.3f), new Vector3(2.4f, .4f, .8f), stone, g, true);
            foreach (var d in Object.FindObjectsByType<AreaDoor>()) if (d.TargetArea == "crypt") d.Destination = refs.Anchor("crypt_inside");
            var chest = WorldKit.Box("Warden's coffer", o + new Vector3(5.5f, .35f, 12.5f), new Vector3(1f, .6f, .6f), M("Chest wood", new Color(.38f, .20f, .065f)), g, true);
            var sc = chest.AddComponent<SupplyChest>(); sc.Id = "crypt_coffer"; sc.Gold = 40;
            sc.Contents.Add(new ItemAmount { itemId = "grave_iron", count = 1 });
            sc.Contents.Add(new ItemAmount { itemId = "potion_mooncap", count = 2 });
        }

        /// <summary>Materials created at runtime must exist in the build with their shader variants.</summary>
        static void Library()
        {
            var lib = new GameObject("Runtime material library").AddComponent<MaterialLibrary>();
            lib.transform.SetParent(root);
            lib.Materials.Add(WorldKit.Transparent("Telegraph ring", new Color(.95f, .18f, .08f, .35f)));
            lib.Materials.Add(WorldKit.Transparent("Lock ring", new Color(1f, .82f, .3f, .55f)));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .15f, .05f), 4));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .8f, .3f)));
            lib.Materials.Add(WorldKit.Glow(new Color(.85f, .7f, .45f)));
            lib.Materials.Add(WorldKit.Glow(new Color(.8f, .15f, .08f)));
            lib.Materials.Add(WorldKit.Glow(new Color(.4f, .9f, .4f)));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .35f, .06f), 3));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .95f, .7f), 3));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .38f, .08f)));
            lib.Materials.Add(WorldKit.Glow(new Color(1f, .85f, .3f), 3));
            lib.Materials.Add(WorldKit.Glow(new Color(.85f, .75f, .2f)));
            lib.Materials.Add(WorldKit.Mat("Loot sack", new Color(.55f, .42f, .25f)));
            foreach (var r in new[] { Rarity.Uncommon, Rarity.Rare })
            {
                var c = r == Rarity.Uncommon ? new Color(.78f, 1f, .82f) : new Color(.85f, .8f, 1f);
                var m = WorldKit.Mat("Hunter Atlas blade " + r, c); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * .25f);
                m.mainTexture = Resources.Load<Texture2D>("Models/Hunter_texture");
                lib.Materials.Add(m);
            }
            var holder = new GameObject("Material holders (hidden)"); holder.transform.SetParent(lib.transform);
            foreach (var m in lib.Materials)
            {
                var h = WorldKit.Box("holder " + m.name, new Vector3(0, -200, 0), Vector3.one * .01f, m, holder.transform);
                h.GetComponent<Renderer>().enabled = false;
            }
        }
    }
}
