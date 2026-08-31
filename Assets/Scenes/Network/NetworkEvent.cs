using System;
using System.Collections.Generic;
using UnityEngine;

public interface INetworkEvent
{
    void Execute();
}

public sealed class NetworkMapBuildEvent : INetworkEvent
{
    private readonly Dictionary<UInt32, Node> m_Nodes;
    private readonly List<Link> m_Links;
    private readonly Action<int, int> m_OnRendered;

    public NetworkMapBuildEvent(
        Dictionary<UInt32, Node> nodes,
        List<Link> links,
        Action<int, int> onRendered)
    {
        m_Nodes = nodes;
        m_Links = links;
        m_OnRendered = onRendered;
    }

    public void Execute()
    {
        if (Map.Instance == null || RenderManager.Instance == null)
        {
            throw new InvalidOperationException("Map or RenderManager is not initialized.");
        }

        Map.Instance.SetData(m_Nodes, m_Links);
        RenderManager.Instance.MapBuild(m_Nodes, m_Links);
        m_OnRendered?.Invoke(m_Nodes.Count, m_Links.Count);
    }
}

public sealed class NetworkSpawnEvent : INetworkEvent
{
    private readonly UInt32 m_NetworkID;
    private readonly UInt32 m_ClassID;
    private readonly Vector2 m_Position;
    private readonly float m_HeadingRadians;
    private readonly Action<UInt32> m_OnCreated;

    public NetworkSpawnEvent(
        UInt32 networkID,
        UInt32 classID,
        Vector2 position,
        float headingRadians,
        Action<UInt32> onCreated)
    {
        m_NetworkID = networkID;
        m_ClassID = classID;
        m_Position = position;
        m_HeadingRadians = headingRadians;
        m_OnCreated = onCreated;
    }

    public void Execute()
    {
        if (RenderManager.Instance == null ||
            !RenderManager.Instance.OnNetworkObjectCreated(
                m_NetworkID,
                m_ClassID,
                m_Position,
                m_HeadingRadians))
        {
            throw new InvalidOperationException($"Failed to render network object {m_NetworkID}.");
        }

        m_OnCreated?.Invoke(m_NetworkID);
    }
}

public sealed class NetworkUpdateEvent : INetworkEvent
{
    private readonly UInt32 m_NetworkID;
    private readonly Vector2 m_Position;
    private readonly float m_HeadingRadians;

    public NetworkUpdateEvent(UInt32 networkID, Vector2 position, float headingRadians)
    {
        m_NetworkID = networkID;
        m_Position = position;
        m_HeadingRadians = headingRadians;
    }

    public UInt32 NetworkID => m_NetworkID;

    public void Execute()
    {
        RenderManager.Instance.UpdateObjectPosition(m_NetworkID, m_Position, m_HeadingRadians);
    }
}

public sealed class NetworkDestroyEvent : INetworkEvent
{
    private readonly UInt32 m_NetworkID;

    public NetworkDestroyEvent(UInt32 networkID)
    {
        m_NetworkID = networkID;
    }

    public void Execute()
    {
        RenderManager.Instance.RemoveNetworkObject(m_NetworkID);
    }
}

public sealed class VisionObservationEvent : INetworkEvent
{
    private readonly VisionObservationPacket m_Packet;

    public VisionObservationEvent(VisionObservationPacket packet)
    {
        m_Packet = packet;
    }

    public UInt32 AgvID => m_Packet.AgvID;

    public void Execute()
    {
        if (RenderManager.Instance == null)
        {
            throw new InvalidOperationException("RenderManager is not initialized.");
        }

        RenderManager.Instance.UpdateVisionObservation(m_Packet);
    }
}

public sealed class NetworkActionEvent : INetworkEvent
{
    private readonly Action m_Action;

    public NetworkActionEvent(Action action)
    {
        m_Action = action;
    }

    public void Execute()
    {
        m_Action?.Invoke();
    }
}