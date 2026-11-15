#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <wmcodecdsp.h>
#include <stdint.h>
#include <new>
#pragma comment(lib,"mfplat.lib")
#pragma comment(lib,"mfuuid.lib")
#pragma comment(lib,"wmcodecdspuuid.lib")
#pragma comment(lib,"ole32.lib")

template<class T> struct Ptr {
 T* p=nullptr; ~Ptr(){if(p)p->Release();} T* operator->(){return p;}
 T** out(){return &p;} void reset(){if(p){p->Release();p=nullptr;}}
};
using Emit = int(__cdecl*)(void*, const uint8_t*, uint32_t, int64_t, uint32_t);
struct Encoder {
 Ptr<IMFTransform> mft; Ptr<IMFSample> output; Ptr<IMFMediaBuffer> buffer;
 Emit emit=nullptr; void* user=nullptr; DWORD thread=0; bool co=false,mf=false,drained=false,failed=false;
 int64_t nextInput=0,nextOutput=0; bool started=false;
 ~Encoder(){buffer.reset();output.reset();mft.reset();if(mf)MFShutdown();if(co)CoUninitialize();}
};
#define TRY(x) do { HRESULT h_=(x); if(FAILED(h_)) return h_; } while(0)
static HRESULT MediaType(IMFMediaType** result, bool output){
 Ptr<IMFMediaType> t; TRY(MFCreateMediaType(t.out()));
 TRY(t->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));
 TRY(t->SetGUID(MF_MT_SUBTYPE,output?MFAudioFormat_AAC:MFAudioFormat_PCM));
 TRY(t->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS,2)); TRY(t->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND,48000));
 TRY(t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16));
 if(output){TRY(t->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND,24000));TRY(t->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE,0));TRY(t->SetUINT32(MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION,0x29));}
 else {TRY(t->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT,4));TRY(t->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND,192000));}
 *result=t.p;t.p=nullptr;return S_OK;
}
static HRESULT Pump(Encoder* e){
 for(int guard=0;guard<4096;guard++){
  TRY(e->buffer->SetCurrentLength(0)); MFT_OUTPUT_DATA_BUFFER out={};out.pSample=e->output.p;DWORD status=0;
  HRESULT hr=e->mft->ProcessOutput(0,1,&out,&status);if(out.pEvents)out.pEvents->Release();
  if(hr==MF_E_TRANSFORM_NEED_MORE_INPUT)return S_OK;
  if(FAILED(hr))return hr;
  if(out.pSample!=e->output.p){if(out.pSample)out.pSample->Release();return E_UNEXPECTED;}
  BYTE* bytes=nullptr;DWORD count=0;TRY(e->buffer->Lock(&bytes,nullptr,&count));
  LONGLONG time=0;HRESULT timed=e->output->GetSampleTime(&time);
  int64_t index=(time/10000000)*48000+((time%10000000)*48000+(time>=0?5000000:-5000000))/10000000;
  HRESULT emitted=FAILED(timed)?timed:(count?e->emit(e->user,bytes,count,index,1024):E_UNEXPECTED);
  HRESULT unlock=e->buffer->Unlock();if(FAILED(emitted))return emitted;TRY(unlock);
  e->nextOutput+=1024;
 }
 return E_UNEXPECTED;
}
static HRESULT Create(Encoder* e){
 HRESULT co=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(co))return co;e->co=true;
 TRY(MFStartup(MF_VERSION,MFSTARTUP_NOSOCKET));e->mf=true;
 TRY(CoCreateInstance(CLSID_AACMFTEncoder,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(e->mft.out())));
 Ptr<IMFMediaType> input,output;TRY(MediaType(output.out(),true));TRY(e->mft->SetOutputType(0,output.p,0));
 TRY(MediaType(input.out(),false));TRY(e->mft->SetInputType(0,input.p,0));
 MFT_OUTPUT_STREAM_INFO info={};TRY(e->mft->GetOutputStreamInfo(0,&info));
 if((info.dwFlags&MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)||info.cbSize>65536)return E_NOTIMPL;
 TRY(MFCreateSample(e->output.out()));TRY(MFCreateMemoryBuffer(info.cbSize>8192?info.cbSize:8192,e->buffer.out()));
 TRY(e->output->AddBuffer(e->buffer.p));
 TRY(e->mft->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,0));
 TRY(e->mft->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM,0));return S_OK;
}
extern "C" __declspec(dllexport) int __cdecl zc_aac_create(Emit emit,void* user,void** handle){
 if(!emit||!handle)return E_POINTER;*handle=nullptr;
 Encoder* e=new(std::nothrow) Encoder();if(!e)return E_OUTOFMEMORY;e->emit=emit;e->user=user;e->thread=GetCurrentThreadId();
 HRESULT hr=Create(e);if(FAILED(hr)){delete e;return hr;}*handle=e;return S_OK;
}
static HRESULT Encode(Encoder* e,const uint8_t* pcm,uint32_t frames,int64_t index){
 if(!e->started){e->started=true;e->nextInput=index;e->nextOutput=index;}
 if(index!=e->nextInput || index<0 || frames>48000)return E_INVALIDARG;
 while(frames){
  uint32_t n=frames>1024?1024:frames;Ptr<IMFSample> sample;Ptr<IMFMediaBuffer> buffer;
  TRY(MFCreateSample(sample.out()));TRY(MFCreateMemoryBuffer(n*4,buffer.out()));
  BYTE* data=nullptr;TRY(buffer->Lock(&data,nullptr,nullptr));memcpy(data,pcm,n*4);TRY(buffer->Unlock());
  TRY(buffer->SetCurrentLength(n*4));TRY(sample->AddBuffer(buffer.p));
  TRY(sample->SetSampleTime(e->nextInput*10000000/48000));TRY(sample->SetSampleDuration((int64_t)n*10000000/48000));
  HRESULT hr=e->mft->ProcessInput(0,sample.p,0);
  if(hr==MF_E_NOTACCEPTING){TRY(Pump(e));hr=e->mft->ProcessInput(0,sample.p,0);}TRY(hr);
  e->nextInput+=n;frames-=n;pcm+=n*4;TRY(Pump(e));
 }
 return S_OK;
}
extern "C" __declspec(dllexport) int __cdecl zc_aac_encode(void* handle,const uint8_t* pcm,uint32_t frames,int64_t index){
 auto e=(Encoder*)handle;if(!e||(!pcm&&frames))return E_POINTER;
 if(e->thread!=GetCurrentThreadId()||e->drained||e->failed)return E_UNEXPECTED;
 HRESULT hr=Encode(e,pcm,frames,index);if(FAILED(hr))e->failed=true;return hr;
}
extern "C" __declspec(dllexport) int __cdecl zc_aac_drain(void* handle){
 auto e=(Encoder*)handle;if(!e)return E_POINTER;if(e->thread!=GetCurrentThreadId()||e->failed)return E_UNEXPECTED;
 if(e->drained)return S_OK;e->drained=true;HRESULT hr=e->mft->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM,0);
 if(SUCCEEDED(hr))hr=e->mft->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN,0);
 if(SUCCEEDED(hr))hr=Pump(e);if(FAILED(hr))e->failed=true;return hr;
}
extern "C" __declspec(dllexport) int __cdecl zc_aac_destroy(void* handle){
 auto e=(Encoder*)handle;if(!e)return S_OK;if(e->thread!=GetCurrentThreadId())return E_UNEXPECTED;delete e;return S_OK;
}

