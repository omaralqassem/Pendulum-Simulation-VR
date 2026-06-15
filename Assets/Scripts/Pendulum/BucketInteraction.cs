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
    public float keyboardDragSpeed = 10.0f;
    public float maxDragDistance = 5.0f;

    [Tooltip("How smoothly the bucket tracks your input. Lower values make it stiffer; higher values make the bucket feel heavier and lag behind realistically.")]
    public float grabSmoothTime = 0.12f;

    [Tooltip("Smoothes out the digital on/off transitions of keyboard keys.")]
    public float inputResponseDamping = 8.0f;

    private bool isDragging = false;
    private bool autoStarted = false;
    private Vector3 anchorPosition;
    private Vector3 targetDragPosition;
    private Vector3 smoothedInputVelocity;
    private Vector3 smoothDampVelocityRef;

    private const int VELOCITY_HISTORY_SIZE = 6;
    private Vector3[] dragVelocityHistory = new Vector3[VELOCITY_HISTORY_SIZE];
    private int historyWriteIndex = 0;

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

        if (useAutoStart && !autoStarted)
        {
            autoStarted = true;
            InitializeAutoStart();
        }

        HandleInputDetection();
    }

    private void InitializeAutoStart()
    {
        float ropeLength = (ropeController.allRopeSections.Count - 1) * 0.5f;
        Vector3 restPosition = anchorPosition + Vector3.down * ropeLength;
        Vector3 startPosition = restPosition + startingOffset;

        int sectionCount = ropeController.allRopeSections.Count;

        for (int i = 0; i < sectionCount; i++)
        {
            float t = (float)i / (sectionCount - 1);
            var section = ropeController.allRopeSections[i];
            
            section.pos = Vector3.Lerp(startPosition, anchorPosition, t);
            section.vel = Vector3.zero;
            
            ropeController.allRopeSections[i] = section;
        }

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel = initialPushVelocity;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void HandleInputDetection()
    {
        if (Input.GetKeyDown(grabKey))
        {
            isDragging = true;
            targetDragPosition = ropeController.allRopeSections[0].pos;
            smoothedInputVelocity = Vector3.zero;
            smoothDampVelocityRef = ropeController.allRopeSections[0].vel;

            for (int i = 0; i < VELOCITY_HISTORY_SIZE; i++)
            {
                dragVelocityHistory[i] = smoothDampVelocityRef;
            }
        }

        if (Input.GetKeyUp(grabKey) && isDragging)
        {
            isDragging = false;
            
            var bottomSection = ropeController.allRopeSections[0];
            bottomSection.vel = GetPeakThrowVelocity();
            ropeController.allRopeSections[0] = bottomSection;
        }
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        if (isDragging)
        {
            float timeStep = Time.fixedDeltaTime;

            float horizontalInput = Input.GetAxisRaw("Horizontal");
            float verticalInput = Input.GetAxisRaw("Vertical");
            Vector3 rawInputDirection = new Vector3(horizontalInput, 0f, verticalInput).normalized;

            Vector3 targetInputVel = rawInputDirection * keyboardDragSpeed;
            smoothedInputVelocity = Vector3.Lerp(smoothedInputVelocity, targetInputVel, inputResponseDamping * timeStep);

            targetDragPosition += smoothedInputVelocity * timeStep;

            Vector3 displacement = targetDragPosition - anchorPosition;
            if (displacement.magnitude > maxDragDistance)
            {
                targetDragPosition = anchorPosition + displacement.normalized * maxDragDistance;
            }

            var bottomSection = ropeController.allRopeSections[0];
            
            Vector3 currentPos = bottomSection.pos;
            Vector3 nextPos = Vector3.SmoothDamp(
                currentPos, 
                targetDragPosition, 
                ref smoothDampVelocityRef, 
                grabSmoothTime, 
                float.PositiveInfinity, 
                timeStep
            );

            bottomSection.pos = nextPos;
            bottomSection.vel = smoothDampVelocityRef;
            
            ropeController.allRopeSections[0] = bottomSection;

            dragVelocityHistory[historyWriteIndex] = smoothDampVelocityRef;
            historyWriteIndex = (historyWriteIndex + 1) % VELOCITY_HISTORY_SIZE;
        }
    }


    private Vector3 GetPeakThrowVelocity()
    {
        Vector3 peakVelocity = Vector3.zero;
        float maxSqMagnitude = 0f;

        for (int i = 0; i < VELOCITY_HISTORY_SIZE; i++)
        {
            float sqMag = dragVelocityHistory[i].sqrMagnitude;
            if (sqMag > maxSqMagnitude)
            {
                maxSqMagnitude = sqMag;
                peakVelocity = dragVelocityHistory[i];
            }
        }

        return peakVelocity;
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}