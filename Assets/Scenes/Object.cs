using System;

// Network state is plain C#. Unity views are created separately on the main thread.
public abstract class NetworkObjectState
{
    protected NetworkObjectState(UInt32 classID)
    {
        ClassID = classID;
    }

    public UInt32 ClassID { get; }
    public float PosX { get; protected set; }
    public float PosZ { get; protected set; }
    public float HeadingRadians { get; protected set; }

    public abstract void Read(InputMemoryStream inStream);
}
