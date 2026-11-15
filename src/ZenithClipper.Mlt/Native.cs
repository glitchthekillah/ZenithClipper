using System.Reflection;
using System.Runtime.InteropServices;
namespace ZenithClipper.Mlt;

internal static class Native
{
    const string Lib="zenith_mlt";
    internal static string? Root;
    static nint library;
    static readonly Lazy<bool> ready=new(InitializeCore);
    internal static void Initialize()=>_=ready.Value;
    static bool InitializeCore()
    {
        if(library!=0)return true;
        Root=FindRoot();
        if(OperatingSystem.IsWindows())
        {
            if(Root==null)throw new FileNotFoundException("MLT runtime missing. Set ZENITHCLIPPER_MLT_ROOT to the portable runtime folder.");
            SetEnvironment("PATH",Root+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"));
            SetEnvironment("MLT_DATA",Path.Combine(Root,"share","mlt"));
            SetEnvironment("MLT_PROFILES_PATH",Path.Combine(Root,"share","mlt","profiles"));
            // frei0r (blend modes, keyframed motion) and LADSPA plugins live under lib\ in the portable runtime;
            // without these MLT looks next to the editor exe and those services silently fail to load.
            SetEnvironment("FREI0R_PATH",Path.Combine(Root,"lib","frei0r-1"));
            SetEnvironment("LADSPA_PATH",Path.Combine(Root,"lib","ladspa"));
            if(!SetDllDirectoryW(Root))throw new System.ComponentModel.Win32Exception();
            library=NativeLibrary.Load(Path.Combine(Root,"libmlt-7.dll"),typeof(Native).Assembly,DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.SafeDirectories);
        }
        else library=NativeLibrary.Load("libmlt-7.so.7");
        // These modules discover external devices/plugins or require a Qt application.
        // They will be hosted explicitly when their workflows are implemented, rather than scanned on every launch.
        // OpenCV (motion tracking) loads only in the short-lived tracking helper process, never in the editor itself.
        SetEnvironment("MLT_REPOSITORY_DENY",Environment.GetEnvironmentVariable("ZENITHCLIPPER_MLT_OPENCV")=="1"?"libmltdecklink:libmltglaxnimate-qt6:libmltmovit:libmltopenfx:libmltqt6":"libmltdecklink:libmltglaxnimate-qt6:libmltmovit:libmltopencv:libmltopenfx:libmltqt6");
        SetEnvironment("MLT_SLICES","2");
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,Resolve);
        if(FactoryInit(OperatingSystem.IsWindows()?Path.Combine(Root!,"lib","mlt"):null)==0)throw new InvalidOperationException("MLT plugin factory failed to initialize.");
        LogLevel(16);
        return true;
    }
    static void SetEnvironment(string name,string? value)
    {
        Environment.SetEnvironmentVariable(name,value);
        if(OperatingSystem.IsWindows()){PutEnvUcrt(name,value??"");PutEnvMsvcrt(name,value??"");}
    }
    [DllImport("ucrtbase.dll",EntryPoint="_putenv_s",CallingConvention=CallingConvention.Cdecl)]static extern int PutEnvUcrt([MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("msvcrt.dll",EntryPoint="_putenv_s",CallingConvention=CallingConvention.Cdecl)]static extern int PutEnvMsvcrt([MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetDllDirectoryW(string directory);
    static nint Resolve(string name,Assembly assembly,DllImportSearchPath? search)=>name==Lib?library:name=="zenith_sdl"?NativeLibrary.Load(OperatingSystem.IsWindows()?Path.Combine(Root!,"SDL2.dll"):"libSDL2-2.0.so.0"):0;
    static string? FindRoot()
    {
        string? explicitRoot=Environment.GetEnvironmentVariable("ZENITHCLIPPER_MLT_ROOT");
        if(!string.IsNullOrWhiteSpace(explicitRoot))return Path.GetFullPath(explicitRoot);
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            foreach(string candidate in new[]{Path.Combine(d.FullName,"mlt"),Path.Combine(d.FullName,"tools","mlt-runtime","package","Shotcut")})
                if(File.Exists(Path.Combine(candidate,"libmlt-7.dll")))return candidate;
        return null;
    }
    [DllImport(Lib,EntryPoint="mlt_factory_init",CallingConvention=CallingConvention.Cdecl)] internal static extern nint FactoryInit([MarshalAs(UnmanagedType.LPUTF8Str)]string? directory);
    [DllImport(Lib,EntryPoint="mlt_log_set_level",CallingConvention=CallingConvention.Cdecl)] internal static extern void LogLevel(int level);
    [DllImport(Lib,EntryPoint="mlt_version_get_string",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Version();
    [DllImport(Lib,EntryPoint="mlt_profile_load_string",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Profile([MarshalAs(UnmanagedType.LPUTF8Str)]string text);
    [DllImport(Lib,EntryPoint="mlt_profile_close",CallingConvention=CallingConvention.Cdecl)] internal static extern void ProfileClose(nint profile);
    [DllImport(Lib,EntryPoint="mlt_factory_producer",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Producer(nint profile,[MarshalAs(UnmanagedType.LPUTF8Str)]string service,[MarshalAs(UnmanagedType.LPUTF8Str)]string resource);
    [DllImport(Lib,EntryPoint="mlt_producer_service",CallingConvention=CallingConvention.Cdecl)] internal static extern nint ProducerService(nint producer);
    [DllImport(Lib,EntryPoint="mlt_producer_properties",CallingConvention=CallingConvention.Cdecl)] internal static extern nint ProducerProperties(nint producer);
    [DllImport(Lib,EntryPoint="mlt_producer_seek",CallingConvention=CallingConvention.Cdecl)] internal static extern int Seek(nint producer,int frame);
    [DllImport(Lib,EntryPoint="mlt_producer_close",CallingConvention=CallingConvention.Cdecl)] internal static extern void ProducerClose(nint producer);
    [DllImport(Lib,EntryPoint="mlt_service_get_frame",CallingConvention=CallingConvention.Cdecl)] internal static extern int GetFrame(nint service,out nint frame,int index);
    [DllImport(Lib,EntryPoint="mlt_frame_get_image",CallingConvention=CallingConvention.Cdecl)] internal static extern int Image(nint frame,out nint buffer,ref int format,ref int width,ref int height,int writable);
    [DllImport(Lib,EntryPoint="mlt_frame_get_audio",CallingConvention=CallingConvention.Cdecl)] internal static extern int Audio(nint frame,out nint buffer,ref int format,ref int frequency,ref int channels,ref int samples);
    [DllImport(Lib,EntryPoint="mlt_frame_close",CallingConvention=CallingConvention.Cdecl)] internal static extern void FrameClose(nint frame);
    [DllImport(Lib,EntryPoint="mlt_properties_set",CallingConvention=CallingConvention.Cdecl)] internal static extern int Set(nint properties,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport(Lib,EntryPoint="mlt_factory_consumer",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Consumer(nint profile,[MarshalAs(UnmanagedType.LPUTF8Str)]string service,[MarshalAs(UnmanagedType.LPUTF8Str)]string resource);
    [DllImport(Lib,EntryPoint="mlt_consumer_properties",CallingConvention=CallingConvention.Cdecl)] internal static extern nint ConsumerProperties(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_consumer_connect",CallingConvention=CallingConvention.Cdecl)] internal static extern int Connect(nint consumer,nint producerService);
    [DllImport(Lib,EntryPoint="mlt_consumer_start",CallingConvention=CallingConvention.Cdecl)] internal static extern int Start(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_consumer_is_stopped",CallingConvention=CallingConvention.Cdecl)] internal static extern int IsStopped(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_consumer_position",CallingConvention=CallingConvention.Cdecl)] internal static extern int ConsumerPosition(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_consumer_stop",CallingConvention=CallingConvention.Cdecl)] internal static extern int Stop(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_consumer_close",CallingConvention=CallingConvention.Cdecl)] internal static extern void ConsumerClose(nint consumer);
    [DllImport(Lib,EntryPoint="mlt_factory_filter",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Filter(nint profile,[MarshalAs(UnmanagedType.LPUTF8Str)]string service,nint input);
    [DllImport(Lib,EntryPoint="mlt_filter_properties",CallingConvention=CallingConvention.Cdecl)] internal static extern nint FilterProperties(nint filter);
    [DllImport(Lib,EntryPoint="mlt_filter_close",CallingConvention=CallingConvention.Cdecl)] internal static extern void FilterClose(nint filter);
    [DllImport(Lib,EntryPoint="mlt_service_attach",CallingConvention=CallingConvention.Cdecl)] internal static extern int Attach(nint service,nint filter);
    [DllImport(Lib,EntryPoint="mlt_properties_get",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Get(nint properties,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DllImport(Lib,EntryPoint="mlt_producer_set_in_and_out",CallingConvention=CallingConvention.Cdecl)] internal static extern int SetInOut(nint producer,int @in,int @out);
    [StructLayout(LayoutKind.Sequential)]internal struct EventData{public nint Value;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void EventListener(nint properties,nint data,EventData eventData);
    [DllImport(Lib,EntryPoint="mlt_events_listen",CallingConvention=CallingConvention.Cdecl)]internal static extern nint Listen(nint properties,nint data,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,EventListener callback);
}
