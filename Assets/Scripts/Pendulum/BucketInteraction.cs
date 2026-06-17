using UnityEngine;

public class BucketInteractionCustom : MonoBehaviour
{
    private BucketPhysics bucketPhysics;
    private RopeControllerRealistic ropeController;

    [Header("Automatic Start Settings")]
    public bool useAutoStart = true;
    public Vector3 startingOffset = new Vector3(2.5f, 0.5f, 0f);
    public Vector3 initialPushVelocity = Vector3.zero;

    [Header("Realistic Mouse Grab Settings")]
    [Tooltip("How strongly your mouse pulls the bucket (spring stiffness).")]
    public float mousePullStrength = 150.0f;
    [Tooltip("How much your hand stabilizes the bucket while holding it (prevents infinite bouncing).")]
    public float handDamping = 0.85f;
    [Tooltip("How close the mouse needs to be to grab the bucket.")]
    public float grabRadius = 1.0f;

    private Camera mainCamera;
    private bool isDragging = false;
    private bool autoStarted = false;
    private float dragDepth; 

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

        HandleMouseInteraction();
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
    private void HandleMouseInteraction()
    {
        if (mainCamera == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 bucketPos = ropeController.allRopeSections[0].pos;
            
            Ray mouseRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            Vector3 closestPointOnRay = mouseRay.origin + mouseRay.direction * Vector3.Dot(mouseRay.direction, bucketPos - mouseRay.origin);
            
            if (Vector3.Distance(closestPointOnRay, bucketPos) <= grabRadius)
            {
                isDragging = true;
                
                dragDepth = Vector3.Distance(mainCamera.transform.position, bucketPos);
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        if (isDragging && mainCamera != null)
        {
            float timeStep = Time.fixedDeltaTime;

            Ray mouseRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            Vector3 targetDragPosition = mouseRay.GetPoint(dragDepth);

            var bottomSection = ropeController.allRopeSections[0];
            Vector3 currentPos = bottomSection.pos;

            Vector3 springForce = (targetDragPosition - currentPos) * mousePullStrength;
            
            bottomSection.vel += springForce * timeStep;

            
            bottomSection.vel *= handDamping;

            ropeController.allRopeSections[0] = bottomSection;
        }
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}