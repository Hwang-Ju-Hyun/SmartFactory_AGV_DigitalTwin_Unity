using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
public enum PACKET_TYPE : byte
{    
    PT_REPLICATION = 0,
    PT_TEST=1,    
    PT_HELLO = 2,
}

public enum REPLICATION_ACTION:byte
{
    RT_CREATE=0,
    RT_UPDATE=1,
    RT_DESTORY=2,
    MAX
}

public enum CLASS_ID:UInt32
{
    OBJ_DEFAULT = 1000,
    OBJ_AGV = 1001
}

public class NetworkManagerClient : MonoBehaviour
{
    private TCPSession m_Session;
    private Thread m_ReceiveThread = null;


    private TcpClient m_Client;
    public LinkingContext m_LinkingContext { get; set; }
    private Queue<INetworkEvent> m_NetworkEventQueue = new Queue<INetworkEvent>();
    private readonly object m_Lock = new object();

    public static NetworkManagerClient Instance { get; private set; }    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        m_Session= ConnectTOServer();
        OutputMemoryStream outStream = new OutputMemoryStream();
        
        WriteHelloPacket(outStream);

        m_Session.SendPacket(outStream);

        m_ReceiveThread = new Thread(m_Session.ProcessIncomingData);
        m_ReceiveThread.Start();
    }


    private void Update()
    {        
        lock(m_Lock)
        {
            while (m_NetworkEventQueue.Count > 0)
            {
                INetworkEvent eve = m_NetworkEventQueue.Dequeue();
                eve.Excute();
            }
        }        
    }
    public TCPSession ConnectTOServer()
    {
        m_Client = new TcpClient();
        m_Client.Connect("127.0.0.1", 9999);

        TCPSession serverSession = new TCPSession(m_Client);
        serverSession.onPacketReceived = (inStream) => { this.ProcessPacket(inStream); };

        m_LinkingContext = new LinkingContext();

        ObjectRegistry.StaticInit();
        ObjectRegistry.Instance.RegistCreateFunction((UInt32)CLASS_ID.OBJ_AGV, AGV.Create);

        return serverSession;
    }
    public void ProcessPacket(InputMemoryStream _inStream)
    {
        PACKET_TYPE packet_type = (PACKET_TYPE)_inStream.ReadByte();
        switch (packet_type)
        {
            case PACKET_TYPE.PT_HELLO:
                {
                    HandleHelloPacket_Recv(_inStream);
                    break;
                }
            case PACKET_TYPE.PT_REPLICATION:
                {
                    HandleReplicatePacket_Recv(_inStream);
                    break;
                }
        }
    }

    public void HandleHelloPacket_Recv(InputMemoryStream _inStream)
    {
        Debug.Log($"<color=cyan> Hello packet</color>을 서버에게서 받았습니다!");
        UInt32 networkID = _inStream.ReadUInt32();
        UInt32 sessionID = _inStream.ReadUInt32();
        Debug.Log($"<color=blue>Nework ID :</color>" + networkID);
        Debug.Log($"<color=green>Session ID :</color>" + sessionID);
        
    }

    public void HandleReplicatePacket_Recv(InputMemoryStream _inStream)
    {
        UInt32 commandCount = _inStream.ReadUInt32();
        for(int i=0;i<commandCount;i++)
        {
            UInt32 networkID = _inStream.ReadUInt32();
            REPLICATION_ACTION action = (REPLICATION_ACTION)_inStream.ReadByte();
            switch(action)
            {
                case REPLICATION_ACTION.RT_CREATE:
                    {
                        UInt32 classID = _inStream.ReadUInt32();

                        Object obj = ObjectRegistry.Instance.CreateObject(classID);                        
                        m_LinkingContext.AddObject(networkID, obj);
                        
                        lock(m_Lock)
                        {                            
                            NetworkSpawnEvent nse = new NetworkSpawnEvent(networkID, classID);
                            m_NetworkEventQueue.Enqueue(nse);
                        }                                                
                        break;
                    }
                case REPLICATION_ACTION.RT_UPDATE:
                    {                        
                        Object obj = m_LinkingContext.GetObject(networkID);
                        obj.Read(_inStream);
                        Vector2 pos = new Vector2(obj.m_PosX, obj.m_PosY);
                        NetworkUpdateEvent nue = new NetworkUpdateEvent(networkID, pos, Quaternion.identity);
                        m_NetworkEventQueue.Enqueue(nue);

                        break;
                    }
            }
        }
    }
    public void WriteHelloPacket(OutputMemoryStream _inStream)
    {
        short packet_type = (short)PACKET_TYPE.PT_HELLO;
        _inStream.WriteShort(packet_type);        
    }

}
