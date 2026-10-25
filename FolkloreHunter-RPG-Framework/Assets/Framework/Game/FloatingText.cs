using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    public class FloatingText : MonoBehaviour
    {
        float life = .9f;
        void LateUpdate()
        {
            life -= Time.deltaTime;
            transform.position += Vector3.up * Time.deltaTime * .9f;
            var cam = Camera.main;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            if (life < 0) Destroy(gameObject);
        }
    }
}
