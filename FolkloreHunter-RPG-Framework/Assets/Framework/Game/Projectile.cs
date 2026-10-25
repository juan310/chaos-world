using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>A spell bolt: moves each frame, hits the first hurtbox it touches.</summary>
    public class Projectile : MonoBehaviour
    {
        SpellDef spell;
        StatSheet stats;
        Vector3 velocity;
        float life;
        GameSession session;

        public static Projectile Fire(GameSession s, SpellDef spell, Vector3 origin, Vector3 dir, StatSheet stats)
        {
            var color = spell.damageType == DamageType.Holy ? new Color(1f, .95f, .7f) : new Color(1f, .35f, .06f);
            var orb = WorldKit.Orb(spell.name + " bolt", origin, spell.damageType == DamageType.Holy ? .12f : .16f, WorldKit.Glow(color, 3));
            var light = orb.AddComponent<Light>();
            light.color = color; light.range = 4; light.intensity = 2;
            var p = orb.AddComponent<Projectile>();
            p.spell = spell; p.stats = stats; p.session = s; p.velocity = dir.normalized * spell.speed;
            return p;
        }

        readonly Collider[] hits = new Collider[8];

        void Update()
        {
            if (session == null || !session.Simulating) return;
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            transform.position += velocity * dt;
            life += dt;
            int n = Physics.OverlapCapsuleNonAlloc(from, transform.position, .22f, hits, Layers.HurtboxMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var box = hits[i].GetComponent<Hurtbox>();
                if (!box || !box.Owner || !box.Owner.Alive) continue;
                var monster = box.Owner;
                monster.ReceiveHit(box, transform.position, (part, broken) => DamageMath.SpellHit(spell, stats, monster.Def, part, broken), spell.statusId, spell.statusBuildup);
                WorldKit.Burst(transform.position, spell.damageType == DamageType.Holy ? new Color(1f, .95f, .7f) : new Color(1f, .38f, .08f), 12);
                Destroy(gameObject);
                return;
            }
            if (life > spell.lifetime || Physics.Linecast(from, transform.position, Layers.WorldMask, QueryTriggerInteraction.Ignore)) Destroy(gameObject);
        }
    }
}
