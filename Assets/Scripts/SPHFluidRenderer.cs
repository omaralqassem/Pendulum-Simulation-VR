using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class SPHFluidRenderer : MonoBehaviour
{


    [Header("Dependencies")]
    [SerializeField] private SPHSystem sphSystem;
    [SerializeField] private ComputeShader bilateralCompute;
    [SerializeField] private Shader fluidParticleShader;
    [SerializeField] private Shader compositeShader;


    [Header("Particle Scaling")]
    [Tooltip("Render radius multiplier to help small physical particles merge into a solid sheet.")]
    [Range(1.0f, 30.0f)] public float particleRenderScale = 8.0f;

    [Header("Paint Shading Settings")]
    [ColorUsage(true, true)] public Color paintBaseColor = new Color(0.1f, 0.4f, 0.9f, 1f);
    [ColorUsage(true, true)] public Color paintDeepColor = new Color(0.01f, 0.05f, 0.2f, 1f);
    [Range(0.01f, 20.0f)] public float paintDensity = 5.0f;
    [Range(0.0f, 1.0f)] public float roughness = 0.1f;
    [Range(0.0f, 1.0f)] public float metallic = 0.05f;
    [Range(1.0f, 2.5f)] public float refractiveIndex = 1.45f;

    [Header("Bilateral Filtering (Depth Smoothing)")]
    [Range(1, 12)] public int blurRadius = 5;
    [Range(0.001f, 0.5f)] public float depthThreshold = 0.05f;

    private Material particleMat;
    private Material compositeMat;
    private Camera cam;

    private RenderTexture depthTarget;
    private RenderTexture smoothDepthTarget;
    private RenderTexture thicknessTarget;
    private ComputeBuffer drawArgsBuffer;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void OnEnable()
    {
        InitializeMaterials();
        CreateBuffersAndTextures();
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void InitializeMaterials()
    {
        if (fluidParticleShader != null)
            particleMat = new Material(fluidParticleShader);
        if (compositeShader != null)
            compositeMat = new Material(compositeShader);
    }

    private void CreateBuffersAndTextures()
    {
        if (cam == null) return;

        int w = cam.pixelWidth;
        int h = cam.pixelHeight;
        if (w <= 0 || h <= 0) { w = 1280; h = 720; }

        depthTarget = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        depthTarget.enableRandomWrite = true;
        depthTarget.filterMode = FilterMode.Point;
        depthTarget.Create();

        smoothDepthTarget = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        smoothDepthTarget.enableRandomWrite = true;
        smoothDepthTarget.filterMode = FilterMode.Bilinear;
        smoothDepthTarget.Create();

        thicknessTarget = new RenderTexture(w / 2, h / 2, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
        thicknessTarget.filterMode = FilterMode.Bilinear;
        thicknessTarget.Create();

        drawArgsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        UpdateArgsBuffer(0);
    }

    private void UpdateArgsBuffer(int particleCount)
    {
        if (drawArgsBuffer == null) return;
        uint[] args = new uint[4] { 6, (uint)particleCount, 0, 0 };
        drawArgsBuffer.SetData(args);
    }

    private void Cleanup()
    {
        if (depthTarget != null) { depthTarget.Release(); depthTarget = null; }
        if (smoothDepthTarget != null) { smoothDepthTarget.Release(); smoothDepthTarget = null; }
        if (thicknessTarget != null) { thicknessTarget.Release(); thicknessTarget = null; }
        if (drawArgsBuffer != null) { drawArgsBuffer.Dispose(); drawArgsBuffer = null; }
        if (particleMat != null) DestroyImmediate(particleMat);
        if (compositeMat != null) DestroyImmediate(compositeMat);
    }

    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (sphSystem == null || !sphSystem.isInitialized || particleMat == null || compositeMat == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        int currentW = cam.pixelWidth;
        int currentH = cam.pixelHeight;
        if (depthTarget == null || depthTarget.width != currentW || depthTarget.height != currentH)
        {
            Cleanup();
            CreateBuffersAndTextures();
        }

        Matrix4x4 viewMatrix = cam.worldToCameraMatrix;
        Matrix4x4 projMatrix = cam.projectionMatrix;
        Matrix4x4 vpMatrix = projMatrix * viewMatrix;

        Matrix4x4 rtProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, true);

        particleMat.SetBuffer("_Particles", sphSystem.ParticlesSortedBuffer);
        particleMat.SetFloat("_ParticleRadius", sphSystem.ParticleRadius * particleRenderScale);
        particleMat.SetMatrix("_ViewMatrix", viewMatrix);
        particleMat.SetMatrix("_ProjMatrix", rtProjMatrix);

        UpdateArgsBuffer(sphSystem.MaxParticles);

        Graphics.SetRenderTarget(depthTarget);
        GL.Clear(true, true, Color.white * 10000.0f);
        particleMat.SetPass(0);
        Graphics.DrawProceduralIndirectNow(MeshTopology.Triangles, drawArgsBuffer, 0);

        int kernelHandle = bilateralCompute.FindKernel("CSBilateralFilter");
        bilateralCompute.SetTexture(kernelHandle, "InputDepth", depthTarget);
        bilateralCompute.SetTexture(kernelHandle, "OutputDepth", smoothDepthTarget);
        bilateralCompute.SetInt("Width", depthTarget.width);
        bilateralCompute.SetInt("Height", depthTarget.height);
        bilateralCompute.SetInt("BlurRadius", blurRadius);
        bilateralCompute.SetFloat("DepthThreshold", depthThreshold);

        int threadGroupsX = Mathf.CeilToInt((float)depthTarget.width / 16.0f);
        int threadGroupsY = Mathf.CeilToInt((float)depthTarget.height / 16.0f);
        bilateralCompute.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, 1);

        Graphics.SetRenderTarget(thicknessTarget);
        GL.Clear(false, true, Color.clear);
        particleMat.SetPass(1);
        Graphics.DrawProceduralIndirectNow(MeshTopology.Triangles, drawArgsBuffer, 0);

        Matrix4x4 screenProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, false);

        compositeMat.SetTexture("_FluidDepthTex", smoothDepthTarget);
        compositeMat.SetTexture("_RawDepthTex", depthTarget);
        compositeMat.SetTexture("_ThicknessTex", thicknessTarget);
        compositeMat.SetMatrix("_InvViewProj", vpMatrix.inverse);
        compositeMat.SetMatrix("_ProjMatrix", screenProjMatrix);
        compositeMat.SetMatrix("_ViewMatrix", viewMatrix);
        compositeMat.SetColor("_PaintBaseColor", paintBaseColor);
        compositeMat.SetColor("_PaintDeepColor", paintDeepColor);
        compositeMat.SetFloat("_PaintDensity", paintDensity);
        compositeMat.SetFloat("_Roughness", roughness);
        compositeMat.SetFloat("_Metallic", metallic);
        compositeMat.SetFloat("_RefractiveIndex", refractiveIndex);

        Vector3 worldLightDir = Vector3.Normalize(new Vector3(0.5f, 1.0f, -0.5f));
        Vector3 viewLightDir = cam.worldToCameraMatrix.MultiplyVector(worldLightDir).normalized;
        compositeMat.SetVector("_LightDir", viewLightDir);
        Graphics.Blit(source, destination, compositeMat, 0);
    }
}