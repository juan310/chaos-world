using System.Collections.Generic;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>One boss area attack (HuntBoss): a ground telegraph that grows during the wind-up, then the blow: a
    /// circle, a cone, eruptions, a leap's landing, a roar, or shockwave rings that travel outward (only the wave front
    /// hits, so a roll through it evades). It strikes the hunter (i-frames, guards and counters apply as for any monster
    /// blow) and the Briarhound. A wind-up interrupted by a flinch or topple cancels it.</summary>
    public class MonsterAoe : MonoBehaviour
    {
        public static readonly List<MonsterAoe> Active = new List<MonsterAoe>();
        public MonsterActor Owner;
        public MonsterAttackDef Attack;
        public Vector3 Origin, Forward;
        public bool FollowOwner;            // centred on the monster when it goes off (slams, sweeps, roars, breath)
        public float FireAt, FiredAt = -1;
        public bool Fired => FiredAt >= 0;
        /// <summary>Blows that reached the hunter (evaded ones, through a roll's i-frames, are counted apart).</summary>
        public int HunterHits { get; private set; }
        public int HunterEvaded { get; private set; }
        public int HoundHits { get; private set; }
        /// <summary>The newest shockwave's radius now (0 before it goes off).</summary>
        public float WaveRadius => Fired ? HuntBoss.WaveRadius(Attack, Time.time - FiredAt) : 0;

        GameSession S;
        float startedAt;
        GameObject decal;
        readonly List<LineRenderer> waves = new List<LineRenderer>();
        readonly HashSet<int> hunterPulses = new HashSet<int>(), houndPulses = new HashSet<int>();

        public static MonsterAoe Begin(GameSession s, MonsterActor owner, MonsterAttackDef a, Vector3 origin, Vector3 forward, float fireIn, bool followOwner)
        {
            var go = new GameObject("Boss area: " + a.id);
            var x = go.AddComponent<MonsterAoe>();
            x.S = s; x.Owner = owner; x.Attack = a; x.Origin = origin; x.Forward = forward; x.FollowOwner = followOwner;
            x.startedAt = Time.time; x.FireAt = Time.time + fireIn;
            x.BuildDecal();
            Active.Add(x);
            return x;
        }

        void OnDestroy() => Active.Remove(this);

        static Material Warn => WorldKit.Transparent("Telegraph ring", new Color(.95f, .18f, .08f, .35f));
        static Material RoarWarn => WorldKit.Transparent("Roar warning", new Color(1f, .7f, .15f, .22f));
        static Material Wave => WorldKit.Transparent("Shockwave", new Color(1f, .5f, .15f, .6f));

        void BuildDecal()
        {
            var shape = Attack.Shape;
            if (shape == AoeShape.Cone)
            {
                decal = new GameObject("Cone warning");
                var mf = decal.AddComponent<MeshFilter>(); decal.AddComponent<MeshRenderer>().sharedMaterial = Warn;
                var m = new Mesh(); int seg = 16; var v = new Vector3[seg + 2]; var tri = new int[seg * 3];
                v[0] = Vector3.zero;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = (-Attack.aoeAngle * .5f + Attack.aoeAngle * i / seg) * Mathf.Deg2Rad;
                    v[i + 1] = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * Attack.aoeLength;
                }
                for (int i = 0; i < seg; i++) { tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = i + 2; }
                m.vertices = v; m.triangles = tri; m.RecalculateNormals(); mf.sharedMesh = m;
            }
            else
            {
                float r = shape == AoeShape.Ring ? Attack.aoeRadius : Attack.aoeRadius;
                decal = WorldKit.Cylinder("Area warning", Origin, r, .02f, shape == AoeShape.Roar ? RoarWarn : Warn);
            }
            decal.transform.SetParent(transform, true);
            PlaceDecal(0);
        }

        void PlaceDecal(float grow)
        {
            if (!decal) return;
            var o = FollowOwner && Owner ? Owner.transform.position : Origin;
            var f = FollowOwner && Owner ? Owner.transform.forward : Forward;
            o.y = S.GroundHeight(o) + .04f;
            decal.transform.position = o;
            if (Attack.Shape == AoeShape.Cone) { decal.transform.rotation = Quaternion.LookRotation(new Vector3(f.x, 0, f.z)); decal.transform.localScale = Vector3.one * Mathf.Lerp(.3f, 1, grow); }
            else
            {
                // A shockwave's warning shows its full reach faintly; the others grow from a third to full size.
                float k = Attack.Shape == AoeShape.Ring ? 1 : Mathf.Lerp(.3f, 1, grow);
                float d = Attack.aoeRadius * 2 * k;
                decal.transform.localScale = new Vector3(d, .01f, d);
            }
        }

        void Update()
        {
            if (!S || !S.Simulating) return;
            float now = Time.time;
            if (!Fired)
            {
                // Interrupted before it went off (flinched, toppled, killed): no blow.
                bool winding = Owner && Owner.Alive && Owner.Brain.CurrentAttack == Attack && (Owner.Brain.Mode == MonsterMode.Telegraph || Owner.Brain.Mode == MonsterMode.Attack);
                if (!winding && Attack.Shape != AoeShape.Burst && Attack.Shape != AoeShape.Leap) { Destroy(gameObject); return; }
                if (!winding && now < FireAt - Attack.aoeDelay) { Destroy(gameObject); return; }
                PlaceDecal(Mathf.Clamp01((now - startedAt) / Mathf.Max(.05f, FireAt - startedAt)));
                if (now >= FireAt) Fire();
                return;
            }
            if (Attack.Shape == AoeShape.Ring) Waves(now - FiredAt);
            float total = Attack.Shape == AoeShape.Ring ? (Attack.pulses - 1) * Attack.pulseGap + HuntBoss.Lifetime(Attack) : .6f;
            if (now - FiredAt > total) Destroy(gameObject);
        }

        void Fire()
        {
            FiredAt = Time.time;
            if (FollowOwner && Owner) { Origin = Owner.transform.position; Forward = Owner.transform.forward; }
            if (decal) decal.SetActive(Attack.Shape == AoeShape.Ring);   // the ring warning stays under the waves
            var shape = Attack.Shape;
            float dist = S.Hunter ? Vector3.Distance(S.Hunter.transform.position, Origin) : 99;
            S.CameraRig.Shake(Mathf.Lerp(.45f, .08f, Mathf.Clamp01(dist / 18)));
            GamePad.Rumble(.3f, .5f, .15f);
            if (shape == AoeShape.Roar)
            {
                WorldKit.Sound(Origin, "chime", .6f);
                for (int i = 0; i < 3; i++) WorldKit.Burst(Origin + Vector3.up * (1.5f + i * .4f), new Color(1f, .8f, .4f), 10);
                S.Hud?.Flash(Owner ? Owner.DisplayName + " roars!" : "Roar!");
                var h = S.Hunter;
                if (h && !h.Dead && Hit(h.transform.position, 0)) StrikeHunter(h, () => h.Roared(Attack, Origin));
                var hound = S.Hound;
                if (hound && !hound.Ridden && Hit(hound.transform.position, 0)) { hound.Cower(Attack.roarTime); HoundHits++; }
                return;
            }
            WorldKit.Burst(Origin + Vector3.up * .2f, new Color(.75f, .6f, .45f), shape == AoeShape.Burst ? 14 : 22);
            WorldKit.Sound(Origin, "hit", .45f);
            if (shape != AoeShape.Ring) Strike(0, 0);
        }

        bool Hit(Vector3 p, float t) => HuntBoss.Hits(Attack, Origin.x, Origin.z, Forward.x, Forward.z, p.x, p.z, t);

        void Strike(int pulse, float t)
        {
            var h = S.Hunter;
            if (h && !h.Dead && !hunterPulses.Contains(pulse) && Hit(h.transform.position, t))
            {
                hunterPulses.Add(pulse);
                StrikeHunter(h, () => h.ReceiveHit(Attack, Origin));
            }
            var hound = S.Hound;
            if (hound && !hound.Ridden && !houndPulses.Contains(pulse) && Hit(hound.transform.position, t))
            {
                houndPulses.Add(pulse); HoundHits++;
                hound.TakeBlow(Attack, Origin);
            }
        }

        void StrikeHunter(HunterController h, System.Action blow)
        {
            float evaded = h.LastDodgeEvade;
            blow();
            if (h.LastDodgeEvade > evaded) HunterEvaded++; else HunterHits++;
        }

        void Waves(float t)
        {
            for (int p = 0; p < Attack.pulses; p++)
            {
                float tp = t - p * Attack.pulseGap;
                if (p >= waves.Count && tp >= 0) waves.Add(NewWave());
                if (p >= waves.Count) break;
                var lr = waves[p];
                float r = HuntBoss.WaveRadius(Attack, tp);
                bool live = tp >= 0 && tp <= HuntBoss.Lifetime(Attack);
                lr.enabled = live;
                if (!live) continue;
                for (int i = 0; i < lr.positionCount; i++)
                {
                    float a = i * Mathf.PI * 2 / lr.positionCount;
                    var q = Origin + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                    q.y = S.GroundHeight(q) + .15f;
                    lr.SetPosition(i, q);
                }
                if (p > 0 && tp < .02f) { S.CameraRig.Shake(.2f); WorldKit.Sound(Origin, "hit", .35f); }
                Strike(p, tp);
            }
        }

        LineRenderer NewWave()
        {
            var go = new GameObject("Shockwave"); go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Wave; lr.loop = true; lr.positionCount = 56; lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = Attack.aoeWidth * .7f;
            lr.startColor = lr.endColor = new Color(1f, .55f, .2f, .75f);
            return lr;
        }
    }
}
