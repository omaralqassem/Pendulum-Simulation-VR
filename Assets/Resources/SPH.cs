using UnityEngine;
using UnityEngine.Rendering;

public class SPHSystem : MonoBehaviour
{
    [Header("Simulation Parameters")]
    [SerializeField] private int maxParticles = 131072;
    [SerializeField] private Vector3 gravity = new Vector3(0, -9.81f, 0);
    [SerializeField] private float particleRadius = 0.05f;
    [SerializeField] private float smoothingLength = 0.1f;
    [SerializeField] private float restDensity = 1000f;
    [SerializeField] private float gasConstant = 2000f;
    [SerializeField] private float viscosity = 0.018f;
    [SerializeField] private float particleMass = 0.02f;
    [SerializeField] private float boundaryDamping = 0.5f;

    [Header("Boundary Settings")]
    [SerializeField] private Vector3 boundaryMin = new Vector3(-5, 0, -5);
    [SerializeField] private Vector3 boundaryMax = new Vector3(5, 10, 5);

    [Header("Rendering")]
    [SerializeField] private Mesh particleMesh;
    [SerializeField] private Material particleMaterial;
    [SerializeField] private float renderScale = 1f;

    [Header("Compute Shader")]
    [SerializeField] private ComputeShader sphCompute;

    // Kernel indices
    private int clearGridKernel;
    private int hashParticlesKernel;
    private int bitonicSortKernel;
    private int buildGridKernel;
    private int densityPressureKernel;
    private int forcesKernel;
    private int integrateKernel;

    private GraphicsBuffer particleBuffer;
    private GraphicsBuffer particleHashBuffer;
    private GraphicsBuffer gridBuffer;
    private GraphicsBuffer argsBuffer;

    // Particle management
    private int activeParticleCount = 0;
    private int nextParticleIndex = 0;

    // Grid dimensions
    private Vector3Int gridDimensions;
    private float cellSize;
    private int gridSize;

    // Particle structure size
    private const int PARTICLE_STRIDE = sizeof(float) * 11 + sizeof(int); // 48 bytes

    private struct ParticleData
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 force;
        public float density;
        public float pressure;
        public int isActive;
    }

    void Start()
    {
        InitializeBuffers();
        InitializeComputeShader();
        InitializeRendering();
    }

    void InitializeBuffers()
    {
        // Calculate grid dimensions
        cellSize = smoothingLength * 2f;
        Vector3 boundarySize = boundaryMax - boundaryMin;
        gridDimensions = new Vector3Int(
            Mathf.CeilToInt(boundarySize.x / cellSize),
            Mathf.CeilToInt(boundarySize.y / cellSize),
            Mathf.CeilToInt(boundarySize.z / cellSize)
        );
        gridSize = gridDimensions.x * gridDimensions.y * gridDimensions.z;

        // Create buffers
        particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxParticles, PARTICLE_STRIDE);
        particleHashBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxParticles, sizeof(uint) * 2);
        gridBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, gridSize, sizeof(int) * 2);

        // Initialize particle data
        ParticleData[] initialData = new ParticleData[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            initialData[i] = new ParticleData
            {
                position = Vector3.zero,
                velocity = Vector3.zero,
                force = Vector3.zero,
                density = restDensity,
                pressure = 0,
                isActive = 0
            };
        }
        particleBuffer.SetData(initialData);

        Debug.Log($"SPH Initialized: Grid {gridDimensions} ({gridSize} cells), Max Particles: {maxParticles}");
    }

    void InitializeComputeShader()
    {
        clearGridKernel = sphCompute.FindKernel("ClearGrid");
        hashParticlesKernel = sphCompute.FindKernel("HashParticles");
        bitonicSortKernel = sphCompute.FindKernel("BitonicSortStep");
        buildGridKernel = sphCompute.FindKernel("BuildGridIndices");
        densityPressureKernel = sphCompute.FindKernel("CalculateDensityPressure");
        forcesKernel = sphCompute.FindKernel("CalculateForces");
        integrateKernel = sphCompute.FindKernel("Integrate");

        sphCompute.SetBuffer(clearGridKernel, "grid", gridBuffer);
        
        sphCompute.SetBuffer(hashParticlesKernel, "particles", particleBuffer);
        sphCompute.SetBuffer(hashParticlesKernel, "particleHashes", particleHashBuffer);
        
        sphCompute.SetBuffer(bitonicSortKernel, "particleHashes", particleHashBuffer);
        
        sphCompute.SetBuffer(buildGridKernel, "particleHashes", particleHashBuffer);
        sphCompute.SetBuffer(buildGridKernel, "grid", gridBuffer);
        
        sphCompute.SetBuffer(densityPressureKernel, "particles", particleBuffer);
        sphCompute.SetBuffer(densityPressureKernel, "particleHashes", particleHashBuffer);
        sphCompute.SetBuffer(densityPressureKernel, "grid", gridBuffer);
        
        sphCompute.SetBuffer(forcesKernel, "particles", particleBuffer);
        sphCompute.SetBuffer(forcesKernel, "particleHashes", particleHashBuffer);
        sphCompute.SetBuffer(forcesKernel, "grid", gridBuffer);
        
        sphCompute.SetBuffer(integrateKernel, "particles", particleBuffer);

        sphCompute.SetInt("maxParticles", maxParticles);
        sphCompute.SetFloat("smoothingLength", smoothingLength);
        sphCompute.SetFloat("smoothingLengthSq", smoothingLength * smoothingLength);
        sphCompute.SetFloat("particleMass", particleMass);
        sphCompute.SetFloat("restDensity", restDensity);
        sphCompute.SetFloat("gasConstant", gasConstant);
        sphCompute.SetFloat("viscosity", viscosity);
        sphCompute.SetFloat("particleRadius", particleRadius);
        sphCompute.SetFloat("boundaryDamping", boundaryDamping);
        sphCompute.SetVector("gravity", gravity);
        sphCompute.SetVector("boundaryMin", boundaryMin);
        sphCompute.SetVector("boundaryMax", boundaryMax);
        sphCompute.SetInts("gridDimensions", new int[] { gridDimensions.x, gridDimensions.y, gridDimensions.z });
        sphCompute.SetFloat("cellSize", cellSize);
    }

    void InitializeRendering()
    {
        if (particleMesh == null)
        {
            particleMesh = CreateSphereMesh(8, 8);
        }

        uint[] args = new uint[] { 
            particleMesh.GetIndexCount(0), 
            (uint)maxParticles, 
            0, 
            0, 
            0 
        };
        argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, args.Length * sizeof(uint));
        argsBuffer.SetData(args);

        if (particleMaterial != null)
        {
            particleMaterial.SetBuffer("particles", particleBuffer);
            particleMaterial.SetFloat("_ParticleRadius", particleRadius * renderScale);
        }
    }

    void FixedUpdate()
    {
        if (activeParticleCount == 0) return;

        float dt = Time.fixedDeltaTime;
        sphCompute.SetFloat("deltaTime", dt);
        sphCompute.SetInt("activeParticles", activeParticleCount);

        sphCompute.Dispatch(clearGridKernel, Mathf.CeilToInt(gridSize / 256f), 1, 1);

        sphCompute.Dispatch(hashParticlesKernel, Mathf.CeilToInt(maxParticles / 256f), 1, 1);

        BitonicSort();

        sphCompute.Dispatch(buildGridKernel, Mathf.CeilToInt(maxParticles / 256f), 1, 1);

        sphCompute.Dispatch(densityPressureKernel, Mathf.CeilToInt(maxParticles / 256f), 1, 1);

        sphCompute.Dispatch(forcesKernel, Mathf.CeilToInt(maxParticles / 256f), 1, 1);

        sphCompute.Dispatch(integrateKernel, Mathf.CeilToInt(maxParticles / 256f), 1, 1);
    }

    void BitonicSort()
    {
        int n = maxParticles;
        
        for (int blockSize = 2; blockSize <= n; blockSize *= 2)
        {
            for (int stepSize = blockSize / 2; stepSize > 0; stepSize /= 2)
            {
                sphCompute.SetInt("bitonicBlockSize", blockSize);
                sphCompute.SetInt("bitonicStepSize", stepSize);
                sphCompute.Dispatch(bitonicSortKernel, Mathf.CeilToInt(n / 256f), 1, 1);
            }
        }
    }

    void Update()
    {
        if (particleMaterial != null && particleMesh != null && activeParticleCount > 0)
        {
            // Render particles using indirect rendering
            Bounds bounds = new Bounds(
                (boundaryMin + boundaryMax) * 0.5f,
                (boundaryMax - boundaryMin) * 2f
            );

            Graphics.RenderMeshIndirect(
                new RenderParams(particleMaterial) { worldBounds = bounds },
                particleMesh,
                argsBuffer
            );
        }
    }


    public void EmitParticles(Vector3 worldPosition, Vector3 worldVelocity, int count)
    {
        if (count <= 0) return;

        int particlesToEmit = Mathf.Min(count, maxParticles);
        
        ParticleData[] emissionData = new ParticleData[particlesToEmit];
        
        for (int i = 0; i < particlesToEmit; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * particleRadius * 0.5f;
            Vector3 randomVelocity = Random.insideUnitSphere * 0.1f;

            emissionData[i] = new ParticleData
            {
                position = worldPosition + randomOffset,
                velocity = worldVelocity + randomVelocity,
                force = Vector3.zero,
                density = restDensity,
                pressure = 0,
                isActive = 1
            };
        }

        if (nextParticleIndex + particlesToEmit <= maxParticles)
        {
            particleBuffer.SetData(emissionData, 0, nextParticleIndex, particlesToEmit);
            nextParticleIndex += particlesToEmit;
        }
        else
        {
            int firstChunk = maxParticles - nextParticleIndex;
            int secondChunk = particlesToEmit - firstChunk;

            particleBuffer.SetData(emissionData, 0, nextParticleIndex, firstChunk);
            particleBuffer.SetData(emissionData, firstChunk, 0, secondChunk);
            
            nextParticleIndex = secondChunk;
        }

        activeParticleCount = Mathf.Min(activeParticleCount + particlesToEmit, maxParticles);
    }


    Mesh CreateSphereMesh(int latSegments, int lonSegments)
    {
        Mesh mesh = new Mesh();
        
        int vertexCount = (latSegments + 1) * (lonSegments + 1);
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        
        int vertIndex = 0;
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * Mathf.PI / latSegments;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);
            
            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = lon * 2 * Mathf.PI / lonSegments;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);
                
                Vector3 position = new Vector3(
                    cosPhi * sinTheta,
                    cosTheta,
                    sinPhi * sinTheta
                );
                
                vertices[vertIndex] = position;
                normals[vertIndex] = position.normalized;
                uvs[vertIndex] = new Vector2((float)lon / lonSegments, (float)lat / latSegments);
                vertIndex++;
            }
        }
        
        int indexCount = latSegments * lonSegments * 6;
        int[] indices = new int[indexCount];
        int index = 0;
        
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int current = lat * (lonSegments + 1) + lon;
                int next = current + lonSegments + 1;
                
                indices[index++] = current;
                indices[index++] = next;
                indices[index++] = current + 1;
                
                indices[index++] = current + 1;
                indices[index++] = next;
                indices[index++] = next + 1;
            }
        }
        
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
        
        return mesh;
    }

    void OnDestroy()
    {
        particleBuffer?.Release();
        particleHashBuffer?.Release();
        gridBuffer?.Release();
        argsBuffer?.Release();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube((boundaryMin + boundaryMax) * 0.5f, boundaryMax - boundaryMin);
    }
}