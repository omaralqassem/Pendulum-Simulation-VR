using UnityEngine;

public class BucketPhysics : MonoBehaviour
{
    public RopeControllerRealistic ropeController;
    public SPHSystem fluidSystem;

    [Header("Bucket Properties")]
    public float dryBucketMass = 2.0f;       
    public float bucketHeight = 0.4f;        
    public float bucketRadius = 0.15f;       
    public float bucketDamping = 0.5f;       
    
    [Header("Paint Properties")]    
    public bool hasHole = true;
    public float currentPaintMass = 13.0f;   
    public float paintDensity = 1300f;      
    public float holeRadius = 0.015f; 

    public float dischargeCoefficient = 0.35f; 
    public float paintDensityFactor = 0.001f;

    [Header("Hole Offsets")]
    public Vector3 holeLocalOffset = new Vector3(0.15f, -0.4f, 0f); 
    public Vector3 paintExitDirectionLocal = new Vector3(0f, -1f, 0.5f); 

    private float emptyBucketCenterOfMassOffset; 
    private Vector3 angularVelocity = Vector3.zero; 
    private Quaternion bucketRotation = Quaternion.identity;
    private Vector3 lastPivotVelocity = Vector3.zero;
    private Vector3 smoothedPivotAcc = Vector3.zero;

    void Start()
    {
        bucketRotation = transform.rotation;
        emptyBucketCenterOfMassOffset = bucketHeight * 0.5f; 
    }

    public float GetTotalMass()
    {
        return dryBucketMass + currentPaintMass;
    }

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count < 2) return;

        float timeStep = Time.fixedDeltaTime;

        // effective acceleration calculation with frame-rate independent smoothing
        Vector3 currentPivotVel = ropeController.allRopeSections[0].vel;
        Vector3 rawPivotAcc = (currentPivotVel - lastPivotVelocity) / timeStep;
        lastPivotVelocity = currentPivotVel;

        float smoothingFactor = 4.0f;
        float blend = 1.0f - Mathf.Exp(-smoothingFactor * timeStep);
        smoothedPivotAcc = Vector3.Lerp(smoothedPivotAcc, rawPivotAcc, blend);

        Vector3 gravityVec = new Vector3(0f, -9.81f, 0f);
        Vector3 effAcc = gravityVec - smoothedPivotAcc;

        // physics
        CalculateFluidDynamics(timeStep, effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque);
        ApplyThrustToRope(localThrustForce);
        UpdateRotation(localThrustTorque, effAcc, timeStep);

        transform.position = ropeController.allRopeSections[0].pos;
    }

    private void CalculateFluidDynamics(float timeStep, Vector3 effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque)
    {
        localThrustForce = Vector3.zero;
        localThrustTorque = Vector3.zero;

        if (!hasHole || currentPaintMass <= 0f)
        {
            currentPaintMass = 0f;
            return;
        }

        float areaBucket = Mathf.PI * (bucketRadius * bucketRadius);
        float areaHole = Mathf.PI * (holeRadius * holeRadius);
        
        // Calculate nominal paint height
        float paintHeight = (currentPaintMass / paintDensity) / areaBucket;
        paintHeight = Mathf.Clamp(paintHeight, 0f, bucketHeight);

        // Calculate pressure head accounting for tilt relative to effective gravity
        Vector3 effAccDir = effAcc.normalized;
        Vector3 localUpWorld = bucketRotation * Vector3.up;
        float alignment = Mathf.Max(0.01f, Vector3.Dot(localUpWorld, -effAccDir));
        
        float adjustedHead = paintHeight * alignment;
        float effectiveG = effAcc.magnitude;

        // Torricelli's law with adjusted pressure head
        float exitVelocity = Mathf.Sqrt(2f * effectiveG * adjustedHead);
        
        // Mass flow rate
        float massFlowRate = dischargeCoefficient * paintDensity * areaHole * exitVelocity;
        float massToDrain = massFlowRate * timeStep;
        
        if (massToDrain > currentPaintMass)
        {
            massToDrain = currentPaintMass;
            massFlowRate = massToDrain / timeStep;
        }
        
        currentPaintMass -= massToDrain;

        // Note: Manual particle emission (EmitParticles) was removed here because 
        // the SPH compute shader now handles particles physically falling out of the hole!

        float thrustMagnitude = massFlowRate * exitVelocity;
        Vector3 exitDirNormalized = paintExitDirectionLocal.normalized;
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
        
        float paintHeight = (currentPaintMass / paintDensity) / (Mathf.PI * bucketRadius * bucketRadius);
        //center of Mass
        float paintCOMOffsetFromPivot = bucketHeight - (paintHeight * 0.5f); 
        float dynamicCOMOffset = ((dryBucketMass * emptyBucketCenterOfMassOffset) + (currentPaintMass * paintCOMOffsetFromPivot)) / totalMass;

        float rSq = bucketRadius * bucketRadius;
        
        // Moment of inertia calculation
        float iPaintXZ = currentPaintMass * (3f * rSq + (paintHeight * paintHeight)) / 12f + (currentPaintMass * paintCOMOffsetFromPivot * paintCOMOffsetFromPivot);
        float iPaintY = currentPaintMass * rSq / 2f; 
        
        float iBucketXZ = dryBucketMass * (3f * rSq + (bucketHeight * bucketHeight)) / 12f + (dryBucketMass * emptyBucketCenterOfMassOffset * emptyBucketCenterOfMassOffset);
        float iBucketY = dryBucketMass * rSq; 

        Vector3 totalInertia = new Vector3(
            Mathf.Max(0.01f, iBucketXZ + iPaintXZ),
            Mathf.Max(0.01f, iBucketY + iPaintY),   
            Mathf.Max(0.01f, iBucketXZ + iPaintXZ) 
        );

        // Torque due to gravity and linear inertia 
        Vector3 currentCOMDir = bucketRotation * Vector3.down * dynamicCOMOffset;
        Vector3 totalForceOnCOM = totalMass * effAcc;
        Vector3 gravityTorque = Vector3.Cross(currentCOMDir, totalForceOnCOM);

        Vector3 totalLocalTorque = Quaternion.Inverse(bucketRotation) * gravityTorque;

        if (hasHole && currentPaintMass > 0f)
            totalLocalTorque += localThrustTorque;

        // Torsional alignment to the rope direction (Yaw)
        Vector3 ropeDir = (ropeController.allRopeSections[1].pos - ropeController.allRopeSections[0].pos).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(ropeDir, Vector3.up).normalized;
        if (targetForward.sqrMagnitude < 0.01f) targetForward = ropeController.whatTheRopeIsConnectedTo.forward;
        
        Vector3 currentForward = bucketRotation * Vector3.forward;
        Vector3 yawError = Vector3.Cross(currentForward, targetForward);
        float torsionalStiffness = 5.0f * (totalMass / dryBucketMass); 
        totalLocalTorque += Vector3.up * Vector3.Dot(Quaternion.Inverse(bucketRotation) * yawError, Vector3.up) * torsionalStiffness;

        //apply Euler's equations of motion: T = I * alpha + w x (I * w)
        // expressed locally: alpha = I^-1 * (Torque - w x (I * w))
        Vector3 iw = new Vector3(
            totalInertia.x * angularVelocity.x,
            totalInertia.y * angularVelocity.y,
            totalInertia.z * angularVelocity.z
        );
        Vector3 gyroscopicTorque = Vector3.Cross(angularVelocity, iw);
        Vector3 netLocalTorque = totalLocalTorque - gyroscopicTorque;

        Vector3 localAngularAcc = new Vector3(
            netLocalTorque.x / totalInertia.x,
            netLocalTorque.y / totalInertia.y,
            netLocalTorque.z / totalInertia.z
        );

        Vector3 dampingForce = angularVelocity * bucketDamping;
        localAngularAcc -= dampingForce;

        angularVelocity += localAngularAcc * timeStep;

        // Integrate rotation
        float angleRad = angularVelocity.magnitude * timeStep;
        if (angleRad > 0.0001f)
        {
            Quaternion deltaRot = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, angularVelocity.normalized);
            bucketRotation = bucketRotation * deltaRot; 
        }

        transform.rotation = bucketRotation;
    }
}