using System;
using UnityEngine;

public abstract class Object : MonoBehaviour
{
    public float m_PosX { get; set; }
    public float m_PosY { get; set; }

    public Quaternion m_Rot { get; set; } 
    public float m_HeadingAngle {  get; set; }

    Vector2 m_Position { get; set; }

    protected UInt32 m_ClassID = (UInt32)CLASS_ID.OBJ_DEFAULT;
    public virtual UInt32 GetClassID() { return m_ClassID; }
    
    public abstract void Read(InputMemoryStream _inStream);
    
}
