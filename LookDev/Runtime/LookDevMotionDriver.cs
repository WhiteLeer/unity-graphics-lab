using UnityEngine;

namespace UnityGraphicsLab.LookDev
{
    public sealed class LookDevMotionDriver : MonoBehaviour
    {
        [SerializeField] private Vector3 angularVelocity = new Vector3(0f, 45f, 0f);

        private void Update()
        {
            transform.Rotate(angularVelocity * Time.deltaTime, Space.Self);
        }

        public void SetPhase(float phase)
        {
            transform.localRotation = Quaternion.Euler(0f, phase * 360f, 0f);
        }
    }
}
