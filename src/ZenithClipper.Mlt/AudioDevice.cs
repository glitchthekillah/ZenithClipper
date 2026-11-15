using System.Runtime.InteropServices;
namespace ZenithClipper.Mlt;
internal sealed class AudioDevice : IDisposable
{
    const string Lib="zenith_sdl";
    uint device;
    internal AudioDevice()
    {
        if(Init(0x10)!=0)throw new IOException(Error());
        var requested=new Spec{Frequency=48000,Format=0x8010,Channels=2,Samples=1024};
        device=Open(null,0,ref requested,out _,0);
        if(device==0){Quit(0x10);throw new IOException(Error());}
        Pause(device,0);
    }
    internal void Queue(byte[] pcm){if(pcm.Length>0 && QueueAudio(device,pcm,(uint)pcm.Length)!=0)throw new IOException(Error());}
    internal uint QueuedBytes=>Queued(device);
    public void Dispose(){if(device!=0){Clear(device);Close(device);device=0;Quit(0x10);}}
    static string Error()=>Marshal.PtrToStringUTF8(GetError())??"Audio output unavailable.";
    [StructLayout(LayoutKind.Sequential)]struct Spec{public int Frequency;public ushort Format;public byte Channels,Silence;public ushort Samples,Padding;public uint Size;public nint Callback,Userdata;}
    [DllImport(Lib,EntryPoint="SDL_InitSubSystem",CallingConvention=CallingConvention.Cdecl)]static extern int Init(uint flags);
    [DllImport(Lib,EntryPoint="SDL_QuitSubSystem",CallingConvention=CallingConvention.Cdecl)]static extern void Quit(uint flags);
    [DllImport(Lib,EntryPoint="SDL_OpenAudioDevice",CallingConvention=CallingConvention.Cdecl)]static extern uint Open([MarshalAs(UnmanagedType.LPUTF8Str)]string? name,int capture,ref Spec desired,out Spec obtained,int allowed);
    [DllImport(Lib,EntryPoint="SDL_CloseAudioDevice",CallingConvention=CallingConvention.Cdecl)]static extern void Close(uint device);
    [DllImport(Lib,EntryPoint="SDL_PauseAudioDevice",CallingConvention=CallingConvention.Cdecl)]static extern void Pause(uint device,int pause);
    [DllImport(Lib,EntryPoint="SDL_QueueAudio",CallingConvention=CallingConvention.Cdecl)]static extern int QueueAudio(uint device,byte[] bytes,uint length);
    [DllImport(Lib,EntryPoint="SDL_GetQueuedAudioSize",CallingConvention=CallingConvention.Cdecl)]static extern uint Queued(uint device);
    [DllImport(Lib,EntryPoint="SDL_ClearQueuedAudio",CallingConvention=CallingConvention.Cdecl)]static extern void Clear(uint device);
    [DllImport(Lib,EntryPoint="SDL_GetError",CallingConvention=CallingConvention.Cdecl)]static extern nint GetError();
}
