#include "wgc_capture.h"
#include "wgc_lifetime.h"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <stdexcept>
#include <string>
#include <cstdio>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
struct Encoder;
extern "C" int __cdecl nc_create_format(ID3D11Device*,unsigned,unsigned,unsigned,unsigned,const wchar_t*,unsigned,Encoder**);
extern "C" int __cdecl nc_configure_resize(Encoder*,unsigned,unsigned);
extern "C" int __cdecl nc_copy_surface(Encoder*,IUnknown*);
extern "C" int __cdecl nc_encode(Encoder*,unsigned long long);
extern "C" void __cdecl nc_request_idr(Encoder*);
extern "C" int __cdecl nc_flush(Encoder*);
extern "C" void __cdecl nc_destroy(Encoder*);
extern "C" void __cdecl nc_set_callback(Encoder*,WgcPacket,void*);
extern "C" const char* __cdecl nc_error();

namespace {
long long ticks() noexcept { LARGE_INTEGER value; QueryPerformanceCounter(&value); return value.QuadPart; }
long long frequency() noexcept { LARGE_INTEGER value; QueryPerformanceFrequency(&value); return value.QuadPart; }
bool stopping(WgcState* state) noexcept { return InterlockedCompareExchange(&state->stopping,0,0)!=0; }
bool consume_keyframe_request(WgcState* state) noexcept { return (InterlockedAnd(&state->controlFlags,~2L)&2)!=0; }
void check_nc(int status) { if(status) throw std::runtime_error(nc_error()); }
struct Apartment {
    bool initialized=false;
    void start() { init_apartment(apartment_type::multi_threaded); initialized=true; }
    ~Apartment() { if(initialized) { clear_factory_cache(); uninit_apartment(); } }
};
struct Timer {
    HANDLE value=nullptr;
    bool exactDeadline=false;
    ~Timer() { if(value) CloseHandle(value); }
    void create() {
        // Diagnostic A/B switch is read once per capture, never on the frame hot path.
        wchar_t setting[24]{}; GetEnvironmentVariableW(L"ZENITH_NATIVE_PACER",setting,24);
        exactDeadline=_wcsicmp(setting,L"deadline")==0;
        value=CreateWaitableTimerExW(nullptr,nullptr,2,TIMER_ALL_ACCESS); if(!value) throw_last_error();
    }
    void wait(long long start, unsigned long long index, unsigned fps, WgcState* state) {
        const auto hz=frequency();
        while(!stopping(state)) {
            double remaining=static_cast<double>(index)/fps-static_cast<double>(ticks()-start)/hz;
            if(remaining<=0) return;
            if(exactDeadline||remaining>0.0003) {
                double wait=std::min(remaining,0.05)-(exactDeadline?0.0:0.00015);
                LARGE_INTEGER due; due.QuadPart=-std::max(1LL,static_cast<long long>(wait*10000000.0));
                if(!SetWaitableTimer(value,&due,0,nullptr,nullptr,FALSE)) throw_last_error();
                if(WaitForSingleObject(value,1000)!=WAIT_OBJECT_0) throw std::runtime_error("Video pacing timer failed.");
            } else { for(int i=0;i<100;++i) YieldProcessor(); }
        }
    }
};
struct Encoded {
    Encoder* value=nullptr;
    bool ready=false;
    ~Encoded() { if(value) nc_destroy(value); }
};
struct FinishingCallbackError : std::runtime_error {
    FinishingCallbackError():std::runtime_error("Managed video finishing callback failed.") {}
};
struct Callbacks {
    WgcState* state; WgcPacket packet; WgcEvent event; void* user;
    bool callbackFailed=false;
    void notify(unsigned kind) {
        if(event(user,kind)) {
            callbackFailed=true;
            if(kind==2)throw FinishingCallbackError();
            throw std::runtime_error("Managed video lifecycle callback failed.");
        }
    }
    static int __cdecl emit(void* context,const void* data,unsigned length,unsigned long long index,unsigned key) noexcept {
        auto& self=*static_cast<Callbacks*>(context);
        int result=self.packet(self.user,data,length,index,key);
        if(result) self.callbackFailed=true;
        else { InterlockedIncrement64(&self.state->encoded); InterlockedAdd64(&self.state->bytes,length); }
        return result;
    }
};
struct Capture {
    // Declaration order deliberately leaves the apartment alive until every COM member is released.
    Apartment apartment;
    com_ptr<IDXGIFactory1> factory;
    com_ptr<IDXGIAdapter1> adapter;
    com_ptr<ID3D11Device> device;
    com_ptr<ID3D11DeviceContext> context;
    IDirect3DDevice projected{nullptr};
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    Encoded encoder;
    Timer timer;
    ~Capture() {
        // Close capture producers while all their dependencies still exist. Destructors then Release.
        if(session) { try { session.Close(); } catch(...) {} }
        if(pool) { try { pool.Close(); } catch(...) {} }
        if(projected) { try { projected.Close(); } catch(...) {} }
    }
    void copy(Direct3D11CaptureFrame const& frame,const WgcConfig& config) {
        auto size=frame.ContentSize();
        if(size.Width!=static_cast<int>(config.sourceWidth)||size.Height!=static_cast<int>(config.sourceHeight))
            throw std::runtime_error("Display size changed during capture; resume to rebuild capture.");
        auto surface=frame.Surface();
        check_nc(nc_copy_surface(encoder.value,reinterpret_cast<IUnknown*>(get_abi(surface))));
    }
    void run(const WgcConfig& config,Callbacks& callbacks) {
        apartment.start();
        check_hresult(CreateDXGIFactory1(__uuidof(IDXGIFactory1),factory.put_void()));
        for(unsigned index=0;;++index) {
            com_ptr<IDXGIAdapter1> candidate;
            HRESULT status=factory->EnumAdapters1(index,candidate.put());
            if(status==DXGI_ERROR_NOT_FOUND) throw std::runtime_error("A supported NVIDIA GPU is required by the native video encoder.");
            check_hresult(status); DXGI_ADAPTER_DESC1 description{}; check_hresult(candidate->GetDesc1(&description));
            if(description.VendorId==0x10de) { adapter=std::move(candidate); break; }
        }
        check_hresult(D3D11CreateDevice(adapter.get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,device.put(),nullptr,context.put()));
        com_ptr<IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(device.as<IDXGIDevice>().get(),inspectable.put()));
        projected=inspectable.as<IDirect3DDevice>();
        auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        check_hresult(interop->CreateForMonitor(config.monitor,guid_of<GraphicsCaptureItem>(),put_abi(item)));
        auto size=item.Size();
        if(size.Width!=static_cast<int>(config.sourceWidth)||size.Height!=static_cast<int>(config.sourceHeight))
            throw std::runtime_error("Display configuration changed before capture started.");
        check_nc(nc_create_format(device.get(),config.width,config.height,config.fps,config.qp,config.output,config.nv12,&encoder.value));
        encoder.ready=true;
        if(config.nv12||config.width!=config.sourceWidth||config.height!=config.sourceHeight)
            check_nc(nc_configure_resize(encoder.value,config.sourceWidth,config.sourceHeight));
        nc_set_callback(encoder.value,&Callbacks::emit,&callbacks);
        pool=Direct3D11CaptureFramePool::CreateFreeThreaded(projected,DirectXPixelFormat::B8G8R8A8UIntNormalized,config.buffers,size);
        session=pool.CreateCaptureSession(item); session.MinUpdateInterval(winrt::Windows::Foundation::TimeSpan{0});
        // Never draw Windows' yellow capture border: this records the user's own screen in the background on purpose.
        // Windows 11 allows borderless capture for desktop apps; older builds without the API just keep the border.
        try { GraphicsCaptureAccess::RequestAccessAsync(GraphicsCaptureAccessKind::Borderless).get(); } catch (...) {}
        try { session.IsBorderRequired(false); } catch (...) {}
        session.StartCapture(); timer.create();
        Direct3D11CaptureFrame first{nullptr}; const auto readyStart=ticks(),hz=frequency();
        while(!stopping(callbacks.state)&&!(first=pool.TryGetNextFrame())) {
            if(static_cast<double>(ticks()-readyStart)/hz>5) throw std::runtime_error("No capture frame arrived within five seconds.");
            Sleep(1);
        }
        if(!first) return;
        try { copy(first,config); } catch(...) { first.Close(); throw; }
        first.Close(); first=nullptr; InterlockedIncrement64(&callbacks.state->captured);
        const auto start=ticks(); callbacks.notify(1);
        for(unsigned long long index=0;!stopping(callbacks.state);++index) {
            timer.wait(start,index,config.fps,callbacks.state); if(stopping(callbacks.state)) break;
            auto frame=pool.TryGetNextFrame();
            if(frame) {
                try { copy(frame,config); } catch(...) { frame.Close(); throw; }
                frame.Close(); InterlockedIncrement64(&callbacks.state->captured);
            } else InterlockedIncrement64(&callbacks.state->repeated);
            // All encoder mutation stays on this worker; concurrent callers only set the shared bit.
            if(consume_keyframe_request(callbacks.state))nc_request_idr(encoder.value);
            check_nc(nc_encode(encoder.value,index*10000000/config.fps));
            auto elapsed=ticks()-start; InterlockedExchange64(&callbacks.state->elapsedQpc,elapsed);
            if(static_cast<double>(elapsed)/hz>static_cast<double>(index+1)/config.fps) InterlockedIncrement64(&callbacks.state->missed);
        }
        InterlockedExchange64(&callbacks.state->elapsedQpc,ticks()-start);
    }
    void flush() { if(encoder.ready) check_nc(nc_flush(encoder.value)); }
};
struct BridgeError {
    char* output; unsigned capacity; int code=0;
    void capture() noexcept {
        if(code) return; code=-1;
        try { throw; }
        catch(hresult_error const& error) {
            code=error.code().value; if(!code)code=-1;
            try { auto text=to_string(error.message()); if(capacity)strncpy_s(output,capacity,text.c_str(),_TRUNCATE); } catch(...) {}
        }
        catch(FinishingCallbackError const& error) { code=-4097; if(capacity)strncpy_s(output,capacity,error.what(),_TRUNCATE); }
        catch(std::exception const& error) { if(capacity)strncpy_s(output,capacity,error.what(),_TRUNCATE); }
        catch(...) { if(capacity)strncpy_s(output,capacity,"Unknown native capture error.",_TRUNCATE); }
    }
};
// A real counted COM object verifies that native lifetime scopes do not rely on a GC/finalizer.
struct CountedUnknown final : IUnknown {
    volatile LONG refs=1; WgcState* state;
    explicit CountedUnknown(WgcState* value):state(value) { InterlockedIncrement(&state->fakeAlive); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER; *result=nullptr; if(iid!=__uuidof(IUnknown))return E_NOINTERFACE;
        *result=this; AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { auto remaining=InterlockedDecrement(&refs); if(!remaining){InterlockedDecrement(&state->fakeAlive);InterlockedIncrement(&state->fakeDestroyed);delete this;} return remaining; }
};
struct FakeCapture {
    WgcState* state; com_ptr<IUnknown> first,second;
    explicit FakeCapture(WgcState* value):state(value) {}
    void run(WgcConfig const* config,Callbacks& callbacks,unsigned mode) {
        first.attach(new CountedUnknown(state)); second=first.as<IUnknown>();
        if(mode==1)throw std::runtime_error("Injected partial startup failure.");
        callbacks.notify(1);
        const unsigned char packet[]={0,0,0,1,0x65};
        if(mode==6) {
            for(unsigned long long index=0;!stopping(state);++index) {
                bool requested=consume_keyframe_request(state);
                if(Callbacks::emit(&callbacks,packet,sizeof(packet),index,requested||index%(config->fps*2)==0))throw std::runtime_error("Managed keyframe test callback failed.");
                Sleep(1);
            }
            return;
        }
        if(mode==5) {
            Timer timer; timer.create(); auto start=ticks(),hz=frequency();
            for(unsigned long long index=0;index<config->fps*5&&!stopping(state);++index) {
                timer.wait(start,index,config->fps,state); if(stopping(state))break;
                if(Callbacks::emit(&callbacks,packet,sizeof(packet),index,index%(config->fps*2)==0))throw std::runtime_error("Managed paced callback failed.");
                auto elapsed=ticks()-start; InterlockedExchange64(&state->elapsedQpc,elapsed);
                if(static_cast<double>(elapsed)/hz>static_cast<double>(index+1)/config->fps)InterlockedIncrement64(&state->missed);
            }
            return;
        }
        if(Callbacks::emit(&callbacks,packet,sizeof(packet),0,1))throw std::runtime_error("Managed packet callback failed.");
        if(mode==2)throw std::runtime_error("Injected native capture failure.");
        while(mode==4?(InterlockedCompareExchange(&state->controlFlags,0,0)&1)==0:!stopping(state)) Sleep(1);
    }
    void flush(unsigned mode) { if(mode==3)throw std::runtime_error("Injected encoder flush failure."); }
};
}
extern "C" __declspec(dllexport) int __cdecl wgc_run(const WgcConfig* config,WgcState* state,WgcPacket packet,WgcEvent event,void* user,char* error,unsigned capacity) {
    if(!config||!state||!packet||!event||!error||!capacity)return E_INVALIDARG;
    error[0]=0; BridgeError failure{error,capacity}; Callbacks callbacks{state,packet,event,user};
    {
        Capture owner;
        wgc_owned_lifetime(owner,[&](auto& capture){capture.run(*config,callbacks);},[&](auto& capture){capture.flush();},
            [&]{callbacks.notify(2);},[&]()noexcept{failure.capture();});
    }
    return failure.code;
}
extern "C" __declspec(dllexport) int __cdecl wgc_run_test(const WgcConfig* config,WgcState* state,WgcPacket packet,WgcEvent event,void* user,char* error,unsigned capacity,unsigned mode) {
    if(!config||!state||!packet||!event||!error||!capacity)return E_INVALIDARG;
    error[0]=0; BridgeError failure{error,capacity}; Callbacks callbacks{state,packet,event,user};
    {
        FakeCapture owner(state);
        wgc_owned_lifetime(owner,[&](auto& capture){capture.run(config,callbacks,mode);},[&](auto& capture){capture.flush(mode);},
            [&]{callbacks.notify(2);},[&]()noexcept{failure.capture();});
    }
    return failure.code;
}
