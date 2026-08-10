using System;
using UnityEngine;

public sealed class AGVState : NetworkObjectState
{
    public AGVState()
        : base((UInt32)CLASS_ID.OBJ_AGV)
    {
    }

    public static NetworkObjectState Create()
    {
        return new AGVState();
    }

    public override void Read(InputMemoryStream inStream)
    {
        PosX = inStream.ReadFloat();
        PosZ = inStream.ReadFloat();
        HeadingRadians = inStream.ReadFloat();
    }
}

// View-only component retained on AGV_Prefab. It is never created with new.
public class AGV : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.GetComponentInChildren<AGV>() != null)
        {
            Debug.Log("OnCollisionEnter " + collision.gameObject.name);
        }
    }
}
