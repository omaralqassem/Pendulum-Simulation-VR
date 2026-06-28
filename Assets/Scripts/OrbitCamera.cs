using UnityEngine;

public class CameraOrbit : MonoBehaviour
{
    [Header("Orbit Target")]
    [Tooltip("The object the camera will focus on (e.g., your bucket or paint canvas).")]
    public Transform target;
    public Vector3 targetOffset = Vector3.zero;

    [Header("Distance & Zoom")]
    public float distance = 15.0f;
    public float minDistance = 2.0f;
    public float maxDistance = 40.0f;
    public float zoomSpeed = 5.0f;

    [Header("Orbit Speeds")]
    public float xSpeed = 120.0f;
    public float ySpeed = 120.0f;

    [Header("Angle Limits")]
    [Tooltip("Prevent the camera from flipping upside down.")]
    public float yMinLimit = -20.0f;
    public float yMaxLimit = 80.0f;

    [Header("Automatic Orbit")]
    public bool autoOrbit = true;
    public float autoOrbitSpeed = 5.0f;

    private float x = 0.0f;
    private float y = 0.0f;
    private bool isDragging = false;

    void Start()
    {
        if (target == null)
        {
            SPHSystem sph = FindFirstObjectByType<SPHSystem>();
            if (sph != null)
            {
                target = sph.transform;
                Debug.Log($"CameraOrbit: No target assigned. Defaulting focus to SPHSystem: {target.name}");
            }
        }

        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;

        UpdateCameraPosition();
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            isDragging = true;
            x += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
        }
        else
        {
            isDragging = false;
        }

        if (autoOrbit && !isDragging)
        {
            x += autoOrbitSpeed * Time.deltaTime;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        y = ClampAngle(y, yMinLimit, yMaxLimit);
        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        if (target == null) return;

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 targetPosition = target.position + targetOffset;
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + targetPosition;

        transform.rotation = rotation;
        transform.position = position;
    }

    private static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360F) angle += 360F;
        if (angle > 360F) angle -= 360F;
        return Mathf.Clamp(angle, min, max);
    }
}