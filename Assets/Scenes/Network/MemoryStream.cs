using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class OutputMemoryStream
{
    private byte[] m_Buffer;
    private int m_Head;

    public OutputMemoryStream(UInt32 initialCapacity = 1024)
    {
        m_Buffer = new byte[Math.Max(1, initialCapacity)];
    }

    public int GetLength()
    {
        return m_Head;
    }

    public byte[] GetBuffer()
    {
        byte[] result = new byte[m_Head];
        Buffer.BlockCopy(m_Buffer, 0, result, 0, m_Head);
        return result;
    }

    public void Write(byte[] data, int byteCount)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (byteCount < 0 || byteCount > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        EnsureCapacity(checked(m_Head + byteCount));
        Buffer.BlockCopy(data, 0, m_Buffer, m_Head, byteCount);
        m_Head += byteCount;
    }

    public void WriteByte(byte value)
    {
        Write(new[] { value }, sizeof(byte));
    }

    public void WriteInt16(short value)
    {
        Write(BitConverter.GetBytes(value), sizeof(short));
    }

    public void WriteShort(short value)
    {
        WriteInt16(value);
    }

    public void WriteUInt16(ushort value)
    {
        Write(BitConverter.GetBytes(value), sizeof(ushort));
    }

    public void WriteFloat(float value)
    {
        Write(BitConverter.GetBytes(value), sizeof(float));
    }

    public void WriteUInt32(UInt32 value)
    {
        Write(BitConverter.GetBytes(value), sizeof(UInt32));
    }

    public void WriteInt32(int value)
    {
        Write(BitConverter.GetBytes(value), sizeof(int));
    }

    public void WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > short.MaxValue)
        {
            throw new InvalidDataException("String is too large for the legacy protocol.");
        }

        WriteInt16((short)bytes.Length);
        Write(bytes, bytes.Length);
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= m_Buffer.Length)
        {
            return;
        }

        int newCapacity = Math.Max(m_Buffer.Length * 2, requiredCapacity);
        Array.Resize(ref m_Buffer, newCapacity);
    }
}

public sealed class InputMemoryStream
{
    private readonly byte[] m_Buffer;
    private int m_Head;

    public InputMemoryStream(byte[] buffer)
    {
        m_Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public int Remaining => m_Buffer.Length - m_Head;

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return m_Buffer[m_Head++];
    }

    public short ReadInt16()
    {
        EnsureAvailable(sizeof(short));
        short value = BitConverter.ToInt16(m_Buffer, m_Head);
        m_Head += sizeof(short);
        return value;
    }

    public short ReadShort()
    {
        return ReadInt16();
    }

    public UInt32 ReadUInt32()
    {
        EnsureAvailable(sizeof(UInt32));
        UInt32 value = BitConverter.ToUInt32(m_Buffer, m_Head);
        m_Head += sizeof(UInt32);
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        int value = BitConverter.ToInt32(m_Buffer, m_Head);
        m_Head += sizeof(int);
        return value;
    }

    public float ReadFloat()
    {
        EnsureAvailable(sizeof(float));
        float value = BitConverter.ToSingle(m_Buffer, m_Head);
        m_Head += sizeof(float);
        return value;
    }

    public string ReadString(int length)
    {
        EnsureAvailable(length);
        string value = Encoding.UTF8.GetString(m_Buffer, m_Head, length);
        m_Head += length;
        return value;
    }

    public Dictionary<UInt32, Node> ReadNodes()
    {
        UInt32 count = ReadUInt32();
        Dictionary<UInt32, Node> nodes = new Dictionary<UInt32, Node>();
        for (UInt32 i = 0; i < count; i++)
        {
            Node node = new Node
            {
                m_Id = ReadUInt32(),
                m_PosX = ReadFloat(),
                m_PosY = ReadFloat(),
                type = ReadByte()
            };
            nodes.Add(node.m_Id, node);
        }

        return nodes;
    }

    public List<Link> ReadLinks()
    {
        UInt32 count = ReadUInt32();
        List<Link> links = new List<Link>();
        for (UInt32 i = 0; i < count; i++)
        {
            links.Add(new Link
            {
                m_Id = ReadUInt32(),
                m_FromNodeID = ReadUInt32(),
                m_ToNodeID = ReadUInt32(),
                m_Type = ReadByte(),
                m_CX1 = ReadFloat(),
                m_CZ1 = ReadFloat(),
                m_CX2 = ReadFloat(),
                m_CZ2 = ReadFloat()
            });
        }

        return links;
    }

    private void EnsureAvailable(int byteCount)
    {
        if (byteCount < 0 || byteCount > Remaining)
        {
            throw new EndOfStreamException(
                $"Legacy packet ended early. requested={byteCount}, remaining={Remaining}.");
        }
    }
}
