using System;
using UnityEngine;

public class AGV : Object
{
   
    private void Awake()
    {
        m_ClassID = (UInt32)CLASS_ID.OBJ_AGV;

    }
    public override UInt32 GetClassID() { return m_ClassID; }
    public static AGV Create()
    {
        return new AGV();
    }
    public override void Read(InputMemoryStream _inStream)
    {
        m_PosX = _inStream.ReadFloat();
        m_PosY = _inStream.ReadFloat();

        float qx = _inStream.ReadFloat();
        float qy = _inStream.ReadFloat();
        float qz= _inStream.ReadFloat();
        float qw= _inStream.ReadFloat();

        Quaternion quaternion = new Quaternion(qx, qy, qz, qw);

        //m_Rot= quaternion;
    }
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.GetComponentInChildren<Object>().GetClassID() == (UInt32)CLASS_ID.OBJ_AGV)
        {
            Debug.Log("OnCollisionEnter " + collision.gameObject.name);
        }
        
    }

}