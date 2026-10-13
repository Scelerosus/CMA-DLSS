// A probe: neural rendering by loading nvngx_dlssnr.dll itself and calling its own entry points (the ones NGX's core
// would call), on a Direct3D 11 device. Arguments: the folder with nvngx_dlss.dll (for the core), the full path of
// nvngx_dlssnr.dll.
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <cstdio>
#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"

typedef NVSDK_NGX_Result (NVSDK_CONV *InitExtFn)(unsigned long long, const wchar_t*, ID3D11Device*, NVSDK_NGX_Version, const NVSDK_NGX_Parameter*);
typedef NVSDK_NGX_Result (NVSDK_CONV *PopulateFn)(NVSDK_NGX_Parameter*);
typedef NVSDK_NGX_Result (NVSDK_CONV *CreateFn)(ID3D11DeviceContext*, NVSDK_NGX_Feature, const NVSDK_NGX_Parameter*, NVSDK_NGX_Handle**);
typedef NVSDK_NGX_Result (NVSDK_CONV *EvaluateFn)(ID3D11DeviceContext*, const NVSDK_NGX_Handle*, const NVSDK_NGX_Parameter*, PFN_NVSDK_NGX_ProgressCallback);
typedef NVSDK_NGX_Result (NVSDK_CONV *ReleaseFn)(NVSDK_NGX_Handle*);
typedef NVSDK_NGX_Result (NVSDK_CONV *ShutdownFn)(ID3D11Device*);

static InitExtFn s_init; static PopulateFn s_populate; static CreateFn s_create; static EvaluateFn s_evaluate; static ReleaseFn s_release; static ShutdownFn s_shutdown;

static ID3D11Texture2D* Make(ID3D11Device* dev, unsigned w, unsigned h, DXGI_FORMAT format, bool uav)
{
    D3D11_TEXTURE2D_DESC d = {};
    d.Width = w; d.Height = h; d.MipLevels = 1; d.ArraySize = 1; d.Format = format; d.SampleDesc.Count = 1;
    d.Usage = D3D11_USAGE_DEFAULT;
    d.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET | (uav ? D3D11_BIND_UNORDERED_ACCESS : 0);
    ID3D11Texture2D* t = nullptr;
    HRESULT hr = dev->CreateTexture2D(&d, nullptr, &t);
    if (FAILED(hr)) printf("texture format %d failed 0x%08X\n", (int)format, (unsigned)hr);
    return t;
}

static void Mean(ID3D11Device* dev, ID3D11DeviceContext* ctx, ID3D11Texture2D* tex, const char* name)
{
    D3D11_TEXTURE2D_DESC d; tex->GetDesc(&d);
    d.Usage = D3D11_USAGE_STAGING; d.BindFlags = 0; d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* copy = nullptr;
    if (FAILED(dev->CreateTexture2D(&d, nullptr, &copy))) return;
    ctx->CopyResource(copy, tex);
    D3D11_MAPPED_SUBRESOURCE m;
    if (SUCCEEDED(ctx->Map(copy, 0, D3D11_MAP_READ, 0, &m)))
    {
        double sum[4] = {}; unsigned n = 0;
        for (unsigned y = 0; y < d.Height; y += 5)
        {
            const unsigned char* row = (const unsigned char*)m.pData + (size_t)y * m.RowPitch;
            for (unsigned x = 0; x < d.Width; x += 5) { for (int c = 0; c < 4; c++) sum[c] += row[x * 4 + c]; n++; }
        }
        printf("%s: mean r %.1f g %.1f b %.1f a %.1f\n", name, sum[0] / n, sum[1] / n, sum[2] / n, sum[3] / n);
        ctx->Unmap(copy, 0);
    }
    copy->Release();
}

static NVSDK_NGX_Result Guard(int what, ID3D11Device* dev, ID3D11DeviceContext* ctx, int feature, NVSDK_NGX_Parameter* p, NVSDK_NGX_Handle** h, const wchar_t* path)
{
    __try
    {
        switch (what)
        {
        case 0: return s_init(0x876232C, path, dev, NVSDK_NGX_Version_API, p);
        case 1: return s_populate(p);
        case 2: return s_create(ctx, (NVSDK_NGX_Feature)feature, p, h);
        case 3: return s_evaluate(ctx, *h, p, nullptr);
        case 4: return s_release(*h);
        default: return s_shutdown(dev);
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
    if (argc < 3) { printf("usage: probe_nr2 <folder with nvngx_dlss.dll> <path of nvngx_dlssnr.dll> [feature id]\n"); return 2; }
    int feature = argc > 3 ? _wtoi(argv[3]) : 18;
    ID3D11Device* dev = nullptr; ID3D11DeviceContext* ctx = nullptr; D3D_FEATURE_LEVEL level;
    if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &dev, &level, &ctx))) { printf("no device\n"); return 1; }
    const wchar_t* paths[1] = { argv[1] };
    NVSDK_NGX_FeatureCommonInfo info = {};
    info.PathListInfo.Path = paths; info.PathListInfo.Length = 1;
    info.LoggingInfo.MinimumLoggingLevel = NVSDK_NGX_LOGGING_LEVEL_ON;
    NVSDK_NGX_Result r = NVSDK_NGX_D3D11_Init_with_ProjectID("6f1d6c1e-52a7-4c0b-9d7a-c3a5d0b84e21", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", argv[1], dev, &info, NVSDK_NGX_Version_API);
    printf("core init 0x%08X\n", (unsigned)r);
    if (NVSDK_NGX_FAILED(r)) return 1;
    NVSDK_NGX_Parameter* caps = nullptr; NVSDK_NGX_Parameter* p = nullptr;
    NVSDK_NGX_D3D11_GetCapabilityParameters(&caps);
    NVSDK_NGX_D3D11_AllocateParameters(&p);

    HMODULE lib = LoadLibraryW(argv[2]);
    if (lib == nullptr) { printf("the DLL could not be loaded: %lu\n", GetLastError()); return 1; }
    s_init = (InitExtFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_Init_Ext");
    s_populate = (PopulateFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_PopulateParameters_Impl");
    s_create = (CreateFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_CreateFeature");
    s_evaluate = (EvaluateFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_EvaluateFeature");
    s_release = (ReleaseFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_ReleaseFeature");
    s_shutdown = (ShutdownFn)GetProcAddress(lib, "NVSDK_NGX_D3D11_Shutdown1");
    if (!s_init || !s_populate || !s_create || !s_evaluate || !s_release || !s_shutdown) { printf("an entry point is missing\n"); return 1; }

    NVSDK_NGX_Handle* handle = nullptr;
    r = Guard(0, dev, ctx, 0, caps, &handle, argv[1]);
    printf("snippet init 0x%08X\n", (unsigned)r);
    r = Guard(1, dev, ctx, 0, caps, &handle, argv[1]);
    printf("snippet populate 0x%08X\n", (unsigned)r);
    int available = -1, initResult = -1;
    NVSDK_NGX_Parameter_GetI(caps, "DLSSNR.Available", &available);
    NVSDK_NGX_Parameter_GetI(caps, "DLSSNR.FeatureInitResult", &initResult);
    printf("DLSSNR.Available %d, FeatureInitResult 0x%08X\n", available, (unsigned)initResult);

    const unsigned w = 1280, h = 720;
    ID3D11Texture2D* color = Make(dev, w, h, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, false);
    ID3D11Texture2D* output = Make(dev, w, h, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, true);
    ID3D11Texture2D* motion = Make(dev, w, h, DXGI_FORMAT_R16G16_FLOAT, false);
    ID3D11Texture2D* depth = Make(dev, w, h, DXGI_FORMAT_R32_FLOAT, false);
    ID3D11RenderTargetView* view = nullptr;
    dev->CreateRenderTargetView(color, nullptr, &view);
    const float grey[4] = { 0.5f, 0.4f, 0.3f, 1.0f };
    ctx->ClearRenderTargetView(view, grey);
    view->Release();

    NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Width", w);
    NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Height", h);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Width, w);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Height, h);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutWidth, w);
    NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutHeight, h);
    NVSDK_NGX_Parameter_SetI(p, "DLSSNR.DepthInverted", 1);
    r = Guard(2, dev, ctx, feature, p, &handle, argv[1]);
    printf("snippet create (feature %d) 0x%08X, handle %p\n", feature, (unsigned)r, (void*)handle);
    if (NVSDK_NGX_SUCCEED(r) && handle != nullptr)
    {
        NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Color", color);
        NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Output", output);
        NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.MVec", motion);
        NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Depth", depth);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.Intensity", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalToneStrength", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalStructureStrength", 1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.SkinStructureStrength", -1.0f);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleX", -(float)w);
        NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleY", (float)h);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Enabled", 1);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.UseAutoMask", 1);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Reset", 1);
        Mean(dev, ctx, output, "output before");
        for (int i = 0; i < 4; i++)
        {
            r = Guard(3, dev, ctx, feature, p, &handle, argv[1]);
            printf("  evaluate %d: 0x%08X\n", i, (unsigned)r);
            NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Reset", 0);
        }
        ctx->Flush();
        Mean(dev, ctx, color, "colour");
        Mean(dev, ctx, output, "output after");
        Guard(4, dev, ctx, feature, p, &handle, argv[1]);
    }
    Guard(5, dev, ctx, feature, p, &handle, argv[1]);
    NVSDK_NGX_D3D11_DestroyParameters(p);
    NVSDK_NGX_D3D11_DestroyParameters(caps);
    NVSDK_NGX_D3D11_Shutdown1(dev);
    return 0;
}
