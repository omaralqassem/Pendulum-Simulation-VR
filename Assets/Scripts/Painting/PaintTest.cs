using UnityEngine;

public class PaintTest : MonoBehaviour
{
    public Paintable targetPaintable;
    public Color testColor = Color.red;
    public float testRadius = 1f;

    void Update()
    {
        // Press Space to paint at the object's center  
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (targetPaintable != null)
            {
                Vector3 paintPos = targetPaintable.transform.position;
                PaintManager.instance.paint(
                    targetPaintable,
                    paintPos,
                    testRadius,
                    hardness: 0.5f,
                    strength: 0.5f,
                    testColor
                );
                Debug.Log("Painted at: " + paintPos);
            }
        }

        // Press T to paint at multiple random points  
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (targetPaintable != null)
            {
                Renderer renderer = targetPaintable.GetComponent<Renderer>();
                Bounds bounds = renderer.bounds;

                for (int i = 0; i < 10; i++)
                {
                    Vector3 randomPoint = new Vector3(
                        Random.Range(bounds.min.x, bounds.max.x),
                        Random.Range(bounds.min.y, bounds.max.y),
                        Random.Range(bounds.min.z, bounds.max.z)
                    );

                    PaintManager.instance.paint(
                        targetPaintable,
                        randomPoint,
                        Random.Range(0.5f, 1.5f),
                        hardness: 0.5f,
                        strength: 0.5f,
                        testColor
                    );
                }
                Debug.Log("Painted 10 random points");
            }
        }
    }
}
