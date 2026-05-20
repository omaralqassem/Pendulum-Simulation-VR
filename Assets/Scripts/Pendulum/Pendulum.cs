using UnityEngine;

public class Pendulum: MonoBehaviour
{
    public Transform pivot;
    public Transform bucket;
    public float length = 5f;
    public float gravity = 9.81f;
    public float angleDegrees = 45f;
   
    //هي تيتا
    private float angle;
//هي نفسا w يلي حنستخدما بالرانج كوتا
    private float angularVelocity;
<<<<<<< HEAD
    public float damping = 0.2f;
=======

    //connect with Damping
    private DampingController dampingController;
>>>>>>> 12a6ab03a2ad0fbf8ee143711ed41b0ad189ef8a

   
    void Start()
    {
        angle = angleDegrees * Mathf.Deg2Rad;
<<<<<<< HEAD
=======
        dampingController =
            GetComponent<DampingController>();  
>>>>>>> 12a6ab03a2ad0fbf8ee143711ed41b0ad189ef8a
    }
    void FixedUpdate()
    {
        //يلي هي h بمعادلة رنج كوتا وقت كنا نكتب k=f(t+h/2,y+h/2*k)
        float dt = Time.fixedDeltaTime;
        RK4(dt);

        UpdateBucketPosition();

    }

    //هي نفسا  dw/dt
    float AngularAcceleration(float currentAngle,float currentVelocity)
    {
<<<<<<< HEAD
        return -(gravity / length)* Mathf.Sin(currentAngle)- damping * currentVelocity;
=======
        float gravityTerm =
                -(gravity / length) *
                Mathf.Sin(currentAngle);

                
        //Air drag effect
        float dragTerm =
                dampingController.CalculateAngularDrag
                (
                    currentVelocity,
                    length
                );

            return gravityTerm + dragTerm;

>>>>>>> 12a6ab03a2ad0fbf8ee143711ed41b0ad189ef8a
    }
    void RK4(float dt)
    {
        float k1_theta = angularVelocity;
        float k1_omega = AngularAcceleration(angle,angularVelocity);
        float k2_theta =angularVelocity +0.5f * dt * k1_omega;
        float k2_omega = AngularAcceleration(angle + 0.5f * dt * k1_theta, angularVelocity + 0.5f * dt * k1_omega);
        float k3_theta =angularVelocity +0.5f * dt * k2_omega;
        float k3_omega =AngularAcceleration( angle + 0.5f * dt * k2_theta,angularVelocity + 0.5f * dt * k2_omega);
        float k4_theta =angularVelocity +dt * k3_omega;
        float k4_omega =AngularAcceleration(angle + dt * k3_theta,angularVelocity + dt * k3_omega);
        angle += (dt / 6f) *(k1_theta +2f * k2_theta +2f * k3_theta +k4_theta);
        angularVelocity += (dt / 6f) *(k1_omega +2f * k2_omega +2f * k3_omega +k4_omega);
    }

    void UpdateBucketPosition()
    {
        float x = length * Mathf.Sin(angle);

        float y = -length * Mathf.Cos(angle);

        bucket.position =pivot.position +new Vector3(x, y, 0);
        bucket.rotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
    }

}
