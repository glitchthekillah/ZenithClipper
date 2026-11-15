#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <stdio.h>
#include <wchar.h>
using Create = int(__cdecl*)(ID3D11Device*, unsigned, unsigned, unsigned, unsigned, const wchar_t*, unsigned, void**);
using Configure = int(__cdecl*)(void*, unsigned, unsigned);
using Copy = int(__cdecl*)(void*, ID3D11Texture2D*);
using Encode = int(__cdecl*)(void*, unsigned long long);
using Flush = int(__cdecl*)(void*);
using Destroy = void(__cdecl*)(void*);
using Error = const char*(__cdecl*)();
struct Resources {
    IDXGIFactory1* factory=0; IDXGIAdapter1* adapter=0; ID3D11Device* device=0; ID3D11DeviceContext* context=0;
    ID3D11DeviceContext1* context1=0; ID3D11Texture2D* source=0; ID3D11RenderTargetView* view=0;
    HMODULE module=0; void* encoder=0; Destroy destroy=0;
    ~Resources() { if(encoder&&destroy)destroy(encoder); if(view)view->Release(); if(source)source->Release();
        if(context1)context1->Release(); if(context)context->Release(); if(device)device->Release(); if(adapter)adapter->Release();
        if(factory)factory->Release(); if(module)FreeLibrary(module); }
};
int wmain(int argc, wchar_t** argv) {
    bool nv12=argc>1 && wcscmp(argv[1],L"nv12")==0;
    unsigned outWidth=argc>2 ? _wtoi(argv[2]) : 2560, outHeight=outWidth*1600/2560;
    if(argc<4 || !outWidth || (outWidth&1) || (outHeight&1))return 2;
    Resources r;
    HRESULT hr=CreateDXGIFactory1(__uuidof(IDXGIFactory1),(void**)&r.factory); if(FAILED(hr))return 3;
    for(unsigned i=0;;i++){ IDXGIAdapter1* candidate=0; hr=r.factory->EnumAdapters1(i,&candidate); if(FAILED(hr))return 4;
        DXGI_ADAPTER_DESC1 desc; candidate->GetDesc1(&desc); if(desc.VendorId==0x10de){r.adapter=candidate;break;} candidate->Release(); }
    hr=D3D11CreateDevice(r.adapter,D3D_DRIVER_TYPE_UNKNOWN,0,D3D11_CREATE_DEVICE_BGRA_SUPPORT,0,0,D3D11_SDK_VERSION,&r.device,0,&r.context);
    if(FAILED(hr))return 5;
    hr=r.context->QueryInterface(__uuidof(ID3D11DeviceContext1),(void**)&r.context1); if(FAILED(hr))return 6;
    D3D11_TEXTURE2D_DESC desc={}; desc.Width=2560;desc.Height=1600;desc.MipLevels=1;desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;
    desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    hr=r.device->CreateTexture2D(&desc,0,&r.source);if(FAILED(hr))return 7;
    hr=r.device->CreateRenderTargetView(r.source,0,&r.view);if(FAILED(hr))return 8;
    wchar_t dll[MAX_PATH];GetModuleFileNameW(0,dll,MAX_PATH);wchar_t* name=wcsrchr(dll,92);
    if(!name)return 9;wcscpy_s(name+1,MAX_PATH-(name+1-dll),L"nvenc_shim.dll");
    r.module=LoadLibraryW(dll);if(!r.module)return 10;
    auto create=(Create)GetProcAddress(r.module,"nc_create_format");auto configure=(Configure)GetProcAddress(r.module,"nc_configure_resize");
    auto copy=(Copy)GetProcAddress(r.module,"nc_copy_texture");auto encode=(Encode)GetProcAddress(r.module,"nc_encode");
    auto flush=(Flush)GetProcAddress(r.module,"nc_flush");r.destroy=(Destroy)GetProcAddress(r.module,"nc_destroy");auto error=(Error)GetProcAddress(r.module,"nc_error");
    if(!create||!configure||!copy||!encode||!flush||!r.destroy||!error)return 11;
    int status=create(r.device,outWidth,outHeight,165,20,argv[3],nv12?1:0,&r.encoder);
    if(status){fprintf(stderr,"create: %s\n",error());return 12;}
    if(nv12||outWidth!=2560){status=configure(r.encoder,2560,1600);if(status){fprintf(stderr,"configure: %s\n",error());return 13;}}
    static const unsigned colors[16]={0x000000,0xffffff,0xff0000,0x00ff00,0x0000ff,0x00ffff,0xff00ff,0xffff00,
        0x404040,0x808080,0xc0c0c0,0x4080c0,0xcc4c19,0x1966cc,0x99cc33,0xe61980};
    for(unsigned frame=0;frame<3;frame++){
        for(int i=0;i<16;i++){
            unsigned c=colors[i];float color[4]={float((c>>16)&255)/255.f,float((c>>8)&255)/255.f,float(c&255)/255.f,1.f};
            D3D11_RECT rect={i*160,0,(i+1)*160,1600};r.context1->ClearView(r.view,color,&rect,1);
        }
        status=copy(r.encoder,r.source);if(status){fprintf(stderr,"copy: %s\n",error());return 14;}
        status=encode(r.encoder,frame*10000000ull/165);if(status){fprintf(stderr,"encode: %s\n",error());return 15;}
    }
    status=flush(r.encoder);if(status){fprintf(stderr,"flush: %s\n",error());return 16;}
    printf("Encoded3 synthetic %s frames at%ux%u from2560x1600 GPU RGB bars\n",nv12?"NV12":"BGRA",outWidth,outHeight);
    return 0;
}
