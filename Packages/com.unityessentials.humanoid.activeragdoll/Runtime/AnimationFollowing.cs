using UnityEngine;
using System;

namespace UnityEssentials.ActiveRagdoll
{
    /// <summary>
    /// Applies animation following to a ragdoll using PD control and ConfigurableJoints.
    /// </summary>
    public class AnimationFollowing : MonoBehaviour
    {
        public Transform slave;     // slave root (ragdoll hips)
        public Transform master;    // master root (static animation hips)

        // ALL TRANSFORMS
        private Transform[] slaveTransforms;
        private Transform[] masterTransforms;

        // RIGIDBODIES
        private Transform[] slaveRigidTransforms;
        private Transform[] masterRigidTransforms;
        private Vector3[] rigidbodiesPosToCOM;  // positions of rigidbodies relative to center of mass

        // JOINTS
        private ConfigurableJoint[] slaveConfigurableJoints;
        private Quaternion[] startLocalRotation;
        private Quaternion[] localToJointSpace;
        private JointDrive jointDrive = new JointDrive();

        // USEFUL VARIABLES
        private Vector3[] forceLastError;
        private int numOfRigids;
        [NonSerialized]
        public bool isAlive = true;
        [NonSerialized]
        public float forceCoefficient = 1.0f; // Controls force applied to limbs
        [NonSerialized]
        public float torqueCoefficient = 1.0f; // Controls torque applied to limbs

        // ALL ADJUSTABLE PARAMETERS
        [Range(0f, 340f)] public float angularDrag = 0f;
        [Range(0f, 2f)] public float drag = 0.1f;
        [Range(0f, 1000f)] public float maxAngularVelocity = 1000f;
        [Range(0f, 10f)] public float jointDamping = 0.6f;

        [Tooltip("Proportional force of PID controller.")]
        [Range(0f, 160f)] public float PForce = 8f;
        [Tooltip("Derivative force of PID controller.")]
        [Range(0f, .064f)] public float DForce = 0.01f;

        [Range(0f, 100f)] public float maxForce = 10f;
        [Range(0f, 10000f)] public float maxJointTorque = 2000f;

        public bool useGravity = true;

        private float[] maxForceProfile;
        private float[] maxJointTorqueProfile;
        private float[] jointDampingProfile;
        private bool[] limbProfile;

        void Start()
        {
            HumanoidSetUp setUp = this.GetComponentInParent<HumanoidSetUp>();
            if (setUp != null)
            {
                if (master == null) master = setUp.masterRoot;
                if (slave == null) slave = setUp.slaveRoot;
            }

            Initialize();
        }

        public void Initialize()
        {
            if (slave == null || master == null) return;

            slaveTransforms = slave.GetComponentsInChildren<Transform>();
            masterTransforms = master.GetComponentsInChildren<Transform>();

            if (masterTransforms.Length != slaveTransforms.Length)
            {
                Debug.LogWarning("[ActiveRagdoll] Master transform count (" + masterTransforms.Length + ") does not equal slave transform count (" + slaveTransforms.Length + ").\n");
                return;
            }

            numOfRigids = slave.GetComponentsInChildren<Rigidbody>().Length;
            slaveRigidTransforms = new Transform[numOfRigids];
            masterRigidTransforms = new Transform[numOfRigids];
            rigidbodiesPosToCOM = new Vector3[numOfRigids];
            slaveConfigurableJoints = new ConfigurableJoint[numOfRigids];
            startLocalRotation = new Quaternion[numOfRigids];
            localToJointSpace = new Quaternion[numOfRigids];
            forceLastError = new Vector3[numOfRigids];

            maxForceProfile = new float[numOfRigids];
            maxJointTorqueProfile = new float[numOfRigids];
            jointDampingProfile = new float[numOfRigids];
            limbProfile = new bool[numOfRigids];

            for (int k = 0; k < numOfRigids; k++)
            {
                maxForceProfile[k] = 1f;
                maxJointTorqueProfile[k] = 1f;
                jointDampingProfile[k] = 1f;
                limbProfile[k] = true;
            }

            int i = 0, j = 0;
            foreach (Transform t in slaveTransforms)
            {
                Rigidbody rb = t.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    slaveRigidTransforms[i] = t;
                    masterRigidTransforms[i] = masterTransforms[j];
                    rigidbodiesPosToCOM[i] = Quaternion.Inverse(t.rotation) * (rb.worldCenterOfMass - t.position);

                    ConfigurableJoint cj = t.GetComponent<ConfigurableJoint>();
                    if (cj != null)
                    {
                        slaveConfigurableJoints[i] = cj;

                        Vector3 forward = Vector3.Cross(cj.axis, cj.secondaryAxis);
                        Vector3 up = cj.secondaryAxis;

                        localToJointSpace[i] = Quaternion.LookRotation(forward, up);
                        startLocalRotation[i] = t.localRotation * localToJointSpace[i];

                        jointDrive = cj.slerpDrive;
                        cj.slerpDrive = jointDrive;
                    }

                    if (t.GetComponent<CollisionDetector>() == null)
                    {
                        t.gameObject.AddComponent<CollisionDetector>();
                    }

                    i++;
                }
                j++;
            }

            ApplyRigidbodySettings();
            EnableJointLimits(true);
        }

        private void ApplyRigidbodySettings()
        {
            if (slaveRigidTransforms == null) return;
            foreach (Transform t in slaveRigidTransforms)
            {
                if (t == null) continue;
                Rigidbody rb = t.GetComponent<Rigidbody>();
                if (rb == null) continue;

                rb.useGravity = useGravity;
                rb.linearDamping = drag;
                rb.angularDamping = angularDrag;
                rb.maxAngularVelocity = maxAngularVelocity;
            }
        }

        public void FollowAnimation()
        {
            if (slaveRigidTransforms == null || slaveRigidTransforms.Length == 0) return;

            if (!isAlive)
            {
                SetJointTorque(0, 0);
                return;
            }
            else
            {
                SetJointTorque(maxJointTorque, jointDamping);
            }

            for (int i = 0; i < slaveRigidTransforms.Length; i++)
            {
                if (!limbProfile[i] || slaveRigidTransforms[i] == null) continue;

                Rigidbody rb = slaveRigidTransforms[i].GetComponent<Rigidbody>();
                if (rb == null || rb.isKinematic) continue;

                rb.linearDamping = drag;
                rb.angularDamping = angularDrag;
                rb.maxAngularVelocity = maxAngularVelocity;
                rb.useGravity = useGravity;

                // APPLY FORCE TO MATCH MASTER
                Vector3 masterWCOM = masterRigidTransforms[i].position + masterRigidTransforms[i].rotation * rigidbodiesPosToCOM[i];
                Vector3 forceError = masterWCOM - rb.worldCenterOfMass;
                Vector3 forceSignal = PDControl(PForce, DForce, forceError, ref forceLastError[i]);
                forceSignal = Vector3.ClampMagnitude(forceSignal, maxForce * maxForceProfile[i] * forceCoefficient);
                rb.AddForce(forceSignal, ForceMode.VelocityChange);

                // APPLY ROTATION TARGET
                if (i != 0 && slaveConfigurableJoints[i] != null)
                {
                    slaveConfigurableJoints[i].targetRotation = Quaternion.Inverse(localToJointSpace[i]) * Quaternion.Inverse(masterRigidTransforms[i].localRotation) * startLocalRotation[i];
                }
            }
        }

        private Vector3 PDControl(float P, float D, Vector3 error, ref Vector3 lastError)
        {
            Vector3 signal = P * (error + D * (error - lastError) / Time.fixedDeltaTime);
            lastError = error;
            return signal;
        }

        public void SetJointTorque(float positionSpring, float positionDamper)
        {
            if (slaveConfigurableJoints == null) return;
            for (int i = 1; i < slaveConfigurableJoints.Length; i++)
            {
                if (slaveConfigurableJoints[i] == null) continue;
                if (!limbProfile[i]) continue;

                jointDrive.positionSpring = positionSpring * maxJointTorqueProfile[i] * torqueCoefficient;
                jointDrive.positionDamper = positionDamper * jointDampingProfile[i];
                slaveConfigurableJoints[i].slerpDrive = jointDrive;
            }
        }

        public void EnableJointLimits(bool jointLimits)
        {
            if (slaveConfigurableJoints == null) return;
            for (int i = 1; i < slaveConfigurableJoints.Length; i++)
            {
                if (slaveConfigurableJoints[i] == null) continue;

                if (jointLimits)
                {
                    slaveConfigurableJoints[i].angularXMotion = ConfigurableJointMotion.Limited;
                    slaveConfigurableJoints[i].angularYMotion = ConfigurableJointMotion.Limited;
                    slaveConfigurableJoints[i].angularZMotion = ConfigurableJointMotion.Limited;
                }
                else
                {
                    slaveConfigurableJoints[i].angularXMotion = ConfigurableJointMotion.Free;
                    slaveConfigurableJoints[i].angularYMotion = ConfigurableJointMotion.Free;
                    slaveConfigurableJoints[i].angularZMotion = ConfigurableJointMotion.Free;
                }
            }
        }
    }
}
