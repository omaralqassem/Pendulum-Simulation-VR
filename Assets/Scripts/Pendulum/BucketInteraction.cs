using UnityEngine;

public class BucketInteractionCustom : MonoBehaviour
{
    private BucketPhysics bucketPhysics;
    private RopeControllerRealistic ropeController;

    [Header("Automatic Start Settings")]
    public bool useAutoStart = true;
    public Vector3 startingOffset = new Vector3(2.5f, 0.5f, 0f);
    public Vector3 initialPushVelocity = Vector3.zero;

    [Header("Snappy Mouse Grab Settings")]
    [Tooltip("How instantly the bucket snaps to your mouse. (Recommended: 800 - 1500 for snappy real-life feel)")]
    public float mousePullStrength = 1000.0f;

    [Tooltip("Stops the bucket from oscillating. High values make it stop instantly when your mouse stops moving. (Recommended: 15 - 25)")]
    public float handDamping = 18.0f;

    [Tooltip("How close the mouse needs to be to grab the bucket.")]
    public float grabRadius = 1.2f;

    [Tooltip("If true, the hand pull force scales with the bucket's current weight so it doesn't feel sluggish when full of paint.")]
    public bool accountForBucketMass = true;

    private Camera mainCamera;
    private bool isDragging = false;
    private bool autoStarted = false;
    
    private Plane dragPlane; 
    private Vector3 dragOffset;

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
                        float rayDistance = Vector3.Dot(bucketPos - mouseRay.origin, mouseRay.direction);
            Vector3 closestPointOnRay = mouseRay.origin + mouseRay.direction * rayDistance;
            
            if (Vector3.Distance(closestPointOnRay, bucketPos) <= grabRadius)
            {
                isDragging = true;
                
                dragPlane = new Plane(-mainCamera.transform.forward, bucketPos);
                
                if (dragPlane.Raycast(mouseRay, out float enter))
                {
                    Vector3 grabPoint = mouseRay.GetPoint(enter);
                    dragOffset = bucketPos - grabPoint;
                }
                else
                {
                    dragOffset = Vector3.zero;
                }
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
            
            if (dragPlane.Raycast(mouseRay, out float enter))
            {
                Vector3 worldMousePosition = mouseRay.GetPoint(enter);
                Vector3 targetDragPosition = worldMousePosition + dragOffset;

                var bottomSection = ropeController.allRopeSections[0];
                Vector3 currentPos = bottomSection.pos;
                Vector3 springForce = (targetDragPosition - currentPos) * mousePullStrength;
                Vector3 dampingForce = -bottomSection.vel * handDamping;
                Vector3 netAcceleration = springForce + dampingForce;
                if (accountForBucketMass && bucketPhysics != null)
                {
                    float totalMass = bucketPhysics.GetTotalMass();
                    netAcceleration /= Mathf.Max(0.5f, totalMass);
                }

                bottomSection.vel += netAcceleration * timeStep;

                ropeController.allRopeSections[0] = bottomSection;
            }
        }
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}