using UnityEngine;

public class CanvasDrying : MonoBehaviour
{
    private RenderTexture wetnessMap;
    public float dryingRate = 0.5f;
    private Material dryMaterial;

    private bool isInitialized = false;

    public Shader DryShader;

    void Update()
    {
        // Wait here until Paintable creates the wetness map
        if (!isInitialized)
        {
            dryMaterial = new Material(DryShader);
            wetnessMap = GetComponent<Paintable>().getWetness();

            if (dryMaterial == null)
            {
                Debug.LogError("DRY SHADER MISSING! Make sure you created Hidden/DryShader.");
                return;
            }
            if (wetnessMap == null)
            {
                return; // Paintable hasn't started yet, wait one more frame
            }

            isInitialized = true; // Map is found, start drying!
        }

        // Normal drying logic
        dryMaterial.SetFloat("DryingRate", dryingRate * Time.deltaTime);

        RenderTexture temp = RenderTexture.GetTemporary(wetnessMap.width, wetnessMap.height, 0, wetnessMap.format);
        Graphics.Blit(wetnessMap, temp, dryMaterial);
        Graphics.Blit(temp, wetnessMap);
        RenderTexture.ReleaseTemporary(temp);
    }
}