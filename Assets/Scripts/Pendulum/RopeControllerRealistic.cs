using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RopeControllerRealistic : MonoBehaviour
{
    //Objects that will interact with the rope
    public Transform whatTheRopeIsConnectedTo;
    public Transform whatIsHangingFromTheRope;

    //Line renderer used to display the rope
    LineRenderer lineRenderer;

    //A list with all rope section
    public List<RopeSection> allRopeSections = new List<RopeSection>();

    //Rope data
    [Header("Rope data")]
    [SerializeField]private float ropeSectionLength = 5f;

    //Data we can change to change the properties of the rope
    //Spring constant
    [Header("Spring constant")]
    [SerializeField] private float kRope = 40f;
    //Damping from rope friction constant
    [Header("Damping from rope friction constant")]
    [SerializeField] private float dRope = 2f;
    //Damping from air resistance constant
    [Header("Damping from air resistance constant")]
    [SerializeField] private float aRope = 0.05f;
    //Mass of one rope section
    [Header("Mass of one rope section")]
    [SerializeField] private float mRopeSection = 0.2f;
    [Header("number of Section")]
    [SerializeField] private int numberSection = 7;
    [Header("Simulate the rope \n How accurate should the simulation be?")]
    [SerializeField] private int iterations = 1;
    [Header("ropeWidth")]
    [SerializeField] private float ropeWidth = 0.2f;

    void Start()
    {
        //Init the line renderer we use to display the rope
        lineRenderer = GetComponent<LineRenderer>();

        //
        //Create the rope
        //
        //Build the rope from the top
        Vector3 pos = whatTheRopeIsConnectedTo.position;

        List<Vector3> ropePositions = new List<Vector3>();

        for (int i = 0; i < numberSection; i++)
        {
            ropePositions.Add(pos);

            pos.y -= ropeSectionLength;
        }

        //But add the rope sections from bottom because it's easier to add
        //more sections to it if we have a winch
        for (int i = ropePositions.Count - 1; i >= 0; i--)
        {
            allRopeSections.Add(new RopeSection(ropePositions[i]));
        }
    }

    void Update()
    {
        //Display the rope with the line renderer
        DisplayRope();

        //Compare the current length of the rope with the wanted length
        DebugRopeLength();

        //Move what is hanging from the rope to the end of the rope
        whatIsHangingFromTheRope.position = allRopeSections[0].pos;

    }

    void FixedUpdate()
    {
        if (allRopeSections.Count > 0)
        {
            //Simulate the rope
            //How accurate should the simulation be?
            

            //Time step
            float timeStep = Time.fixedDeltaTime / (float)iterations;

            for (int i = 0; i < iterations; i++)
            {
                UpdateRopeSimulation(allRopeSections, timeStep);
            }
        }
    }

    //Display the rope with a line renderer
    private void DisplayRope()
    {
        

        lineRenderer.startWidth = ropeWidth;
        lineRenderer.endWidth = ropeWidth;

        //An array with all rope section positions
        Vector3[] positions = new Vector3[allRopeSections.Count];

        for (int i = 0; i < allRopeSections.Count; i++)
        {
            positions[i] = allRopeSections[i].pos;
        }

        lineRenderer.positionCount = positions.Length;

        lineRenderer.SetPositions(positions);
    }

    private void UpdateRopeSimulation(List<RopeSection> allRopeSections, float timeStep)
    {
        //Move the last position, which is the top position, to what the rope is attached to
        RopeSection lastRopeSection = allRopeSections[allRopeSections.Count - 1];

        lastRopeSection.pos = whatTheRopeIsConnectedTo.position;

        allRopeSections[allRopeSections.Count - 1] = lastRopeSection;


        //
        // Calculate the next pos and vel with 4th Order Runge-Kutta (RK4)
        //

        // k1
        List<Vector3> a0 = CalculateAccelerations(allRopeSections);
        List<RopeSection> state1 = new List<RopeSection>(allRopeSections.Count);
        for (int i = 0; i < allRopeSections.Count; i++)
        {
            if (i == allRopeSections.Count - 1) { state1.Add(allRopeSections[i]); continue; }
            RopeSection rs = RopeSection.zero;
            rs.pos = allRopeSections[i].pos + allRopeSections[i].vel * (timeStep * 0.5f);
            rs.vel = allRopeSections[i].vel + a0[i] * (timeStep * 0.5f);
            state1.Add(rs);
        }

        // k2
        List<Vector3> a1 = CalculateAccelerations(state1);
        List<RopeSection> state2 = new List<RopeSection>(allRopeSections.Count);
        for (int i = 0; i < allRopeSections.Count; i++)
        {
            if (i == allRopeSections.Count - 1) { state2.Add(allRopeSections[i]); continue; }
            RopeSection rs = RopeSection.zero;
            rs.pos = allRopeSections[i].pos + state1[i].vel * (timeStep * 0.5f);
            rs.vel = allRopeSections[i].vel + a1[i] * (timeStep * 0.5f);
            state2.Add(rs);
        }

        // k3
        List<Vector3> a2 = CalculateAccelerations(state2);
        List<RopeSection> state3 = new List<RopeSection>(allRopeSections.Count);
        for (int i = 0; i < allRopeSections.Count; i++)
        {
            if (i == allRopeSections.Count - 1) { state3.Add(allRopeSections[i]); continue; }
            RopeSection rs = RopeSection.zero;
            rs.pos = allRopeSections[i].pos + state2[i].vel * timeStep;
            rs.vel = allRopeSections[i].vel + a2[i] * timeStep;
            state3.Add(rs);
        }

        // k4
        List<Vector3> a3 = CalculateAccelerations(state3);

        // Final integration step for all segments (except top Anchor)
        for (int i = 0; i < allRopeSections.Count - 1; i++)
        {
            RopeSection rs = allRopeSections[i];

            Vector3 k1_v = a0[i], k2_v = a1[i], k3_v = a2[i], k4_v = a3[i];
            Vector3 k1_p = allRopeSections[i].vel;
            Vector3 k2_p = state1[i].vel;
            Vector3 k3_p = state2[i].vel;
            Vector3 k4_p = state3[i].vel;

            rs.vel += (timeStep / 6f) * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);
            rs.pos += (timeStep / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);

            allRopeSections[i] = rs;
        }


        //Implement maximum stretch to avoid numerical instabilities
        //May need to run the algorithm several times
        int maximumStretchIterations = 2;

        for (int i = 0; i < maximumStretchIterations; i++)
        {
            ImplementMaximumStretch(allRopeSections);
        }
    }

    //Calculate accelerations in each rope section which is what is needed to get the next pos and vel
    private List<Vector3> CalculateAccelerations(List<RopeSection> allRopeSections)
    {
        List<Vector3> accelerations = new List<Vector3>();

        //Spring constant
        float k = kRope;
        //Damping constant
        float d = dRope;
        //Damping constant from air resistance
        float a = aRope;
        //Mass of one rope section
        float m = mRopeSection;
        //How long should the rope section be
        float wantedLength = ropeSectionLength;
        

        //Calculate all forces once because some sections are using the same force but negative
        List<Vector3> allForces = new List<Vector3>();

        for (int i = 0; i < allRopeSections.Count - 1; i++)
        {
            //From Physics for game developers book
            //The force exerted on body 1
            //pos1 (above) - pos2
            Vector3 vectorBetween = allRopeSections[i + 1].pos - allRopeSections[i].pos;

            float distanceBetween = vectorBetween.magnitude;

            Vector3 dir = vectorBetween.normalized;

            float springForce = k * (distanceBetween - wantedLength);


            //Damping from rope friction 
            //vel1 (above) - vel2
            float frictionForce = d * ((Vector3.Dot(allRopeSections[i + 1].vel - allRopeSections[i].vel, vectorBetween)) / distanceBetween);


            //The total force on the spring
            Vector3 springForceVec = -(springForce + frictionForce) * dir;

            //This is body 2 if we follow the book because we are looping from below, so negative
            springForceVec = -springForceVec;

            allForces.Add(springForceVec);
        }


        //Loop through all line segments (except the last because it's always connected to something)
        //and calculate the acceleration
        for (int i = 0; i < allRopeSections.Count - 1; i++)
        {
            Vector3 springForce = Vector3.zero;

            //Spring 1 - above
            springForce += allForces[i];

            //Spring 2 - below
            //The first spring is at the bottom so it doesnt have a section below it
            if (i != 0)
            {
                springForce -= allForces[i - 1];
            }

            //Damping from air resistance, which depends on the square of the velocity
            float vel = allRopeSections[i].vel.magnitude;

            Vector3 dampingForce = a * vel * vel * allRopeSections[i].vel.normalized;

            //The mass attached to this spring
            float springMass = m;

           
            if (i == 0)
            {
                if (whatIsHangingFromTheRope.TryGetComponent<BucketPhysics>(out BucketPhysics bp))
                {
                    springMass += bp.GetTotalMass();
                }
            }

            //Force from gravity
            Vector3 gravityForce = springMass * new Vector3(0f, -9.81f, 0f);

            //The total force on this spring
            Vector3 totalForce = springForce + gravityForce - dampingForce;
           
            //Calculate the acceleration a = F / m
            Vector3 acceleration = totalForce / springMass;

            accelerations.Add(acceleration);
        }

        //The last line segment's acc is always 0 because it's attached to something
        accelerations.Add(Vector3.zero);


        return accelerations;
    }

    //Implement maximum stretch to avoid numerical instabilities
    private void ImplementMaximumStretch(List<RopeSection> allRopeSections)
    {
        //Make sure each spring are not less compressed than 90% nor more stretched than 110%
        float maxStretch = 1.1f;
        float minStretch = 0.9f;

        //Loop from the end because it's better to adjust the top section of the rope before the bottom
        //And the top of the rope is at the end of the list
        for (int i = allRopeSections.Count - 1; i > 0; i--)
        {
            RopeSection topSection = allRopeSections[i];

            RopeSection bottomSection = allRopeSections[i - 1];

            //The distance between the sections
            float dist = (topSection.pos - bottomSection.pos).magnitude;

            //What's the stretch/compression
            float stretch = dist / ropeSectionLength;

            if (stretch > maxStretch)
            {
                //How far do we need to compress the spring?
                float compressLength = dist - (ropeSectionLength * maxStretch);

                //In what direction should we compress the spring?
                Vector3 compressDir = (topSection.pos - bottomSection.pos).normalized;

                Vector3 change = compressDir * compressLength;

                MoveSection(change, i - 1);
            }
            else if (stretch < minStretch)
            {
                //How far do we need to stretch the spring?
                float stretchLength = (ropeSectionLength * minStretch) - dist;

                //In what direction should we compress the spring?
                Vector3 stretchDir = (bottomSection.pos - topSection.pos).normalized;

                Vector3 change = stretchDir * stretchLength;

                MoveSection(change, i - 1);
            }
        }
    }

    //Move a rope section based on stretch/compression
    private void MoveSection(Vector3 finalChange, int listPos)
    {
        RopeSection bottomSection = allRopeSections[listPos];

        //Move the bottom section
        Vector3 pos = bottomSection.pos;

        pos += finalChange;

        bottomSection.pos = pos;

        allRopeSections[listPos] = bottomSection;
    }

    //Compare the current length of the rope with the wanted length
    private void DebugRopeLength()
    {
        float currentLength = 0f;

        for (int i = 1; i < allRopeSections.Count; i++)
        {
            float thisLength = (allRopeSections[i].pos - allRopeSections[i - 1].pos).magnitude;

            currentLength += thisLength;
        }

        float wantedLength = ropeSectionLength * (float)(allRopeSections.Count - 1);

        //print("Wanted: " + wantedLength + " Actual: " + currentLength);
    }
}