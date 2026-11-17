using UnityEngine;

namespace Unity.Animations.SpringBones
{
    // Base class for providing forces to SpringBones
    public class ForceProvider : MonoBehaviour
    {
        public virtual Vector3 GetForceOnBone(SpringBone springBone)
        {
            return Vector3.zero;
        }
    }
}