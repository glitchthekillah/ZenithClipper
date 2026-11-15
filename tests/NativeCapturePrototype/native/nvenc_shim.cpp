#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <stdio.h>
#include "nvEncodeAPI.h"
using Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;

struct Encoder {
    HMODULE module;
    NV_ENCODE_API_FUNCTION_LIST api;
    void* session;
    ID3D11Device* device;
    ID3D11DeviceContext* context;
    ID3D11Texture2D* texture;
    ID3D11Query* fence;
    NV_ENC_REGISTERED_PTR registered;
    NV_ENC_OUTPUT_PTR bitstream;
    HANDLE file;
    unsigned width, height, fps;
    unsigned long long frames, bytes;
};
static char errorText[1024];
static int Fail(Encoder* e, const char* stage, int code) {
    const char* detail = e && e->session && e->api.nvEncGetLastErrorString ? e->api.nvEncGetLastErrorString(e->session) : "";
    _snprintf_s(errorText, sizeof(errorText), _TRUNCATE, "%s: status=%d %s", stage, code, detail ? detail : "");
    return code ? code : -1;
}
#define NC_CHECK(call) do { NVENCSTATUS status = (call); if (status != NV_ENC_SUCCESS) return Fail(e, #call, status); } while(0)
#define HR_CHECK(call) do { HRESULT status = (call); if (FAILED(status)) return Fail(e, #call, status); } while(0)
extern "C" __declspec(dllexport) const char* __cdecl nc_error() { return errorText; }
extern "C" __declspec(dllexport) void __cdecl nc_destroy(Encoder* e) {
    if (!e) return;
    if (e->session) {
        if (e->registered) e->api.nvEncUnregisterResource(e->session, e->registered);
        if (e->bitstream) e->api.nvEncDestroyBitstreamBuffer(e->session, e->bitstream);
        e->api.nvEncDestroyEncoder(e->session);
    }
    if (e->file && e->file != INVALID_HANDLE_VALUE) CloseHandle(e->file);
    if (e->fence) e->fence->Release();
    if (e->texture) e->texture->Release();
    if (e->context) e->context->Release();
    if (e->device) e->device->Release();
    if (e->module) FreeLibrary(e->module);
    free(e);
}
extern "C" __declspec(dllexport) int __cdecl nc_create(ID3D11Device* device, unsigned width, unsigned height,
    unsigned fps, unsigned qp, const wchar_t* output, Encoder** result) {
    if (!device || !result || !width || !height || !fps || qp > 51) return Fail(0, "arguments", -1);
    Encoder* e = (Encoder*)calloc(1, sizeof(Encoder));
    if (!e) return Fail(0, "calloc", -1);
    *result = e;
    e->width = width; e->height = height; e->fps = fps;
    e->device = device; device->AddRef(); device->GetImmediateContext(&e->context);
    e->module = LoadLibraryExW(L"nvEncodeAPI64.dll", 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!e->module) return Fail(e, "LoadLibrary nvEncodeAPI64.dll", GetLastError());
    using CreateFn = NVENCSTATUS (NVENCAPI *)(NV_ENCODE_API_FUNCTION_LIST*);
    auto create = (CreateFn)GetProcAddress(e->module, "NvEncodeAPICreateInstance");
    if (!create) return Fail(e, "GetProcAddress", GetLastError());
    e->api.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    NC_CHECK(create(&e->api));
    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS open = {};
    open.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    open.device = device; open.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX; open.apiVersion = NVENCAPI_VERSION;
    NC_CHECK(e->api.nvEncOpenEncodeSessionEx(&open, &e->session));
    NV_ENC_PRESET_CONFIG preset = {};
    preset.version = NV_ENC_PRESET_CONFIG_VER; preset.presetCfg.version = NV_ENC_CONFIG_VER;
    NC_CHECK(e->api.nvEncGetEncodePresetConfigEx(e->session, NV_ENC_CODEC_H264_GUID,
        NV_ENC_PRESET_P1_GUID, NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY, &preset));
    NV_ENC_CONFIG config = preset.presetCfg;
    config.version = NV_ENC_CONFIG_VER;
    config.profileGUID = NV_ENC_H264_PROFILE_HIGH_GUID;
    config.gopLength = fps * 2; config.frameIntervalP = 1;
    config.rcParams.rateControlMode = NV_ENC_PARAMS_RC_CONSTQP;
    config.rcParams.constQP.qpIntra = qp; config.rcParams.constQP.qpInterP = qp; config.rcParams.constQP.qpInterB = qp;
    config.rcParams.enableLookahead = 0; config.rcParams.lookaheadDepth = 0;
    config.rcParams.enableAQ = 0; config.rcParams.enableTemporalAQ = 0;
    config.rcParams.multiPass = NV_ENC_MULTI_PASS_DISABLED;
    config.encodeCodecConfig.h264Config.maxNumRefFrames = 1;
    config.encodeCodecConfig.h264Config.idrPeriod = fps * 2;
    config.encodeCodecConfig.h264Config.repeatSPSPPS = 1;
    NV_ENC_INITIALIZE_PARAMS init = {};
    init.version = NV_ENC_INITIALIZE_PARAMS_VER; init.encodeGUID = NV_ENC_CODEC_H264_GUID;
    init.presetGUID = NV_ENC_PRESET_P1_GUID; init.tuningInfo = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
    init.encodeWidth = width; init.encodeHeight = height; init.darWidth = width; init.darHeight = height;
    init.frameRateNum = fps; init.frameRateDen = 1; init.enablePTD = 1; init.enableEncodeAsync = 0;
    init.encodeConfig = &config; init.maxEncodeWidth = width; init.maxEncodeHeight = height;
    NC_CHECK(e->api.nvEncInitializeEncoder(e->session, &init));
    D3D11_TEXTURE2D_DESC td = {};
    td.Width = width; td.Height = height; td.MipLevels = 1; td.ArraySize = 1;
    td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    HR_CHECK(device->CreateTexture2D(&td, 0, &e->texture));
    D3D11_QUERY_DESC qd = { D3D11_QUERY_EVENT, 0 };
    HR_CHECK(device->CreateQuery(&qd, &e->fence));
    NV_ENC_REGISTER_RESOURCE resource = {};
    resource.version = NV_ENC_REGISTER_RESOURCE_VER; resource.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
    resource.width = width; resource.height = height; resource.resourceToRegister = e->texture;
    resource.bufferFormat = NV_ENC_BUFFER_FORMAT_ARGB; resource.bufferUsage = NV_ENC_INPUT_IMAGE;
    NC_CHECK(e->api.nvEncRegisterResource(e->session, &resource));
    e->registered = resource.registeredResource;
    NV_ENC_CREATE_BITSTREAM_BUFFER bs = {}; bs.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    NC_CHECK(e->api.nvEncCreateBitstreamBuffer(e->session, &bs));
    e->bitstream = bs.bitstreamBuffer;
    e->file = CreateFileW(output, GENERIC_WRITE, FILE_SHARE_READ, 0, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
    if (e->file == INVALID_HANDLE_VALUE) return Fail(e, "CreateFile", GetLastError());
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl nc_copy_surface(Encoder* e, IUnknown* surface) {
    IDirect3DDxgiInterfaceAccess* access = 0;
    HR_CHECK(surface->QueryInterface(__uuidof(IDirect3DDxgiInterfaceAccess), (void**)&access));
    ID3D11Texture2D* source = 0;
    HRESULT hr = access->GetInterface(__uuidof(ID3D11Texture2D), (void**)&source);
    access->Release();
    if (FAILED(hr)) return Fail(e, "GetInterface texture", hr);
    D3D11_TEXTURE2D_DESC td; source->GetDesc(&td);
    if (td.Width != e->width || td.Height != e->height || td.Format != DXGI_FORMAT_B8G8R8A8_UNORM) {
        source->Release(); return Fail(e, "Unexpected WGC texture dimensions/format", -1);
    }
    e->context->CopyResource(e->texture, source);
    e->context->End(e->fence); e->context->Flush();
    // Keep the source capture frame alive until the copy is complete, without a CPU pixel readback.
    DWORD start = GetTickCount();
    while ((hr = e->context->GetData(e->fence, 0, 0, 0)) == S_FALSE) {
        if (GetTickCount() - start > 3000) { source->Release(); return Fail(e, "GPU copy timeout", -1); }
        SwitchToThread();
    }
    source->Release();
    if (FAILED(hr)) return Fail(e, "GPU copy completion", hr);
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl nc_encode(Encoder* e, unsigned long long timestamp) {
    NV_ENC_MAP_INPUT_RESOURCE map = {}; map.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
    map.registeredResource = e->registered;
    NC_CHECK(e->api.nvEncMapInputResource(e->session, &map));
    NV_ENC_PIC_PARAMS pic = {}; pic.version = NV_ENC_PIC_PARAMS_VER;
    pic.inputBuffer = map.mappedResource; pic.bufferFmt = map.mappedBufferFmt;
    pic.inputWidth = e->width; pic.inputHeight = e->height; pic.outputBitstream = e->bitstream;
    pic.pictureStruct = NV_ENC_PIC_STRUCT_FRAME; pic.inputTimeStamp = timestamp;
    pic.inputDuration = 10000000 / e->fps;
    NVENCSTATUS status = e->api.nvEncEncodePicture(e->session, &pic);
    if (status != NV_ENC_SUCCESS) { e->api.nvEncUnmapInputResource(e->session, map.mappedResource); return Fail(e, "nvEncEncodePicture", status); }
    NV_ENC_LOCK_BITSTREAM lock = {}; lock.version = NV_ENC_LOCK_BITSTREAM_VER;
    lock.outputBitstream = e->bitstream; lock.doNotWait = 0;
    status = e->api.nvEncLockBitstream(e->session, &lock);
    if (status == NV_ENC_SUCCESS) {
        DWORD written = 0;
        BOOL ok = WriteFile(e->file, lock.bitstreamBufferPtr, lock.bitstreamSizeInBytes, &written, 0);
        e->bytes += written; e->frames++;
        e->api.nvEncUnlockBitstream(e->session, e->bitstream);
        e->api.nvEncUnmapInputResource(e->session, map.mappedResource);
        if (!ok || written != lock.bitstreamSizeInBytes) return Fail(e, "WriteFile", GetLastError());
        return 0;
    }
    e->api.nvEncUnmapInputResource(e->session, map.mappedResource);
    return Fail(e, "nvEncLockBitstream", status);
}
extern "C" __declspec(dllexport) int __cdecl nc_flush(Encoder* e) {
    NV_ENC_PIC_PARAMS pic = {}; pic.version = NV_ENC_PIC_PARAMS_VER; pic.encodePicFlags = NV_ENC_PIC_FLAG_EOS;
    NC_CHECK(e->api.nvEncEncodePicture(e->session, &pic));
    FlushFileBuffers(e->file);
    return 0;
}


