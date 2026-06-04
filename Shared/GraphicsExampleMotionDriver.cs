using UnityEngine;

public sealed class GraphicsExampleMotionDriver : MonoBehaviour
{
    [SerializeField] private Vector3 localCenter = new(0f, 0.85f, 5.2f);
    [SerializeField] private Vector3 axis = Vector3.right;
    [SerializeField] private float amplitude = 2.75f;
    [SerializeField] private float cyclesPerSecond = 0.45f;

    private void Start()
    {
        ApplyPose(0f);
    }

    private void Update()
    {
        ApplyPose(Time.time);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        ApplyPose(0f);
    }

    private void ApplyPose(float timeValue)
    {
        var direction = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.right;
        var offset = Mathf.Sin(timeValue * Mathf.PI * 2f * cyclesPerSecond) * amplitude;
        transform.localPosition = localCenter + direction * offset;
    }
}
