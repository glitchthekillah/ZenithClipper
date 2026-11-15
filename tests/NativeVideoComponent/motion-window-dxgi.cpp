#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_3.h>
#include <stdio.h>
#include <wchar.h>
static const int W = 640, H = 360;
static void WriteMetrics(unsigned frames, double seconds, unsigned status, double maxGapMs) {
    wchar_t path[MAX_PATH]; GetModuleFileNameW(0, path, MAX_PATH);
    wchar_t* name = wcsrchr(path, 92); if (!name) return;
    wcscpy_s(name + 1, MAX_PATH - (name + 1 - path), L"motion-window-metrics.txt");
    HANDLE log = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
    if (log != INVALID_HANDLE_VALUE) {
        char summary[256]; int length = sprintf_s(summary, "frames=%u seconds=%.6f fps=%.3f maxGapMs=%.3f status=0x%08x\n", frames,
            seconds, seconds > 0 ? frames / seconds : 0, maxGapMs, status);
        DWORD written; WriteFile(log, summary, length, &written, 0); CloseHandle(log);
    }
}
static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_PAINT) { PAINTSTRUCT paint; BeginPaint(window, &paint); EndPaint(window, &paint); return 0; }
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    if (message == WM_ERASEBKGND) return 1;
    return DefWindowProcW(window, message, w, l);
}
struct Scene {
    ID3D11Device* device = 0;
    ID3D11DeviceContext* context = 0;
    ID3D11DeviceContext1* context1 = 0;
    IDXGIFactory2* factory = 0;
    IDXGISwapChain1* chain = 0;
    IDXGISwapChain2* chain2 = 0;
    ID3D11RenderTargetView* target = 0;
    ~Scene() {
        if (target) target->Release(); if (chain2) chain2->Release(); if (chain) chain->Release();
        if (factory) factory->Release(); if (context1) context1->Release(); if (context) context->Release(); if (device) device->Release();
    }
    HRESULT Initialize(HWND window) {
        HRESULT hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_HARDWARE, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0,
            D3D11_SDK_VERSION, &device, 0, &context);
        if (FAILED(hr)) return hr;
        if (FAILED(hr = context->QueryInterface(__uuidof(ID3D11DeviceContext1), (void**)&context1))) return hr;
        if (FAILED(hr = CreateDXGIFactory1(__uuidof(IDXGIFactory2), (void**)&factory))) return hr;
        DXGI_SWAP_CHAIN_DESC1 desc = {};
        desc.Width = W; desc.Height = H; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount = 2;
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD; desc.Scaling = DXGI_SCALING_STRETCH; desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        desc.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
        if (FAILED(hr = factory->CreateSwapChainForHwnd(device, window, &desc, 0, 0, &chain))) return hr;
        if (FAILED(hr = chain->QueryInterface(__uuidof(IDXGISwapChain2), (void**)&chain2))) return hr;
        if (FAILED(hr = chain2->SetMaximumFrameLatency(1))) return hr;
        ID3D11Texture2D* back = 0;
        if (FAILED(hr = chain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back))) return hr;
        hr = device->CreateRenderTargetView(back, 0, &target); back->Release(); return hr;
    }
    HRESULT Paint(unsigned frame) {
        float color[4] = { float((frame * 7) & 255) / 255.f, float((frame * 17) & 255) / 255.f, float((frame * 31) & 255) / 255.f, 1.f };
        context1->ClearView(target, color, 0, 0);
        static const float bars[8][4] = { {0,0,0,1},{1,0,0,1},{0,1,0,1},{0,0,1,1},{0,1,1,1},{1,0,1,1},{1,1,0,1},{1,1,1,1} };
        for (int i = 0; i < 8; i++) { D3D11_RECT r = { i * 80, 0, (i + 1) * 80, H / 2 }; context1->ClearView(target, bars[i], &r, 1); }
        int x = (frame * 7) % (W - 80); D3D11_RECT moving = { x, H / 2 + 20, x + 80, H - 20 };
        const float white[4] = { 1,1,1,1 }; context1->ClearView(target, white, &moving, 1);
        return chain->Present(1, 0);
    }
};
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW type = {}; type.lpfnWndProc = WindowProc; type.hInstance = instance; type.lpszClassName = L"ZenithOwnedDxgiMotionTest";
    if (!RegisterClassW(&type)) return 3;
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        type.lpszClassName, L"Zenith controlled DXGI capture test", WS_POPUP, 40, 40, W, H, 0, 0, instance, 0);
    if (!window) return 4;
    SetWindowPos(window, HWND_TOPMOST, 40, 40, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    unsigned frames = 0; HRESULT hr; double maxGapMs = 0;
    LARGE_INTEGER frequency, start, now; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    {
        Scene scene; hr = scene.Initialize(window);
        QueryPerformanceCounter(&start);
        LARGE_INTEGER previous = start;
        bool running = SUCCEEDED(hr);
        while (running) {
            MSG message;
            while (PeekMessageW(&message, 0, 0, 0, PM_REMOVE)) {
                if (message.message == WM_QUIT) running = false;
                TranslateMessage(&message); DispatchMessageW(&message);
            }
            QueryPerformanceCounter(&now);
            if (!running || double(now.QuadPart - start.QuadPart) / frequency.QuadPart >= 8.0) break;
            hr = scene.Paint(frames); if (FAILED(hr)) break;
            frames++;
            QueryPerformanceCounter(&now);
            double gap = 1000.0 * (now.QuadPart - previous.QuadPart) / frequency.QuadPart;
            if (frames > 1 && gap > maxGapMs) maxGapMs = gap; previous = now;
            if (frames % 100 == 0) WriteMetrics(frames, double(now.QuadPart - start.QuadPart) / frequency.QuadPart, hr, maxGapMs);
        }
    }
    QueryPerformanceCounter(&now);
    DestroyWindow(window);
    return FAILED(hr) ? 5 : frames ? 0 : 6;
}

