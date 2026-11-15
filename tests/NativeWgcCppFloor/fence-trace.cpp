#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_4.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <stdio.h>
#include "../../src/ZenithClipper.Native.Video/native/nvEncodeAPI.h"
using Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;

typedef int (__cdecl *PacketCallback)(void*, const void*, unsigned, unsigned long long, unsigned);
struct Encoder {
    PacketCallback callback;
    void* callbackUser;
    HMODULE module;
    NV_ENCODE_API_FUNCTION_LIST api;
    void* session;
    ID3D11Device* device;
    ID3D11DeviceContext* context;
    ID3D11Texture2D* texture;
    ID3D11Query* fence;
    ID3D11Fence* eventFence;
    ID3D11DeviceContext4* eventContext;
    HANDLE completionEvent;
    UINT64 fenceValue;
    bool eventDisabled;
    ID3D11VideoDevice* videoDevice;
    ID3D11VideoContext* videoContext;
    ID3D11VideoContext1* videoContext1;
    ID3D11VideoProcessorEnumerator* videoEnumerator;
    ID3D11VideoProcessor* processor;
    ID3D11VideoProcessorOutputView* outputView;
    unsigned sourceWidth, sourceHeight;
    DXGI_FORMAT textureFormat;
    NV_ENC_BUFFER_FORMAT bufferFormat;
    NV_ENC_REGISTERED_PTR registered;
    NV_ENC_OUTPUT_PTR bitstream;
    HANDLE file;
    unsigned width, height, fps;
    unsigned long long frames, bytes;
};
static thread_local char errorText[1024];
#define TRACE(e,stage) do{fprintf(stderr,"stage=%s encoded=%llu\n",stage,(e)->frames);fflush(stderr);}while(0)
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
    if (e->outputView) e->outputView->Release();
    if (e->processor) e->processor->Release();
    if (e->videoEnumerator) e->videoEnumerator->Release();
    if (e->videoContext1) e->videoContext1->Release();
    if (e->videoContext) e->videoContext->Release();
    if (e->videoDevice) e->videoDevice->Release();
    if(e->completionEvent)CloseHandle(e->completionEvent);
    if(e->eventFence)e->eventFence->Release();
    if(e->eventContext)e->eventContext->Release();
    if (e->fence) e->fence->Release();
    if (e->texture) e->texture->Release();
    if (e->context) e->context->Release();
    if (e->device) e->device->Release();
    if (e->module) FreeLibrary(e->module);
    free(e);
}
static int CreateEncoder(ID3D11Device* device, unsigned width, unsigned height,
    unsigned fps, unsigned qp, const wchar_t* output, Encoder** result, bool nv12) {
    if (!device || !result || !width || !height || !fps || qp > 51) return Fail(0, "arguments", -1);
    Encoder* e = (Encoder*)calloc(1, sizeof(Encoder));
    if (!e) return Fail(0, "calloc", -1);
    *result = e;
    e->width = width; e->height = height; e->fps = fps;
    e->sourceWidth = width; e->sourceHeight = height;
    e->textureFormat = nv12 ? DXGI_FORMAT_NV12 : DXGI_FORMAT_B8G8R8A8_UNORM;
    e->bufferFormat = nv12 ? NV_ENC_BUFFER_FORMAT_NV12 : NV_ENC_BUFFER_FORMAT_ARGB;
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
    NV_ENC_CONFIG_H264_VUI_PARAMETERS& vui = config.encodeCodecConfig.h264Config.h264VUIParameters;
    vui.videoSignalTypePresentFlag = 1; vui.videoFormat = NV_ENC_VUI_VIDEO_FORMAT_UNSPECIFIED;
    vui.videoFullRangeFlag = 0; vui.colourDescriptionPresentFlag = 1;
    vui.colourPrimaries = NV_ENC_VUI_COLOR_PRIMARIES_BT709;
    vui.transferCharacteristics = NV_ENC_VUI_TRANSFER_CHARACTERISTIC_BT709;
    vui.colourMatrix = NV_ENC_VUI_MATRIX_COEFFS_BT709;
    NV_ENC_INITIALIZE_PARAMS init = {};
    init.version = NV_ENC_INITIALIZE_PARAMS_VER; init.encodeGUID = NV_ENC_CODEC_H264_GUID;
    init.presetGUID = NV_ENC_PRESET_P1_GUID; init.tuningInfo = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
    init.encodeWidth = width; init.encodeHeight = height; init.darWidth = width; init.darHeight = height;
    init.frameRateNum = fps; init.frameRateDen = 1; init.enablePTD = 1; init.enableEncodeAsync = 0;
    init.encodeConfig = &config; init.maxEncodeWidth = width; init.maxEncodeHeight = height;
    NC_CHECK(e->api.nvEncInitializeEncoder(e->session, &init));
    D3D11_TEXTURE2D_DESC td = {};
    td.Width = width; td.Height = height; td.MipLevels = 1; td.ArraySize = 1;
    td.Format = e->textureFormat; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    HR_CHECK(device->CreateTexture2D(&td, 0, &e->texture));
    D3D11_QUERY_DESC qd = { D3D11_QUERY_EVENT, 0 };
    HR_CHECK(device->CreateQuery(&qd, &e->fence));
    ID3D11Device5* device5=0;
    if(SUCCEEDED(device->QueryInterface(__uuidof(ID3D11Device5),(void**)&device5))){
        HRESULT fenceHr=device5->CreateFence(0,D3D11_FENCE_FLAG_NONE,__uuidof(ID3D11Fence),(void**)&e->eventFence);
        device5->Release();
        if(SUCCEEDED(fenceHr)&&SUCCEEDED(e->context->QueryInterface(__uuidof(ID3D11DeviceContext4),(void**)&e->eventContext))){
            e->completionEvent=CreateEventW(0,FALSE,FALSE,0);
            // Event creation failure retains the query fallback.
        }
    }
    NV_ENC_REGISTER_RESOURCE resource = {};
    resource.version = NV_ENC_REGISTER_RESOURCE_VER; resource.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
    resource.width = width; resource.height = height; resource.resourceToRegister = e->texture;
    resource.bufferFormat = e->bufferFormat; resource.bufferUsage = NV_ENC_INPUT_IMAGE;
    NC_CHECK(e->api.nvEncRegisterResource(e->session, &resource));
    e->registered = resource.registeredResource;
    NV_ENC_CREATE_BITSTREAM_BUFFER bs = {}; bs.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    NC_CHECK(e->api.nvEncCreateBitstreamBuffer(e->session, &bs));
    e->bitstream = bs.bitstreamBuffer;
    if (output) {
    e->file = CreateFileW(output, GENERIC_WRITE, FILE_SHARE_READ, 0, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
    if (e->file == INVALID_HANDLE_VALUE) return Fail(e, "CreateFile", GetLastError());
    }
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl nc_configure_resize(Encoder* e, unsigned sourceWidth, unsigned sourceHeight) {
    if (!sourceWidth || !sourceHeight || e->processor) return Fail(e, "resize arguments", -1);
    HR_CHECK(e->device->QueryInterface(__uuidof(ID3D11VideoDevice), (void**)&e->videoDevice));
    HR_CHECK(e->context->QueryInterface(__uuidof(ID3D11VideoContext), (void**)&e->videoContext));
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC content = {};
    content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    content.InputFrameRate.Numerator = e->fps; content.InputFrameRate.Denominator = 1;
    content.InputWidth = sourceWidth; content.InputHeight = sourceHeight;
    content.OutputFrameRate = content.InputFrameRate;
    content.OutputWidth = e->width; content.OutputHeight = e->height;
    content.Usage = D3D11_VIDEO_USAGE_OPTIMAL_SPEED;
    HR_CHECK(e->videoDevice->CreateVideoProcessorEnumerator(&content, &e->videoEnumerator));
    UINT inputFlags = 0, outputFlags = 0;
    HR_CHECK(e->videoEnumerator->CheckVideoProcessorFormat(DXGI_FORMAT_B8G8R8A8_UNORM, &inputFlags));
    HR_CHECK(e->videoEnumerator->CheckVideoProcessorFormat(e->textureFormat, &outputFlags));
    if (!(inputFlags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) || !(outputFlags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT))
        return Fail(e, "Video processor input/output format unsupported", -1);
    if (e->textureFormat == DXGI_FORMAT_NV12) {
        ID3D11VideoProcessorEnumerator1* extended = 0;
        HR_CHECK(e->videoEnumerator->QueryInterface(__uuidof(ID3D11VideoProcessorEnumerator1), (void**)&extended));
        BOOL supported = FALSE;
        HRESULT conversion = extended->CheckVideoProcessorFormatConversion(DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709, DXGI_FORMAT_NV12, DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709, &supported);
        extended->Release();
        if (FAILED(conversion) || !supported) return Fail(e, "Full RGB709 to studio NV12 709 conversion unsupported", FAILED(conversion) ? conversion : -1);
    }
    D3D11_VIDEO_PROCESSOR_CAPS caps = {};
    HR_CHECK(e->videoEnumerator->GetVideoProcessorCaps(&caps));
    if (!caps.RateConversionCapsCount || !caps.MaxInputStreams) return Fail(e, "Video processor capabilities", -1);
    HR_CHECK(e->videoDevice->CreateVideoProcessor(e->videoEnumerator, 0, &e->processor));
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output = {};
    output.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    HR_CHECK(e->videoDevice->CreateVideoProcessorOutputView(e->texture, e->videoEnumerator, &output, &e->outputView));
    RECT source = { 0, 0, (LONG)sourceWidth, (LONG)sourceHeight };
    RECT destination = { 0, 0, (LONG)e->width, (LONG)e->height };
    e->videoContext->VideoProcessorSetStreamFrameFormat(e->processor, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    e->videoContext->VideoProcessorSetStreamAutoProcessingMode(e->processor, 0, FALSE);
    e->videoContext->VideoProcessorSetStreamSourceRect(e->processor, 0, TRUE, &source);
    e->videoContext->VideoProcessorSetStreamDestRect(e->processor, 0, TRUE, &destination);
    e->videoContext->VideoProcessorSetOutputTargetRect(e->processor, TRUE, &destination);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE rgb = {};
    rgb.Usage = 1; rgb.RGB_Range = 0; rgb.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255;
    e->videoContext->VideoProcessorSetStreamColorSpace(e->processor, 0, &rgb);
    e->videoContext->VideoProcessorSetOutputColorSpace(e->processor, &rgb);
    if (e->textureFormat == DXGI_FORMAT_NV12) {
        HR_CHECK(e->videoContext->QueryInterface(__uuidof(ID3D11VideoContext1), (void**)&e->videoContext1));
        e->videoContext1->VideoProcessorSetStreamColorSpace1(e->processor, 0, DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
        e->videoContext1->VideoProcessorSetOutputColorSpace1(e->processor, DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709);
    }
    e->sourceWidth = sourceWidth; e->sourceHeight = sourceHeight;
    return 0;
}
static int CopySource(Encoder* e, ID3D11Texture2D* source) {
    TRACE(e,"copy-begin"); HRESULT hr = S_OK;
    D3D11_TEXTURE2D_DESC td; source->GetDesc(&td);
    if (td.Width != e->sourceWidth || td.Height != e->sourceHeight || td.Format != DXGI_FORMAT_B8G8R8A8_UNORM) {
        source->Release(); return Fail(e, "Unexpected WGC texture dimensions/format", -1);
    }
    if (e->processor) {
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC desc = {};
        desc.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
        ID3D11VideoProcessorInputView* input = 0;
        hr = e->videoDevice->CreateVideoProcessorInputView(source, e->videoEnumerator, &desc, &input);
        if (FAILED(hr)) { source->Release(); return Fail(e, "CreateVideoProcessorInputView", hr); }
        D3D11_VIDEO_PROCESSOR_STREAM stream = {};
        stream.Enable = TRUE; stream.pInputSurface = input;
        hr = e->videoContext->VideoProcessorBlt(e->processor, e->outputView, 0, 1, &stream);
        input->Release();
        if (FAILED(hr)) { source->Release(); return Fail(e, "VideoProcessorBlt", hr); }
    } else {
        e->context->CopyResource(e->texture, source);
    }

    TRACE(e,"copy-submitted"); bool eventCompleted=false;
    if(e->completionEvent&&!e->eventDisabled){
        // Clear a previous signal before associating this event with the next monotonic value.
        // Any prior fallback query has already completed all work preceding its End.
        if(!ResetEvent(e->completionEvent))hr=HRESULT_FROM_WIN32(GetLastError());
        else{
            UINT64 value=++e->fenceValue;
            hr=e->eventContext->Signal(e->eventFence,value);
            if(SUCCEEDED(hr)){
                hr=e->eventFence->SetEventOnCompletion(value,e->completionEvent);
            }
            if(SUCCEEDED(hr)){
                e->context->Flush();
                TRACE(e,"event-wait-begin");DWORD result=WaitForSingleObject(e->completionEvent,3000);TRACE(e,"event-wait-end");
                if(result==WAIT_OBJECT_0){eventCompleted=true;}
            }
        }
        if(!eventCompleted){e->eventDisabled=true;}
    }
    if(!eventCompleted){
        // Preserve source ownership even if modern fence setup/wait fails. The query
        // is ordered after conversion and any submitted fence work; only successful
        // completion or a reported device error permits returning from this call.
        e->context->End(e->fence);e->context->Flush();DWORD start=GetTickCount();
        while((hr=e->context->GetData(e->fence,0,0,0))==S_FALSE){

            if(GetTickCount()-start>3000){source->Release();return Fail(e,"GPU copy timeout",-1);}
            SwitchToThread();
        }
    }
    source->Release();
    if(FAILED(hr))return Fail(e,"GPU copy completion",hr);
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl nc_copy_surface(Encoder* e, IUnknown* surface) {
    IDirect3DDxgiInterfaceAccess* access = 0;
    HR_CHECK(surface->QueryInterface(__uuidof(IDirect3DDxgiInterfaceAccess), (void**)&access));
    ID3D11Texture2D* source = 0;
    HRESULT hr = access->GetInterface(__uuidof(ID3D11Texture2D), (void**)&source);
    access->Release();
    if (FAILED(hr)) return Fail(e, "GetInterface texture", hr);
    return CopySource(e, source);
}
extern "C" __declspec(dllexport) int __cdecl nc_copy_texture(Encoder* e, ID3D11Texture2D* source) {
    source->AddRef(); return CopySource(e, source);
}
extern "C" __declspec(dllexport) int __cdecl nc_encode(Encoder* e, unsigned long long timestamp) {
    TRACE(e,"encode-map");NV_ENC_MAP_INPUT_RESOURCE map = {}; map.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
    map.registeredResource = e->registered;
    NC_CHECK(e->api.nvEncMapInputResource(e->session, &map));
    NV_ENC_PIC_PARAMS pic = {}; pic.version = NV_ENC_PIC_PARAMS_VER;
    pic.inputBuffer = map.mappedResource; pic.bufferFmt = map.mappedBufferFmt;
    pic.inputWidth = e->width; pic.inputHeight = e->height; pic.outputBitstream = e->bitstream;
    pic.pictureStruct = NV_ENC_PIC_STRUCT_FRAME; pic.inputTimeStamp = timestamp;
    pic.inputDuration = 10000000 / e->fps;
    if (e->frames % (e->fps * 2) == 0) pic.encodePicFlags = NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS;
    TRACE(e,"encode-submit");NVENCSTATUS status = e->api.nvEncEncodePicture(e->session, &pic);
    if (status != NV_ENC_SUCCESS) { e->api.nvEncUnmapInputResource(e->session, map.mappedResource); return Fail(e, "nvEncEncodePicture", status); }
    NV_ENC_LOCK_BITSTREAM lock = {}; lock.version = NV_ENC_LOCK_BITSTREAM_VER;
    lock.outputBitstream = e->bitstream; lock.doNotWait = 0;
    TRACE(e,"encode-lock-begin");status = e->api.nvEncLockBitstream(e->session, &lock);TRACE(e,"encode-lock-end");
    if (status == NV_ENC_SUCCESS) {
        DWORD written = lock.bitstreamSizeInBytes;
        BOOL ok = TRUE;
        if (e->file) ok = WriteFile(e->file, lock.bitstreamBufferPtr, lock.bitstreamSizeInBytes, &written, 0);
        unsigned key = lock.pictureType == NV_ENC_PIC_TYPE_IDR;
        int callbackResult = e->callback ? e->callback(e->callbackUser, lock.bitstreamBufferPtr,
            lock.bitstreamSizeInBytes, e->frames, key) : 0;
        e->bytes += written; e->frames++;
        e->api.nvEncUnlockBitstream(e->session, e->bitstream);
        e->api.nvEncUnmapInputResource(e->session, map.mappedResource);
        if (!ok || written != lock.bitstreamSizeInBytes) return Fail(e, "WriteFile", GetLastError());
        if (callbackResult) return Fail(e, "managed packet callback failed", callbackResult);
        return 0;
    }
    e->api.nvEncUnmapInputResource(e->session, map.mappedResource);
    return Fail(e, "nvEncLockBitstream", status);
}
extern "C" __declspec(dllexport) int __cdecl nc_flush(Encoder* e) {
    NV_ENC_PIC_PARAMS pic = {}; pic.version = NV_ENC_PIC_PARAMS_VER; pic.encodePicFlags = NV_ENC_PIC_FLAG_EOS;
    NC_CHECK(e->api.nvEncEncodePicture(e->session, &pic));
    if (e->file) FlushFileBuffers(e->file);
    return 0;
}



extern "C" __declspec(dllexport) void __cdecl nc_set_callback(Encoder* e, PacketCallback callback, void* user) {
    e->callback = callback; e->callbackUser = user;
}







extern "C" __declspec(dllexport) int __cdecl nc_create(ID3D11Device* device, unsigned width, unsigned height,
    unsigned fps, unsigned qp, const wchar_t* output, Encoder** result) {
    return CreateEncoder(device, width, height, fps, qp, output, result, false);
}
extern "C" __declspec(dllexport) int __cdecl nc_create_format(ID3D11Device* device, unsigned width, unsigned height,
    unsigned fps, unsigned qp, const wchar_t* output, unsigned nv12, Encoder** result) {
    return CreateEncoder(device, width, height, fps, qp, output, result, nv12 != 0);
}






