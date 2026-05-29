using UnityEngine;
using System;

public interface INetworkEvent
{
    void Excute();
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
    public NetworkUpdateEvent(UInt32 _networkID, Vector2 _position, Quaternion _q)
    {
        m_networkID = _networkID;
        m_position = _position;
    }
    public void Excute()
    {
        RenderManager.Instance.UpdateObjectPosition(m_networkID, m_position);
    }
}