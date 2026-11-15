#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <d3d11.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <stdio.h>
using Microsoft::WRL::ComPtr;
template<class T> using com_ptr=ComPtr<T>;static long long ticks(){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static unsigned long long ft(FILETIME t){ULARGE_INTEGER u;u.LowPart=t.dwLowDateTime;u.HighPart=t.dwHighDateTime;return u.QuadPart;}
static unsigned long long cpu(){FILETIME c,e,k,u;GetProcessTimes(GetCurrentProcess(),&c,&e,&k,&u);return ft(k)+ft(u);}
static void sample(const char* stage,IDXGIAdapter1* adapter,unsigned frames,unsigned outputs){
PROCESS_MEMORY_COUNTERS_EX m={};m.cb=sizeof(m);GetProcessMemoryInfo(GetCurrentProcess(),(PROCESS_MEMORY_COUNTERS*)&m,sizeof(m));
DXGI_QUERY_VIDEO_MEMORY_INFO local={},nonlocal={};com_ptr<IDXGIAdapter3> a;
if(adapter&&SUCCEEDED(adapter->QueryInterface(__uuidof(IDXGIAdapter3),(void**)a.GetAddressOf()))){a->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local);a->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal);}
printf("stage=%s ws=%llu peakWs=%llu private=%llu gpuLocal=%llu gpuNonlocal=%llu captured=%u outputs=%u\n",stage,(unsigned long long)m.WorkingSetSize,(unsigned long long)m.PeakWorkingSetSize,(unsigned long long)m.PrivateUsage,local.CurrentUsage,nonlocal.CurrentUsage,frames,outputs);fflush(stdout);}
#define CHECK(call) do{HRESULT h=(call);if(FAILED(h)){printf("failed=%s hr=%08x\n",#call,(unsigned)h);return 20;}}while(0)
int wmain(){SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);sample("before-device",nullptr,0,0);ComPtr<IDXGIFactory1> factory;CHECK(CreateDXGIFactory1(__uuidof(IDXGIFactory1),(void**)factory.GetAddressOf()));ComPtr<IDXGIAdapter1> adapter;ComPtr<IDXGIOutput> output;DXGI_OUTPUT_DESC chosen={};
for(unsigned i=0;!output;i++){ComPtr<IDXGIAdapter1> a;HRESULT hr=factory->EnumAdapters1(i,a.GetAddressOf());if(hr==DXGI_ERROR_NOT_FOUND)break;CHECK(hr);for(unsigned j=0;;j++){ComPtr<IDXGIOutput> o;hr=a->EnumOutputs(j,o.GetAddressOf());if(hr==DXGI_ERROR_NOT_FOUND)break;CHECK(hr);DXGI_OUTPUT_DESC d;o->GetDesc(&d);if(d.AttachedToDesktop&&d.DesktopCoordinates.left==0&&d.DesktopCoordinates.top==0){adapter=a;output=o;chosen=d;break;}}}
if(!output)return 2;DXGI_ADAPTER_DESC1 ad;adapter->GetDesc1(&ad);wprintf(L"adapter=%s vendor=%04x output=%s width=%ld height=%ld\n",ad.Description,ad.VendorId,chosen.DeviceName,chosen.DesktopCoordinates.right,chosen.DesktopCoordinates.bottom);if(chosen.DesktopCoordinates.right!=2560||chosen.DesktopCoordinates.bottom!=1600)return 3;
ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;CHECK(D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.GetAddressOf(),nullptr,context.GetAddressOf()));sample("after-device",adapter.Get(),0,0);
ComPtr<IDXGIOutput1> output1;CHECK(output.As(&output1));ComPtr<IDXGIOutputDuplication> duplication;CHECK(output1->DuplicateOutput(device.Get(),duplication.GetAddressOf()));sample("after-duplication",adapter.Get(),0,0);DXGI_OUTDUPL_DESC dd;duplication->GetDesc(&dd);printf("dupFormat=%u width=%u height=%u systemMemory=%d fps=165\n",dd.ModeDesc.Format,dd.ModeDesc.Width,dd.ModeDesc.Height,dd.DesktopImageInSystemMemory);
HANDLE timer=CreateWaitableTimerExW(nullptr,nullptr,2,TIMER_ALL_ACCESS);if(!timer)return 4;LARGE_INTEGER freq;QueryPerformanceFrequency(&freq);auto start=ticks();auto cpuStart=cpu();unsigned frames=0,outputs=0,report=1;
for(unsigned i=0;i<825;i++){double remaining=(double)i/165.-(double)(ticks()-start)/freq.QuadPart;if(remaining>0){LARGE_INTEGER due;due.QuadPart=-(long long)(remaining*1e7);SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE);WaitForSingleObject(timer,1000);}DXGI_OUTDUPL_FRAME_INFO info={};ComPtr<IDXGIResource> resource;HRESULT hr=duplication->AcquireNextFrame(0,&info,resource.GetAddressOf());if(hr==S_OK){ComPtr<ID3D11Texture2D> texture;CHECK(resource.As(&texture));D3D11_TEXTURE2D_DESC td;texture->GetDesc(&td);if(td.Width!=2560||td.Height!=1600)return 5;frames++;texture.Reset();resource.Reset();CHECK(duplication->ReleaseFrame());}else if(hr!=DXGI_ERROR_WAIT_TIMEOUT){printf("acquirehr=%08x\n",(unsigned)hr);return 6;}outputs++;double elapsed=(double)(ticks()-start)/freq.QuadPart;if(elapsed>=report){sample("second",adapter.Get(),frames,outputs);report++;}}
double elapsed=(double)(ticks()-start)/freq.QuadPart;double cpuSeconds=(cpu()-cpuStart)/1e7;sample("complete",adapter.Get(),frames,outputs);printf("elapsed=%.6f cpuSeconds=%.6f cpuOneCorePercent=%.4f captured=%u polls=%u\n",elapsed,cpuSeconds,cpuSeconds/elapsed*100,frames,outputs);CloseHandle(timer);return 0;}

