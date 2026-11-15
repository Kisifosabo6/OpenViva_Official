// OpenViva Fork: Decoupled runtime bone physics from MagicaCloth2 to allow clean builds in open-source environments without commercial store assets.
using System.Collections.Generic;
using UnityEngine;

public class PhysicsAttacher : MonoBehaviour
{
    public PhysicsBoneData physicsBoneData;

    public void CreateBoneCloth(GameObject character, List<PhysicsBoneData> boneDataList)
    {
        Debug.LogWarning("MagicaCloth2 is not installed. Skipping CreateBoneCloth.");
    }
}
