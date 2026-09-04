using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public enum PACKET_TYPE : byte
{
    PT_REPLICATION = 0,
    PT_MAZE_DATA = 1,
    PT_HELLO = 2,
    PT_READY_MAP = 4,
    PT_READY_OBJECT = 5,
    PT_VISION_OBSERVATION = 6,
    PT_CARGO_STATE = 7,

    // Deprecated legacy robot IDs. The Unity viewer never uses RobotProtocol.
    PT_ROUTE = 10,
    PT_CANCEL_ROUTE = 11,
    PT_ARRIVED = 12,
    PT_STATUS = 13,
    PT_ERROR = 14,
    PT_HEARTBEAT = 15
}

public enum REPLICATION_ACTION : byte
{
    RT_CREATE = 0,
    RT_UPDATE = 1,
    RT_DESTORY = 2,
    MAX
}

public enum CLASS_ID : UInt32
{
    OBJ_DEFAULT = 1000,
    OBJ_AGV = 1001
}

public class NetworkManagerClient : MonoBehaviour
{
    [Header("Legacy Viewer Server")]
    [SerializeField] private string m_ServerAddress = "127.0.0.1";
    [SerializeField, Range(1, 65535)] private int m_ServerPort = 6666;

    private readonly Queue<INetworkEvent> m_NetworkEventQueue = new Queue<INetworkEvent>();
    private readonly Dictionary<UInt32, NetworkUpdateEvent> m_LatestUpdateByNetworkID =
        new Dictionary<UInt32, NetworkUpdateEvent>();
    private readonly Dictionary<UInt32, VisionObservationEvent> m_LatestVisionByAgvID =
        new Dictionary<UInt32, VisionObservationEvent>();
    private readonly object m_EventQueueLock = new object();

    private TCPSession m_Session;
    private Thread m_ReceiveThread;
    private TcpClient m_Client;
    private volatile bool m_ShuttingDown;
    private int m_ShutdownStarted;
    private bool m_ReadyMapSent;
    private bool m_ReadyObjectSent;

    public static NetworkManagerClient Instance { get; private set; }
    public LinkingContext LinkingContext { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnValidate()
    {
        m_ServerPort = Mathf.Clamp(m_ServerPort, 1, 65535);
        if (string.IsNullOrWhiteSpace(m_ServerAddress))
        {
            m_ServerAddress = "127.0.0.1";
        }
    }

    private void Start()
    {
        try
        {
            m_Session = ConnectToServer();
            m_ReceiveThread = new Thread(m_Session.ProcessIncomingData)
            {
                IsBackground = true,
                Name = "Unity Legacy Viewer Receive"
            };
            m_ReceiveThread.Start();

            if (TrySendControlPacket(PACKET_TYPE.PT_HELLO, "HELLO"))
            {
                Debug.Log("[Viewer] HELLO sent using the legacy viewer protocol.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Viewer] Connection failed: {exception.Message}");
            ShutdownConnection(false);
        }
    }

    private void Update()
    {
        TakeNetworkEvents(
            out INetworkEvent[] orderedEvents,
            out NetworkUpdateEvent[] latestUpdateEvents,
            out VisionObservationEvent[] latestVisionEvents);

        foreach (INetworkEvent networkEvent in orderedEvents)
        {
            try
            {
                networkEvent.Execute();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Viewer] Main-thread network event failed: {exception.Message}");
                ShutdownConnection(false);
                break;
            }
        }

        if (m_ShuttingDown)
        {
            return;
        }

        foreach (NetworkUpdateEvent updateEvent in latestUpdateEvents)
        {
            try
            {
                updateEvent.Execute();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Viewer] AGV position update failed: {exception.Message}");
                ShutdownConnection(false);
                break;
            }
        }

        if (m_ShuttingDown)
        {
            return;
        }

        foreach (VisionObservationEvent visionEvent in latestVisionEvents)
        {
            try
            {
                visionEvent.Execute();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Viewer] Vision position update failed: {exception.Message}");
                ShutdownConnection(false);
                break;
            }
        }
    }

    public TCPSession ConnectToServer()
    {
        RenderManager.Instance?.BeginCargoSession();

        string serverAddress = m_ServerAddress.Trim();
        m_Client = new TcpClient
        {
            NoDelay = true
        };
        m_Client.Connect(serverAddress, m_ServerPort);

        TCPSession session = new TCPSession(m_Client);
        session.onPacketReceived = ProcessPacket;
        session.onDisconnected = reason => EnqueueNetworkEvent(
            new NetworkActionEvent(() => HandleSessionDisconnected(reason)));

        LinkingContext = new LinkingContext();
        ObjectRegistry.StaticInit();
        ObjectRegistry.Instance.RegisterCreateFunction((UInt32)CLASS_ID.OBJ_AGV, AGVState.Create);

        Debug.Log($"[Viewer] Connected to {serverAddress}:{m_ServerPort}.");
        return session;
    }

    public void ProcessPacket(InputMemoryStream inStream)
    {
        if (m_ShuttingDown)
        {
            return;
        }

        PACKET_TYPE packetType = (PACKET_TYPE)inStream.ReadByte();
        switch (packetType)
        {
            case PACKET_TYPE.PT_HELLO:
                HandleHelloPacket(inStream);
                break;
            case PACKET_TYPE.PT_MAZE_DATA:
                HandleMapDataPacket(inStream);
                break;
            case PACKET_TYPE.PT_REPLICATION:
                HandleReplicationPacket(inStream);
                break;
            case PACKET_TYPE.PT_VISION_OBSERVATION:
                HandleVisionObservationPacket(inStream);
                break;
            case PACKET_TYPE.PT_CARGO_STATE:
                HandleCargoStatePacket(inStream);
                break;
            default:
                EnqueueNetworkEvent(new NetworkActionEvent(
                    () => Debug.LogWarning($"[Viewer] Ignored legacy packet type {(byte)packetType}.")));
                break;
        }
    }

    private void HandleHelloPacket(InputMemoryStream inStream)
    {
        UInt32 sessionID = inStream.ReadUInt32();
        EnqueueNetworkEvent(new NetworkActionEvent(
            () =>
            {
                RenderManager.Instance?.BeginCargoSession();
                Debug.Log($"[Viewer] Legacy session accepted. sessionID={sessionID}.");
            }));
    }

    private void HandleMapDataPacket(InputMemoryStream inStream)
    {
        Dictionary<UInt32, Node> nodes = inStream.ReadNodes();
        List<Link> links = inStream.ReadLinks();
        EnqueueNetworkEvent(new NetworkMapBuildEvent(nodes, links, HandleMapRendered));
    }

    private void HandleReplicationPacket(InputMemoryStream inStream)
    {
        UInt32 commandCount = inStream.ReadUInt32();
        bool createdAnyObject = false;

        for (UInt32 i = 0; i < commandCount; i++)
        {
            UInt32 networkID = inStream.ReadUInt32();
            REPLICATION_ACTION action = (REPLICATION_ACTION)inStream.ReadByte();
            switch (action)
            {
                case REPLICATION_ACTION.RT_CREATE:
                    HandleCreateReplication(inStream, networkID);
                    createdAnyObject = true;
                    break;
                case REPLICATION_ACTION.RT_UPDATE:
                    HandleUpdateReplication(inStream, networkID);
                    break;
                case REPLICATION_ACTION.RT_DESTORY:
                    if (LinkingContext.RemoveObject(networkID))
                    {
                        RemoveLatestUpdate(networkID);
                        EnqueueNetworkEvent(new NetworkDestroyEvent(networkID));
                    }
                    break;
                default:
                    throw new InvalidDataException($"Unknown replication action {(byte)action}.");
            }
        }

        if (createdAnyObject)
        {
            EnqueueNetworkEvent(new NetworkActionEvent(SendReadyObjectOnce));
        }
    }

    private void HandleCreateReplication(InputMemoryStream inStream, UInt32 networkID)
    {
        UInt32 classID = inStream.ReadUInt32();
        NetworkObjectState state = ObjectRegistry.Instance.CreateObject(classID);
        if (state == null)
        {
            throw new InvalidDataException($"Unknown replicated ClassID {classID}.");
        }

        state.Read(inStream);
        if (!LinkingContext.AddObject(networkID, state))
        {
            throw new InvalidDataException($"Duplicate replicated NetworkID {networkID}.");
        }

        EnqueueNetworkEvent(new NetworkSpawnEvent(
            networkID,
            classID,
            new Vector2(state.PosX, state.PosZ),
            state.HeadingRadians,
            HandleObjectRendered));
    }

    private void HandleUpdateReplication(InputMemoryStream inStream, UInt32 networkID)
    {
        NetworkObjectState state = LinkingContext.GetObject(networkID);
        if (state == null)
        {
            throw new InvalidDataException($"RT_UPDATE arrived before RT_CREATE for {networkID}.");
        }

        state.Read(inStream);
        StoreLatestUpdate(new NetworkUpdateEvent(
            networkID,
            new Vector2(state.PosX, state.PosZ),
            state.HeadingRadians));
    }

    private void HandleVisionObservationPacket(InputMemoryStream inStream)
    {
        VisionObservationPacket packet = VisionObservationPacket.Deserialize(inStream);
        StoreLatestVisionObservation(new VisionObservationEvent(packet));
    }

    private void HandleCargoStatePacket(InputMemoryStream inStream)
    {
        try
        {
            CargoStatePacket packet = CargoStatePacket.Deserialize(inStream);
            EnqueueNetworkEvent(new CargoStateEvent(packet));
        }
        catch (Exception exception) when (
            exception is InvalidDataException || exception is EndOfStreamException)
        {
            string message = exception.Message;
            EnqueueNetworkEvent(new NetworkActionEvent(
                () => Debug.LogError($"[Cargo] Packet rejected: {message}")));
        }
    }

    private void HandleMapRendered(int nodeCount, int linkCount)
    {
        Debug.Log($"[Viewer] Map rendered. nodes={nodeCount}, links={linkCount}.");
        if (!m_ReadyMapSent && TrySendControlPacket(PACKET_TYPE.PT_READY_MAP, "READY_MAP"))
        {
            m_ReadyMapSent = true;
        }
    }

    private void HandleObjectRendered(UInt32 networkID)
    {
        if (networkID == 1)
        {
            Debug.Log("[Viewer] Physical-demo AGV 1 is visible.");
        }
    }

    private void SendReadyObjectOnce()
    {
        if (!m_ReadyObjectSent && TrySendControlPacket(PACKET_TYPE.PT_READY_OBJECT, "READY_OBJECT"))
        {
            m_ReadyObjectSent = true;
            Debug.Log("[Viewer] Initial replicated objects are ready.");
        }
    }

    private bool TrySendControlPacket(PACKET_TYPE packetType, string label)
    {
        if (m_ShuttingDown || m_Session == null)
        {
            return false;
        }

        OutputMemoryStream outStream = new OutputMemoryStream();
        outStream.WriteByte((byte)packetType);
        if (m_Session.SendPacket(outStream, out string error))
        {
            return true;
        }

        Debug.LogError($"[Viewer] {label} send failed: {error}");
        ShutdownConnection(false);
        return false;
    }

    private void EnqueueNetworkEvent(INetworkEvent networkEvent)
    {
        if (networkEvent == null || m_ShuttingDown)
        {
            return;
        }

        lock (m_EventQueueLock)
        {
            if (!m_ShuttingDown)
            {
                m_NetworkEventQueue.Enqueue(networkEvent);
            }
        }
    }

    private void TakeNetworkEvents(
        out INetworkEvent[] orderedEvents,
        out NetworkUpdateEvent[] latestUpdateEvents,
        out VisionObservationEvent[] latestVisionEvents)
    {
        lock (m_EventQueueLock)
        {
            orderedEvents = m_NetworkEventQueue.Count == 0
                ? Array.Empty<INetworkEvent>()
                : m_NetworkEventQueue.ToArray();
            m_NetworkEventQueue.Clear();

            if (m_LatestUpdateByNetworkID.Count == 0)
            {
                latestUpdateEvents = Array.Empty<NetworkUpdateEvent>();
            }
            else
            {
                latestUpdateEvents =
                    new NetworkUpdateEvent[m_LatestUpdateByNetworkID.Count];
                m_LatestUpdateByNetworkID.Values.CopyTo(latestUpdateEvents, 0);
                m_LatestUpdateByNetworkID.Clear();
            }

            if (m_LatestVisionByAgvID.Count == 0)
            {
                latestVisionEvents = Array.Empty<VisionObservationEvent>();
            }
            else
            {
                latestVisionEvents =
                    new VisionObservationEvent[m_LatestVisionByAgvID.Count];
                m_LatestVisionByAgvID.Values.CopyTo(latestVisionEvents, 0);
                m_LatestVisionByAgvID.Clear();
            }
        }
    }

    private void StoreLatestUpdate(NetworkUpdateEvent updateEvent)
    {
        if (updateEvent == null || m_ShuttingDown)
        {
            return;
        }

        lock (m_EventQueueLock)
        {
            if (!m_ShuttingDown)
            {
                m_LatestUpdateByNetworkID[updateEvent.NetworkID] = updateEvent;
            }
        }
    }

    private void StoreLatestVisionObservation(VisionObservationEvent visionEvent)
    {
        if (visionEvent == null || m_ShuttingDown)
        {
            return;
        }

        lock (m_EventQueueLock)
        {
            if (!m_ShuttingDown)
            {
                // Server TCP is ordered. Keep the last arrived state even when
                // its diagnostic sequence is equal (timeout -> LOST) or lower
                // (Vision process started a new transport session).
                m_LatestVisionByAgvID[visionEvent.AgvID] = visionEvent;
            }
        }
    }

    private void RemoveLatestUpdate(UInt32 networkID)
    {
        lock (m_EventQueueLock)
        {
            m_LatestUpdateByNetworkID.Remove(networkID);
        }
    }

    private void HandleSessionDisconnected(string reason)
    {
        if (m_ShuttingDown)
        {
            return;
        }

        Debug.LogWarning($"[Viewer] Disconnected: {reason}");
        RenderManager.Instance?.EndCargoSession();
        ShutdownConnection(false);
    }

    private void OnApplicationQuit()
    {
        ShutdownConnection(true);
    }

    private void OnDestroy()
    {
        ShutdownConnection(true);
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void ShutdownConnection(bool logShutdown)
    {
        if (Interlocked.Exchange(ref m_ShutdownStarted, 1) != 0)
        {
            return;
        }

        RenderManager.Instance?.EndCargoSession();
        m_ShuttingDown = true;
        m_Session?.Stop();
        m_Client?.Close();

        Thread receiveThread = m_ReceiveThread;
        if (receiveThread != null && receiveThread.IsAlive && receiveThread != Thread.CurrentThread)
        {
            if (!receiveThread.Join(1500))
            {
                Debug.LogWarning("[Viewer] Receive thread did not stop within 1500 ms.");
            }
        }

        m_ReceiveThread = null;
        m_Session = null;
        m_Client = null;
        lock (m_EventQueueLock)
        {
            m_NetworkEventQueue.Clear();
            m_LatestUpdateByNetworkID.Clear();
            m_LatestVisionByAgvID.Clear();
        }

        if (logShutdown)
        {
            Debug.Log("[Viewer] Network session stopped cleanly.");
        }
    }
}
