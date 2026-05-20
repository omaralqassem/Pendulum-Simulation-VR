using UnityEngine;

public class DampingController : MonoBehaviour
{

    //ρ 
    public float airDensity = 1.225f; 
    //C_d
    public float dragCoefficient = 0.47f; 
    //A
    public float crossSectionArea = 0.05f; 
    //must be modefied later
    public float bucketMass = 1f;



    public float CalculateAngularDrag
    (
        float angularVelocity,
        float pendulumLength
    )
    {
        //Linear velocity
        float linearVelocity =
            angularVelocity * pendulumLength;

        //Drag force 
        float dragForce =
            0.5f *
            airDensity *
            dragCoefficient *
            crossSectionArea *
            linearVelocity *
            linearVelocity;

        //Oppose motion direction
        dragForce *= Mathf.Sign(linearVelocity);

        float angularDragAcceleration =
            -(dragForce) /
            (bucketMass * pendulumLength);

        return angularDragAcceleration;
    }
}