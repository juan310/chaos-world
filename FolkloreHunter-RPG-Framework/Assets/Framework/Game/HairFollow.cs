using UnityEngine;

namespace FH.Game
{
    /// <summary>Keeps a hairstyle on the head bone without being a child of it: it follows the bone's position and turn
    /// every frame but not its scale (an exported bone can carry a scale that is not the same on every axis, which squashes
    /// a child; the hair stays its own shape).</summary>
    public class HairFollow : MonoBehaviour
    {
        public Transform Head;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;

        public void Bind(Transform head)
        {
            Head = head;
            LocalPosition = head.InverseTransformPoint(transform.position);
            LocalRotation = Quaternion.Inverse(head.rotation) * transform.rotation;
        }

        void LateUpdate()
        {
            if (!Head) return;
            transform.SetPositionAndRotation(Head.TransformPoint(LocalPosition), Head.rotation * LocalRotation);
        }
    }
}
