using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class Node
{
    public UInt32 m_Id;
    public float m_PosX;
    public float m_PosY;
    public byte type;
}

public sealed class Link
{
    public UInt32 m_Id;
    public UInt32 m_FromNodeID;
    public UInt32 m_ToNodeID;
    public byte m_Type;
    public float m_CX1;
    public float m_CZ1;
    public float m_CX2;
    public float m_CZ2;
}

public class Map : MonoBehaviour
{
    public static Map Instance { get; private set; }

    public Dictionary<UInt32, Node> m_Nodes { get; private set; }
    public List<Link> m_Links { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    public void SetData(Dictionary<UInt32, Node> nodes, List<Link> links)
    {
        m_Nodes = nodes;
        m_Links = links;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
