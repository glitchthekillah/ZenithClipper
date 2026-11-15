using System.Runtime.InteropServices;
public sealed class BaselineRing
{
    readonly short[] data = new short[48000 / 4 * 2];
    int head, count;
    public void Clear(){lock(data){head=0;count=0;}}
    public void Write(byte[] bytes, int length)
    {
        var input = MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, length));
        lock (data)
            foreach (short sample in input)
            {
                if (count == data.Length) { head = (head + 1) % data.Length; count--; }
                data[(head + count) % data.Length] = sample; count++;
            }
    }
    public void Read(short[] destination)
    {
        lock (data)
        {
            int available = Math.Min(count, destination.Length);
            for (int i = 0; i < available; i++) { destination[i] = data[head]; head = (head + 1) % data.Length; }
            count -= available; Array.Clear(destination, available, destination.Length - available);
        }
    }
}

