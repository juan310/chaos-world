using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    public class Spark : MonoBehaviour
    {
        public Vector3 velocity; public float life = .55f;
        void Update()
        {
            life -= Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            velocity += Vector3.down * Time.deltaTime * 2;
            transform.localScale *= Mathf.Exp(-Time.deltaTime * 4);
            if (life < 0) Destroy(gameObject);
        }
    }
}
