using UnityEngine;

public class BucketPhysics : MonoBehaviour
{
    public RopeControllerRealistic ropeController;
    public SPHSystem fluidSystem;

    [Header("Bucket Properties")]
    public float dryBucketMass = 2.0f;       
    public float bucketHeight =4.0f;        
    public float bucketRadius = 0.15f;       
    
    [Header("Rope Twist Settings (Spinning)")]
    [Tooltip("How strongly the rope resists twisting. Higher values make it untwist faster.")]
    public float twistSpringStiffness = 0.1f; 
    [Tooltip("Friction that slows down the spinning motion.")]
    public float spinDamping = 0.2f;   

    [Header("Paint Properties")]    
    public bool hasHole = true;
    public float currentPaintMass = 25.0f;   
    public float paintDensity = 1000f;      
    public float holeRadius = 0.015f; 

    public float dischargeCoefficient = 0.35f; 
    public float paintDensityFactor = 0.002f;

    [Header("Hole Offsets")]
    public Vector3 holeLocalOffset = new Vector3(0f, -4f, 0f); 
    public Vector3 paintExitDirectionLocal = new Vector3(0f, -1f,0f); 
    [Header("Stabilization")]
    public float stabilizationDuration = 1.5f;


   
   private float physicsMassPerParticle = 0f;
    private bool massInitialized = false;
    private bool hasReceivedFirstCount = false; 

    private float spinAngleRad = 0f;
    private float spinVelocityRad = 0f;
    private Vector3 lastPivotVelocity = Vector3.zero;
    private Vector3 smoothedPivotAcc = Vector3.zero;

    void Start()
    {
        transform.rotation = Quaternion.identity;
    }

    public float GetTotalMass()
    {
        return dryBucketMass + currentPaintMass;
    }
     public void ResetState()
    {
        massInitialized = false;
        hasReceivedFirstCount = false;
    }

    public void InitializeMass(int initialParticleCount)
    {
        if (initialParticleCount > 0)
        {
            physicsMassPerParticle = currentPaintMass / (float)initialParticleCount;
            massInitialized = true;
            Debug.Log($"[BucketPhysics] Mass initialized. Mass per particle: {physicsMassPerParticle} ({initialParticleCount} particles)");
        }
    }

  

    void FixedUpdate()
    {
        if (ropeController == null || ropeController.allRopeSections.Count < 2) return;

        float timeStep = Time.fixedDeltaTime;

        // Effective acceleration calculation with frame-rate independent smoothing
        Vector3 currentPivotVel = ropeController.allRopeSections[0].vel;
        Vector3 rawPivotAcc = (currentPivotVel - lastPivotVelocity) / timeStep;
        lastPivotVelocity = currentPivotVel;

        float smoothingFactor = 1.5f;
        float blend = 1.0f - Mathf.Exp(-smoothingFactor * timeStep);
        smoothedPivotAcc = Vector3.Lerp(smoothedPivotAcc, rawPivotAcc, blend);

        Vector3 gravityVec = new Vector3(0f, -9.81f, 0f);
        Vector3 effAcc = gravityVec - smoothedPivotAcc;

        // physics 
        CalculateFluidDynamics(timeStep, effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque);
        ApplyThrustToRope(localThrustForce);
        UpdateRotation(localThrustTorque, timeStep);

        transform.position = ropeController.allRopeSections[0].pos;
    }

private void CalculateFluidDynamics(float timeStep, Vector3 effAcc, out Vector3 localThrustForce, out Vector3 localThrustTorque)
    {
        localThrustForce = Vector3.zero;
        localThrustTorque = Vector3.zero;

        if (!hasHole || fluidSystem == null || !fluidSystem.isInitialized)
            return;

        int currentCount = fluidSystem.ParticlesInBucketCount;

        if (fluidSystem.currentSettleTime > 0f)
        {
            if (currentCount > 0)
            {
                hasReceivedFirstCount = true;
                massInitialized = true;
                physicsMassPerParticle = currentPaintMass / (float)currentCount;
            }
            return;
        }

        if (!hasReceivedFirstCount || currentCount <= 0)
            return;

        float actualSPHMass = currentCount * physicsMassPerParticle;

        if (actualSPHMass > currentPaintMass)
        {
            actualSPHMass = currentPaintMass;
        }

        float massDrained = currentPaintMass - actualSPHMass;
        currentPaintMass = actualSPHMass;

        if (currentPaintMass <= 0.001f || massDrained <= 0.00001f) return;

        float areaBucket = Mathf.PI * (bucketRadius * bucketRadius);
        float paintHeight = (currentPaintMass / paintDensity) / areaBucket;
        paintHeight = Mathf.Clamp(paintHeight, 0f, bucketHeight);

        float effectiveG = effAcc.magnitude;
        float exitVelocity = Mathf.Sqrt(2f * effectiveG * paintHeight);

        float areaHole = Mathf.PI * (holeRadius * holeRadius);
        float maxVolumeFlow = areaHole * exitVelocity * dischargeCoefficient;
        float maxMassFlow = maxVolumeFlow * paintDensity;
        float maxDrained = maxMassFlow * timeStep;

        float validMassDrained = Mathf.Min(massDrained, maxDrained);
        float massFlowRate = validMassDrained / timeStep;
        float thrustMagnitude = massFlowRate * exitVelocity;

        localThrustForce = -paintExitDirectionLocal.normalized * thrustMagnitude;
        localThrustTorque = Vector3.Cross(holeLocalOffset, localThrustForce);
    }

    private void ApplyThrustToRope(Vector3 localThrustForce)
    {
        if (localThrustForce.sqrMagnitude <= 0.0001f) return;

        Vector3 worldThrustVec = transform.rotation * localThrustForce;
        Vector3 thrustAcceleration = worldThrustVec / GetTotalMass();

        var bottomSection = ropeController.allRopeSections[0];
        bottomSection.vel += thrustAcceleration * Time.fixedDeltaTime;
        ropeController.allRopeSections[0] = bottomSection;
    }

    private void UpdateRotation(Vector3 localThrustTorque, float timeStep)
    {
        float rSq = bucketRadius * bucketRadius;
        float iBucketY = dryBucketMass * rSq; 
        float iPaintY = currentPaintMass * rSq / 2f; 
        float totalInertiaY = Mathf.Max(0.01f, iBucketY + iPaintY);

        float spinTorque = 0f;
        if (hasHole && currentPaintMass > 0f)
        {
            spinTorque = localThrustTorque.y;
        }
        float spinAccel = spinTorque / totalInertiaY;

        float restoreTorque = -spinAngleRad * twistSpringStiffness;
        spinAccel += restoreTorque / totalInertiaY;
        spinAccel -= spinVelocityRad * spinDamping; 

        spinVelocityRad += spinAccel * timeStep;
        spinAngleRad += spinVelocityRad * timeStep;

        Vector3 ropeDir = (ropeController.allRopeSections[1].pos - ropeController.allRopeSections[0].pos).normalized;
        if (ropeDir == Vector3.zero) ropeDir = Vector3.up;

        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, ropeDir);
        Quaternion spin = Quaternion.AngleAxis(spinAngleRad * Mathf.Rad2Deg, Vector3.up);

        transform.rotation = tilt * spin;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = Color.green;
        Vector3 localBottomCenter = new Vector3(0f, -bucketHeight, 0f);
        Vector3 localTopCenter = new Vector3(0f, bucketHeight, 0f);

        Gizmos.DrawLine(localBottomCenter, localTopCenter);

        DrawLocalGizmoCircle(localBottomCenter, bucketRadius);
        DrawLocalGizmoCircle(localTopCenter, bucketRadius);

        Gizmos.color = Color.red;
        DrawLocalGizmoCircle(holeLocalOffset, holeRadius);

        Gizmos.matrix = oldMatrix;
    }
    private bool showBucketUI = true;

    private string strDryBucketMass;
    private string strTwistSpringStiffness;
    private string strSpinDamping;
    private string strPaintDensity;
    private string strDischargeCoefficient;
    private string strHoleRadius;

    private void OnGUI()
    {
        if (strDryBucketMass == null)
        {
            strDryBucketMass = dryBucketMass.ToString();
            strTwistSpringStiffness = twistSpringStiffness.ToString();
            strSpinDamping = spinDamping.ToString();
            strPaintDensity = paintDensity.ToString();
            strDischargeCoefficient = dischargeCoefficient.ToString();
            strHoleRadius = holeRadius.ToString();
        }

        if (GUI.Button(new Rect(Screen.width - 130, Screen.height - 45, 120, 30), showBucketUI ? "Hide Bucket UI" : "Show Bucket UI"))
        {
            showBucketUI = !showBucketUI;
        }

        if (!showBucketUI) return;

        GUI.Box(new Rect(Screen.width - 320, Screen.height - 330, 300, 270), "Bucket Physics Parameters");
        GUILayout.BeginArea(new Rect(Screen.width - 300, Screen.height - 300, 260, 250));

        GUI.contentColor = Color.cyan;
        GUILayout.Label($"Live Paint Mass: {currentPaintMass:F2} kg");
        GUI.contentColor = Color.white;
        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Is Hole Open:", GUILayout.Width(130));
        hasHole = GUILayout.Toggle(hasHole, "");
        GUILayout.EndHorizontal();

        DrawFloatField("Dry Bucket Mass:", ref strDryBucketMass, ref dryBucketMass);
        DrawFloatField("Spring Stiffness:", ref strTwistSpringStiffness, ref twistSpringStiffness);
        DrawFloatField("Spin Damping:", ref strSpinDamping, ref spinDamping);
        DrawFloatField("Paint Density:", ref strPaintDensity, ref paintDensity);
        DrawFloatField("Discharge Coeff:", ref strDischargeCoefficient, ref dischargeCoefficient);
        DrawFloatField("Hole Radius:", ref strHoleRadius, ref holeRadius);

        GUILayout.EndArea();
    }

    private void DrawFloatField(string label, ref string strValue, ref float floatValue)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(130));
        strValue = GUILayout.TextField(strValue);
        if (float.TryParse(strValue, out float parsed))
        {
            floatValue = parsed;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawLocalGizmoCircle(Vector3 localCenter, float radius)
    {
        Vector3 lastPoint = localCenter + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= 32; i++)
        {
            float angle = (i / 32f) * Mathf.PI * 2.0f;
            Vector3 nextPoint = localCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(lastPoint, nextPoint);
            lastPoint = nextPoint;
        }
    }
}