#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <d3d11.h>
#include <dxgi1_4.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <stdio.h>
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
static long long ticks(){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static unsigned long long ft(FILETIME t){ULARGE_INTEGER u;u.LowPart=t.dwLowDateTime;u.HighPart=t.dwHighDateTime;return u.QuadPart;}
static unsigned long long cpu(){FILETIME c,e,k,u;GetProcessTimes(GetCurrentProcess(),&c,&e,&k,&u);return ft(k)+ft(u);}
static void sample(const char* stage,IDXGIAdapter1* adapter,unsigned frames,unsigned outputs){
PROCESS_MEMORY_COUNTERS_EX m={};m.cb=sizeof(m);GetProcessMemoryInfo(GetCurrentProcess(),(PROCESS_MEMORY_COUNTERS*)&m,sizeof(m));
DXGI_QUERY_VIDEO_MEMORY_INFO local={},nonlocal={};com_ptr<IDXGIAdapter3> a;
if(adapter&&SUCCEEDED(adapter->QueryInterface(a.put()))){a->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local);a->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal);}
printf("stage=%s ws=%llu peakWs=%llu private=%llu gpuLocal=%llu gpuNonlocal=%llu captured=%u outputs=%u\n",stage,(unsigned long long)m.WorkingSetSize,(unsigned long long)m.PeakWorkingSetSize,(unsigned long long)m.PrivateUsage,local.CurrentUsage,nonlocal.CurrentUsage,frames,outputs);fflush(stdout);}
using Create=int(__cdecl*)(ID3D11Device*,unsigned,unsigned,unsigned,unsigned,const wchar_t*,unsigned,void**);
using Configure=int(__cdecl*)(void*,unsigned,unsigned);using Copy=int(__cdecl*)(void*,ID3D11Texture2D*);using Encode=int(__cdecl*)(void*,unsigned long long);using Flush=int(__cdecl*)(void*);using Destroy=void(__cdecl*)(void*);using ErrorFn=const char*(__cdecl*)();
struct ApartmentScope{ApartmentScope(){init_apartment(apartment_type::multi_threaded);}~ApartmentScope(){clear_factory_cache();uninit_apartment();}};
struct EncoderScope{void*& encoder;Destroy& destroy;HMODULE& shim;~EncoderScope(){if(encoder&&destroy){destroy(encoder);encoder=nullptr;}if(shim){FreeLibrary(shim);shim=nullptr;}}};
struct CaptureScope{GraphicsCaptureSession& session;Direct3D11CaptureFramePool& pool;~CaptureScope(){try{session.Close();}catch(...){}try{pool.Close();}catch(...){}}};
struct HandleScope{HANDLE value;~HandleScope(){if(value)CloseHandle(value);}};
struct StopControl{HANDLE entered=nullptr,release=nullptr,thread=nullptr;volatile LONG stopping=0;
static DWORD WINAPI Run(void* p){auto self=(StopControl*)p;DWORD result=WaitForSingleObject(self->entered,6000);if(result==WAIT_OBJECT_0){InterlockedExchange(&self->stopping,1);fprintf(stderr,"testStop=requested-inside-completion-gate\n");SetEvent(self->release);}return result;}
~StopControl(){if(release)SetEvent(release);if(thread){WaitForSingleObject(thread,7000);CloseHandle(thread);}if(entered)CloseHandle(entered);if(release)CloseHandle(release);}};
int RunProbe(int argc,wchar_t** argv){try{
int mode=argc>1?_wtoi(argv[1]):0; //0 WGC only,1 owned BGRA copy,2 NV12 current shim
sample("before-com",nullptr,0,0);ApartmentScope apartment;
com_ptr<IDXGIFactory1> factory;check_hresult(CreateDXGIFactory1(__uuidof(IDXGIFactory1),factory.put_void()));com_ptr<IDXGIAdapter1> adapter;
for(unsigned i=0;;i++){com_ptr<IDXGIAdapter1> a;check_hresult(factory->EnumAdapters1(i,a.put()));DXGI_ADAPTER_DESC1 desc;a->GetDesc1(&desc);if(desc.VendorId==0x10de){adapter=a;wprintf(L"adapter=%s mode=%d\n",desc.Description,mode);break;}}
com_ptr<ID3D11Device> device;com_ptr<ID3D11DeviceContext> context;check_hresult(D3D11CreateDevice(adapter.get(),D3D_DRIVER_TYPE_UNKNOWN,0,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.put(),nullptr,context.put()));sample("after-device",adapter.get(),0,0);
com_ptr<IInspectable> inspectable;check_hresult(CreateDirect3D11DeviceFromDXGIDevice(device.as<IDXGIDevice>().get(),inspectable.put()));auto projected=inspectable.as<IDirect3DDevice>();
POINT origin={0,0};HMONITOR monitor=MonitorFromPoint(origin,MONITOR_DEFAULTTOPRIMARY);
auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();GraphicsCaptureItem item{nullptr};check_hresult(interop->CreateForMonitor(monitor,guid_of<GraphicsCaptureItem>(),put_abi(item)));auto size=item.Size();printf("width=%d height=%d fps=165 qp=20 buffers=1\n",size.Width,size.Height);if(size.Width!=2560||size.Height!=1600)return 5;
com_ptr<ID3D11Texture2D> target;com_ptr<ID3D11Query> query;void* encoder=nullptr;HMODULE shim=nullptr;Copy copy=nullptr;Encode encode=nullptr;Flush flush=nullptr;Destroy destroy=nullptr;ErrorFn error=nullptr;EncoderScope encoderScope{encoder,destroy,shim};StopControl stopControl;
if(mode==1){D3D11_TEXTURE2D_DESC td={};td.Width=size.Width;td.Height=size.Height;td.MipLevels=td.ArraySize=td.SampleDesc.Count=1;td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;check_hresult(device->CreateTexture2D(&td,nullptr,target.put()));D3D11_QUERY_DESC qd={D3D11_QUERY_EVENT,0};check_hresult(device->CreateQuery(&qd,query.put()));}
if(mode==2){if(argc<4)return 6;shim=LoadLibraryW(argv[2]);if(!shim)return 7;auto create=(Create)GetProcAddress(shim,"nc_create_format");auto configure=(Configure)GetProcAddress(shim,"nc_configure_resize");copy=(Copy)GetProcAddress(shim,"nc_copy_texture");encode=(Encode)GetProcAddress(shim,"nc_encode");flush=(Flush)GetProcAddress(shim,"nc_flush");destroy=(Destroy)GetProcAddress(shim,"nc_destroy");error=(ErrorFn)GetProcAddress(shim,"nc_error");int status=create(device.get(),size.Width,size.Height,165,20,argv[3],1,&encoder);if(!status)status=configure(encoder,size.Width,size.Height);if(status){fprintf(stderr,"NVENC:%s\n",error());return 8;}}
if(mode==2&&argc>4){auto configureTest=(void(__cdecl*)(void*,unsigned,HANDLE,HANDLE))GetProcAddress(shim,"nc_test_configure");if(!configureTest)return 14;
unsigned fail=0;if(wcscmp(argv[4],L"signal-fail")==0)fail=1;if(wcscmp(argv[4],L"arm-fail")==0)fail=2;if(wcscmp(argv[4],L"wait-fail")==0)fail=3;if(wcscmp(argv[4],L"wait-timeout")==0)fail=4;if(wcscmp(argv[4],L"query-fallback")==0)fail=5;if(wcscmp(argv[4],L"copy-fault")==0)fail=7;
if(wcscmp(argv[4],L"stop")==0){stopControl.entered=CreateEventW(nullptr,TRUE,FALSE,nullptr);stopControl.release=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!stopControl.entered||!stopControl.release)return 15;stopControl.thread=CreateThread(nullptr,0,StopControl::Run,&stopControl,0,nullptr);if(!stopControl.thread)return 16;}
configureTest(encoder,fail,stopControl.entered,stopControl.release);}
sample("before-pool",adapter.get(),0,0);auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(projected,DirectXPixelFormat::B8G8R8A8UIntNormalized,1,size);auto session=pool.CreateCaptureSession(item);CaptureScope captureScope{session,pool};session.MinUpdateInterval(winrt::Windows::Foundation::TimeSpan{0});session.StartCapture();sample("started",adapter.get(),0,0);
HANDLE timer=CreateWaitableTimerExW(nullptr,nullptr,2,TIMER_ALL_ACCESS);if(!timer)return 9;HandleScope timerScope{timer};LARGE_INTEGER freq;QueryPerformanceFrequency(&freq);Direct3D11CaptureFrame pending{nullptr};auto readyStart=ticks();while(!(pending=pool.TryGetNextFrame())){if((double)(ticks()-readyStart)/freq.QuadPart>5)return 13;Sleep(1);}sample("first-ready",adapter.get(),0,0);auto start=ticks();auto cpuStart=cpu();unsigned frames=0,outputs=0;bool initialized=false;unsigned report=1;
for(unsigned i=0;i<(unsigned)(argc>5?_wtoi(argv[5]):825);i++){
double remaining=(double)i/165.-(double)(ticks()-start)/freq.QuadPart;if(remaining>0){LARGE_INTEGER due;due.QuadPart=-(long long)(remaining*1e7);SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE);WaitForSingleObject(timer,1000);}
if(auto frame= i==0 ? pending : pool.TryGetNextFrame()){auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();com_ptr<ID3D11Texture2D> texture;check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),texture.put_void()));
if(mode==1){context->CopyResource(target.get(),texture.get());context->End(query.get());context->Flush();auto wait=ticks();while(context->GetData(query.get(),nullptr,0,0)==S_FALSE){if((double)(ticks()-wait)/freq.QuadPart>3)throw hresult_error(E_FAIL);SwitchToThread();}}
if(mode==2&&copy(encoder,texture.get())){fprintf(stderr,"copy:%s\n",error());return 10;}initialized=true;frames++;frame.Close();}if(InterlockedCompareExchange(&stopControl.stopping,0,0)){fprintf(stderr,"testStop=source-copy-completed-before-frame-return-and-stop\n");break;}
if(mode==2&&initialized){if(encode(encoder,(unsigned long long)i*10000000/165)){fprintf(stderr,"encode:%s\n",error());return 11;}outputs++;}else if(mode!=2)outputs++;
double elapsed=(double)(ticks()-start)/freq.QuadPart;if(elapsed>=report){sample("second",adapter.get(),frames,outputs);report++;}}
if(encoder){if(flush(encoder)){fprintf(stderr,"flush:%s\n",error());return 12;}auto stats=(void(__cdecl*)(void*))GetProcAddress(shim,"nc_wait_stats");if(stats)stats(encoder);destroy(encoder);encoder=nullptr;}double elapsed=(double)(ticks()-start)/freq.QuadPart;double cpuSeconds=(cpu()-cpuStart)/1e7;sample("complete",adapter.get(),frames,outputs);printf("elapsed=%.6f cpuSeconds=%.6f cpuOneCorePercent=%.4f captured=%u outputs=%u\n",elapsed,cpuSeconds,cpuSeconds/elapsed*100,frames,outputs);return 0;
}catch(hresult_error const& e){fwprintf(stderr,L"HRESULT=%08x %s\n",(unsigned)e.code(),e.message().c_str());return 20;}}



static LONG WINAPI OwnCrash(EXCEPTION_POINTERS* e){fprintf(stderr,"ownCrash code=%08lx address=%p\n",e->ExceptionRecord->ExceptionCode,e->ExceptionRecord->ExceptionAddress);void* frames[24];USHORT count=CaptureStackBackTrace(0,24,frames,nullptr);for(USHORT i=0;i<count;i++){HMODULE mod=nullptr;wchar_t path[MAX_PATH]={};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)frames[i],&mod);GetModuleFileNameW(mod,path,MAX_PATH);fwprintf(stderr,L"stack %s+%llx\n",path,(unsigned long long)((char*)frames[i]-(char*)mod));}return EXCEPTION_EXECUTE_HANDLER;}
int wmain(int argc,wchar_t** argv){SetUnhandledExceptionFilter(OwnCrash);unsigned cycles=argc>6?_wtoi(argv[6]):1;if(cycles<1||cycles>5)return 17;wchar_t* original=argc>3?argv[3]:nullptr;
for(unsigned i=0;i<cycles;i++){DWORD before,after;GetProcessHandleCount(GetCurrentProcess(),&before);printf("cycle=%u handlesBefore=%lu\n",i,before);fprintf(stderr,"mainPhase=before-format cycle=%u original=%p\n",i,original);wchar_t output[1024];if(cycles>1&&original){_snwprintf_s(output,_countof(output),_TRUNCATE,L"%s.cycle%u.h264",original,i);argv[3]=output;}fprintf(stderr,"mainPhase=before-run cycle=%u\n",i);int result=RunProbe(argc,argv);Sleep(200);GetProcessHandleCount(GetCurrentProcess(),&after);printf("cycle=%u handlesAfter=%lu result=%d\n",i,after,result);fflush(stdout);if(result&&!(argc>4&&wcscmp(argv[4],L"copy-fault")==0&&result==10))return result;Sleep(100);}return 0;}





