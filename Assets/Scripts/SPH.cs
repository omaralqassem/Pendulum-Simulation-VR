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
    public float fillAmount = 0.7f;


    [Header("Bucket Integration")]
    public Transform bucketTransform;
    public BucketPhysics bucketPhysics;

    [Header("Resources")]
    [SerializeField] private ComputeShader sphCompute;
    [SerializeField] private Material renderMaterial;
    [SerializeField] private Mesh particleMesh;
    [SerializeField] private bool showMeshParticles = true;

    [Header("Simulation Space")]
    [SerializeField] private Vector3 boxSize = new Vector3(20f, 40f, 20f);
    [SerializeField] private float boundaryDamping = 0.4f;

    [Header("Fluid Properties")]
    [SerializeField] private int maxParticles = 500000;
    [SerializeField] private float particleRadius = 0.01f;
    [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    [SerializeField] private float maxParticleLifetime = 8f;

    [Header("SPH Parameters")]
    [SerializeField] private float smoothingRadius = 0.03f;
    [SerializeField] private float restDensity = 1000.0f;
    [SerializeField] private float gasConstant = 2000.0f;
    [SerializeField] private float viscosity = 5.0f;
    [SerializeField] private float particleMass = 0.2f;
    [Header("Painting Integration")]
    public Paintable targetCanvas;
    public Color fluidPaintColor = Color.blue;
    public float fluidPaintRadius = 0.5f;
    [SerializeField] private float hardness = 0.5f;
    [SerializeField] private float strength = 0.5f;
    [Range(1, 64)] public int maxPaintsPerFrame = 10;
    [Header("Resources")]

    private ComputeBuffer paintHitsBuffer;
    private ComputeBuffer paintHitCountBuffer;
    private Vector3[] paintHitsArray = new Vector3[64];
    private uint[] paintHitCountArray = new uint[1];
    private ComputeBuffer particleBuffer;
    private ComputeBuffer argsBuffer;
    private ComputeBuffer gridCountersBuffer;
    private ComputeBuffer gridCellsBuffer;
    public ComputeBuffer ParticleBuffer => particleBuffer;
    public int MaxParticles => maxParticles;
    public float ParticleRadius => particleRadius;

    private int clearGridKernel, buildGridKernel, densityKernel, forcesKernel, integrateKernel;
    private int threadGroupsSPH, threadGroupsGrid;

    private SPHParticle[] emissionBuffer;
    private int emitIndex = 0;

    private const int HASH_DIM = 128;
    private const int MAX_PARTICLES_PER_CELL = 64;
    private const int TOTAL_CELLS = HASH_DIM * HASH_DIM * HASH_DIM;
    private Vector3 lastBucketPosition;
    private Vector3 bucketVelocity;

    void Start()
    {
        if (particleMesh == null || particleMesh.vertexCount > 200) 
        {
            particleMesh = GenerateIcosphereMesh(subdivisions: 1); 
        }

        clearGridKernel = sphCompute.FindKernel("CSClearGrid");
        buildGridKernel = sphCompute.FindKernel("CSBuildGrid");
        densityKernel = sphCompute.FindKernel("CSDensityPressure");
        forcesKernel = sphCompute.FindKernel("CSForces");
        integrateKernel = sphCompute.FindKernel("CSIntegrate");

        threadGroupsSPH = Mathf.CeilToInt(maxParticles / 256f);
        threadGroupsGrid = Mathf.CeilToInt(TOTAL_CELLS / 256f);

        emissionBuffer = new SPHParticle[10000];
        if (bucketTransform != null)
        {
            lastBucketPosition = bucketTransform.position;
        }
 InitializeBuffers();
        StartCoroutine(WaitAndPrefillBucket());
    }
    private IEnumerator WaitAndPrefillBucket()
{
    while (bucketPhysics == null || 
           bucketPhysics.ropeController == null || 
           bucketPhysics.ropeController.allRopeSections.Count == 0)
    {
        yield return null;
    }


    yield return new WaitForFixedUpdate();
    yield return new WaitForFixedUpdate();

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
        particleBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 12);
        
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
        particleBuffer.SetData(initialData);
        initialData.Dispose();

        gridCountersBuffer = new ComputeBuffer(TOTAL_CELLS, sizeof(uint));
        gridCellsBuffer = new ComputeBuffer(TOTAL_CELLS * MAX_PARTICLES_PER_CELL, sizeof(uint));

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { particleMesh.GetIndexCount(0), (uint)maxParticles, particleMesh.GetIndexStart(0), particleMesh.GetBaseVertex(0), 0 };
        argsBuffer.SetData(args);

        renderMaterial.SetBuffer("Particles", particleBuffer);

        paintHitsBuffer = new ComputeBuffer(64, sizeof(float) * 3);
        paintHitCountBuffer = new ComputeBuffer(1, sizeof(uint));
        paintHitsArray = new Vector3[64];
        paintHitCountArray = new uint[1];
    }

    private void PrefillBucket()
    {
        if (bucketTransform == null || bucketPhysics == null)
            return;

        float spacing = particleRadius *0.7f;

        float bucketRadius = bucketPhysics.bucketRadius - particleRadius;
        float bucketBottom = -bucketPhysics.bucketHeight + particleRadius;
        float bucketTop = bucketPhysics.bucketHeight - particleRadius;

        float fillTop =
            Mathf.Lerp(
                bucketBottom,
                bucketTop,
                fillAmount
            );

        NativeArray<SPHParticle> initialData =
            new NativeArray<SPHParticle>(maxParticles, Allocator.Temp);

        int particleIndex = 0;

        // Fill layer by layer
        for (float y = bucketBottom; y < fillTop; y += spacing)
        {
            for (float z = -bucketRadius; z < bucketRadius; z += spacing)
            {
                for (float x = -bucketRadius; x < bucketRadius; x += spacing)
                {
                    if (particleIndex >= maxParticles)
                        goto FillFinished;

                    Vector2 radialPos = new Vector2(x, z);

                    // Stay inside cylindrical bucket
                    if (radialPos.magnitude > bucketRadius)
                        continue;

                    Vector3 jitter = new Vector3(
                                     Random.Range(-spacing * 0.05f, spacing * 0.05f),
                                     Random.Range(-spacing * 0.05f, spacing * 0.05f),
                                     Random.Range(-spacing * 0.05f, spacing * 0.05f)
                                     );

                    Vector3 localPos = new Vector3(x, y, z) + jitter;

                   

                    Vector3 worldPos =
                        bucketTransform.TransformPoint(localPos);

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

        // Disable unused particles
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

        Debug.Log(
            $"Prefilled {particleIndex} particles " +
            $"({fillAmount * 100f:F0}% bucket fill)"
        );

        initialData.Dispose();
    }
    public void EmitParticles(Vector3 origin, Vector3 velocity, int count)
    {
        if (count <= 0) return;
        count = Mathf.Min(count, emissionBuffer.Length);

        for (int i = 0; i < count; i++)
        {
            emissionBuffer[i] = new SPHParticle
            {
                position = origin + Random.insideUnitSphere * particleRadius * 0.5f,
                velocity = velocity + Random.insideUnitSphere * 0.05f,
                force = Vector3.zero, density = restDensity, pressure = 0f, lifetime = maxParticleLifetime
            };
        }

        int spaceLeft = maxParticles - emitIndex;
        if (count <= spaceLeft)
        {
            particleBuffer.SetData(emissionBuffer, 0, emitIndex, count);
            emitIndex = (emitIndex + count) % maxParticles;
        }
        else
        {
            particleBuffer.SetData(emissionBuffer, 0, emitIndex, spaceLeft);
            int remaining = count - spaceLeft;
            particleBuffer.SetData(emissionBuffer, spaceLeft, 0, remaining);
            emitIndex = remaining;
        }
    }

  void Update()
    {
        RunSimulation();
        RenderParticles();
        ProcessFluidPainting();
        SPHParticle[] debug = new SPHParticle[13];
        particleBuffer.GetData(debug, 0, 0, 10);

        for (int i = 0; i < 10; i++)
        {
            Debug.Log(
                $"P{i} Pos={debug[i].position} Density={debug[i].density} presuer = {debug[i].pressure}"
            );
        }
    }

  private void ProcessFluidPainting()
    {
        if (targetCanvas == null) return;

        paintHitCountBuffer.GetData(paintHitCountArray);
        int hitCount = (int)paintHitCountArray[0];

        if (hitCount > 0)
        {
            Debug.Log($"[Painting] Fluid hit the canvas {hitCount} times this frame!");

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
    private void RunSimulation()
    {
        float h = smoothingRadius;
        
        sphCompute.SetInt("numParticles", maxParticles);
        sphCompute.SetFloat("smoothingRadius", h);
        sphCompute.SetFloat("restDensity", restDensity);
        sphCompute.SetFloat("gasConstant", gasConstant);
        sphCompute.SetFloat("viscosity", viscosity);
        sphCompute.SetFloat("particleMass", particleMass);
        sphCompute.SetVector("gravity", gravity);
        sphCompute.SetVector("boxSize", boxSize);
        sphCompute.SetVector("boxCenter", transform.position);
        sphCompute.SetFloat("boundaryDamping", boundaryDamping);
        sphCompute.SetFloat("particleRadius", particleRadius);
        sphCompute.SetFloat("deltaTime", 0.004f); 

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
            sphCompute.SetFloat("holeRadiusCS", bucketPhysics.holeRadius);
            sphCompute.SetVector("holeLocalPos", bucketPhysics.holeLocalOffset);
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

        sphCompute.SetBuffer(clearGridKernel, "GridCounters", gridCountersBuffer);
        sphCompute.Dispatch(clearGridKernel, threadGroupsGrid, 1, 1);

        sphCompute.SetBuffer(buildGridKernel, "Particles", particleBuffer);
        sphCompute.SetBuffer(buildGridKernel, "GridCounters", gridCountersBuffer);
        sphCompute.SetBuffer(buildGridKernel, "GridCells", gridCellsBuffer);
        sphCompute.Dispatch(buildGridKernel, threadGroupsSPH, 1, 1);

        sphCompute.SetBuffer(densityKernel, "Particles", particleBuffer);
        sphCompute.SetBuffer(densityKernel, "GridCounters", gridCountersBuffer);
        sphCompute.SetBuffer(densityKernel, "GridCells", gridCellsBuffer);
        sphCompute.Dispatch(densityKernel, threadGroupsSPH, 1, 1);

        sphCompute.SetBuffer(forcesKernel, "Particles", particleBuffer);
        sphCompute.SetBuffer(forcesKernel, "GridCounters", gridCountersBuffer);
        sphCompute.SetBuffer(forcesKernel, "GridCells", gridCellsBuffer);
        sphCompute.Dispatch(forcesKernel, threadGroupsSPH, 1, 1);

        sphCompute.SetBuffer(integrateKernel, "Particles", particleBuffer);
        
        sphCompute.SetBuffer(integrateKernel, "PaintHits", paintHitsBuffer);
        sphCompute.SetBuffer(integrateKernel, "PaintHitCount", paintHitCountBuffer);
        
        sphCompute.Dispatch(integrateKernel, threadGroupsSPH, 1, 1);
    }
private void RenderParticles()
    {
        if (!showMeshParticles) return;

        renderMaterial.SetFloat("_Scale", particleRadius * 2.0f);
        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, renderMaterial, new Bounds(transform.position, boxSize * 2f), argsBuffer);
    }

    private void OnDestroy()
    {
        if (particleBuffer != null) particleBuffer.Release();
        if (gridCountersBuffer != null) gridCountersBuffer.Release();
        if (gridCellsBuffer != null) gridCellsBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
        if (paintHitsBuffer != null) paintHitsBuffer.Release();
        if (paintHitCountBuffer != null) paintHitCountBuffer.Release();
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