using System;
using System.Collections.Generic;

public sealed class LinkingContext
{
    private readonly Dictionary<UInt32, NetworkObjectState> m_Objects =
        new Dictionary<UInt32, NetworkObjectState>();

    public NetworkObjectState GetObject(UInt32 networkID)
    {
        m_Objects.TryGetValue(networkID, out NetworkObjectState state);
        return state;
    }

    public bool AddObject(UInt32 networkID, NetworkObjectState state)
    {
        if (state == null || m_Objects.ContainsKey(networkID))
        {
            return false;
        }

        m_Objects.Add(networkID, state);
        return true;
    }

    public bool RemoveObject(UInt32 networkID)
    {
        return m_Objects.Remove(networkID);
    }
}
