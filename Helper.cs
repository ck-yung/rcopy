using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace rcopy2;

sealed class Byte2
{
    public readonly int Length = 2;
    public readonly byte[] Data = new byte[2];

    public void From(UInt16 uint16)
    {
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
    }

    public void From(int value)
    {
        if (value > UInt16.MaxValue)
        {
            throw new OverflowException($"Cannot pack int:{value} to UInt16!");
        }
        UInt16 uint16 = (UInt16)value;
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
    }

    public UInt16 Value()
    {
        UInt16 rtn = Data[1];
        rtn <<= 8;
        rtn += Data[0];
        return rtn;
    }
}

sealed class Byte8
{
    public readonly int Length = 8;
    public readonly byte[] Data = new byte[8];

    public void From(long value)
    {
        Data[0] = (byte)(value & 0xFF);
        value >>= 8;
        Data[1] = (byte)(value & 0xFF);
        value >>= 8;
        Data[2] = (byte)(value & 0xFF);
        value >>= 8;
        Data[3] = (byte)(value & 0xFF);
        value >>= 8;
        Data[4] = (byte)(value & 0xFF);
        value >>= 8;
        Data[5] = (byte)(value & 0xFF);
        value >>= 8;
        Data[6] = (byte)(value & 0xFF);
        value >>= 8;
        Data[7] = (byte)(value & 0xFF);
    }

    public long Value()
    {
        long rtn = Data[7];
        rtn <<= 8;
        rtn += Data[6];
        rtn <<= 8;
        rtn += Data[5];
        rtn <<= 8;
        rtn += Data[4];
        rtn <<= 8;
        rtn += Data[3];
        rtn <<= 8;
        rtn += Data[2];
        rtn <<= 8;
        rtn += Data[1];
        rtn <<= 8;
        rtn += Data[0];
        return rtn;
    }
}

sealed class ClientQueue
{
    Queue<Socket> Queue = new();
    object Lock = new();

    public void Add(Socket socket)
    {
        lock (Lock)
        {
            Queue.Enqueue(socket);
        }
    }

    public Socket? Get()
    {
        lock (Lock)
        {
            if (Queue.Count == 0) return null;
            return Queue.Dequeue();
        }
    }
}

static class Helper
{
    public static void FromUInt16(this byte[] bytes, UInt16 value)
    {
        bytes[0] = (byte)(value & 0xFF);
        value >>= 8;
        bytes[1] = (byte)value;
    }

    public static UInt16 ToUInt16(this byte[] bytes)
    {
        UInt16 rtn = bytes[1];
        rtn <<= 8;
        rtn += bytes[0];
        return rtn;
    }

    public static string ToHexDigit(this byte[] bytes)
    {
        var rtn = new StringBuilder();
        rtn.Append($"{bytes[0]:x2}");
        rtn.Append($".{bytes[1]:x2}");
        return rtn.ToString();
    }
}
