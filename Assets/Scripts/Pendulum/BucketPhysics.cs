using UnityEngine;

public class BucketPhysics : MonoBehaviour
{
    [Header("References")]
    public RopeControllerRealistic ropeController;

    [Header("Bucket Properties")]
    public float dryBucketMass = 2.0f;       // mass of empty bucket 
    public float bucketHeight = 0.4f;        // height of the bucket
    public float bucketRadius = 0.15f;       // base radius
    public float bucketDamping = 1.5f;       // rotational damping
    
    // The pivot is at the attachment point (top of the bucket handles)
    private float emptyBucketCenterOfMassOffset; 

    [Header("Water & Hole Properties")]
    public bool hasHole = true;
    public float currentWaterMass = 10.0f;    // Water mass 
    public float holeRadius = 0.015f;         // Hole radius 
    [Range(0f, 1f)]
    public float dischargeCoefficient = 0.62f;// Fluid exit efficiency

    [Header("Hole Configuration")]
    public Vector3 holeLocalOffset = new Vector3(0.15f, -0.4f, 0f); // Position of hole relative to pivot
    [Tooltip("Local direction the water shoots OUT of the hole. e.g., (0, -1, 0.5) shoots down and slightly forward.")]
    public Vector3 waterExitDirectionLocal = new Vector3(0f, -1f, 0.5f); 

    // Rotational tracking variables
    private Vector3 angularVelocity = Vector3.zero;
    private Quaternion bucketRotation = Quaternion.identity;
    private Vector3 lastPivotVelocity = Vector3.zero;
    private Vector3 smoothedPivotAcc = Vector3.zero;

    void Start()
    {
        bucketRotation = transform.rotation;
        // Assume empty bucket center of mass is roughly halfway down the bucket height
        emptyBucketCenterOfMassOffset = bucketHeight * 0.5f;
    }

    public float GetTotalMass()
    {
        return dryBucketMass + currentWaterMass;
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        float timeStep = Time.fixedDeltaTime;

        // Calculate water loss and reaction thrust force
        Vector3 localThrustForce = Vector3.zero;
        Vector3 localThrustTorque = Vector3.zero;
        CalculateFluidDynamics(timeStep, out localThrustForce, out localThrustTorque);

        // Apply linear thrust force directly to the bottom rope section
        ApplyThrustToRope(localThrustForce);

        //  Calculate rotational swing and apply it
        UpdateRotation(localThrustTorque, timeStep);
    }

    private void CalculateFluidDynamics(float timeStep, out Vector3 localThrustForce, out Vector3 localThrustTorque)
    {
        localThrustForce = Vector3.zero;
        localThrustTorque = Vector3.zero;

        if (!hasHole || currentWaterMass <= 0f)
        {
            currentWaterMass = Mathf.Max(0f, currentWaterMass);
            return;
        }

        float densityOfWater = 1000f; 
        float g = 9.81f;

        // Calculate water height inside bucket
        float areaBucket = Mathf.PI * (bucketRadius * bucketRadius);
        float areaHole = Mathf.PI * (holeRadius * holeRadius);
        float waterHeight = (currentWaterMass / densityOfWater) / areaBucket;
        waterHeight = Mathf.Clamp(waterHeight, 0f, bucketHeight);

        // Torricelli Law
        float exitVelocity = Mathf.Sqrt(2f * g * waterHeight);

        // Mass flow rate 
        float massFlowRate = dischargeCoefficient * densityOfWater * areaHole * exitVelocity;

        // Update mass
        currentWaterMass -= massFlowRate * timeStep;
        currentWaterMass = Mathf.Max(0f, currentWaterMass);

        // Thrust force magnitude 
        float thrustMagnitude = massFlowRate * exitVelocity;

        // Thrust vector acts in the opposite direction of water exit vector
        Vector3 exitDirNormalized = waterExitDirectionLocal.normalized;
        localThrustForce = -exitDirNormalized * thrustMagnitude;

        // Calculate local torque: r x F
        localThrustTorque = Vector3.Cross(holeLocalOffset, localThrustForce);
    }

    private void ApplyThrustToRope(Vector3 localThrustForce)
    {
        if (localThrustForce.sqrMagnitude <= 0.0001f) return;

        // Convert thrust to world coordinates based on current bucket rotation
        Vector3 worldThrustVec = bucketRotation * localThrustForce;

        // Apply acceleration to the bottom rope node
        float totalMass = GetTotalMass();
        Vector3 thrustAcceleration = worldThrustVec / totalMass;

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel += thrustAcceleration * Time.fixedDeltaTime;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void UpdateRotation(Vector3 localThrustTorque, float timeStep)
    {
        float totalMass = GetTotalMass();

        // Calculate dynamic Center of Mass (COM) offset from pivot
        // Approximation: Empty bucket COM is at 0.5 * height. Water COM is at half the water height from the bottom.
        float waterHeight = (currentWaterMass / 1000f) / (Mathf.PI * bucketRadius * bucketRadius);
        float waterCOMOffsetFromPivot = bucketHeight - (waterHeight * 0.5f); // Distance from top pivot down to water COM

        float dynamicCOMOffset = ((dryBucketMass * emptyBucketCenterOfMassOffset) + (currentWaterMass * waterCOMOffsetFromPivot)) / totalMass;

        //  Calculate dynamic Moment of Inertia (I) about the pivot point
        // Using parallel axis theorem: I = I_cm + m * d^2
        // Approximation of bucket as thin hollow cylinder, water as solid cylinder.
        float I_bucket = dryBucketMass * ((bucketRadius * bucketRadius) + (bucketHeight * bucketHeight) / 12f) + (dryBucketMass * emptyBucketCenterOfMassOffset * emptyBucketCenterOfMassOffset);
        float I_water = currentWaterMass * (3f * (bucketRadius * bucketRadius) + (waterHeight * waterHeight)) / 12f + (currentWaterMass * waterCOMOffsetFromPivot * waterCOMOffsetFromPivot);
        float totalInertia = I_bucket + I_water;

        // Prevent division by zero
        if (totalInertia < 0.01f) totalInertia = 0.01f;

        //  Smooth pivot acceleration using a low-pass filter to reject numerical jitter
        Vector3 currentPivotVel = ropeController.allRopeSections[0].vel;
        Vector3 rawPivotAcc = (currentPivotVel - lastPivotVelocity) / timeStep;
        lastPivotVelocity = currentPivotVel;

        //  Low-pass filter 
        smoothedPivotAcc = Vector3.Lerp(smoothedPivotAcc, rawPivotAcc, 4.0f * timeStep);

        // Effective acceleration (gravity + inertial frame acceleration)
        Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        Vector3 effAcc = gravity - smoothedPivotAcc;

        // Pendulum Torque (Gravity and Inertial forces acting on the Center of Mass)
        Vector3 currentCOMDir = bucketRotation * Vector3.down * dynamicCOMOffset;
        Vector3 totalForceOnCOM = totalMass * effAcc;
        Vector3 gravityTorque = Vector3.Cross(currentCOMDir, totalForceOnCOM);

        // Convert gravity torque to local space for consistency
        Vector3 localGravityTorque = Quaternion.Inverse(bucketRotation) * gravityTorque;

        //  Accumulate local torques
        Vector3 totalLocalTorque = localGravityTorque;

        // Add thrust torque if water is spraying
        if (hasHole && currentWaterMass > 0f)
        {
            totalLocalTorque += localThrustTorque;
        }

        // Torsional spring (Rope twist prevention)
        // Computes how twisted the bucket is relative to the rope direction
        Vector3 ropeDir = (ropeController.allRopeSections[1].pos - ropeController.allRopeSections[0].pos).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(ropeDir, Vector3.up).normalized;
        if (targetForward.sqrMagnitude < 0.01f)
        {
            targetForward = ropeController.whatTheRopeIsConnectedTo.forward;
        }
        Vector3 currentForward = bucketRotation * Vector3.forward;
        Vector3 yawError = Vector3.Cross(currentForward, targetForward);
        Vector3 localUp = Vector3.up; 
        
        // Add torsional spring torque directly to local torque calculations (stiffness scales with tension/mass)
        float torsionalStiffness = 5.0f * (totalMass / dryBucketMass); // Scales dynamically
        totalLocalTorque += localUp * Vector3.Dot(Quaternion.Inverse(bucketRotation) * yawError, localUp) * torsionalStiffness;

        //  Calculate local angular acceleration
        Vector3 localAngularAcc = totalLocalTorque / totalInertia;

        // Apply angular damping directly to local space
        localAngularAcc -= bucketDamping * angularVelocity;

        // Integration
        angularVelocity += localAngularAcc * timeStep;

        float angleRad = angularVelocity.magnitude * timeStep;
        if (angleRad > 0f)
        {
            Quaternion deltaRot = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, angularVelocity.normalized);
            bucketRotation = bucketRotation * deltaRot; 
        }

        // Apply orientation
        transform.rotation = bucketRotation;
    }
}