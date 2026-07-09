using UnityEngine;
using UnityEngine.Rendering;

public class PaintManager : Singleton<PaintManager>
{

    public Shader texturePaint;
    public Shader extendIslands;
    public Shader wetnessPaint;
    [Header("Watercolor Settings")]
    public bool enableWatercolorMixing = true;

    int prepareUVID = Shader.PropertyToID("_PrepareUV");
    int positionID = Shader.PropertyToID("_PainterPosition");
    int hardnessID = Shader.PropertyToID("_Hardness");
    int strengthID = Shader.PropertyToID("_Strength");
    int radiusID = Shader.PropertyToID("_Radius");
    int blendOpID = Shader.PropertyToID("_BlendOp");
    int colorID = Shader.PropertyToID("_PainterColor");
    int textureID = Shader.PropertyToID("_MainTex");
    int uvOffsetID = Shader.PropertyToID("_OffsetUV");
    int uvIslandsID = Shader.PropertyToID("_UVIslands");

    Material paintMaterial;
    Material extendMaterial;
    Material wetnessMaterial;

    CommandBuffer command;

    public override void Awake()
    {
        base.Awake();

        paintMaterial = new Material(texturePaint);
        extendMaterial = new Material(extendIslands);
        wetnessMaterial = new Material(wetnessPaint);

        command = new CommandBuffer();
        command.name = "CommmandBuffer - " + gameObject.name;
    }

    public void initTextures(Paintable paintable)
    {
        RenderTexture mask = paintable.getMask();
        RenderTexture uvIslands = paintable.getUVIslands();
        RenderTexture extend = paintable.getExtend();
        RenderTexture support = paintable.getSupport();
        RenderTexture wetness = paintable.getWetness();
        Renderer rend = paintable.getRenderer();

        command.SetRenderTarget(mask);
        command.ClearRenderTarget(true, true, Color.clear);
        command.SetRenderTarget(support);
        command.ClearRenderTarget(true, true, Color.clear);
        command.SetRenderTarget(extend);
        command.ClearRenderTarget(true, true, Color.clear);
        command.SetRenderTarget(wetness);
        command.ClearRenderTarget(true, true, Color.clear);

        paintMaterial.SetFloat(prepareUVID, 1);
        command.SetRenderTarget(uvIslands);
        command.ClearRenderTarget(true, true, Color.clear);
        command.DrawRenderer(rend, paintMaterial, 0);

        Graphics.ExecuteCommandBuffer(command);
        command.Clear();
    }

    public void paint(Paintable paintable, Vector3 pos, float radius = 1f, float hardness = .5f, float strength = .5f, Color? color = null)
    {
        RenderTexture mask = paintable.getMask();
        RenderTexture uvIslands = paintable.getUVIslands();
        RenderTexture extend = paintable.getExtend();
        RenderTexture support = paintable.getSupport();
        RenderTexture wetness = paintable.getWetness();
        Renderer rend = paintable.getRenderer();

        // STEP 1: DRAW COLOR
        paintMaterial.SetFloat(prepareUVID, 0);
        paintMaterial.SetVector(positionID, pos);
        paintMaterial.SetFloat(hardnessID, hardness);
        paintMaterial.SetFloat(strengthID, strength);
        paintMaterial.SetFloat(radiusID, radius);
        paintMaterial.SetTexture(textureID, support);
        paintMaterial.SetColor(colorID, color ?? Color.red);
        extendMaterial.SetFloat(uvOffsetID, paintable.extendsIslandOffset);
        extendMaterial.SetTexture(uvIslandsID, uvIslands);

        // Toggle the bool
        if (enableWatercolorMixing)
        {
            command.EnableShaderKeyword("WATERCOLOR_MIX");
        }
        else
        {
            command.DisableShaderKeyword("WATERCOLOR_MIX");
        }

        command.SetRenderTarget(mask);
        command.DrawRenderer(rend, paintMaterial, 0);
        command.SetRenderTarget(support);
        command.Blit(mask, support);



        // Turn it off after drawing so it doesn't affect the wetness pass
        command.DisableShaderKeyword("WATERCOLOR_MIX");

        Graphics.ExecuteCommandBuffer(command);
        command.Clear();

        // STEP 2: DRAW WETNESS
        wetnessMaterial.SetVector(positionID, pos);
        wetnessMaterial.SetFloat(hardnessID, hardness);
        wetnessMaterial.SetFloat(strengthID, 1.0f);
        wetnessMaterial.SetFloat(radiusID, radius);

        command.SetRenderTarget(wetness);
        command.DrawRenderer(rend, wetnessMaterial, 0);

        Graphics.ExecuteCommandBuffer(command);
        command.Clear();

        // RE-EXTEND AT THE VERY END
        command.SetRenderTarget(extend);
        command.Blit(mask, extend, extendMaterial);
        Graphics.ExecuteCommandBuffer(command);
        command.Clear();
    }
}