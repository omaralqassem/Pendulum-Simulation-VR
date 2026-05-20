using UnityEngine;

public class BucketPhysics : MonoBehaviour
{
    [Header("References")]
    public RopeControllerRealistic ropeController;

    [Header("Bucket Properties")]
    public float dryBucketMass = 2.0f;       // mass of empty bucket 
    public float bucketDamping = 1.5f;       // Rotational damping
    public float centerOfMassOffset = 0.5f;  //   center of mass 

    [Header("Water & Hole Properties")]
    public bool hasHole = true;
    public float currentWaterMass = 10.0f;    // Water mass 
    public float bucketRadius = 0.15f;        // Base radius
    public float holeRadius = 0.015f;         // Hole radius 
    [Range(0f, 1f)]
    public float dischargeCoefficient = 0.62f;// fluid exit efficiency

    [Header("Hole Offset (For Spin/Tilt)")]
    public Vector3 holeLocalOffset = new Vector3(0.05f, -0.2f, 0f);

    // Rotational tracking variables
    private Vector3 angularVelocity = Vector3.zero;
    private Quaternion bucketRotation = Quaternion.identity;
    private Vector3 lastPivotVelocity = Vector3.zero;

    void Start()
    {
        bucketRotation = transform.rotation;
    }

    public float GetTotalMass()
    {
        return dryBucketMass + currentWaterMass;
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count == 0) return;

        float timeStep = Time.fixedDeltaTime;

        //  Calculate water loss, linear thrust force, and torque
        float thrustForce = 0f;
        Vector3 thrustTorque = Vector3.zero;
        CalculateFluidDynamics(timeStep, out thrustForce, out thrustTorque);

        //  Apply linear thrust force directly to the bottom rope section
        ApplyThrustToRope(thrustForce);

        //  Calculate rotational swing and apply it to this transform
        UpdateRotation(thrustTorque, timeStep);
    }

    private void CalculateFluidDynamics(float timeStep, out float thrustForce, out Vector3 thrustTorque)
    {
        thrustForce = 0f;
        thrustTorque = Vector3.zero;

        if (!hasHole || currentWaterMass <= 0f)
        {
            currentWaterMass = Mathf.Max(0f, currentWaterMass);
            return;
        }

        float densityOfWater = 1000f; 
        float g = 9.81f;

        // calculate water height inside bucket
        float areaBucket = Mathf.PI * (bucketRadius * bucketRadius);
        float areaHole = Mathf.PI * (holeRadius * holeRadius);
        float waterHeight = (currentWaterMass / densityOfWater) / areaBucket;

        // Torricelli Law
        float exitVelocity = Mathf.Sqrt(2f * g * waterHeight);

        // mass flow rate 
        float massFlowRate = dischargeCoefficient * densityOfWater * areaHole * exitVelocity;

        // Update mass
        currentWaterMass -= massFlowRate * timeStep;
        currentWaterMass = Mathf.Max(0f, currentWaterMass);

        // thrust force magnitude 
        thrustForce = massFlowRate * exitVelocity;

        // local thrust torque
        Vector3 localThrustForce = Vector3.up * thrustForce;
        thrustTorque = Vector3.Cross(holeLocalOffset, localThrustForce);
    }

    private void ApplyThrustToRope(float thrustForce)
    {
        if (thrustForce <= 0f) return;

        //convert upward thrust to world coordinates based on current rotation
        Vector3 worldThrustVec = bucketRotation * Vector3.up * thrustForce;

        // apply acceleration directly to the bottom rope section's velocity: a = F / m
        float totalMass = GetTotalMass();
        Vector3 thrustAcceleration = worldThrustVec / totalMass;

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel += thrustAcceleration * Time.fixedDeltaTime;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void UpdateRotation(Vector3 thrustTorque, float timeStep)
    {
        // Get bottom rope node velocity to compute its acceleration
        Vector3 currentPivotVel = ropeController.allRopeSections[0].vel;
        Vector3 pivotAcc = (currentPivotVel - lastPivotVelocity) / timeStep;
        lastPivotVelocity = currentPivotVel;

        // Effective acceleration (gravity + inertial frame acceleration)
        Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        Vector3 effAcc = gravity - pivotAcc;

        // Pendulum calculations
        Vector3 currentOffset = bucketRotation * Vector3.down * centerOfMassOffset;
        Vector3 gravityTorque = Vector3.Cross(currentOffset, effAcc);

        float rSq = centerOfMassOffset * centerOfMassOffset;
        if (rSq < 0.001f) rSq = 0.001f;

        Vector3 angularAcc = gravityTorque / rSq;

        // Apply water thrust torque (converted to world space)
        if (hasHole && currentWaterMass > 0f)
        {
            Vector3 worldThrustTorque = bucketRotation * thrustTorque;
            angularAcc += worldThrustTorque / rSq;
        }

        // Apply damping
        angularAcc -= bucketDamping * angularVelocity;

        // Yaw alignment torsional spring (prevents infinite spinning)
        Vector3 ropeDir = (ropeController.allRopeSections[1].pos - ropeController.allRopeSections[0].pos).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(ropeDir, Vector3.up).normalized;
        if (targetForward.sqrMagnitude < 0.01f)
        {
            targetForward = ropeController.whatTheRopeIsConnectedTo.forward;
        }
        Vector3 currentForward = bucketRotation * Vector3.forward;
        Vector3 yawError = Vector3.Cross(currentForward, targetForward);
        Vector3 localUp = bucketRotation * Vector3.up;
        angularAcc += localUp * Vector3.Dot(yawError, localUp) * 3.0f;

        // Integrate
        angularVelocity += angularAcc * timeStep;
        float angleRad = angularVelocity.magnitude * timeStep;
        if (angleRad > 0f)
        {
            Quaternion deltaRot = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, angularVelocity.normalized);
            bucketRotation = deltaRot * bucketRotation;
        }

        transform.rotation = bucketRotation;
    }
}