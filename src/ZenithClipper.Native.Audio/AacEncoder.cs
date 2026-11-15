using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace ZenithClipper.Native.Audio;

public delegate void AacPacketHandler(ReadOnlySpan<byte> packet, long sampleIndex, int durationSamples);

/// <summary>Synchronous, thread-affine AAC-LC encoder. Create, Encode, Complete and Dispose on one OS thread.
/// Packet spans are borrowed until callback return. Calls from the callback are rejected.</summary>
public sealed unsafe class AacEncoder : IDisposable
{
    public const int SampleRate = 48000, Channels = 2, Bitrate = 192000;
    public static ReadOnlySpan<byte> AudioSpecificConfig => [0x11, 0x90];
    private sealed class State(AacPacketHandler callback)
    { internal readonly AacPacketHandler Callback = callback; internal ExceptionDispatchInfo? Failure; }
    private readonly State state;
    private GCHandle context;
    private nint handle;
    private readonly uint owner = GetCurrentThreadId();
    private bool busy, completed;
    public AacEncoder(AacPacketHandler callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        state = new(callback); context = GCHandle.Alloc(state);
        try { Check(zc_aac_create(&Emit, GCHandle.ToIntPtr(context), out handle)); }
        catch { context.Free(); throw; }
    }
    public void Encode(ReadOnlySpan<byte> pcm16Stereo, long sampleIndex)
    {
        CheckAccess();
        if (completed) throw new InvalidOperationException("Encoder already drained.");
        if ((pcm16Stereo.Length & 3) != 0 || pcm16Stereo.Length > SampleRate * 4)
            throw new ArgumentException("Require complete PCM16 stereo frames, at most one second per call.", nameof(pcm16Stereo));
        if (sampleIndex < 0 || sampleIndex > long.MaxValue / 10_000_000 - SampleRate)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex));
        busy = true;
        try { fixed(byte* pcm = pcm16Stereo) Check(zc_aac_encode(handle, pcm, (uint)(pcm16Stereo.Length / 4), sampleIndex)); }
        finally { busy = false; }
    }
    public void Complete()
    {
        CheckAccess(); if(completed)return;
        busy=true;
        try { Check(zc_aac_drain(handle)); completed=true; }
        finally { busy=false; }
    }
    public void Dispose()
    {
        if(handle==0)return;
        CheckAccess();
        Marshal.ThrowExceptionForHR(zc_aac_destroy(handle));handle=0;context.Free();
    }
    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(handle==0,this);
        if(GetCurrentThreadId()!=owner)throw new InvalidOperationException("AAC encoder is bound to its creating OS thread.");
        if(busy)throw new InvalidOperationException("AAC callback cannot reenter encoder.");
    }
    private void Check(int hr) { state.Failure?.Throw(); Marshal.ThrowExceptionForHR(hr); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Emit(nint user, byte* data, uint length, long index, uint duration)
    {
        State? state=null;
        try
        {
            state=(State)GCHandle.FromIntPtr(user).Target!;
            state.Callback(new ReadOnlySpan<byte>(data,checked((int)length)),index,checked((int)duration));return 0;
        }
        catch(Exception ex) { if(state!=null)state.Failure=ExceptionDispatchInfo.Capture(ex);return unchecked((int)0x80004005); }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("zenith_aac.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int zc_aac_create(delegate* unmanaged[Cdecl]<nint,byte*,uint,long,uint,int> emit,nint context,out nint handle);
    [DllImport("zenith_aac.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int zc_aac_encode(nint handle,byte* pcm,uint frames,long sampleIndex);
    [DllImport("zenith_aac.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int zc_aac_drain(nint handle);
    [DllImport("zenith_aac.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int zc_aac_destroy(nint handle);
}

