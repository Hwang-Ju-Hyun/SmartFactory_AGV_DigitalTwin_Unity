using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
public class Node
{
    public UInt32 m_Id;
    public float m_PosX;
    public float m_PosY;
    public byte type;
};

public class Link
{
    public UInt32 m_Id;
    public UInt32 m_FromNodeID;
    public UInt32 m_ToNodeID;    
};

public class Map : MonoBehaviour
{
    public static Map Instance { get; set; }
    private void Awake()
    {
        if (Instance == null)
        {            
            Instance = this;
        }
    }

    public List<Node> m_Nodes { get; set; }
    public List<Link> m_Links {  get; set; }
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
