using UnityEngine;

public class PaintDripDrawCustom : MonoBehaviour
{
    public BucketPhysics bucketPhysics;
    
    [Header("Drawing Configuration")]
    [Tooltip("The virtual drawing tip object containing a TrailRenderer.")]
    public Transform paintBrushTip;
    [Tooltip("The height of your flat floor in world coordinates (Y).")]
    public float floorY = 0f;

    private TrailRenderer trailRenderer;

    void Start()
    {
        if (bucketPhysics == null) bucketPhysics = GetComponent<BucketPhysics>();

        if (paintBrushTip != null)
        {
            trailRenderer = paintBrushTip.GetComponent<TrailRenderer>();
        }
    }

    void Update()
    {
        if (bucketPhysics == null || paintBrushTip == null || trailRenderer == null) return;

        bool hasPaintLeft = bucketPhysics.currentPaintMass > 0f;

        if (hasPaintLeft && bucketPhysics.hasHole)
        {
            Vector3 holeWorldPos = transform.TransformPoint(bucketPhysics.holeLocalOffset);

            Vector3 bucketVelocity = Vector3.zero;
            if (bucketPhysics.ropeController != null && bucketPhysics.ropeController.allRopeSections.Count > 0)
            {
                bucketVelocity = bucketPhysics.ropeController.allRopeSections[0].vel;
            }

            float areaBucket = Mathf.PI * (bucketPhysics.bucketRadius * bucketPhysics.bucketRadius);
            float paintHeight = (bucketPhysics.currentPaintMass / bucketPhysics.paintDensity) / areaBucket;
            paintHeight = Mathf.Clamp(paintHeight, 0f, bucketPhysics.bucketHeight);
            
            float exitSpeed = Mathf.Sqrt(2f * 9.81f * paintHeight);
            Vector3 exitDirWorld = transform.TransformDirection(bucketPhysics.paintExitDirectionLocal.normalized);

            Vector3 initialPaintVelocity = bucketVelocity + (exitDirWorld * exitSpeed);

            if (CalculateProjectileIntersection(holeWorldPos, initialPaintVelocity, floorY, out Vector3 impactPoint))
            {
                paintBrushTip.position = impactPoint + Vector3.up * 0.01f;

                float flowRatio = bucketPhysics.currentPaintMass / 13.0f;
                trailRenderer.widthMultiplier = Mathf.Lerp(0.01f, 0.08f, flowRatio);

                trailRenderer.emitting = true;
            }
            else
            {
                trailRenderer.emitting = false;
            }
        }
        else
        {
            trailRenderer.emitting = false;
        }
    }

    private bool CalculateProjectileIntersection(Vector3 origin, Vector3 initialVelocity, float targetY, out Vector3 impactPoint)
    {
        impactPoint = Vector3.zero;
        float g = 9.81f;

        float a = -0.5f * g;
        float b = initialVelocity.y;
        float c = origin.y - targetY;

        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < 0f) return false;

        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
        float t1 = (-b + sqrtDiscriminant) / (2f * a);
        float t2 = (-b - sqrtDiscriminant) / (2f * a);

        float t = -1f;
        if (t1 >= 0f && t2 >= 0f) t = Mathf.Min(t1, t2);
        else if (t1 >= 0f) t = t1;
        else if (t2 >= 0f) t = t2;

        if (t < 0f) return false;

        impactPoint.x = origin.x + (initialVelocity.x * t);
        impactPoint.y = targetY;
        impactPoint.z = origin.z + (initialVelocity.z * t);

        return true;
    }
}