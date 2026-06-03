using UnityEngine;

public class SPH : MonoBehaviour
{
    [System.Serializable]
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public float lifetime;
        public uint active; 
    }   

    [System.Serializable]
    public struct GridCell
    {
        public uint start;
        public uint count;
    }

    [Header("Simulation Parameters")]
    [Tooltip("Must be a power of 2 for Bitonic Sort (e.g., 131072, 262144, 524288)")]
    public int particleCount = 262144;
    public float smoothingLength = 0.1f;
    public float particleRadius = 0.015f;
    public float restDensity = 1000f;
    public float gasConstant = 1.5f;
    public float viscosity = 0.1f;
    public Vector3 gravity = new Vector3(0, -9.81f, 0);
    public float timeStep = 0.003f;
    public float damping = 0.99f;

    [Header("Collision Boundaries")]
    public Vector3 floorPosition = Vector3.zero;
    public float floorRestitution = 0.3f;
    public Vector3 boundaryPosition = Vector3.zero;
    public float boundaryRadius = 2.0f;
    public float boundaryRestitution = 0.3f;

    [Header("Rendering References")]
    public Mesh particleMesh;
    public Material particleMaterial;
    public int meshSubMeshIndex = 0;

    [Header("Grid Partitioning")]
    public int gridResolution = 64;

    private GraphicsBuffer particleBuffer;
    private GraphicsBuffer sortedIndicesBuffer;
    private GraphicsBuffer tempSortedIndicesBuffer; 
    private GraphicsBuffer gridHashBuffer;
    private GraphicsBuffer gridCellBuffer;
    private GraphicsBuffer argsBuffer;
    private GraphicsBuffer sortingKeyBuffer;
    private GraphicsBuffer tempSortingKeyBuffer;   

    private ComputeShader sphComputeShader;
    private int kernelClearGrid;
    private int kernelHashParticles;
    private int kernelBitonicSort;
    private int kernelBuildGrid;
    private int kernelDensityPressure;
    private int kernelForces;
    private int kernelIntegrate;
    private int kernelEmit;

    private int currentEmitRingIndex = 0;

    void OnEnable()
    {
        if (!Mathf.IsPowerOfTwo(particleCount))
        {
            particleCount = Mathf.NextPowerOfTwo(particleCount);
            Debug.LogWarning($"Adjusted SPH particle count to power of 2: {particleCount}");
        }

        InitializeBuffers();
        InitializeComputeShader();
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    void InitializeBuffers()
    {
        int particleSize = System.Runtime.InteropServices.Marshal.SizeOf<Particle>();
        int gridCellSize = System.Runtime.InteropServices.Marshal.SizeOf<GridCell>();

        // Using GraphicsBuffer.Target.Structured for compute buffer equivalent behavior
        particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, particleSize);
        sortedIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));
        tempSortedIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));
        gridHashBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));
        gridCellBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, gridResolution * gridResolution * gridResolution, gridCellSize);
        sortingKeyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));
        tempSortingKeyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(uint));

        Particle[] particles = new Particle[particleCount];
        uint[] indices = new uint[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            particles[i].position = Vector3.zero;
            particles[i].velocity = Vector3.zero;
            particles[i].density = restDensity;
            particles[i].pressure = 0;
            particles[i].lifetime = 0;
            particles[i].active = 0;
            indices[i] = (uint)i;
        }
        particleBuffer.SetData(particles);
        sortedIndicesBuffer.SetData(indices);
        tempSortedIndicesBuffer.SetData(indices);

        uint[] args = new uint[5] { 0, 1, 0, 0, 0 };
        if (particleMesh != null)
        {
            args[0] = (uint)particleMesh.GetIndexCount(meshSubMeshIndex);
            args[1] = (uint)particleCount;
        }
        argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, args.Length * sizeof(uint));
        argsBuffer.SetData(args);
    }

    void InitializeComputeShader()
    {
        sphComputeShader = Resources.Load<ComputeShader>("SPH");
        if (sphComputeShader == null)
        {
            Debug.LogError("SPH compute shader not found at Assets/Resources/SPH.compute");
            return;
        }

        kernelClearGrid = sphComputeShader.FindKernel("ClearGrid");
        kernelHashParticles = sphComputeShader.FindKernel("HashParticles");
        kernelBitonicSort = sphComputeShader.FindKernel("BitonicSort");
        kernelBuildGrid = sphComputeShader.FindKernel("BuildGrid");
        kernelDensityPressure = sphComputeShader.FindKernel("DensityPressure");
        kernelForces = sphComputeShader.FindKernel("Forces");
        kernelIntegrate = sphComputeShader.FindKernel("Integrate");
        kernelEmit = sphComputeShader.FindKernel("EmitParticles");
    }

    void SetConstantParameters()
    {
        sphComputeShader.SetInt("gridResolution", gridResolution);
        sphComputeShader.SetInt("particleCount", particleCount);
        sphComputeShader.SetFloat("smoothingLength", smoothingLength);
        sphComputeShader.SetFloat("particleRadius", particleRadius);
        sphComputeShader.SetFloat("restDensity", restDensity);
        sphComputeShader.SetFloat("gasConstant", gasConstant);
        sphComputeShader.SetFloat("viscosity", viscosity);
        sphComputeShader.SetVector("gravity", gravity);
        sphComputeShader.SetFloat("timeStep", timeStep);
        sphComputeShader.SetFloat("damping", damping);
        sphComputeShader.SetVector("floorPosition", floorPosition);
        sphComputeShader.SetFloat("floorRestitution", floorRestitution);
        sphComputeShader.SetVector("boundaryPosition", boundaryPosition);
        sphComputeShader.SetFloat("boundaryRadius", boundaryRadius);
        sphComputeShader.SetFloat("boundaryRestitution", boundaryRestitution);
    }

    public void EmitParticles(Vector3 position, Vector3 velocity, int count)
    {
        if (count <= 0 || sphComputeShader == null) return;

        sphComputeShader.SetInt("particleCount", particleCount);
        sphComputeShader.SetInt("emitCount", count);
        sphComputeShader.SetVector("emitPosition", position);
        sphComputeShader.SetVector("emitVelocity", velocity);
        sphComputeShader.SetInt("emitStartIndex", currentEmitRingIndex);

        sphComputeShader.SetBuffer(kernelEmit, "particles", particleBuffer);

        int threadGroups = Mathf.CeilToInt(count / 64f);
        sphComputeShader.Dispatch(kernelEmit, threadGroups, 1, 1);

        currentEmitRingIndex = (currentEmitRingIndex + count) % particleCount;
    }

    void FixedUpdate()
    {
        if (sphComputeShader == null || particleBuffer == null) return;

        SetConstantParameters();

        int cellCount = gridResolution * gridResolution * gridResolution;
        int dispatchCount = Mathf.CeilToInt(particleCount / 256f);
        int clearDispatchCount = Mathf.CeilToInt(cellCount / 256f);
        
        sphComputeShader.SetBuffer(kernelClearGrid, "gridCells", gridCellBuffer);
        sphComputeShader.Dispatch(kernelClearGrid, clearDispatchCount, 1, 1);

        sphComputeShader.SetBuffer(kernelHashParticles, "particles", particleBuffer);
        sphComputeShader.SetBuffer(kernelHashParticles, "gridHashes", gridHashBuffer);
        sphComputeShader.SetBuffer(kernelHashParticles, "sortingKeys", sortingKeyBuffer);
        sphComputeShader.SetBuffer(kernelHashParticles, "sortedIndices", sortedIndicesBuffer);
        sphComputeShader.Dispatch(kernelHashParticles, dispatchCount, 1, 1);

        BitonicSort(particleCount);

        sphComputeShader.SetBuffer(kernelBuildGrid, "gridHashes", gridHashBuffer);
        sphComputeShader.SetBuffer(kernelBuildGrid, "sortedIndices", sortedIndicesBuffer);
        sphComputeShader.SetBuffer(kernelBuildGrid, "gridCells", gridCellBuffer);
        sphComputeShader.Dispatch(kernelBuildGrid, dispatchCount, 1, 1);

        sphComputeShader.SetBuffer(kernelDensityPressure, "particles", particleBuffer);
        sphComputeShader.SetBuffer(kernelDensityPressure, "sortedIndices", sortedIndicesBuffer);
        sphComputeShader.SetBuffer(kernelDensityPressure, "gridCells", gridCellBuffer);
        sphComputeShader.Dispatch(kernelDensityPressure, dispatchCount, 1, 1);

        sphComputeShader.SetBuffer(kernelForces, "particles", particleBuffer);
        sphComputeShader.SetBuffer(kernelForces, "sortedIndices", sortedIndicesBuffer);
        sphComputeShader.SetBuffer(kernelForces, "gridCells", gridCellBuffer);
        sphComputeShader.Dispatch(kernelForces, dispatchCount, 1, 1);

        sphComputeShader.SetBuffer(kernelIntegrate, "particles", particleBuffer);
        sphComputeShader.Dispatch(kernelIntegrate, dispatchCount, 1, 1);
    }

    void BitonicSort(int count)
    {
        int numStages = Mathf.RoundToInt(Mathf.Log(count, 2));

        GraphicsBuffer currentKeys = sortingKeyBuffer;
        GraphicsBuffer tempKeys = tempSortingKeyBuffer;
        GraphicsBuffer currentIndices = sortedIndicesBuffer;
        GraphicsBuffer tempIndices = tempSortedIndicesBuffer;

        int dispatchCount = Mathf.CeilToInt(count / 512f);

        for (int stage = 0; stage < numStages; stage++)
        {
            sphComputeShader.SetInt("stage", stage);
            for (int step = stage; step >= 0; step--)
            {
                sphComputeShader.SetInt("step", step);

                sphComputeShader.SetBuffer(kernelBitonicSort, "srcKeys", currentKeys);
                sphComputeShader.SetBuffer(kernelBitonicSort, "dstKeys", tempKeys);
                sphComputeShader.SetBuffer(kernelBitonicSort, "srcIndices", currentIndices);
                sphComputeShader.SetBuffer(kernelBitonicSort, "dstIndices", tempIndices);

                sphComputeShader.Dispatch(kernelBitonicSort, dispatchCount, 1, 1);

                SwapBuffers(ref currentKeys, ref tempKeys);
                SwapBuffers(ref currentIndices, ref tempIndices);
            }
        }

        if (currentKeys != sortingKeyBuffer)
        {
            CopyBufferData(tempSortingKeyBuffer, sortingKeyBuffer);
            CopyBufferData(tempSortedIndicesBuffer, sortedIndicesBuffer);
        }
    }

    void SwapBuffers(ref GraphicsBuffer bufA, ref GraphicsBuffer bufB)
    {
        GraphicsBuffer temp = bufA;
        bufA = bufB;
        bufB = temp;
    }

    void CopyBufferData(GraphicsBuffer src, GraphicsBuffer dst)
    {
        Graphics.CopyBuffer(src, dst); 
    }

    void Update()
    {
        if (particleMesh == null || particleMaterial == null || particleBuffer == null)
            return;

        particleMaterial.SetBuffer("_ParticleBuffer", particleBuffer);
        particleMaterial.SetFloat("_ParticleRadius", particleRadius);

        Graphics.DrawMeshInstancedIndirect(
            particleMesh,
            meshSubMeshIndex,
            particleMaterial,
            new Bounds(boundaryPosition, Vector3.one * (boundaryRadius * 2.5f)),
            argsBuffer,
            0,
            null,
            UnityEngine.Rendering.ShadowCastingMode.On,
            true
        );
    }

    void ReleaseBuffers()
    {
        particleBuffer?.Release();
        sortedIndicesBuffer?.Release();
        tempSortedIndicesBuffer?.Release();
        gridHashBuffer?.Release();
        gridCellBuffer?.Release();
        argsBuffer?.Release();
        sortingKeyBuffer?.Release();
        tempSortingKeyBuffer?.Release();
    }
}