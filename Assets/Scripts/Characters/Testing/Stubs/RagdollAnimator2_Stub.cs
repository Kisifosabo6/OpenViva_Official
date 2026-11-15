using System.Collections.Generic;
using UnityEngine;

namespace FIMSpace.FProceduralAnimation
{
    public class RagdollAnimatorFeatureBase : ScriptableObject
    {
    }

    public class RagdollHandler
    {
        public enum EAnimatingMode
        {
            Standing,
            Falling,
            Sleep
        }

        public class RagdollChain
        {
            public float ChainThicknessMultiplier = 1f;
        }

        public List<RagdollChain> Chains { get; set; } = new List<RagdollChain>
        {
            new RagdollChain(),
            new RagdollChain(),
            new RagdollChain(),
            new RagdollChain(),
            new RagdollChain()
        };

        public void AddRagdollFeature(RagdollAnimatorFeatureBase feature)
        {
        }
    }

    public class RagdollSettings
    {
        public Transform BaseTransform { get; set; }

        public void Initialize(RagdollAnimator2 ragdoll, GameObject target)
        {
        }

        public void User_UpdateAllBonesParametersAfterManualChanges()
        {
        }
    }

    [AddComponentMenu("FIMSpace/Ragdoll Animator 2 (Fallback Stub)")]
    public class RagdollAnimator2 : MonoBehaviour
    {
        public RagdollSettings Settings { get; set; } = new RagdollSettings();
        public RagdollHandler Handler { get; set; } = new RagdollHandler();

        public float RagdollBlend { get; set; } = 0f;
        public RagdollHandler.EAnimatingMode AnimatingMode { get; set; } = RagdollHandler.EAnimatingMode.Standing;

        public void TryFindBonesAndDoFullSetup()
        {
        }

        public void User_SwitchFallState(RagdollHandler.EAnimatingMode mode)
        {
            AnimatingMode = mode;
        }

        public void User_SwitchFallState(bool standing)
        {
            AnimatingMode = standing ? RagdollHandler.EAnimatingMode.Standing : RagdollHandler.EAnimatingMode.Falling;
        }

        public void User_FadeMusclesPower(float targetPower = 0f, float duration = 1f, float delay = 0f)
        {
        }
    }
}
