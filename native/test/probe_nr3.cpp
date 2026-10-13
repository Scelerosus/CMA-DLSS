// A probe: neural rendering by loading nvngx_dlssnr.dll itself and calling its own Direct3D 12 entry points.
// Arguments: the folder with nvngx_dlss.dll (for the core), the full path of nvngx_dlssnr.dll, [feature id].
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <cstdio>
#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"

typedef NVSDK_NGX_Result (NVSDK_CONV *InitExtFn)(unsigned long long, const wchar_t*, ID3D12Device*, NVSDK_NGX_Version, const NVSDK_NGX_Parameter*);
typedef NVSDK_NGX_Result (NVSDK_CONV *PopulateFn)(NVSDK_NGX_Parameter*);
typedef NVSDK_NGX_Result (NVSDK_CONV *CreateFn)(ID3D12GraphicsCommandList*, NVSDK_NGX_Feature, const NVSDK_NGX_Parameter*, NVSDK_NGX_Handle**);
typedef NVSDK_NGX_Result (NVSDK_CONV *EvaluateFn)(ID3D12GraphicsCommandList*, const NVSDK_NGX_Handle*, const NVSDK_NGX_Parameter*, PFN_NVSDK_NGX_ProgressCallback);
typedef NVSDK_NGX_Result (NVSDK_CONV *ReleaseFn)(NVSDK_NGX_Handle*);
typedef NVSDK_NGX_Result (NVSDK_CONV *ShutdownFn)(ID3D12Device*);

static InitExtFn s_init; static PopulateFn s_populate; static CreateFn s_create; static EvaluateFn s_evaluate; static ReleaseFn s_release; static ShutdownFn s_shutdown;
static ID3D12Device* g_dev; static ID3D12CommandQueue* g_queue; static ID3D12CommandAllocator* g_alloc; static ID3D12GraphicsCommandList* g_list; static ID3D12Fence* g_fence; static UINT64 g_fenceValue; static HANDLE g_event;

static void Run()
{
    g_list->Close();
    ID3D12CommandList* lists[1] = { g_list };
    g_queue->ExecuteCommandLists(1, lists);
    g_queue->Signal(g_fence, ++g_fenceValue);
    g_fence->SetEventOnCompletion(g_fenceValue, g_event);
    WaitForSingleObject(g_event, 20000);
    g_alloc->Reset();
    g_list->Reset(g_alloc, nullptr);
}

static ID3D12Resource* Make(unsigned w, unsigned h, DXGI_FORMAT format, bool uav, D3D12_RESOURCE_STATES state)
{
    D3D12_HEAP_PROPERTIES heap = {}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    D3D12_RESOURCE_DESC d = {};
    d.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D; d.Width = w; d.Height = h; d.DepthOrArraySize = 1; d.MipLevels = 1; d.Format = format; d.SampleDesc.Count = 1;
    d.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET | (uav ? D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS : D3D12_RESOURCE_FLAG_NONE);
    ID3D12Resource* r = nullptr;
    HRESULT hr = g_dev->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &d, state, nullptr, __uuidof(ID3D12Resource), (void**)&r);
    if (FAILED(hr)) printf("texture format %d failed 0x%08X\n", (int)format, (unsigned)hr);
    return r;
}

// The mean of an 8-bit texture, read back through a buffer.
static void Mean(ID3D12Resource* tex, D3D12_RESOURCE_STATES state, const char* name)
{
    D3D12_RESOURCE_DESC d = tex->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT fp; UINT rows; UINT64 rowSize, total;
    g_dev->GetCopyableFootprints(&d, 0, 1, 0, &fp, &rows, &rowSize, &total);
    D3D12_HEAP_PROPERTIES heap = {}; heap.Type = D3D12_HEAP_TYPE_READBACK;
    D3D12_RESOURCE_DESC b = {}; b.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; b.Width = total; b.Height = 1; b.DepthOrArraySize = 1; b.MipLevels = 1; b.SampleDesc.Count = 1; b.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    ID3D12Resource* buf = nullptr;
    if (FAILED(g_dev->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &b, D3D12_RESOURCE_STATE_COPY_DEST, nullptr, __uuidof(ID3D12Resource), (void**)&buf))) return;
    D3D12_RESOURCE_BARRIER bar = {}; bar.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION; bar.Transition.pResource = tex; bar.Transition.Subresource = 0;
    bar.Transition.StateBefore = state; bar.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
    g_list->ResourceBarrier(1, &bar);
    D3D12_TEXTURE_COPY_LOCATION src = {}; src.pResource = tex; src.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    D3D12_TEXTURE_COPY_LOCATION dst = {}; dst.pResource = buf; dst.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT; dst.PlacedFootprint = fp;
    g_list->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);
    bar.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_SOURCE; bar.Transition.StateAfter = state;
    g_list->ResourceBarrier(1, &bar);
    Run();
    void* data = nullptr;
    if (SUCCEEDED(buf->Map(0, nullptr, &data)))
    {
        double sum[4] = {}; unsigned n = 0;
        for (unsigned y = 0; y < d.Height; y += 5)
        {
            const unsigned char* row = (const unsigned char*)data + (size_t)y * fp.Footprint.RowPitch;
            for (unsigned x = 0; x < d.Width; x += 5) { for (int c = 0; c < 4; c++) sum[c] += row[x * 4 + c]; n++; }
        }
        printf("%s: mean r %.1f g %.1f b %.1f a %.1f\n", name, sum[0] / n, sum[1] / n, sum[2] / n, sum[3] / n);
        buf->Unmap(0, nullptr);
    }
    buf->Release();
}

static NVSDK_NGX_Result Guard(int what, int feature, NVSDK_NGX_Parameter* p, NVSDK_NGX_Handle** h, const wchar_t* path)
{
    __try
    {
        switch (what)
        {
        case 0: return s_init(0x876232C, path, g_dev, NVSDK_NGX_Version_API, p);
        case 1: return s_populate(p);
        case 2: return s_create(g_list, (NVSDK_NGX_Feature)feature, p, h);
        case 3: return s_evaluate(g_list, *h, p, nullptr);
        case 4: return s_release(*h);
        default: return s_shutdown(g_dev);
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        printf("  (fault inside the DLL at step %d, code 0x%08X)\n", what, (unsigned)GetExceptionCode());
        return NVSDK_NGX_Result_Fail;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3) { printf("usage: probe_nr3 <folder with nvngx_dlss.dll> <path of nvngx_dlssnr.dll> [feature id]\n"); return 2; }
    int feature = argc > 3 ? _wtoi(argv[3]) : 18;
    if (FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_12_0, __uuidof(ID3D12Device), (void**)&g_dev))) { printf("no device\n"); return 1; }
    D3D12_COMMAND_QUEUE_DESC q = {}; q.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    g_dev->CreateCommandQueue(&q, __uuidof(ID3D12CommandQueue), (void**)&g_queue);
    g_dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, __uuidof(ID3D12CommandAllocator), (void**)&g_alloc);
    g_dev->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_alloc, nullptr, __uuidof(ID3D12GraphicsCommandList), (void**)&g_list);
    g_dev->CreateFence(0, D3D12_FENCE_FLAG_NONE, __uuidof(ID3D12Fence), (void**)&g_fence);
    g_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);

    const wchar_t* paths[1] = { argv[1] };
    NVSDK_NGX_FeatureCommonInfo info = {};
    info.PathListInfo.Path = paths; info.PathListInfo.Length = 1;
    info.LoggingInfo.MinimumLoggingLevel = NVSDK_NGX_LOGGING_LEVEL_ON;
    NVSDK_NGX_Result r = NVSDK_NGX_D3D12_Init_with_ProjectID("6f1d6c1e-52a7-4c0b-9d7a-c3a5d0b84e21", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", argv[1], g_dev, &info, NVSDK_NGX_Version_API);
    printf("core init 0x%08X\n", (unsigned)r);
    if (NVSDK_NGX_FAILED(r)) return 1;
    NVSDK_NGX_Parameter* caps = nullptr; NVSDK_NGX_Parameter* p = nullptr;
    NVSDK_NGX_D3D12_GetCapabilityParameters(&caps);
    NVSDK_NGX_D3D12_AllocateParameters(&p);

    HMODULE lib = LoadLibraryW(argv[2]);
    if (lib == nullptr) { printf("the DLL could not be loaded: %lu\n", GetLastError()); return 1; }
    s_init = (InitExtFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_Init_Ext");
    s_populate = (PopulateFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_PopulateParameters_Impl");
    s_create = (CreateFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_CreateFeature");
    s_evaluate = (EvaluateFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_EvaluateFeature");
    s_release = (ReleaseFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_ReleaseFeature");
    s_shutdown = (ShutdownFn)GetProcAddress(lib, "NVSDK_NGX_D3D12_Shutdown1");
    if (!s_init || !s_populate || !s_create || !s_evaluate || !s_release || !s_shutdown) { printf("an entry point is missing\n"); return 1; }

    NVSDK_NGX_Handle* handle = nullptr;
    r = Guard(0, 0, caps, &handle, argv[1]);
    printf("snippet init 0x%08X\n", (unsigned)r);
    r = Guard(1, 0, caps, &handle, argv[1]);
    printf("snippet populate 0x%08X\n", (unsigned)r);
    int available = -1, initResult = -1;
    NVSDK_NGX_Parameter_GetI(caps, "DLSSNR.Available", &available);
    NVSDK_NGX_Parameter_GetI(caps, "DLSSNR.FeatureInitResult", &initResult);
    printf("DLSSNR.Available %d, FeatureInitResult 0x%08X\n", available, (unsigned)initResult);

    const unsigned w = 1280, h = 720;
    ID3D12Resource* color = Make(w, h, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, false, D3D12_RESOURCE_STATE_RENDER_TARGET);
    ID3D12Resource* output = Make(w, h, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, true, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    ID3D12Resource* motion = Make(w, h, DXGI_FORMAT_R16G16_FLOAT, false, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    ID3D12Resource* depth = Make(w, h, DXGI_FORMAT_R32_FLOAT, false, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    // (The colour cleared to something that is not black.)
    D3D12_DESCRIPTOR_HEAP_DESC hd = {}; hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV; hd.NumDescriptors = 1;
    ID3D12DescriptorHeap* heap = nullptr;
    g_dev->CreateDescriptorHeap(&hd, __uuidof(ID3D12DescriptorHeap), (void**)&heap);
    D3D12_CPU_DESCRIPTOR_HANDLE rtv = heap->GetCPUDescriptorHandleForHeapStart();
    g_dev->CreateRenderTargetView(color, nullptr, rtv);
    const float grey[4] = { 0.5f, 0.4f, 0.3f, 1.0f };
    g_list->ClearRenderTargetView(rtv, grey, 0, nullptr);
    D3D12_RESOURCE_BARRIER bar = {}; bar.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION; bar.Transition.pResource = color; bar.Transition.Subresource = 0;
    bar.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET; bar.Transition.StateAfter = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
    g_list->ResourceBarrier(1, &bar);
    Run();

    NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Width", w);
    NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Height", h);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Width, w);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Height, h);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutWidth, w);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutHeight, h);
    NVSDK_NGX_Parameter_SetI(p, "DLSSNR.DepthInverted", 1);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_CreationNodeMask, 1);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_VisibilityNodeMask, 1);
    r = Guard(2, feature, p, &handle, argv[1]);
    printf("snippet create (feature %d) 0x%08X, handle %p\n", feature, (unsigned)r, (void*)handle);
    Run();
    if (NVSDK_NGX_SUCCEED(r) && handle != nullptr)
    {
        NVSDK_NGX_Parameter_SetD3d12Resource(p, "DLSSNR.Color", color);
        NVSDK_NGX_Parameter_SetD3d12Resource(p, "DLSSNR.Output", output);
        NVSDK_NGX_Parameter_SetD3d12Resource(p, "DLSSNR.MVec", motion);
        NVSDK_NGX_Parameter_SetD3d12Resource(p, "DLSSNR.Depth", depth);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.Intensity", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalToneStrength", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalStructureStrength", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.SkinStructureStrength", -1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleX", -(float)w);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleY", (float)h);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Enabled", 1);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.UseAutoMask", 1);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Reset", 1);
        Mean(output, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, "output before");
        for (int i = 0; i < 4; i++)
        {
            r = Guard(3, feature, p, &handle, argv[1]);
            Run();
            printf("  evaluate %d: 0x%08X\n", i, (unsigned)r);
            NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Reset", 0);
        }
        Mean(color, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, "colour");
        Mean(output, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, "output after");
        Guard(4, feature, p, &handle, argv[1]);
    }
    Guard(5, feature, p, &handle, argv[1]);
    NVSDK_NGX_D3D12_DestroyParameters(p);
    NVSDK_NGX_D3D12_DestroyParameters(caps);
    NVSDK_NGX_D3D12_Shutdown1(g_dev);
    return 0;
}
