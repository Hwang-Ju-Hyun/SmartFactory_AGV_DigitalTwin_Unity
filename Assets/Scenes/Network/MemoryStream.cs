using System;
using System.Collections.Generic;
using System.Text;
using Unity.AppUI.UI;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.AI;
using static UnityEditor.Experimental.GraphView.Port;

public class OutputMemoryStream
{
    private byte[] m_Buffer;
    private int m_Head;
    private UInt32 m_Capacity;
    public int GetLength() { return m_Head; }
    public byte[] GetBuffer()
    {
        byte[] actualBuffer = new byte[m_Head];
        Array.Copy(m_Buffer, 0, actualBuffer, 0, m_Head);
        return actualBuffer;
    }

    public OutputMemoryStream(UInt32 _initCapacity=1024)
    {
        m_Head = 0;
        m_Capacity = _initCapacity;
        m_Buffer= new byte[m_Capacity];
    }

    private void ReallocBuffer(UInt32 _newLength)
    {
        byte[] newBuffer=new byte[_newLength];

        Array.Copy(m_Buffer, 0, newBuffer, 0,m_Head);

        m_Buffer = newBuffer;
    }
    public void Write(byte[] _data,int _inByteCount)
    {
        int resultHead = m_Head + _inByteCount;

        if(m_Capacity < resultHead)
        {
            UInt32 newCapacity= (UInt32)Math.Max(m_Capacity * 2, resultHead);
            ReallocBuffer(newCapacity);
        }

        Array.Copy(_data,0,m_Buffer,m_Head,_inByteCount);

        m_Head = resultHead;
    }
    public void WriteByte(byte _data)
    {
        byte[] singleByte = new byte[1];
        singleByte[0] = _data;
        Write(singleByte, 1);
    }    
    public void WriteShort(short _data)
    {
        byte[] bytes = BitConverter.GetBytes(_data);
        Write(bytes, sizeof(short));
    }

    public void WriteFloat(float _data)
    {
        byte[] bytes=BitConverter.GetBytes(_data);
        Write(bytes,sizeof(float));
    }

    public void WriteUInt32(UInt32 _data)
    {
        byte[] bytes= BitConverter.GetBytes(_data);
        Write(bytes, sizeof(UInt32));
    }
    public void WriteInt(int _data)
    {
        byte[] bytes = BitConverter.GetBytes(_data);        
        Write(bytes, sizeof(int));
    }
    public void WriteString(string _data)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(_data);
        
        WriteShort((short)bytes.Length);
        Write(bytes, bytes.Length);
    }   
}
public class InputMemoryStream
{
    private byte[] m_Buffer;
    private int m_Head;
    private UInt32 m_Capacity;

    public InputMemoryStream(byte[] _buffer)
    {
        m_Buffer = _buffer;
    }    
    
    public byte ReadByte()
    {
        byte val = m_Buffer[m_Head];
        m_Head += 1;
        return val;
    }
    public uint ReadUInt32()
    {        
        UInt32 val = BitConverter.ToUInt32(m_Buffer, m_Head);     
        m_Head += 4;
        return val;
    }
    
    public int ReadInt32()
    {
        int val = BitConverter.ToInt32(m_Buffer, m_Head);
        m_Head += 4;
        return val;
    }

    public float ReadFloat()
    {
        float val =BitConverter.ToSingle(m_Buffer, m_Head);
        m_Head += 4;
        return val;
    }

    public string ReadString(int _length)
    {
        string val = Encoding.UTF8.GetString(m_Buffer, m_Head, _length);
        m_Head += _length;
        return val;
    }

    public List<Node> ReadNodes()
    {
        List<Node> nodes=new List<Node>();
        UInt32 length = ReadUInt32();        
        for(int i=0;i<length;i++)
        {
            UInt32 id = ReadUInt32();
            float PosX = ReadFloat();
            float PosY = ReadFloat();
            byte type = ReadByte();
            
            Node node = new Node();
            node.m_Id = id;
            node.m_PosX = PosX;
            node.m_PosY = PosY;
            node.type = type;

            nodes.Add(node);
        }
        
        return nodes;
    }

    public List<Link> ReadLinks()
    {        
        List<Link> links = new List<Link>();
        UInt32 length = ReadUInt32();
        for (int i = 0; i < length; i++)
        {
            UInt32 id = ReadUInt32();
            UInt32 FromNodeID = ReadUInt32();
            UInt32 ToNodeID = ReadUInt32();            

            Link link = new Link();
            link.m_Id = id;
            link.m_FromNodeID  = FromNodeID;
            link.m_ToNodeID= ToNodeID;

            links.Add(link);
        }

        return links;
    }
}
