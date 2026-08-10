using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

public sealed class TCPSession : IDisposable
{
    private const int LegacyHeaderSize = sizeof(ushort);
    private const int MinimumFrameSize = LegacyHeaderSize + sizeof(byte);

    private readonly TcpClient m_TcpClient;
    private readonly NetworkStream m_Stream;
    private readonly byte[] m_SizeBuffer = new byte[LegacyHeaderSize];
    private readonly object m_SendLock = new object();
    private int m_StopRequested;
    private int m_DisconnectReported;

    public Action<InputMemoryStream> onPacketReceived;
    public Action<string> onDisconnected;

    public TCPSession(TcpClient tcpClient)
    {
        m_TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        m_Stream = tcpClient.GetStream();
    }

    private bool IsStopRequested => Volatile.Read(ref m_StopRequested) != 0;

    public void ProcessIncomingData()
    {
        string disconnectReason = null;

        try
        {
            while (!IsStopRequested)
            {
                if (!ReadExactly(m_SizeBuffer, 0, m_SizeBuffer.Length))
                {
                    disconnectReason = "Server closed the connection.";
                    break;
                }

                ushort packetSize = BitConverter.ToUInt16(m_SizeBuffer, 0);
                if (packetSize < MinimumFrameSize)
                {
                    throw new InvalidDataException($"Invalid legacy frame size: {packetSize}.");
                }

                int bodyLength = packetSize - LegacyHeaderSize;
                byte[] bodyBuffer = new byte[bodyLength];
                if (!ReadExactly(bodyBuffer, 0, bodyLength))
                {
                    disconnectReason = "Server closed the connection mid-frame.";
                    break;
                }

                onPacketReceived?.Invoke(new InputMemoryStream(bodyBuffer));
            }
        }
        catch (ObjectDisposedException) when (IsStopRequested)
        {
        }
        catch (IOException) when (IsStopRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsStopRequested)
            {
                disconnectReason = $"{exception.GetType().Name}: {exception.Message}";
            }
        }
        finally
        {
            if (!IsStopRequested && !string.IsNullOrEmpty(disconnectReason) &&
                Interlocked.Exchange(ref m_DisconnectReported, 1) == 0)
            {
                onDisconnected?.Invoke(disconnectReason);
            }
        }
    }

    public bool SendPacket(OutputMemoryStream outStream, out string error)
    {
        error = null;
        if (outStream == null)
        {
            error = "Output stream is null.";
            return false;
        }

        int packetSize = LegacyHeaderSize + outStream.GetLength();
        if (packetSize > ushort.MaxValue)
        {
            error = $"Legacy frame is too large: {packetSize} bytes.";
            return false;
        }

        OutputMemoryStream framedStream = new OutputMemoryStream();
        framedStream.WriteUInt16((ushort)packetSize);
        framedStream.Write(outStream.GetBuffer(), outStream.GetLength());
        byte[] frame = framedStream.GetBuffer();

        try
        {
            lock (m_SendLock)
            {
                if (IsStopRequested)
                {
                    error = "TCP session is stopping.";
                    return false;
                }

                m_Stream.Write(frame, 0, frame.Length);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException ||
                                           exception is SocketException ||
                                           exception is ObjectDisposedException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref m_StopRequested, 1) != 0)
        {
            return;
        }

        try
        {
            m_Stream.Close();
        }
        catch
        {
        }

        try
        {
            m_TcpClient.Close();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private bool ReadExactly(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count && !IsStopRequested)
        {
            int bytesRead = m_Stream.Read(buffer, offset + totalRead, count - totalRead);
            if (bytesRead == 0)
            {
                return false;
            }

            totalRead += bytesRead;
        }

        return totalRead == count;
    }
}
