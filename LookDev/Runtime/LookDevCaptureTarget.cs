using UnityEngine;

namespace UnityGraphicsLab.LookDev
{
    public sealed class LookDevCaptureTarget : MonoBehaviour
    {
        [SerializeField] private string targetId;

        public string TargetId => targetId;
        public Renderer Renderer => GetComponent<Renderer>();

        public void SetTargetId(string value)
        {
            targetId = value;
        }
    }
}
