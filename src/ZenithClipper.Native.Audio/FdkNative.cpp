// SPDX-License-Identifier: MIT
// Isolated adapter. FDK source remains unmodified under its preserved NOTICE license.
#include <windows.h>
#include <stdint.h>
#include <new>
#include "aacenc_lib.h"

using Emit=int(__cdecl*)(void*,const uint8_t*,uint32_t,int64_t,uint32_t);
struct FdkInfo { uint32_t delay,coreDelay,frameLength,maxOutBytes,ascSize;uint8_t asc[64]; };
struct Encoder {
 HANDLE_AACENCODER handle=nullptr;AACENC_InfoStruct info={};uint8_t* output=nullptr;
 Emit emit=nullptr;void* context=nullptr;DWORD owner=0;bool started=false,drained=false,failed=false;
 int64_t nextInput=0,nextPacket=0;
 ~Encoder(){delete[] output;if(handle)aacEncClose(&handle);}
};
static int Init(Encoder* e){
 int status=aacEncOpen(&e->handle,0x01,2);if(status)return status;
 #define SET(p,v) do{status=aacEncoder_SetParam(e->handle,p,v);if(status)return status;}while(0)
 SET(AACENC_AOT,2);SET(AACENC_SAMPLERATE,48000);SET(AACENC_CHANNELMODE,MODE_2);SET(AACENC_CHANNELORDER,1);
 SET(AACENC_BITRATE,192000);SET(AACENC_BITRATEMODE,0);SET(AACENC_TRANSMUX,TT_MP4_RAW);SET(AACENC_AFTERBURNER,1);
 status=aacEncEncode(e->handle,nullptr,nullptr,nullptr,nullptr);if(status)return status;
 status=aacEncInfo(e->handle,&e->info);if(status)return status;
 if(e->info.inputChannels!=2||e->info.frameLength!=1024||e->info.maxOutBufBytes>65536||e->info.confSize!=2||e->info.confBuf[0]!=0x11||e->info.confBuf[1]!=0x90)return -2;
 e->output=new(std::nothrow) uint8_t[e->info.maxOutBufBytes];return e->output?0:-3;
}
extern "C" __declspec(dllexport) int __cdecl fdk_aac_create(Emit emit,void* context,void** result,FdkInfo* info){
 if(!emit||!result||!info)return -1;*result=nullptr;
 Encoder* e=new(std::nothrow) Encoder();if(!e)return -3;e->emit=emit;e->context=context;e->owner=GetCurrentThreadId();
 int status=Init(e);if(status){delete e;return status;}
 info->delay=e->info.nDelay;info->coreDelay=e->info.nDelayCore;info->frameLength=e->info.frameLength;info->maxOutBytes=e->info.maxOutBufBytes;
 info->ascSize=e->info.confSize;memcpy(info->asc,e->info.confBuf,e->info.confSize);*result=e;return 0;
}
static int Process(Encoder* e,const int16_t* pcm,int samples,bool flush,int* consumed,bool* eof){
 void* in=(void*)pcm;int inId=IN_AUDIO_DATA,inSize=samples>0?samples*2:0,inElement=2;
 void* out=e->output;int outId=OUT_BITSTREAM_DATA,outSize=(int)e->info.maxOutBufBytes,outElement=1;
 AACENC_BufDesc input={1,&in,&inId,&inSize,&inElement},output={1,&out,&outId,&outSize,&outElement};
 AACENC_InArgs args={};AACENC_OutArgs produced={};args.numInSamples=flush?-1:samples;
 int status=aacEncEncode(e->handle,&input,&output,&args,&produced);
 if(status!=AACENC_OK&&status!=AACENC_ENCODE_EOF)return status;
 if(produced.numOutBytes<0||(uint32_t)produced.numOutBytes>e->info.maxOutBufBytes)return -4;
 if(produced.numOutBytes){int callback=e->emit(e->context,e->output,(uint32_t)produced.numOutBytes,e->nextPacket,1024);if(callback)return callback;e->nextPacket+=1024;}
 *consumed=produced.numInSamples;*eof=status==AACENC_ENCODE_EOF;
 if(!flush&&(*consumed<0||*consumed>samples||(*consumed&1)))return -4;
 if(!flush&&*consumed==0&&produced.numOutBytes==0)return -4;
 return 0;
}
extern "C" __declspec(dllexport) int __cdecl fdk_aac_encode(void* handle,const int16_t* pcm,uint32_t frames,int64_t index){
 auto e=(Encoder*)handle;if(!e||(!pcm&&frames)||frames>48000||index<0||index>INT64_MAX-48000)return -1;
 if(e->owner!=GetCurrentThreadId()||e->failed||e->drained)return -5;
 if(!e->started){e->started=true;e->nextInput=index;e->nextPacket=index;}
 if(index!=e->nextInput)return -1;
 int samples=(int)frames*2;
 while(samples){int consumed=0;bool eof=false;int status=Process(e,pcm,samples,false,&consumed,&eof);if(status){e->failed=true;return status;}samples-=consumed;pcm+=consumed;e->nextInput+=consumed/2;}
 return 0;
}
extern "C" __declspec(dllexport) int __cdecl fdk_aac_drain(void* handle){
 auto e=(Encoder*)handle;if(!e||e->owner!=GetCurrentThreadId()||e->failed)return -5;if(e->drained)return 0;e->drained=true;
 for(int i=0;i<64;i++){int consumed=0;bool eof=false;int status=Process(e,nullptr,0,true,&consumed,&eof);if(status){e->failed=true;return status;}if(eof)return 0;}
 e->failed=true;return -4;
}
extern "C" __declspec(dllexport) int __cdecl fdk_aac_destroy(void* handle){auto e=(Encoder*)handle;if(!e)return 0;if(e->owner!=GetCurrentThreadId())return -5;delete e;return 0;}
