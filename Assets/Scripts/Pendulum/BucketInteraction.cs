using UnityEngine;

public class BucketInteractionCustom : MonoBehaviour
{
    private BucketPhysics bucketPhysics;
    private RopeControllerRealistic ropeController;

    [Header("Automatic Start Settings")]
    [Tooltip("If true, the bucket starts from the custom offset below and swings on its own.")]
    public bool useAutoStart = true;
    [Tooltip("Position offset from the natural hanging rest point where the bucket starts.")]
    public Vector3 startingOffset = new Vector3(2.5f, 0.5f, 0f);
    [Tooltip("Optional initial push force vector applied to the bucket on start.")]
    public Vector3 initialPushVelocity = Vector3.zero;

    [Header("Manual Keyboard Grab Settings")]
    [Tooltip("Key used to grab and manually swing/throw the bucket during runtime.")]
    public KeyCode grabKey = KeyCode.Space;
    public float keyboardDragSpeed = 6.0f;
    public float maxDragDistance = 3.5f;
    public float velocitySmoothingFactor = 12.0f;

    private bool isDragging = false;
    private bool autoStarted = false;
    private Vector3 anchorPosition;
    private Vector3 targetDragPosition;
    private Vector3 lastDragPosition;
    private Vector3 smoothedDragVelocity;

    void Start()
    {
        bucketPhysics = GetComponent<BucketPhysics>();
        if (bucketPhysics != null)
        {
            ropeController = bucketPhysics.ropeController;
        }
    }

    void Update()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        anchorPosition = ropeController.whatTheRopeIsConnectedTo.position;

        //  automatic release on the first active frame
        if (useAutoStart && !autoStarted)
        {
            autoStarted = true;
            InitializeAutoStart();
        }

        HandleKeyboardInteraction();
    }

    private void InitializeAutoStart()
    {
        float ropeLength = (ropeController.allRopeSections.Count - 1) * 0.5f; // approximate rest length
        Vector3 restPosition = anchorPosition + Vector3.down * ropeLength;
        Vector3 startPosition = restPosition + startingOffset;

        int sectionCount = ropeController.allRopeSections.Count;

        // Linearly distribute all rope segments between the anchor and start position.
        // This simulates pulling the rope taut before letting it go.
        for (int i = 0; i < sectionCount; i++)
        {
            float t = (float)i / (sectionCount - 1);
            var section = ropeController.allRopeSections[i];
            
            // Interpolate position from the bucket (index 0) to the anchor (last index)
            section.pos = Vector3.Lerp(startPosition, anchorPosition, t);
            section.vel = Vector3.zero;
            
            ropeController.allRopeSections[i] = section;
        }

        // Apply any initial push velocity (shove) directly to the bucket segment
        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel = initialPushVelocity;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void HandleKeyboardInteraction()
    {
        float timeStep = Time.deltaTime;
        float ropeLength = ropeController.allRopeSections.Count * 0.5f;
        Vector3 restPosition = anchorPosition + Vector3.down * ropeLength;

        // Grab
        if (Input.GetKeyDown(grabKey))
        {
            isDragging = true;
            targetDragPosition = ropeController.allRopeSections[0].pos;
            lastDragPosition = targetDragPosition;
            smoothedDragVelocity = Vector3.zero;
        }

        // Hold and Drag
        if (isDragging && Input.GetKey(grabKey))
        {
            float horizontalInput = Input.GetAxisRaw("Horizontal");
            float verticalInput = Input.GetAxisRaw("Vertical");

            Vector3 moveDirection = new Vector3(horizontalInput, 0f, verticalInput).normalized;
            targetDragPosition += moveDirection * keyboardDragSpeed * timeStep;

            Vector3 displacement = targetDragPosition - anchorPosition;
            if (displacement.magnitude > maxDragDistance)
            {
                targetDragPosition = anchorPosition + displacement.normalized * maxDragDistance;
            }

            var bottomSection = ropeController.allRopeSections[0];
            bottomSection.pos = targetDragPosition;

            if (timeStep > 0.0001f)
            {
                Vector3 instantaneousVelocity = (targetDragPosition - lastDragPosition) / timeStep;
                smoothedDragVelocity = Vector3.Lerp(smoothedDragVelocity, instantaneousVelocity, velocitySmoothingFactor * timeStep);
            }

            bottomSection.vel = Vector3.zero;
            ropeController.allRopeSections[0] = bottomSection;
            lastDragPosition = targetDragPosition;
        }

        // Release
        if (Input.GetKeyUp(grabKey) && isDragging)
        {
            isDragging = false;

            var bottomSection = ropeController.allRopeSections[0];
            bottomSection.vel = smoothedDragVelocity;
            ropeController.allRopeSections[0] = bottomSection;
        }
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}