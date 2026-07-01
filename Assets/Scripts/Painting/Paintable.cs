using UnityEngine;

public class Paintable : MonoBehaviour
{
    const int TEXTURE_SIZE = 1024;

    public float extendsIslandOffset = 1;

    RenderTexture extendIslandsRenderTexture;
    RenderTexture uvIslandsRenderTexture;
    RenderTexture maskRenderTexture;
    RenderTexture supportTexture;

    // ADDED: Wetness map variable
    RenderTexture wetnessRenderTexture;

    Renderer rend;

    int maskTextureID = Shader.PropertyToID("_MaskTexture");
    // ADDED: Wetness shader ID
    int wetnessTextureID = Shader.PropertyToID("_WetnessMap");

    public RenderTexture getMask() => maskRenderTexture;
    public RenderTexture getUVIslands() => uvIslandsRenderTexture;
    public RenderTexture getExtend() => extendIslandsRenderTexture;
    public RenderTexture getSupport() => supportTexture;

    // ADDED: Public getter so other scripts can find the wetness map
    public RenderTexture getWetness() => wetnessRenderTexture;

    public Renderer getRenderer() => rend;

    void Start()
    {
        maskRenderTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0);
        maskRenderTexture.filterMode = FilterMode.Bilinear;

        extendIslandsRenderTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0);
        extendIslandsRenderTexture.filterMode = FilterMode.Bilinear;

        uvIslandsRenderTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0);
        uvIslandsRenderTexture.filterMode = FilterMode.Bilinear;

        supportTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0);
        supportTexture.filterMode = FilterMode.Bilinear;

        // ADDED: Create the Wetness Map (ARGB32 prevents the R8 warning)
        wetnessRenderTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0, RenderTextureFormat.ARGB32);
        wetnessRenderTexture.filterMode = FilterMode.Bilinear;

        rend = GetComponent<Renderer>();
        rend.material.SetTexture(maskTextureID, extendIslandsRenderTexture);

        // ADDED: Send the wetness map to your TargetShader automatically
        rend.material.SetTexture(wetnessTextureID, wetnessRenderTexture);

        PaintManager.instance.initTextures(this);
    }

    void OnDisable()
    {
        maskRenderTexture.Release();
        uvIslandsRenderTexture.Release();
        extendIslandsRenderTexture.Release();
        supportTexture.Release();
        // ADDED: Release wetness map from memory
        if (wetnessRenderTexture != null) wetnessRenderTexture.Release();
    }
}