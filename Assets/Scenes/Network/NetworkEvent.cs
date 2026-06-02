using UnityEngine;
using System;
using NUnit.Framework;
using System.Collections.Generic;

public interface INetworkEvent
{
    void Excute();
}

public class NetworkMapBuildEvent:INetworkEvent
{
    private List<Node> m_Nodes;
    private List<Link> m_Links;
    public NetworkMapBuildEvent(List<Node> nodes, List<Link> links)
    {
        m_Nodes = nodes;
        m_Links = links;
    }
    public void Excute()
    {
        RenderManager.Instance.MapBuild(m_Nodes, m_Links);
    }
}

public class NetworkSpawnEvent:INetworkEvent
{
    private UInt32 m_networkID, m_classID;
    public NetworkSpawnEvent(UInt32 _networkID,UInt32 _classID)
    {
        m_networkID = _networkID;
        m_classID = _classID;
    }

    public void Excute() 
    {
        RenderManager.Instance.OnNetworkObjectCreated(m_networkID, m_classID);
    }
}

//todo: ¹Ì¿Ï¼º
public class NetworkUpdateEvent : INetworkEvent
{
    private UInt32 m_networkID;
    private Vector2 m_position;
    private Quaternion m_quat;
    public NetworkUpdateEvent(UInt32 _networkID, Vector2 _position, Quaternion _quat)
    {
        m_networkID = _networkID;
        m_position = _position;
        m_quat = _quat;
    }
    public void Excute()
    {
        RenderManager.Instance.UpdateObjectPosition(m_networkID, m_position,m_quat);
    }
}