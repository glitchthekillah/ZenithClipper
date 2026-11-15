#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <stdint.h>
#include <stddef.h>

struct WgcConfig {
    HMONITOR monitor;
    uint32_t sourceWidth, sourceHeight, width, height, fps, qp, buffers, nv12;
    const wchar_t* output;
};
// Shared only while the native call is active. Interlocked access on both sides.
struct WgcState {
    // Bit 1 releases the non-GPU timeout test; bit 2 requests an IDR on the encoder worker.
    volatile LONG stopping, controlFlags;
    volatile LONG64 encoded, captured, repeated, missed, bytes, elapsedQpc;
    volatile LONG fakeAlive, fakeDestroyed;
};
static_assert(sizeof(void*)==8 && sizeof(WgcConfig)==48, "Native WGC bridge requires x64 ABI.");
static_assert(sizeof(WgcState)==64 && offsetof(WgcState,encoded)==8 && offsetof(WgcState,fakeAlive)==56, "Unexpected WGC state layout.");
using WgcPacket = int(__cdecl*)(void*, const void*, unsigned, unsigned long long, unsigned);
// Event 1: first source frame copied; event 2: encoder flushed, before native COM teardown.
using WgcEvent = int(__cdecl*)(void*, unsigned);
extern "C" __declspec(dllexport) int __cdecl wgc_run(const WgcConfig*, WgcState*, WgcPacket, WgcEvent, void*, char*, unsigned);
// Non-GPU lifetime/fault harness. It uses the identical owning lifetime helper and callbacks.
extern "C" __declspec(dllexport) int __cdecl wgc_run_test(const WgcConfig*, WgcState*, WgcPacket, WgcEvent, void*, char*, unsigned, unsigned);
