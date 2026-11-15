#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dwmapi.h>
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

static unsigned* pixels;
static BITMAPINFO image;
static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_PAINT) {
        PAINTSTRUCT paint; HDC dc = BeginPaint(window, &paint);
        SetDIBitsToDevice(dc, 0, 0, W, H, 0, 0, 0, H, pixels, &image, DIB_RGB_COLORS);
        EndPaint(window, &paint); return 0;
    }
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    if (message == WM_ERASEBKGND) return 1;
    return DefWindowProcW(window, message, w, l);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    WriteMetrics(0, 0, 0xdead0001, 0);
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    pixels = (unsigned*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, W * H * 4);
    if (!pixels) return 2;
    image.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    image.bmiHeader.biWidth = W; image.bmiHeader.biHeight = -H;
    image.bmiHeader.biPlanes = 1; image.bmiHeader.biBitCount = 32; image.bmiHeader.biCompression = BI_RGB;
    WNDCLASSW type = {}; type.lpfnWndProc = WindowProc; type.hInstance = instance; type.lpszClassName = L"ZenithOwnedMotionTest";
    if (!RegisterClassW(&type)) return 3;
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        type.lpszClassName, L"Zenith controlled capture motion test", WS_POPUP | WS_BORDER, 40, 40, W, H, 0, 0, instance, 0);
    if (!window) return 4;
    WriteMetrics(0, 0, 0xdead0002, 0);
    SetWindowPos(window, HWND_TOPMOST, 40, 40, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    WriteMetrics(0, 0, IsWindowVisible(window) ? 0xdead0003 : 0xdead0004, 0);
    LARGE_INTEGER frequency, start, now; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    unsigned frames = 0; double maxGapMs = 0; LARGE_INTEGER previous = start;
    bool running = true;
    while (running) {
        MSG message;
        while (PeekMessageW(&message, 0, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) running = false;
            TranslateMessage(&message); DispatchMessageW(&message);
        }
        QueryPerformanceCounter(&now);
        if (!running || double(now.QuadPart - start.QuadPart) / frequency.QuadPart >= 8.0) break;
        unsigned phase = frames * 7;
        for (unsigned y = 0; y < H; y++) for (unsigned x = 0; x < W; x++) {
            if (y < H / 2) {
                static const unsigned bars[8] = { 0x000000, 0xff0000, 0x00ff00, 0x0000ff, 0x00ffff, 0xff00ff, 0xffff00, 0xffffff };
                pixels[y * W + x] = bars[x / (W / 8)]; continue;
            }
            unsigned r = (x + phase) & 255, g = (y * 2 + phase * 3) & 255;
            unsigned b = ((x ^ y) + phase * 5) & 255;
            pixels[y * W + x] = (r << 16) | (g << 8) | b;
        }
        InvalidateRect(window, 0, FALSE); UpdateWindow(window);
        frames++;
        // Synchronize this owned window's animation to DWM composition; no display/system settings change.
        HRESULT status = DwmFlush();
        QueryPerformanceCounter(&now);
        double gap = 1000.0 * (now.QuadPart - previous.QuadPart) / frequency.QuadPart;
        if (frames > 1 && gap > maxGapMs) maxGapMs = gap; previous = now;
        if (frames % 100 == 0) WriteMetrics(frames, double(now.QuadPart - start.QuadPart) / frequency.QuadPart, status, maxGapMs);
    }
    QueryPerformanceCounter(&now);
    WriteMetrics(frames, double(now.QuadPart - start.QuadPart) / frequency.QuadPart, 0, maxGapMs);
    DestroyWindow(window); HeapFree(GetProcessHeap(), 0, pixels);
    return frames ? 0 : 5;
}






