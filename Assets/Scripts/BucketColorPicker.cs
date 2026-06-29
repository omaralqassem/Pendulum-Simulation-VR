using UnityEditor;
using UnityEngine;

public class BucketColorPicker : MonoBehaviour
{
    [SerializeField] private SPHSystem sphSystem;

    private Color selectedColor = Color.blue;

    // Custom UI Styles
    private GUIStyle windowStyle;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle buttonStyle;

    private Texture2D bgTexture;
    private Texture2D previewTexture;

    private void Start()
    {
        bgTexture = CreateSolidTexture(new Color(0.11f, 0.11f, 0.12f, 0.98f));

        // Make the texture slightly larger internally to ensure sharp display
        previewTexture = new Texture2D(16, 16);
        UpdateSolidTexture(previewTexture, selectedColor);
    }

    private void OnGUI()
    {
        if (windowStyle == null) InitializeStyles();

        float width = 380f;
        float height = 440f;

        Rect windowRect = new Rect(
            Screen.width - width - 30,
            40,
            width,
            height);

        GUILayout.BeginArea(windowRect, windowStyle);

        // Header Title
        GUILayout.Label("BUCKET COLOR CONFIG", headerStyle);
        GUILayout.Space(15);

        // ------------------------------------
        // Giant Color Preview Panel (GUARANTEED VISIBILITY)
        // ------------------------------------
        UpdateSolidTexture(previewTexture, selectedColor);

        // 1. Reserve layout space for our preview block
        Rect previewRect = GUILayoutUtility.GetRect(340, 80);

        // 2. Force Unity to draw the texture directly to that rectangle
        GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill);

        GUILayout.Space(25);

        // ------------------------------------
        // RGB Slider Rows
        // ------------------------------------
        EditorGUI.BeginChangeCheck();

        float r = DrawColorSlider("R", selectedColor.r, Color.red);
        float g = DrawColorSlider("G", selectedColor.g, Color.green);
        float b = DrawColorSlider("B", selectedColor.b, Color.cyan);
        float a = DrawColorSlider("A", selectedColor.a, Color.white);

        if (EditorGUI.EndChangeCheck())
        {
            selectedColor = new Color(r, g, b, a);
        }

        GUILayout.Space(30);

        // ------------------------------------
        // Large Action Button
        // ------------------------------------
        if (GUILayout.Button("FILL BUCKET", buttonStyle, GUILayout.Height(55)))
        {
            RefillBucket(selectedColor);
        }

        GUILayout.EndArea();
    }

    private float DrawColorSlider(string label, float value, Color labelColor)
    {
        GUILayout.BeginHorizontal();

        labelStyle.normal.textColor = labelColor;
        GUILayout.Label(label, labelStyle, GUILayout.Width(25));

        float newValue = GUILayout.HorizontalSlider(value, 0f, 1f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, GUILayout.MinWidth(220));
        GUILayout.Space(15);

        labelStyle.normal.textColor = Color.white;
        GUILayout.Label(Mathf.RoundToInt(newValue * 255f).ToString().PadLeft(3), valueStyle, GUILayout.Width(40));

        GUILayout.EndHorizontal();
        GUILayout.Space(12);

        return newValue;
    }

    private void InitializeStyles()
    {
        windowStyle = new GUIStyle();
        windowStyle.normal.background = bgTexture;
        windowStyle.padding = new RectOffset(20, 20, 20, 20);

        headerStyle = new GUIStyle();
        headerStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 16;
        headerStyle.alignment = TextAnchor.MiddleCenter;

        labelStyle = new GUIStyle();
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.fontSize = 16;
        labelStyle.alignment = TextAnchor.MiddleLeft;

        valueStyle = new GUIStyle(labelStyle);
        valueStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        valueStyle.alignment = TextAnchor.MiddleRight;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.fontSize = 16;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.green;
        buttonStyle.active.textColor = Color.yellow;
    }

    private Texture2D CreateSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void UpdateSolidTexture(Texture2D texture, Color color)
    {
        if (texture != null)
        {
            // Fill all pixels of our 16x16 grid to make sure it renders cleanly
            for (int x = 0; x < texture.width; x++)
            {
                for (int y = 0; y < texture.height; y++)
                {
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
        }
    }

    private void RefillBucket(Color color)
    {
        if (sphSystem != null)
        {
            sphSystem.RefillBucket(color);
        }
    }

    private void OnDestroy()
    {
        if (bgTexture != null) Destroy(bgTexture);
        if (previewTexture != null) Destroy(previewTexture);
    }
}