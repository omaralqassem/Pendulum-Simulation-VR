using UnityEngine;

public class BucketInteractionCustom : MonoBehaviour
{
    private BucketPhysics bucketPhysics;
    private RopeControllerRealistic ropeController;

    [Header("Automatic Start Settings")]
    public bool useAutoStart = true;
    public Vector3 startingOffset = new Vector3(2.5f, 0.5f, 0f);
    public Vector3 initialPushVelocity = Vector3.zero;

    [Header("3D WASD Movement Settings")]
    [Tooltip("Hold Left Click to grab the bucket.")]
    public float grabRadius = 2.0f;

    [Tooltip("How hard WASD pushes the bucket.")]
    public float pushForce = 800.0f;

    [Tooltip("Stops the bucket from wobbling when you stop pressing WASD. (Recommended: 15 - 25)")]
    public float handDamping = 20.0f;

    [Tooltip("If true, push force scales with the bucket's weight.")]
    public bool accountForBucketMass = true;

    private Camera mainCamera;
    private bool isDragging = false;
    private bool autoStarted = false;

    void Start()
    {
        bucketPhysics = GetComponent<BucketPhysics>();
        if (bucketPhysics != null)
        {
            ropeController = bucketPhysics.ropeController;
        }

        mainCamera = Camera.main;
    }

    void Update()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        if (useAutoStart && !autoStarted)
        {
            autoStarted = true;
            InitializeAutoStart();
        }

        HandleGrabInput();
    }

    private void InitializeAutoStart()
    {
        Vector3 anchorPosition = ropeController.whatTheRopeIsConnectedTo.position;

        float sectionLength = 0.5f;
        var lengthField = ropeController.GetType().GetField("ropeSectionLength", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (lengthField != null)
        {
            sectionLength = (float)lengthField.GetValue(ropeController);
        }

        float totalRopeLength = (ropeController.allRopeSections.Count - 1) * sectionLength;
        Vector3 restPosition = anchorPosition + (Vector3.down * totalRopeLength);
        Vector3 desiredStartPos = restPosition + startingOffset;

        Vector3 ropeDirection = (desiredStartPos - anchorPosition).normalized;
        Vector3 perfectStartPosition = anchorPosition + (ropeDirection * totalRopeLength);

        int sectionCount = ropeController.allRopeSections.Count;

        for (int i = 0; i < sectionCount; i++)
        {
            float t = (float)i / (sectionCount - 1);
            var section = ropeController.allRopeSections[i];

            section.pos = Vector3.Lerp(perfectStartPosition, anchorPosition, t);
            section.vel = Vector3.zero;

            ropeController.allRopeSections[i] = section;
        }

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel = initialPushVelocity;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void HandleGrabInput()
    {
        if (mainCamera == null) return;

        // Click to Grab
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 bucketPos = ropeController.allRopeSections[0].pos;

            // Simple screen distance check for grabbing
            Vector3 screenPos = mainCamera.WorldToScreenPoint(bucketPos);
            Vector2 mousePos = Input.mousePosition;

            // If mouse is near the bucket on screen, grab it
            if (Vector2.Distance(new Vector2(screenPos.x, screenPos.y), mousePos) <= grabRadius * 50f)
            {
                isDragging = true;
            }
        }

        // Release to Throw
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        // Only apply forces if we are holding the bucket
        if (isDragging && mainCamera != null)
        {
            float timeStep = Time.fixedDeltaTime;

            // 1. Get WASD Input
            float horizontal = Input.GetAxis("Horizontal"); // A/D
            float vertical = Input.GetAxis("Vertical");     // W/S

            // 2. Convert to 3D directions based on Camera angle
            // Camera.right handles A/D (Left/Right)
            // Camera.forward handles W/S (Forward/Backward)
            Vector3 moveDirection = (mainCamera.transform.right * horizontal) + (mainCamera.transform.forward * vertical);

            // 3. Apply 3D Force
            var bottomSection = ropeController.allRopeSections[0];
            Vector3 currentVel = bottomSection.vel;

            Vector3 netAcceleration = Vector3.zero;

            // If pushing a key, add force in that 3D direction
            if (moveDirection.sqrMagnitude > 0.01f)
            {
                moveDirection.Normalize(); // Prevent diagonal speed boost
                netAcceleration += moveDirection * pushForce;
            }

            // Always apply damping to stop wobble when keys are released
            netAcceleration += -currentVel * handDamping;

            // 4. Account for Mass (so heavy paint feels heavy)
            if (accountForBucketMass && bucketPhysics != null)
            {
                float totalMass = bucketPhysics.GetTotalMass();
                netAcceleration /= Mathf.Max(0.5f, totalMass);
            }

            // 5. Apply to Verlet Rope
            bottomSection.vel += netAcceleration * timeStep;
            ropeController.allRopeSections[0] = bottomSection;
        }
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}