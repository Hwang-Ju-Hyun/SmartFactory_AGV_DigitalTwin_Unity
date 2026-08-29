using System;
using System.IO;

public enum VisionTrackingState : byte
{
    Measured = 1,
    Held = 2,
    Lost = 3
}

public readonly struct VisionObservationPacket
{
    // Payload bytes after the one-byte legacy viewer packet type.
    public const int WirePayloadSize =
        sizeof(UInt32) + // AGV ID
        sizeof(UInt32) + // Vision transport sequence
        sizeof(byte) +   // tracking state
        sizeof(byte) +   // pose valid
        (3 * sizeof(float)) +
        sizeof(UInt32);  // Server receive age

    public VisionObservationPacket(
        UInt32 agvID,
        UInt32 transportSequence,
        VisionTrackingState trackingState,
        bool poseValid,
        float serverX,
        float serverZ,
        float headingRadians,
        UInt32 serverReceiveAgeMs)
    {
        AgvID = agvID;
        TransportSequence = transportSequence;
        TrackingState = trackingState;
        PoseValid = poseValid;
        ServerX = serverX;
        ServerZ = serverZ;
        HeadingRadians = headingRadians;
        ServerReceiveAgeMs = serverReceiveAgeMs;
    }

    public UInt32 AgvID { get; }
    public UInt32 TransportSequence { get; }
    public VisionTrackingState TrackingState { get; }
    public bool PoseValid { get; }
    public float ServerX { get; }
    public float ServerZ { get; }
    public float HeadingRadians { get; }
    public UInt32 ServerReceiveAgeMs { get; }

    public static VisionObservationPacket Deserialize(InputMemoryStream inStream)
    {
        if (inStream == null)
        {
            throw new ArgumentNullException(nameof(inStream));
        }

        if (inStream.Remaining != WirePayloadSize)
        {
            throw new InvalidDataException(
                $"Invalid Vision viewer payload size: {inStream.Remaining}; expected {WirePayloadSize}.");
        }

        UInt32 agvID = inStream.ReadUInt32();
        UInt32 transportSequence = inStream.ReadUInt32();
        byte trackingStateValue = inStream.ReadByte();
        byte poseValidValue = inStream.ReadByte();
        float serverX = inStream.ReadFloat();
        float serverZ = inStream.ReadFloat();
        float headingRadians = inStream.ReadFloat();
        UInt32 serverReceiveAgeMs = inStream.ReadUInt32();

        if (agvID == 0)
        {
            throw new InvalidDataException("Vision viewer payload has AGV ID 0.");
        }

        if (transportSequence == 0)
        {
            throw new InvalidDataException("Vision viewer payload has transport sequence 0.");
        }

        if (trackingStateValue < (byte)VisionTrackingState.Measured ||
            trackingStateValue > (byte)VisionTrackingState.Lost)
        {
            throw new InvalidDataException(
                $"Invalid Vision tracking state: {trackingStateValue}.");
        }

        if (poseValidValue > 1)
        {
            throw new InvalidDataException(
                $"Invalid Vision pose-valid value: {poseValidValue}.");
        }

        bool isLost = trackingStateValue == (byte)VisionTrackingState.Lost;
        if ((isLost && poseValidValue != 0) || (!isLost && poseValidValue != 1))
        {
            throw new InvalidDataException(
                $"Vision state {trackingStateValue} and pose-valid {poseValidValue} are inconsistent.");
        }

        if (!IsFinite(serverX) || !IsFinite(serverZ) || !IsFinite(headingRadians))
        {
            throw new InvalidDataException("Vision viewer payload contains a non-finite pose.");
        }

        if (isLost && (serverX != 0.0f || serverZ != 0.0f || headingRadians != 0.0f))
        {
            throw new InvalidDataException("LOST Vision payload must contain a canonical zero pose.");
        }

        if (inStream.Remaining != 0)
        {
            throw new InvalidDataException(
                $"Vision viewer payload has {inStream.Remaining} trailing bytes.");
        }

        return new VisionObservationPacket(
            agvID,
            transportSequence,
            (VisionTrackingState)trackingStateValue,
            poseValidValue != 0,
            serverX,
            serverZ,
            headingRadians,
            serverReceiveAgeMs);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
