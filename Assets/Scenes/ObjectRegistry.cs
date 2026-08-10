using System;
using System.Collections.Generic;

public sealed class ObjectRegistry
{
    private readonly Dictionary<UInt32, Func<NetworkObjectState>> m_CreationFunctions =
        new Dictionary<UInt32, Func<NetworkObjectState>>();

    public static ObjectRegistry Instance { get; private set; }

    public static void StaticInit()
    {
        Instance = new ObjectRegistry();
    }

    public void RegisterCreateFunction(UInt32 classID, Func<NetworkObjectState> createFunction)
    {
        if (createFunction == null)
        {
            throw new ArgumentNullException(nameof(createFunction));
        }

        if (m_CreationFunctions.ContainsKey(classID))
        {
            throw new InvalidOperationException($"ClassID {classID} is already registered.");
        }

        m_CreationFunctions.Add(classID, createFunction);
    }

    public NetworkObjectState CreateObject(UInt32 classID)
    {
        return m_CreationFunctions.TryGetValue(classID, out Func<NetworkObjectState> createFunction)
            ? createFunction()
            : null;
    }
}
