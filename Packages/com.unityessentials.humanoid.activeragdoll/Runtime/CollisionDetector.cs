using UnityEngine;

namespace UnityEssentials.ActiveRagdoll
{
    /// <summary>
    /// This script is attached automatically to every ragdoll limb with Rigidbody.
    /// It informs SlaveController if collision occurred.
    /// </summary>
    public class CollisionDetector : MonoBehaviour
    {
        private SlaveController slaveController;
        private LayerMask layerMask;

        void Start()
        {
            HumanoidSetUp setUp = this.GetComponentInParent<HumanoidSetUp>();
            if (setUp != null)
            {
                slaveController = setUp.slaveController;
                layerMask = setUp.dontLooseStrengthLayerMask;
            }
        }

        private bool CheckIfLayerIsInLayerMask(int layer)
        {
            return layerMask == (layerMask | (1 << layer));
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!CheckIfLayerIsInLayerMask(collision.gameObject.layer))
            {
                if (slaveController != null)
                    slaveController.currentNumberOfCollisions++;
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (!CheckIfLayerIsInLayerMask(collision.gameObject.layer))
            {
                if (slaveController != null)
                    slaveController.currentNumberOfCollisions--;
            }
        }
    }
}
