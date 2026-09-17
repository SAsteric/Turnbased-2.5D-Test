using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 5.5f, -11f);
    [Tooltip("Fallback value. The LIVE value comes from GameManager.settings.cameraSmoothTime.")]
    public float smoothTime = 0.25f;

    private Vector3 velocity;

    private void LateUpdate()
    {
        if (target == null) return;
        float smooth = (GameManager.Instance != null)
            ? Mathf.Max(0.01f, GameManager.Instance.settings.cameraSmoothTime)
            : smoothTime;

        Vector3 desired = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smooth);
    }
}