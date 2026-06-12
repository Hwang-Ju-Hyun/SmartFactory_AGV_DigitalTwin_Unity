using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

public class TCPSession
{
    private TcpClient m_tcpClient;
    private NetworkStream m_Stream;
    private byte[] m_SizeBuffer=new byte[2];

    public Action<InputMemoryStream> onPacketReceived;

    public TCPSession(TcpClient tcpClient)
    {
        m_tcpClient = tcpClient;
        m_Stream=tcpClient.GetStream();
    }
    public void ProcessIncomingData()
    {
        bool isConnected = m_tcpClient.Connected;
        if (isConnected)
        {
            while (true)
            {
                try
                {
                    //1. 패킷 사이즈
                    int headerReader = 0;
                    while (headerReader < 2)
                    {
                        int read = m_Stream.Read(m_SizeBuffer,headerReader,2 - headerReader);
                        headerReader+= read;
                    }
                    
                    ushort packetSize = BitConverter.ToUInt16(m_SizeBuffer, 0);

                    //2. 패킷 바디
                    byte[] bodyBuffer = new byte[packetSize - 2];
                    int totalRead = 0;
                    while (totalRead < bodyBuffer.Length)
                    {
                        int bytesRead = m_Stream.Read(bodyBuffer, totalRead, bodyBuffer.Length - totalRead);
                        totalRead += bytesRead;
                    }

                    //다모였으면
                    if (totalRead == bodyBuffer.Length)
                    {
                        InputMemoryStream inputStream = new InputMemoryStream(bodyBuffer);

                        onPacketReceived?.Invoke(inputStream);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log(e);
                    break;
                }
            }
        }
        else
        {
            Debug.Log($"<color=red> 서버 접속 실패</color>");
        }        
    }

    public void SendPacket(OutputMemoryStream _inStream)
    {
        short packet_size = (short)(sizeof(short)+_inStream.GetLength());

        OutputMemoryStream finalOutStream = new OutputMemoryStream();
        finalOutStream.WriteShort(packet_size);
        finalOutStream.Write(_inStream.GetBuffer(), _inStream.GetLength());

        m_Stream.Write(finalOutStream.GetBuffer(),0,finalOutStream.GetLength());
    }
}