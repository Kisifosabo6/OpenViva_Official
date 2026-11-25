using UnityEngine;
using System;

namespace UnityEssentials.ActiveRagdoll
{
    public enum RagdollState
    {
        FOLLOWING_ANIMATION,
        LOOSING_STRENGTH,
        GAINING_STRENGTH,
        DEAD
    }

    public class SlaveController : MonoBehaviour
    {
        /// <summary>
        /// Controls Ragdoll following and strength.
        /// </summary>
        public RagdollState state;

        [SerializeField]
        [Tooltip("Determines how fast ragdoll loses strength when in contact.")]
        private float looseStrengthLerp = 1.0f;
        [SerializeField]
        [Tooltip("Determines how fast ragdoll gains strength after being freed from contact.")]
        private float gainStrengthLerp = 0.05f;
        [SerializeField]
        [Tooltip("Minimum force strength during collision.")]
        private float minContactForce = 0.1f;
        [SerializeField]
        [Tooltip("Minimum torque strength during collision.")]
        private float minContactTorque = 0.1f;
        [SerializeField]
        [Tooltip("Time of being dead expressed in seconds passed in simulation.")]
        private float deadTime = 4.0f;

        private AnimationFollowing animFollow;
        private float maxTorqueCoefficient;
        private float maxForceCoefficient;
        private float currentDeadStep;
        private float currentStrength;
        [NonSerialized] public int currentNumberOfCollisions;

        void Start()
        {
            HumanoidSetUp setUp = this.GetComponentInParent<HumanoidSetUp>();
            if (setUp != null)
            {
                animFollow = setUp.animFollow;
            }

            if (animFollow == null)
            {
                animFollow = GetComponent<AnimationFollowing>();
            }

            if (animFollow != null)
            {
                maxForceCoefficient = animFollow.forceCoefficient;
                maxTorqueCoefficient = animFollow.torqueCoefficient;
            }

            currentNumberOfCollisions = 0;
            currentDeadStep = deadTime;
            currentStrength = 1.0f;
        }

        void FixedUpdate()
        {
            if (animFollow == null) return;

            animFollow.FollowAnimation();

            state = GetRagdollState();
            switch (state)
            {
                case RagdollState.DEAD:
                    currentDeadStep += Time.fixedDeltaTime;
                    if (currentDeadStep >= deadTime)
                        ComeAlive();
                    break;

                case RagdollState.LOOSING_STRENGTH:
                    LooseStrength();
                    break;

                case RagdollState.GAINING_STRENGTH:
                    GainStrength();
                    break;

                case RagdollState.FOLLOWING_ANIMATION:
                    break;
            }
        }

        private RagdollState GetRagdollState()
        {
            if (!animFollow.isAlive)
            {
                return RagdollState.DEAD;
            }
            else if (currentNumberOfCollisions != 0)
            {
                return RagdollState.LOOSING_STRENGTH;
            }
            else if (currentStrength < 1)
            {
                return RagdollState.GAINING_STRENGTH;
            }
            else
            {
                return RagdollState.FOLLOWING_ANIMATION;
            }
        }

        private void LooseStrength()
        {
            currentStrength -= looseStrengthLerp * Time.fixedDeltaTime;
            currentStrength = Mathf.Clamp(currentStrength, 0, 1);
            InterpolateStrength(currentStrength);
        }

        private void GainStrength()
        {
            currentStrength += gainStrengthLerp * Time.fixedDeltaTime;
            currentStrength = Mathf.Clamp(currentStrength, 0, 1);
            InterpolateStrength(currentStrength);
        }

        private void InterpolateStrength(float ratio)
        {
            animFollow.forceCoefficient = Mathf.Lerp(minContactForce, maxForceCoefficient, ratio);
            animFollow.torqueCoefficient = Mathf.Lerp(minContactTorque, maxTorqueCoefficient, ratio);
        }

        [ContextMenu("Die")]
        public void Die()
        {
            if (animFollow != null)
            {
                animFollow.isAlive = false;
                currentDeadStep = 0;
                ResetForces();
            }
        }

        [ContextMenu("Come alive")]
        public void ComeAlive()
        {
            if (animFollow != null)
            {
                animFollow.isAlive = true;
            }
        }

        [ContextMenu("Reset forces")]
        public void ResetForces()
        {
            if (animFollow != null)
            {
                animFollow.forceCoefficient = 0f;
                animFollow.torqueCoefficient = 0f;
                currentStrength = 0;
            }
        }
    }
}
