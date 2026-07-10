using System;
using System.Collections.Generic;
public struct RouteNodeTime
{
    public UInt32 nodeID;
    public float arrivalTime;
    public float departureTime;
};

public struct RoutePacket
{
    public UInt32 agvID;
    public List<RouteNodeTime> nodes;

    public void Serialize(OutputMemoryStream outStream)
    {
        outStream.WriteUInt32(agvID);
        outStream.WriteShort((short)nodes.Count);
        foreach (RouteNodeTime n in nodes)
        {
            outStream.WriteUInt32(n.nodeID);
            outStream.WriteFloat(n.arrivalTime);
            outStream.WriteFloat((float)n.departureTime);
        }
    }

    public void Deserialize(InputMemoryStream inStream)
    {
        agvID = inStream.ReadUInt32();
        short count = inStream.ReadShort();
        nodes = new List<RouteNodeTime>();

        for (int i = 0; i < count; i++)
        {
            RouteNodeTime n = new RouteNodeTime();
            n.nodeID = inStream.ReadUInt32();
            n.arrivalTime = inStream.ReadFloat();
            n.departureTime = inStream.ReadFloat();
            nodes.Add(n);
        }
    }
}

// 2. 도착 보고 패킷 (PT_ARRIVED)
public struct ArrivedPacket
{
    public UInt32 agvID;
    public UInt32 currentNodeID;

    public void Serialize(OutputMemoryStream outStream)
    {
        outStream.WriteUInt32(agvID);
        outStream.WriteUInt32(currentNodeID);
    }

    public void Deserialize(InputMemoryStream inStream)
    {
        agvID = inStream.ReadUInt32();
        currentNodeID = inStream.ReadUInt32();
    }
}

// 3. 상태 보고 패킷 (PT_STATUS)
public struct StatusPacket
{
    public UInt32 agvID;
    public UInt32 currentLinkID;
    public float progress;
    public float x;
    public float z;
    public float heading;
    public float velocity;
    public float battery;

    public void Serialize(OutputMemoryStream outStream)
    {
        outStream.WriteUInt32(agvID);
        outStream.WriteUInt32(currentLinkID);
        outStream.WriteFloat(progress);
        outStream.WriteFloat(x);
        outStream.WriteFloat(z);
        outStream.WriteFloat(heading);
        outStream.WriteFloat(velocity);
        outStream.WriteFloat(battery);
    }

    public void Deserialize(InputMemoryStream inStream)
    {
        agvID = inStream.ReadUInt32();
        currentLinkID = inStream.ReadUInt32();
        progress = inStream.ReadFloat();
        x = inStream.ReadFloat();
        z = inStream.ReadFloat();
        heading = inStream.ReadFloat();
        velocity = inStream.ReadFloat();
        battery = inStream.ReadFloat();
    }
}