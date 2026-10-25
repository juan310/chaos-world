using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Anything the hunter can use with the interact key. Prompts are player-facing text.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public string Id;
        public float Range = 2.2f;
        public virtual Vector3 Point => transform.position;
        public abstract string Prompt(GameSession s);
        public virtual bool Available(GameSession s) => gameObject.activeInHierarchy;
        public abstract void Use(GameSession s);
    }
}
