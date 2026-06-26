using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using System.Collections;

public class SPHSystem : MonoBehaviour
{
    public struct SPHParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 force;
        public float density;
        public float pressure;
        public float lifetime;
    }

    [Header("Initial Fill")]
    [Range(0f, 1f)]
    public float fillAmount = 1.0f;

    [Header("Bucket Integration")]
    public Transform bucketTransform;
    public BucketPhysics bucketPhysics;

    [Header("Resources")]
    [SerializeField] private ComputeShader sphCompute;
    [SerializeField] private Material renderMaterial;
    [SerializeField] private Mesh particleMesh;
    [SerializeField] private bool showMeshParticles = true;

    [Header("Simulation Space")]
    [SerializeField] public Vector3 boxSize = new Vector3(42f, 39.7f, 37f);
    [SerializeField] private float boundaryDamping =0.4f;

    [Header("Fluid Properties")]
    [SerializeField] private int maxParticles = 424288; 
    [SerializeField] private float particleRadius = 0.015f;
    [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    [SerializeField] private float maxParticleLifetime = 20f;

    [Header("SPH Parameters")]
    [SerializeField] public float smoothingRadius = 0.005f;
    [SerializeField] private float restDensity = 1300.0f;
    [SerializeField] private float gasConstant = 10.0f;
    [SerializeField] private float viscosity = 1f;
    [SerializeField] private float particleMass = 3.06e-05f;
    [SerializeField] private float surfaceTension = 40f;

    [Header("Painting Integration")]
    public Paintable targetCanvas;
    public Color fluidPaintColor = Color.blue;
    public float fluidPaintRadius = 0.008f;
    [SerializeField] private float hardness = 0.5f;
    [SerializeField] private float strength = 0.5f;
    [Range(1, 64)] public int maxPaintsPerFrame = 64;

    public ComputeBuffer gridKeyValuesBuffer;
    public ComputeBuffer gridCellStartsBuffer;
    public ComputeBuffer gridCellEndsBuffer;

    private ComputeBuffer paintHitsBuffer;
    private ComputeBuffer paintHitCountBuffer;
    private Vector3[] paintHitsArray = new Vector3[256];
    private uint[] paintHitCountArray = new uint[1];
    private ComputeBuffer particleBuffer;
    private ComputeBuffer particlesSortedBuffer;
    private ComputeBuffer argsBuffer;

    public ComputeBuffer ParticleBuffer => particlesSortedBuffer;
    public ComputeBuffer UnsortedParticleBuffer => particleBuffer;
    public ComputeBuffer ParticlesSortedBuffer => particlesSortedBuffer;
    public int MaxParticles => maxParticles;
    public float ParticleRadius => particleRadius;

    [Header("State Tracking")]
    public bool isInitialized = false;
    public int ParticlesInBucketCount { get; private set; }

    private ComputeBuffer particlesInBucketBuffer;
    private uint[] particlesInBucketArray = new uint[1];

    private int clearGridKernel;
    private int generateKeyValuesKernel;
    private int bitonicSortLocalKernel;
    private int bitonicSortGlobalKernel;
    private int reorderParticlesKernel;
    private int buildGridOffsetsKernel;
    private int densityKernel;
    private int forcesKernel;
    private int integrateKernel;

    private int threadGroupsSPH;
    private int threadGroupsGrid;
    private int threadGroupsSort256;
    private int threadGroupsSort512;

    private SPHParticle[] emissionBuffer;
    private int emitIndex = 0;

    private const int HASH_DIM = 64;
    private const int TOTAL_CELLS = HASH_DIM * HASH_DIM * HASH_DIM;
    private Vector3 lastBucketPosition;
    private Vector3 bucketVelocity;

    private int sortedSize; 
    [Header("Simulation Speed & Substepping")]
[Tooltip("How many simulation ticks to run per frame. Higher values bring the fluid up to real-time speed.")]
[Range(1, 10)] public int simulationSubSteps = 3;
[Tooltip("The time-step size for each sub-step. Keep this small (e.g., 0.0015 - 0.0025) to prevent the SPH solver from exploding.")]
public float sphTimeStep = 0.002f;
[Header("Simulation Settle")]
    public float fluidSettleTime = 1.5f;
    [HideInInspector] public float currentSettleTime;
    private float holeOpenFactor = 0f;

    void Start()
    {currentSettleTime = fluidSettleTime;
        if (particleMesh == null || particleMesh.vertexCount > 200) 
        {
            particleMesh = GenerateIcosphereMesh(subdivisions: 1); 
        }

        sortedSize = 1;
        while (sortedSize < maxParticles) sortedSize <<= 1;

        clearGridKernel = sphCompute.FindKernel("CSClearGrid");
        generateKeyValuesKernel = sphCompute.FindKernel("CSGenerateKeyValues");
        bitonicSortLocalKernel = sphCompute.FindKernel("CSBitonicSortLocal");
        bitonicSortGlobalKernel = sphCompute.FindKernel("CSBitonicSortGlobal");
        reorderParticlesKernel = sphCompute.FindKernel("CSReorderParticles");
        buildGridOffsetsKernel = sphCompute.FindKernel("CSBuildGridOffsets");
        densityKernel = sphCompute.FindKernel("CSDensityPressure");
        forcesKernel = sphCompute.FindKernel("CSForces");
        integrateKernel = sphCompute.FindKernel("CSIntegrate");

        threadGroupsSPH = Mathf.CeilToInt(maxParticles / 256f);
        threadGroupsGrid = Mathf.CeilToInt(TOTAL_CELLS / 256f);
        threadGroupsSort256 = Mathf.CeilToInt(sortedSize / 256f);
        threadGroupsSort512 = Mathf.CeilToInt(sortedSize / 512f); // Used for LDS sorting

        emissionBuffer = new SPHParticle[10000];
        if (bucketTransform != null)
        {
            lastBucketPosition = bucketTransform.position;
        }
        
        InitializeBuffers();
        PrefillBucket();
    }



    private void InitializeBuffers()
    {
        if (bucketTransform != null)
        {
            if (Time.deltaTime > 0)
            {
                bucketVelocity = (bucketTransform.position - lastBucketPosition) / Time.deltaTime;
                lastBucketPosition = bucketTransform.position;
            }
            sphCompute.SetVector("bucketVelocity", bucketVelocity);
        }
        else
        {
            sphCompute.SetVector("bucketVelocity", Vector3.zero);
        }

        // Allocate double buffers for reordering
        particleBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 12);
        particlesSortedBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 12);
        
        NativeArray<SPHParticle> initialData = new NativeArray<SPHParticle>(maxParticles, Allocator.Temp);
        for (int i = 0; i < maxParticles; i++)
        {
            initialData[i] = new SPHParticle
            {
                position = new Vector3(99999.0f, 99999.0f, 99999.0f),
                velocity = Vector3.zero, force = Vector3.zero,
                density = restDensity, pressure = 0f, lifetime = 0f
            };
        }
        particlesInBucketBuffer = new ComputeBuffer(1, sizeof(uint));

        particleBuffer.SetData(initialData);
        particlesSortedBuffer.SetData(initialData);
        initialData.Dispose();

        gridKeyValuesBuffer = new ComputeBuffer(sortedSize, sizeof(uint) * 2);
        gridCellStartsBuffer = new ComputeBuffer(TOTAL_CELLS, sizeof(uint));
        gridCellEndsBuffer = new ComputeBuffer(TOTAL_CELLS, sizeof(uint));

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { particleMesh.GetIndexCount(0), (uint)maxParticles, particleMesh.GetIndexStart(0), particleMesh.GetBaseVertex(0), 0 };
        argsBuffer.SetData(args);

        // Bind the sorted buffer to the render material to take advantage of cache spatial locality
        renderMaterial.SetBuffer("Particles", particlesSortedBuffer);

        paintHitsBuffer = new ComputeBuffer(64, sizeof(float) * 3);
        paintHitCountBuffer = new ComputeBuffer(1, sizeof(uint));
        paintHitsArray = new Vector3[64];
        paintHitCountArray = new uint[1];
    }

   private void PrefillBucket()
{
    if (bucketTransform == null || bucketPhysics == null)
        return;

    float spacing = particleRadius * 2.0f; 

    float bucketR = bucketPhysics.bucketRadius - particleRadius;
    float bucketBottom = -bucketPhysics.bucketHeight + particleRadius;
    float bucketTop = bucketPhysics.bucketHeight - particleRadius;

    float fillTop = Mathf.Lerp(bucketBottom, bucketTop, fillAmount);

    NativeArray<SPHParticle> initialData = new NativeArray<SPHParticle>(maxParticles, Allocator.Temp);
    int particleIndex = 0;

    int countX = Mathf.FloorToInt((bucketR * 2f) / spacing);
    int countZ = Mathf.FloorToInt((bucketR * 2f) / spacing);
    float startX = -(countX * spacing) / 1.5f + (spacing / 1.5f);
    float startZ = -(countZ * spacing) / 1.5f + (spacing / 1.5f);

    for (float y = bucketBottom; y < fillTop; y += spacing)
    {
        for (int zi = 0; zi < countZ; zi++)
        {
            for (int xi = 0; xi < countX; xi++)
            {
                if (particleIndex >= maxParticles)
                    goto FillFinished;

                float x = startX + xi * spacing;
                float z = startZ + zi * spacing;

                Vector2 radialPos = new Vector2(x, z);

                if (radialPos.magnitude > bucketR)
                    continue;

                Vector3 jitter = new Vector3(
                    Random.Range(-spacing * 0.05f, spacing * 0.05f),
                    Random.Range(-spacing * 0.05f, spacing * 0.05f),
                    Random.Range(-spacing * 0.05f, spacing * 0.05f)
                );

                Vector3 localPos = new Vector3(x, y, z) + jitter;
                Vector3 worldPos = bucketTransform.TransformPoint(localPos);

                initialData[particleIndex] = new SPHParticle
                {
                    position = worldPos,
                    velocity = Vector3.zero,
                    force = Vector3.zero,
                    density = restDensity,
                    pressure = 0f,
                    lifetime = maxParticleLifetime
                };

                particleIndex++;
            }
        }
    }

FillFinished:

    for (int i = particleIndex; i < maxParticles; i++)
    {
        initialData[i] = new SPHParticle
        {
            position = new Vector3(99999f, 99999f, 99999f),
            velocity = Vector3.zero,
            force = Vector3.zero,
            density = 0f,
            pressure = 0f,
            lifetime = 0f
        };
    }

    particleBuffer.SetData(initialData);
    particlesSortedBuffer.SetData(initialData);

    Debug.Log($"Prefilled {particleIndex} particles ({fillAmount * 100f:F0}% bucket fill)");

    if (bucketPhysics != null)
    {
        bucketPhysics.ResetState();
        bucketPhysics.InitializeMass(particleIndex);
    }

    initialData.Dispose();
    isInitialized = true;
}
//updates

   private int warmupFrames = 40;

    void Update()
{
    if (currentSettleTime > 0f) {
        currentSettleTime -= Time.deltaTime;
        holeOpenFactor = 0f; 
    } else {
        holeOpenFactor = Mathf.Min(1.0f, holeOpenFactor + Time.deltaTime * 1.0f);
    }

    if (bucketTransform != null && Time.deltaTime > 0f)
    {
        Vector3 currentVel = (bucketTransform.position - lastBucketPosition) / Time.deltaTime;
        if (currentVel.magnitude > 100f) currentVel = Vector3.zero;
        
        bucketVelocity = currentVel;
        lastBucketPosition = bucketTransform.position;
    }

    if (isInitialized)
    {
        for (int i = 0; i < simulationSubSteps; i++)
        {
            bool isLastStep = (i == simulationSubSteps - 1);
            RunSimulation(sphTimeStep, isLastStep);
        }
    }

    if (warmupFrames > 0) {
        warmupFrames--;
        return;
    }

    RenderParticles();
    ProcessFluidPainting();
}
    private void ProcessFluidPainting()
    {
        if (targetCanvas == null) return;

        paintHitCountBuffer.GetData(paintHitCountArray);
        int hitCount = (int)paintHitCountArray[0];

        if (hitCount > 0)
        {
            int paintsToProcess = Mathf.Min(hitCount, maxPaintsPerFrame);
            paintHitsBuffer.GetData(paintHitsArray, 0, 0, paintsToProcess);

            for (int i = 0; i < paintsToProcess; i++)
            {
                PaintManager.instance.paint(
                    targetCanvas,
                    paintHitsArray[i],
                    Mathf.Max(fluidPaintRadius, 0.5f),
                    hardness,  
                    strength, 
                    fluidPaintColor
                );
            }
        }
    }
private void RunSimulation(float dt, bool retrieveDataFromGPU)
{
    float h = smoothingRadius;
    
    sphCompute.SetInt("numParticles", maxParticles);
    sphCompute.SetInt("sortedSize", sortedSize);
    sphCompute.SetFloat("smoothingRadius", h);
    sphCompute.SetFloat("restDensity", restDensity);
    float currentGasConstant = gasConstant;
    if (currentSettleTime > 0f)
    {
        float settleProgress = 1.0f - (currentSettleTime / fluidSettleTime);
        currentGasConstant = Mathf.Lerp(gasConstant * 0.05f, gasConstant, settleProgress);
    }
    sphCompute.SetFloat("gasConstant", currentGasConstant);
    sphCompute.SetFloat("viscosity", viscosity);
    sphCompute.SetFloat("particleMass", particleMass);
    sphCompute.SetVector("gravity", gravity);
    sphCompute.SetFloat("surfaceTension", surfaceTension); 
    sphCompute.SetVector("boxSize", boxSize);
    sphCompute.SetVector("boxCenter", transform.position);
    sphCompute.SetFloat("boundaryDamping", boundaryDamping);
    sphCompute.SetFloat("particleRadius", particleRadius);
    sphCompute.SetFloat("deltaTime", dt); // Use the sub-step delta time
    sphCompute.SetFloat("startupTimer", currentSettleTime);

    sphCompute.SetFloat("cellSize", h);
    sphCompute.SetFloat("poly6", 315.0f / (64.0f * Mathf.PI * Mathf.Pow(h, 9)));
    sphCompute.SetFloat("spikyGrad", -45.0f / (Mathf.PI * Mathf.Pow(h, 6)));
    sphCompute.SetFloat("viscLap", 45.0f / (Mathf.PI * Mathf.Pow(h, 6)));

    if (bucketTransform != null && bucketPhysics != null)
    {
        sphCompute.SetMatrix("bucketWorldToLocal", bucketTransform.worldToLocalMatrix);
        sphCompute.SetMatrix("bucketLocalToWorld", bucketTransform.localToWorldMatrix);
        sphCompute.SetFloat("bucketRadiusCS", bucketPhysics.bucketRadius);
        sphCompute.SetFloat("bucketHeightCS", bucketPhysics.bucketHeight); 
        sphCompute.SetFloat("holeRadiusCS", bucketPhysics.holeRadius * holeOpenFactor);
        sphCompute.SetVector("holeLocalPos", bucketPhysics.holeLocalOffset);
    }

    if (bucketTransform != null)
    {
        sphCompute.SetVector("bucketVelocity", bucketVelocity);
    }

    if (targetCanvas != null)
    {
        MeshFilter meshFilter = targetCanvas.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            sphCompute.SetMatrix("canvasWorldToLocal", targetCanvas.transform.worldToLocalMatrix);
            sphCompute.SetVector("canvasLocalCenter", meshFilter.sharedMesh.bounds.center);
            
            Vector3 extents = meshFilter.sharedMesh.bounds.extents;
            extents.x = Mathf.Max(extents.x, 0.1f);
            extents.y = Mathf.Max(extents.y, 0.1f);
            extents.z = Mathf.Max(extents.z, 0.1f);
            sphCompute.SetVector("canvasLocalExtents", extents);
        }
    }
    else
    {
        sphCompute.SetVector("canvasLocalCenter", new Vector3(99999f, 99999f, 99999f));
        sphCompute.SetVector("canvasLocalExtents", Vector3.zero);
    }

    paintHitCountArray[0] = 0;
    paintHitCountBuffer.SetData(paintHitCountArray);

    sphCompute.SetBuffer(clearGridKernel, "GridCellStarts", gridCellStartsBuffer);
    sphCompute.SetBuffer(clearGridKernel, "GridCellEnds", gridCellEndsBuffer);
    sphCompute.Dispatch(clearGridKernel, threadGroupsGrid, 1, 1);

    sphCompute.SetBuffer(generateKeyValuesKernel, "Particles", particleBuffer);
    sphCompute.SetBuffer(generateKeyValuesKernel, "GridKeyValues", gridKeyValuesBuffer);
    sphCompute.Dispatch(generateKeyValuesKernel, threadGroupsSort256, 1, 1);

    sphCompute.SetBuffer(bitonicSortLocalKernel, "GridKeyValues", gridKeyValuesBuffer);
    sphCompute.Dispatch(bitonicSortLocalKernel, threadGroupsSort256, 1, 1);

    sphCompute.SetBuffer(bitonicSortGlobalKernel, "GridKeyValues", gridKeyValuesBuffer);
    for (int stage = 512; stage <= sortedSize; stage <<= 1)
    {
        for (int step = stage >> 1; step > 0; step >>= 1)
        {
            sphCompute.SetInt("_BlockSize", step);
            sphCompute.SetInt("_Stage", stage);
            sphCompute.Dispatch(bitonicSortGlobalKernel, threadGroupsSort256, 1, 1);
        }
    }

    sphCompute.SetBuffer(reorderParticlesKernel, "Particles", particleBuffer);
    sphCompute.SetBuffer(reorderParticlesKernel, "ParticlesSorted", particlesSortedBuffer);
    sphCompute.SetBuffer(reorderParticlesKernel, "GridKeyValues", gridKeyValuesBuffer);
    sphCompute.Dispatch(reorderParticlesKernel, threadGroupsSort256, 1, 1);

    sphCompute.SetBuffer(buildGridOffsetsKernel, "GridKeyValues", gridKeyValuesBuffer);
    sphCompute.SetBuffer(buildGridOffsetsKernel, "GridCellStarts", gridCellStartsBuffer);
    sphCompute.SetBuffer(buildGridOffsetsKernel, "GridCellEnds", gridCellEndsBuffer);
    sphCompute.Dispatch(buildGridOffsetsKernel, threadGroupsSort256, 1, 1);

    sphCompute.SetBuffer(densityKernel, "ParticlesSorted", particlesSortedBuffer);
    sphCompute.SetBuffer(densityKernel, "GridCellStarts", gridCellStartsBuffer);
    sphCompute.SetBuffer(densityKernel, "GridCellEnds", gridCellEndsBuffer);
    sphCompute.Dispatch(densityKernel, threadGroupsSPH, 1, 1);

    sphCompute.SetBuffer(forcesKernel, "ParticlesSorted", particlesSortedBuffer);
    sphCompute.SetBuffer(forcesKernel, "GridCellStarts", gridCellStartsBuffer);
    sphCompute.SetBuffer(forcesKernel, "GridCellEnds", gridCellEndsBuffer);
    sphCompute.Dispatch(forcesKernel, threadGroupsSPH, 1, 1);

    if (retrieveDataFromGPU)
    {
        particlesInBucketArray[0] = 0;
        particlesInBucketBuffer.SetData(particlesInBucketArray);
    }
    
    sphCompute.SetBuffer(integrateKernel, "Particles", particleBuffer);
    sphCompute.SetBuffer(integrateKernel, "ParticlesSorted", particlesSortedBuffer);
    sphCompute.SetBuffer(integrateKernel, "GridKeyValues", gridKeyValuesBuffer);
    sphCompute.SetBuffer(integrateKernel, "ParticlesInBucketCount", particlesInBucketBuffer);
    sphCompute.SetBuffer(integrateKernel, "PaintHits", paintHitsBuffer);
    sphCompute.SetBuffer(integrateKernel, "PaintHitCount", paintHitCountBuffer);
    sphCompute.Dispatch(integrateKernel, threadGroupsSPH, 1, 1);

    if (retrieveDataFromGPU)
    {
        particlesInBucketBuffer.GetData(particlesInBucketArray);
        ParticlesInBucketCount = (int)particlesInBucketArray[0];
    }
}    private void RenderParticles()
    {
        if (!showMeshParticles) return;

        renderMaterial.SetFloat("_Scale", particleRadius * 2.0f);
        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, renderMaterial, new Bounds(transform.position, boxSize * 2f), argsBuffer);
    }

    private void OnDestroy()
    {
        if (particleBuffer != null) particleBuffer.Release();
        if (particlesSortedBuffer != null) particlesSortedBuffer.Release();
        if (gridKeyValuesBuffer != null) gridKeyValuesBuffer.Release();
        if (gridCellStartsBuffer != null) gridCellStartsBuffer.Release();
        if (gridCellEndsBuffer != null) gridCellEndsBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
        if (paintHitsBuffer != null) paintHitsBuffer.Release();
        if (paintHitCountBuffer != null) paintHitCountBuffer.Release();
        if (particlesInBucketBuffer != null) particlesInBucketBuffer.Release();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, boxSize);
    }
    private Mesh GenerateIcosphereMesh(int subdivisions = 1)
    {
        Mesh mesh = new Mesh();
        mesh.name = "SPH_Icosphere";
        float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;
        List<Vector3> vertices = new List<Vector3>() {
            new Vector3(-1, t, 0).normalized * 0.5f,
            new Vector3(1, t, 0).normalized * 0.5f,
            new Vector3(-1, -t, 0).normalized * 0.5f,
            new Vector3(1, -t, 0).normalized * 0.5f,
            new Vector3(0, -1, t).normalized * 0.5f,
            new Vector3(0, 1, t).normalized * 0.5f,
            new Vector3(0, -1, -t).normalized * 0.5f,
            new Vector3(0, 1, -t).normalized * 0.5f,
            new Vector3(t, 0, -1).normalized * 0.5f,
            new Vector3(t, 0, 1).normalized * 0.5f,
            new Vector3(-t, 0, -1).normalized * 0.5f,
            new Vector3(-t, 0, 1).normalized * 0.5f
        };
        List<int> triangles = new List<int>() {
            0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
            1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
            3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
            4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
        };
        Dictionary<long, int> midpointCache = new Dictionary<long, int>();
        int GetMidpoint(int v1, int v2)
        {
            long smaller = Mathf.Min(v1, v2);
            long greater = Mathf.Max(v1, v2);
            long key = (smaller << 32) + greater;
            if (midpointCache.TryGetValue(key, out int index))
                return index;

            Vector3 middle = (vertices[v1] + vertices[v2]) / 2.0f;
            vertices.Add(middle.normalized * 0.5f);
            
            index = vertices.Count - 1;
            midpointCache.Add(key, index);
            return index;
        }
        for (int i = 0; i < subdivisions; i++)
        {
            List<int> nextTriangles = new List<int>();
            for (int j = 0; j < triangles.Count; j += 3)
            {
                int v0 = triangles[j];
                int v1 = triangles[j + 1];
                int v2 = triangles[j + 2];

                int a = GetMidpoint(v0, v1);
                int b = GetMidpoint(v1, v2);
                int c = GetMidpoint(v2, v0);

                nextTriangles.AddRange(new int[] { v0, a, c });
                nextTriangles.AddRange(new int[] { v1, b, a });
                nextTriangles.AddRange(new int[] { v2, c, b });
                nextTriangles.AddRange(new int[] { a, b, c });
            }
            triangles = nextTriangles;
        }
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();

        return mesh;
    }
}