using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Collections.Generic;

public class SPHSystemForbox : MonoBehaviour
{
    public struct SPHParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 force;
        public float density;
        public float pressure;
        public float lifetime;
        public float dryState;
    }

    [Header("Initial Fill")]
    [Range(0.1f, 1f)] public float fillAmount = 1.0f;

    [Header("Resources")]
    [SerializeField] private ComputeShader sphCompute;
    [SerializeField] private Material renderMaterial;
    [SerializeField] private bool showMeshParticles = true;

    [Header("Simulation Space")]
    [SerializeField] public Vector3 boxSize = new Vector3(1.5f, 1.5f, 1.5f);
    [SerializeField] private float boundaryDamping = 0.1f;

    [Header("Fluid Properties")]
    [SerializeField] private int maxParticles = 1060720; 
    [SerializeField] private float particleRadius = 0.0045f;
    [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    [SerializeField] private float maxParticleLifetime = 9999f;

    [Header("SPH Parameters")]
    [SerializeField] public float smoothingRadius = 0.014f;
    [SerializeField] private float restDensity = 1000.0f;
    [SerializeField] private float gasConstant = 5.0f;
    [SerializeField] private float viscosity = 0.8f;
    [SerializeField] private float particleMass = 0.0000365f;
    [SerializeField] private float surfaceTension = 2f;

    private Mesh particleQuadMesh;
    private ComputeBuffer gridKeyValuesBuffer;
    private ComputeBuffer gridCellStartsBuffer;
    private ComputeBuffer gridCellEndsBuffer;

    private ComputeBuffer particleBuffer;
    private ComputeBuffer particlesSortedBuffer;
    private ComputeBuffer argsBuffer;

    [Header("State Tracking")]
    public bool isInitialized = false;

    private Vector3 lastBoxPosition;
    private Vector3 boxVelocity;
    private Vector3 boxAcceleration;

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

    private const int HASH_DIM = 128;
    private const int TOTAL_CELLS = HASH_DIM * HASH_DIM * HASH_DIM;

    private int sortedSize; 

    [Header("Simulation Speed")]
    [Range(1, 2)] public int simulationSubSteps = 1; 
    public float sphTimeStep = 0.0035f;

    void Start()
    {
        particleQuadMesh = GenerateIcosphereMesh();

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
        
        lastBoxPosition = transform.position;
        boxVelocity = Vector3.zero;
        boxAcceleration = Vector3.zero;

        InitializeBuffers();
        PrefillBox();
    }

    private void InitializeBuffers()
    {
        particleBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 13);
        particlesSortedBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 13);
        
        gridKeyValuesBuffer = new ComputeBuffer(sortedSize, sizeof(uint) * 2);
        gridCellStartsBuffer = new ComputeBuffer(TOTAL_CELLS, sizeof(uint));
        gridCellEndsBuffer = new ComputeBuffer(TOTAL_CELLS, sizeof(uint));

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { particleQuadMesh.GetIndexCount(0), (uint)maxParticles, particleQuadMesh.GetIndexStart(0), particleQuadMesh.GetBaseVertex(0), 0 };
        argsBuffer.SetData(args);

        renderMaterial.SetBuffer("Particles", particlesSortedBuffer);
    }

    private void PrefillBox()
    {
        float spacing = particleRadius * 1.95f; 

        Vector3 fillSize = boxSize * fillAmount;
        int countX = Mathf.FloorToInt(fillSize.x / spacing);
        int countY = Mathf.FloorToInt(fillSize.y / spacing);
        int countZ = Mathf.FloorToInt(fillSize.z / spacing);

        Vector3 startPos = - (new Vector3(countX, countY, countZ) * spacing) * 0.5f;

        NativeArray<SPHParticle> initialData = new NativeArray<SPHParticle>(maxParticles, Allocator.Temp);
        int particleIndex = 0;

        for (int y = 0; y < countY; y++)
        {
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    if (particleIndex >= maxParticles)
                        goto FillFinished;

                    Vector3 pos = startPos + new Vector3(x, y, z) * spacing;
                    pos += new Vector3(
                        Random.Range(-spacing * 0.02f, spacing * 0.02f),
                        Random.Range(-spacing * 0.02f, spacing * 0.02f),
                        Random.Range(-spacing * 0.02f, spacing * 0.02f)
                    );

                    initialData[particleIndex] = new SPHParticle
                    {
                        position = pos,
                        velocity = Vector3.zero,
                        force = Vector3.zero,
                        density = restDensity,
                        pressure = 0f,
                        lifetime = maxParticleLifetime,
                        dryState = 0f
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
                lifetime = 0f,
                dryState = 0f
            };
        }

        particleBuffer.SetData(initialData);
        particlesSortedBuffer.SetData(initialData);
        initialData.Dispose();
        isInitialized = true;
    }

    void Update()
    {
        if (isInitialized)
        {
            float dt = Time.deltaTime;
            if (dt > 0.0001f)
            {
                Vector3 currentPosition = transform.position;
                Vector3 newVelocity = (currentPosition - lastBoxPosition) / dt;
                Vector3 newAcceleration = (newVelocity - boxVelocity) / dt;

                boxAcceleration = Vector3.Lerp(boxAcceleration, newAcceleration, 0.2f);
                boxVelocity = newVelocity;
                lastBoxPosition = currentPosition;
            }

            for (int i = 0; i < simulationSubSteps; i++)
            {
                bool rebuildGrid = (i == 0);
                RunSimulation(sphTimeStep, rebuildGrid);
            }
        }

        RenderParticles();
    }
private void RunSimulation(float dt, bool rebuildGrid)
    {
        float h = smoothingRadius;
        
        Vector3 combinedWorldForces = gravity - boxAcceleration;
        Vector3 localGravity = transform.InverseTransformDirection(combinedWorldForces);

        sphCompute.SetInt("HashDim", HASH_DIM);
        sphCompute.SetInt("TotalCells", TOTAL_CELLS);

        sphCompute.SetInt("numParticles", maxParticles);
        sphCompute.SetInt("sortedSize", sortedSize);
        sphCompute.SetFloat("smoothingRadius", h);
        sphCompute.SetFloat("restDensity", restDensity);
        sphCompute.SetFloat("gasConstant", gasConstant);
        sphCompute.SetFloat("viscosity", viscosity);
        sphCompute.SetFloat("particleMass", particleMass);
        sphCompute.SetVector("gravity", localGravity);
        sphCompute.SetFloat("surfaceTension", surfaceTension); 
        sphCompute.SetVector("boxSize", boxSize);
        sphCompute.SetVector("boxCenter", Vector3.zero); 
        sphCompute.SetFloat("boundaryDamping", boundaryDamping);
        sphCompute.SetFloat("particleRadius", particleRadius);
        sphCompute.SetFloat("deltaTime", dt); 

        sphCompute.SetFloat("cellSize", h);
        sphCompute.SetFloat("invCellSize", 1.0f / h);
        sphCompute.SetFloat("poly6", 315.0f / (64.0f * Mathf.PI * Mathf.Pow(h, 9)));
        sphCompute.SetFloat("spikyGrad", -45.0f / (Mathf.PI * Mathf.Pow(h, 6)));
        sphCompute.SetFloat("viscLap", 45.0f / (Mathf.PI * Mathf.Pow(h, 6)));

        if (rebuildGrid)
        {
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
        }

        sphCompute.SetBuffer(densityKernel, "ParticlesSorted", particlesSortedBuffer);
        sphCompute.SetBuffer(densityKernel, "GridCellStarts", gridCellStartsBuffer);
        sphCompute.SetBuffer(densityKernel, "GridCellEnds", gridCellEndsBuffer);
        sphCompute.Dispatch(densityKernel, threadGroupsSPH, 1, 1);

        sphCompute.SetBuffer(forcesKernel, "ParticlesSorted", particlesSortedBuffer);
        sphCompute.SetBuffer(forcesKernel, "GridCellStarts", gridCellStartsBuffer);
        sphCompute.SetBuffer(forcesKernel, "GridCellEnds", gridCellEndsBuffer);
        sphCompute.Dispatch(forcesKernel, threadGroupsSPH, 1, 1);

        sphCompute.SetBuffer(integrateKernel, "Particles", particleBuffer);
        sphCompute.SetBuffer(integrateKernel, "ParticlesSorted", particlesSortedBuffer);
        sphCompute.SetBuffer(integrateKernel, "GridKeyValues", gridKeyValuesBuffer);
        sphCompute.Dispatch(integrateKernel, threadGroupsSPH, 1, 1);
    }
    private void RenderParticles()
    {
        if (!showMeshParticles) return;

        renderMaterial.SetFloat("_Scale", particleRadius * 2.0f);
        renderMaterial.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        Graphics.DrawMeshInstancedIndirect(
            particleQuadMesh, 
            0, 
            renderMaterial, 
            new Bounds(transform.position, boxSize * 2f), 
            argsBuffer
        );
    }

    private void OnDestroy()
    {
        if (particleBuffer != null) particleBuffer.Release();
        if (particlesSortedBuffer != null) particlesSortedBuffer.Release();
        if (gridKeyValuesBuffer != null) gridKeyValuesBuffer.Release();
        if (gridCellStartsBuffer != null) gridCellStartsBuffer.Release();
        if (gridCellEndsBuffer != null) gridCellEndsBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
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