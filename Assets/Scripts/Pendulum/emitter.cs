using UnityEngine;

public class SPHTester : MonoBehaviour
{
    public SPH sph;

    void Start()
    {
        Debug.Log("START CALLED");

        sph.EmitParticles(
            new Vector3(0, 1, 0),
            Vector3.zero,
            1000
        );
    }
}