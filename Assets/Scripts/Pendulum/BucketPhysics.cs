using UnityEngine;

public class BucketPhysics : MonoBehaviour
{
    public RopeControllerRealistic ropeController;
    public SPH fluidSystem;

    [Header("Bucket Properties")]
    public float dryBucketMass = 2.0f;       
    public float bucketHeight = 0.4f;        
    public float bucketRadius = 0.15f;       
    public float bucketDamping = 0.5f;       
    public float paintDensityFactor = 0.001f;
    
    [Header("Hole & Water")]
    public bool hasHole = true;
    public float currentWaterMass = 10.0f;    
    public float holeRadius = 0.015f;         
    public float dischargeCoefficient = 0.62f;

    public Vector3 holeLocalOffset = new Vector3(0.15f, -0.4f, 0f); 
    public Vector3 waterExitDirectionLocal = new Vector3(0f, -1f, 0.5f); 

    private float emptyBucketCenterOfMassOffset; 
    private Vector3 angularVelocity = Vector3.zero;
    private Quaternion bucketRotation = Quaternion.identity;
    private Vector3 lastPivotVelocity = Vector3.zero;
    private Vector3 smoothedPivotAcc = Vector3.zero;

    void Start()
    {
        bucketRotation = transform.rotation;
        // Assuming the pivot (rope connection) is at the TOP of the bucket (local Y = 0)
        // Therefore COM is going down. (Adjust if your pivot is at the bottom).
        emptyBucketCenterOfMassOffset = bucketHeight * 0.5f; 
    }

    public float GetTotalMass()
    {
        return dryBucketMass + currentWaterMass;
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count < 2) return;

        float timeStep = Time.fixedDeltaTime;

        // 1. Calculate Effective Acceleration (Gravity + Rope Swinging Centripetal forces)
        Vector3 currentPivotVel = ropeController.allRopeSections[0].vel;
        Vector3 rawPivotAcc = (currentPivotVel - lastPivotVelocity) / timeStep;
        lastPivotVelocity = currentPivotVel;

        // Smooth to avoid physics jitters from the rope
        smoothedPivotAcc = Vector3.Lerp(smoothedPivotAcc, rawPivotAcc, 4.0f * timeStep);

        Vector3 gravityVec = new Vector3(0f, -9.81f, 0f);
        Vector3 effAcc = gravityVec - smoothedPivotAcc;

        // 2. Do physics
        CalculateFluidDynamics(timeStep, effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque);
        ApplyThrustToRope(localThrustForce);
        UpdateRotation(localThrustTorque, effAcc, timeStep);

        // 3. Sync visual position to the rope!
        transform.position = ropeController.allRopeSections[0].pos;
    }

    private void CalculateFluidDynamics(float timeStep, Vector3 effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque)
    {
        localThrustForce = Vector3.zero;
        localThrustTorque = Vector3.zero;

        if (!hasHole || currentWaterMass <= 0f)
        {
            currentWaterMass = 0f;
            return;
        }

        float densityOfWater = 1000f; 

        // REALISM FIX: Calculate how much "gravity" the water actually feels pushing it out the hole.
        // If the bucket is at the bottom of a fast swing, this is much higher than 9.81!
        Vector3 localDown = bucketRotation * Vector3.down;
        float effectiveG = Mathf.Max(0f, Vector3.Dot(-effAcc, localDown)); 

        float areaBucket = Mathf.PI * (bucketRadius * bucketRadius);
        float areaHole = Mathf.PI * (holeRadius * holeRadius);
        float waterHeight = (currentWaterMass / densityOfWater) / areaBucket;
        waterHeight = Mathf.Clamp(waterHeight, 0f, bucketHeight);

        // Torricelli's law using dynamic swinging gravity
        float exitVelocity = Mathf.Sqrt(2f * effectiveG * waterHeight);
        float massFlowRate = dischargeCoefficient * densityOfWater * areaHole * exitVelocity;

        float massToDrain = massFlowRate * timeStep;
        
        // Prevent draining more than we have
        if (massToDrain > currentWaterMass)
        {
            massToDrain = currentWaterMass;
            massFlowRate = massToDrain / timeStep;
        }
        
        currentWaterMass -= massToDrain;

        if (fluidSystem != null && massToDrain > 0f)
        {
            int particlesToSpawn = Mathf.Max(1, Mathf.RoundToInt(massToDrain / paintDensityFactor));
            Vector3 worldHolePos = transform.TransformPoint(holeLocalOffset);
            Vector3 worldExitDir = (bucketRotation * waterExitDirectionLocal).normalized;
            Vector3 bucketVelocity = ropeController.allRopeSections[0].vel;
            Vector3 worldExitVelocity = (worldExitDir * exitVelocity) + bucketVelocity;

            fluidSystem.EmitParticles(worldHolePos, worldExitVelocity, particlesToSpawn);
        }

        float thrustMagnitude = massFlowRate * exitVelocity;
        Vector3 exitDirNormalized = waterExitDirectionLocal.normalized;
        localThrustForce = -exitDirNormalized * thrustMagnitude;
        localThrustTorque = Vector3.Cross(holeLocalOffset, localThrustForce);
    }

    private void ApplyThrustToRope(Vector3 localThrustForce)
    {
        if (localThrustForce.sqrMagnitude <= 0.0001f) return;

        Vector3 worldThrustVec = bucketRotation * localThrustForce;
        Vector3 thrustAcceleration = worldThrustVec / GetTotalMass();

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel += thrustAcceleration * Time.fixedDeltaTime;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void UpdateRotation(Vector3 localThrustTorque, Vector3 effAcc, float timeStep)
    {
        float totalMass = GetTotalMass();
        float waterHeight = (currentWaterMass / 1000f) / (Mathf.PI * bucketRadius * bucketRadius);
        
        // Offset is distance from top pivot down to center of mass
        float waterCOMOffsetFromPivot = bucketHeight - (waterHeight * 0.5f); 
        float dynamicCOMOffset = ((dryBucketMass * emptyBucketCenterOfMassOffset) + (currentWaterMass * waterCOMOffsetFromPivot)) / totalMass;

        // REALISM FIX: 3D Moment of Inertia (Tensor)
        // X and Z are pitch/roll (swinging), Y is Yaw (spinning around vertical axis)
        float rSq = bucketRadius * bucketRadius;
        
        // Solid cylinder approx for water
        float iWaterXZ = currentWaterMass * (3f * rSq + (waterHeight * waterHeight)) / 12f + (currentWaterMass * waterCOMOffsetFromPivot * waterCOMOffsetFromPivot);
        float iWaterY = currentWaterMass * rSq / 2f; 
        
        // Thin cylinder approx for bucket
        float iBucketXZ = dryBucketMass * (3f * rSq + (bucketHeight * bucketHeight)) / 12f + (dryBucketMass * emptyBucketCenterOfMassOffset * emptyBucketCenterOfMassOffset);
        float iBucketY = dryBucketMass * rSq; 

        Vector3 totalInertia = new Vector3(
            Mathf.Max(0.01f, iBucketXZ + iWaterXZ), // Pitch
            Mathf.Max(0.01f, iBucketY + iWaterY),   // Yaw
            Mathf.Max(0.01f, iBucketXZ + iWaterXZ)  // Roll
        );

        // Calculate swinging torque (Gravity / Pendulum force)
        Vector3 currentCOMDir = bucketRotation * Vector3.down * dynamicCOMOffset;
        Vector3 totalForceOnCOM = totalMass * effAcc;
        Vector3 gravityTorque = Vector3.Cross(currentCOMDir, totalForceOnCOM);

        // Convert world torque to local torque
        Vector3 totalLocalTorque = Quaternion.Inverse(bucketRotation) * gravityTorque;

        if (hasHole && currentWaterMass > 0f)
            totalLocalTorque += localThrustTorque;

        // Torsional Yaw stiffness (keeps bucket roughly facing forward relative to the rope)
        Vector3 ropeDir = (ropeController.allRopeSections[1].pos - ropeController.allRopeSections[0].pos).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(ropeDir, Vector3.up).normalized;
        if (targetForward.sqrMagnitude < 0.01f) targetForward = ropeController.whatTheRopeIsConnectedTo.forward;
        
        Vector3 currentForward = bucketRotation * Vector3.forward;
        Vector3 yawError = Vector3.Cross(currentForward, targetForward);
        float torsionalStiffness = 5.0f * (totalMass / dryBucketMass); 
        totalLocalTorque += Vector3.up * Vector3.Dot(Quaternion.Inverse(bucketRotation) * yawError, Vector3.up) * torsionalStiffness;

        // REALISM FIX: Apply torque based on 3D inertia tensor
        Vector3 localAngularAcc = new Vector3(
            totalLocalTorque.x / totalInertia.x,
            totalLocalTorque.y / totalInertia.y,
            totalLocalTorque.z / totalInertia.z
        );

        // Aerodynamic damping (Velocity squared is more realistic for air resistance)
        Vector3 dampingForce = angularVelocity.magnitude * angularVelocity * bucketDamping;
        localAngularAcc -= dampingForce;

        angularVelocity += localAngularAcc * timeStep;

        float angleRad = angularVelocity.magnitude * timeStep;
        if (angleRad > 0.0001f)
        {
            Quaternion deltaRot = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, angularVelocity.normalized);
            bucketRotation = bucketRotation * deltaRot; 
        }

        transform.rotation = bucketRotation;
    }
}