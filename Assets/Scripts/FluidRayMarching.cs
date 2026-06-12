using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; 

public class FluidRayMarching : MonoBehaviour
{
    public ComputeShader raymarching;
    public Camera cam;
    public SPHSystem sph;

    RenderTexture target;

    [Header("Params")]
    public float viewRadius;
    public float blendStrength;
    public Color waterColor;
    public Color ambientLight;
    public Light lightSource;

    [Header("Performance Limit")]
    public int maxParticlesToCheck = 500000; 

    private bool render = false;
    private bool isURP = false;
    private CommandBuffer cb;

    void Start() {
        if (cam == null) cam = GetComponent<Camera>();
        
        isURP = GraphicsSettings.currentRenderPipeline != null;
        
        if (isURP) {
            Debug.Log("URP/HDRP detected! Using CommandBuffer for raymarching.");
            SetupCommandBuffer();
        } else {
            Debug.Log("Built-in Pipeline detected! Using OnRenderImage.");
        }
    }

    void SetupCommandBuffer() {
        cb = new CommandBuffer();
        cb.name = "Fluid Raymarching";
        cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
    }

    void InitRenderTexture () {
        if (target == null || target.width != cam.pixelWidth || target.height != cam.pixelHeight) {
            if (target != null) target.Release ();
            
            cam.depthTextureMode = DepthTextureMode.Depth;
            target = new RenderTexture (cam.pixelWidth, cam.pixelHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.enableRandomWrite = true;
            target.Create ();
        }
    }

    public void Begin() {
        if (sph == null || sph.ParticleBuffer == null) {
            Debug.LogError("SPHSystem is not assigned or ParticleBuffer is not initialized!");
            return;
        }

        InitRenderTexture();
        
        raymarching.SetBuffer(0, "particles", sph.ParticleBuffer);
        raymarching.SetInt("numParticles", sph.MaxParticles);
        
        raymarching.SetInt("maxParticlesToCheck", maxParticlesToCheck);

        raymarching.SetFloat("particleRadius", viewRadius);
        raymarching.SetFloat("blendStrength", blendStrength);
        raymarching.SetVector("waterColor", waterColor);
        raymarching.SetVector("_AmbientLight", ambientLight);
        raymarching.SetTextureFromGlobal(0, "_DepthTexture", "_CameraDepthTexture");
        
        render = true;
    }

    void Update() {
        if (!render) Begin();

        if (isURP && render && cb != null) {
            DispatchRaymarching(cb);
        }
    }

    void DispatchRaymarching(CommandBuffer buffer) {
        buffer.Clear();
        
        int sourceID = Shader.PropertyToID("_CameraColorTexture");
        
        buffer.SetComputeVectorParam(raymarching, "_Light", lightSource.transform.forward);
        buffer.SetComputeTextureParam(raymarching, 0, "Source", sourceID);
        buffer.SetComputeTextureParam(raymarching, 0, "Destination", target);
        buffer.SetComputeVectorParam(raymarching, "_CameraPos", cam.transform.position);
        buffer.SetComputeMatrixParam(raymarching, "_CameraToWorld", cam.cameraToWorldMatrix);
        buffer.SetComputeMatrixParam(raymarching, "_CameraInverseProjection", cam.projectionMatrix.inverse);

        int threadGroupsX = Mathf.CeilToInt(cam.pixelWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(cam.pixelHeight / 8.0f);
        buffer.DispatchCompute(raymarching, 0, threadGroupsX, threadGroupsY, 1);

        buffer.Blit(target, BuiltinRenderTextureType.CameraTarget);
    }

    void OnRenderImage (RenderTexture source, RenderTexture destination) {
        if (!isURP && render) {
            raymarching.SetVector ("_Light", lightSource.transform.forward);
            raymarching.SetTexture (0, "Source", source);
            raymarching.SetTexture (0, "Destination", target);
            raymarching.SetVector("_CameraPos", cam.transform.position);
            raymarching.SetMatrix ("_CameraToWorld", cam.cameraToWorldMatrix);
            raymarching.SetMatrix ("_CameraInverseProjection", cam.projectionMatrix.inverse);

            int threadGroupsX = Mathf.CeilToInt (cam.pixelWidth / 8.0f);
            int threadGroupsY = Mathf.CeilToInt (cam.pixelHeight / 8.0f);
            raymarching.Dispatch (0, threadGroupsX, threadGroupsY, 1);

            Graphics.Blit (target, destination);
        }
    }

    void OnDestroy() {
        if (target != null) target.Release();
        if (cb != null && cam != null) {
            cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        }
    }
}