using UnityEngine;

namespace Viva.Dolls
{
    [CreateAssetMenu(fileName = "DollPoseCache", menuName = "OpenViva/Doll Pose Cache", order = 1)]
    public class DollPoseCache : ScriptableObject
    {
        public Vector3[] positions;
        public Quaternion[] quaternions;
    }
}
