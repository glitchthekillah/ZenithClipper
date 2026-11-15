using System.Numerics;

namespace ZenithClipper;

/// <summary>Reuse normal event-sized PCM storage, growing only when a delayed native packet needs it.</summary>
public sealed class AudioPacketBuffer
{
    public const int MaximumBytes=48000*2*2;
    byte[] buffer=new byte[4096];

    public byte[] ForPacket(int bytes)
    {
        if(bytes<0||bytes>MaximumBytes)throw new IOException("Audio packet exceeds bounded buffer.");
        if(bytes>buffer.Length)
            buffer=new byte[Math.Min(MaximumBytes,(int)BitOperations.RoundUpToPowerOf2((uint)bytes))];
        return buffer;
    }
}
