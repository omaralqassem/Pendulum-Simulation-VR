using UnityEngine;

public class BoxPureMouseInteraction : MonoBehaviour
{
    [Header("Selection Settings")]
    [Tooltip("If true, you must click near the box to grab it. If false, you can click anywhere.")]
    [SerializeField] private bool mustClickNearBox = true;
    [Tooltip("The grab radius in screen pixels around the center of the box.")]
    [SerializeField] private float grabRadiusPixels = 250f;

    [Header("Translation Settings")]
    [SerializeField] private float dragSmoothing = 15f;
    [SerializeField] private float scrollSpeed = 8f;
    [SerializeField] private float minDistance = 5f;
    [SerializeField] private float maxDistance = 100f;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 5f;

    private Camera mainCamera;
    private float targetDistance;
    private Vector3 dragOffset;
    private bool isDragging = false;
    private bool isRotating = false;
    private Vector3 targetPosition;

    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("No Main Camera found in the scene. Please ensure your camera is tagged as 'MainCamera'.");
        }
        targetPosition = transform.position;
    }

    void Update()
    {
        HandleInput();
    }

    void HandleInput()
    {
        if (mainCamera == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (CanInteract())
            {
                isDragging = true;
                
                // Calculate the perpendicular planar depth instead of straight-line distance
                Vector3 toBox = transform.position - mainCamera.transform.position;
                targetDistance = Vector3.Dot(toBox, mainCamera.transform.forward);
                
                Vector3 mouseWorldPos = GetMouseWorldPosition(targetDistance);
                dragOffset = transform.position - mouseWorldPos;
            }
        }

        if (Input.GetMouseButton(0) && isDragging)
        {
            // Adjust distance (depth) using scroll wheel while dragging
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            targetDistance = Mathf.Clamp(targetDistance + scroll * scrollSpeed, minDistance, maxDistance);

            Vector3 mouseWorldPos = GetMouseWorldPosition(targetDistance);
            targetPosition = mouseWorldPos + dragOffset;

            // Interpolate position smoothly to prevent sudden physics frame breaks
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * dragSmoothing);
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (CanInteract())
            {
                isRotating = true;
            }
        }

        if (Input.GetMouseButton(1) && isRotating)
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
            float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed;

            // Rotate relative to the camera's axes
            transform.Rotate(mainCamera.transform.up, -mouseX, Space.World);
            transform.Rotate(mainCamera.transform.right, mouseY, Space.World);
        }

        if (Input.GetMouseButtonUp(1))
        {
            isRotating = false;
        }
    }

    // Checks if the click is close enough to the box's screen projection center
    private bool CanInteract()
    {
        if (!mustClickNearBox) return true;

        Vector3 screenPos = mainCamera.WorldToScreenPoint(transform.position);
        
        // Ensure the box is in front of the camera frustum
        if (screenPos.z > 0)
        {
            float distanceInPixels = Vector2.Distance(Input.mousePosition, screenPos);
            return distanceInPixels <= grabRadiusPixels;
        }

        return false;
    }

    private Vector3 GetMouseWorldPosition(float distance)
    {
        Vector3 mousePoint = Input.mousePosition;
        mousePoint.z = distance;
        return mainCamera.ScreenToWorldPoint(mousePoint);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}