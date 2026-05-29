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
        m_PosX = _inStream.ReadInt32();
        m_PosY = _inStream.ReadInt32();
    }
}