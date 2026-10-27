using System;
using System.Collections.Generic;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>The editable source of one environment: everything WorldForge makes is regenerated from this (JSON on disk,
    /// Recipes/*.json). Distances are metres, angles degrees; +Y is up and the player's route runs along +Z.
    ///
    /// Features (WFFeature.type), each shaped by its points (a path drawn through them, or a centre) and its numbers:
    ///   add     hills, mountain (masses), cliff / rockwall (a wall along the points), ledge (a shelf), arch (between two
    ///           points), ruin (broken columns and walls along the points), boulders (scattered along the points)
    ///   shape   path (the ground levelled to the points: cut through rises, filled over dips)
    ///   carve   tunnel / cave (a tube along the points, which are its floor line; flared at the mouth), chamber (a domed
    ///           room on a floor at points[0]), canyon / ravine / river / crevice (a channel cut along the points: steep,
    ///           U or V shaped; a river gets water), crater (a bowl with a rim), hole (a shaft between two points)
    /// Lights (WFLight.type): torch, torch_row (torches on a tunnel's walls), torch_pair (either side of a cave mouth),
    /// campfire, brazier_ring (braziers round a chamber), magic (a floating light), eyes (glowing eyes in the dark).
    /// Water (WFWater.type): puddles (in the low spots of a feature's floor), pool, river (along a river feature).</summary>
    [Serializable]
    public class WFRecipe
    {
        public string name = "untitled";
        public int seed = 1;
        public Vector3 min = new Vector3(-30, -8, -15), max = new Vector3(30, 28, 100);
        public float voxel = .45f;              // mesh detail: the size of one sample cell
        public float chunk = 10f;               // the world is cut into chunks this size (lighting, culling, collision)
        public WFGround ground = new WFGround();
        public List<WFFeature> features = new List<WFFeature>();
        public List<WFLight> lights = new List<WFLight>();
        public List<WFWater> water = new List<WFWater>();
        public WFAtmosphere atmosphere = new WFAtmosphere();
        public WFPalette palette = new WFPalette();
        public string quality = "Medium";       // Low, Medium, High
        public WFTest test = new WFTest();

        public WFFeature Feature(string n) => features.Find(f => f.name == n);
        public static WFRecipe FromJson(string json) => JsonUtility.FromJson<WFRecipe>(json);
        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    [Serializable]
    public class WFGround
    {
        public bool enabled = true;
        public float height = 0;                // the ground's base height
        public float hills = 2.5f;              // rolling hills: height (m)
        public float hillScale = 40f;           // and their size (m)
        public float detail = .35f;             // small bumps (m)
    }

    [Serializable]
    public class WFFeature
    {
        public string name = "";
        public string type = "path";
        public List<Vector3> points = new List<Vector3>();
        public float width = 4, height = 4, depth = 2, radius = 6, length = 0;
        public float curvature = 0;             // extra winding added to the drawn path (m, sideways)
        public float roughness = .35f;          // how far the surface is pushed in and out by noise (m)
        public float noiseScale = 1.6f;         // the size of those bumps (m)
        public float strata = .2f;              // rock layering (m)
        public float blend = 1f;                // how softly it merges with what is around it (m)
        public float floor = .08f;              // unevenness of a walkable floor (m)
        public float flare = .6f;               // tunnels: how much wider the mouth is (0 = none)
        public float spacing = 3f;              // ruins and boulders: how far apart
        public int count = 0;                   // boulders: how many
        public int seed = 0;
        public string material = "";            // rock, dirt, moss, ruin (default by type)
    }

    [Serializable]
    public class WFLight
    {
        public string type = "torch";
        public string name = "";
        public string feature = "";             // torch_row / torch_pair / brazier_ring: which feature they follow
        public Vector3 position;
        public float spacing = 7f, offset = 4f, mount = 1.75f;   // torch_row: every spacing m from offset m, mount m above the floor
        public float intensity = 1.7f, range = 8f, flicker = 1f;
        public Color color = new Color(1f, .58f, .26f);
        public int count = 4;
    }

    [Serializable]
    public class WFWater
    {
        public string type = "puddles";
        public string feature = "";
        public Vector3 position;
        public float radius = 1.2f;
        public int count = 5;
        public float level = 0;                 // pool / river: water height (river: below its rim)
        public float ripples = .5f;
        public Color color = new Color(.07f, .09f, .10f, .82f);
    }

    [Serializable]
    public class WFAtmosphere
    {
        public string time = "day";             // day, dusk, night
        public Vector3 sunAngles = new Vector3(42, 35, 0);
        public float fog = .012f;               // fog density
        public float mist = .35f;               // low mist in caves and hollows (0..1)
        public float dust = .4f;                // dust in light shafts (0..1)
        public float rain = 0f;                 // outdoor rain (0..1)
        public float drips = .5f;               // dripping water in caves (0..1)
        public bool lightShafts = true;         // light falling through cave mouths and openings
        public float wetness = .25f;            // everything a little damp (0..1)
    }

    [Serializable]
    public class WFPalette
    {
        public Color rock = new Color(.42f, .40f, .37f), dirt = new Color(.36f, .27f, .18f), moss = new Color(.24f, .31f, .14f), ruin = new Color(.55f, .52f, .46f);
        public float tiling = 2.4f;             // texture size on the surface (m)
    }

    [Serializable]
    public class WFTest
    {
        public List<string> route = new List<string>();     // features walked in order (their points)
        public List<Vector3> viewpoints = new List<Vector3>();
        public string arena = "";                          // the chamber checked for a boss fight
        public float bossRadius = 1.3f, bossHeight = 3.6f;
    }
}
