// A probe: can neural rendering (NGX feature "dlssnr") be made and run on a Direct3D 11 device, and under which
// feature number? Argument: the folder with nvngx_dlssnr.dll. Nothing of the bridge is used: NGX is called directly.
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <cstdio>
#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"

static ID3D11Texture2D* Make(ID3D11Device* dev, unsigned w, unsigned h, DXGI_FORMAT format, bool uav)
{
    D3D11_TEXTURE2D_DESC d = {};
    d.Width = w; d.Height = h; d.MipLevels = 1; d.ArraySize = 1; d.Format = format; d.SampleDesc.Count = 1;
    d.Usage = D3D11_USAGE_DEFAULT;
    d.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET | (uav ? D3D11_BIND_UNORDERED_ACCESS : 0);
    ID3D11Texture2D* t = nullptr;
    dev->CreateTexture2D(&d, nullptr, &t);
    return t;
}

static NVSDK_NGX_Result Create(ID3D11DeviceContext* ctx, int feature, NVSDK_NGX_Parameter* p, NVSDK_NGX_Handle** h)
{
    __try { return NVSDK_NGX_D3D11_CreateFeature(ctx, (NVSDK_NGX_Feature)feature, p, h); }
    __except (EXCEPTION_EXECUTE_HANDLER) { printf("  (fault inside NGX)\n"); return NVSDK_NGX_Result_Fail; }
}

static NVSDK_NGX_Result Evaluate(ID3D11DeviceContext* ctx, NVSDK_NGX_Handle* h, NVSDK_NGX_Parameter* p)
{
    __try { return NVSDK_NGX_D3D11_EvaluateFeature(ctx, h, p, nullptr); }
    __except (EXCEPTION_EXECUTE_HANDLER) { printf("  (fault inside NGX)\n"); return NVSDK_NGX_Result_Fail; }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2) { printf("usage: probe_nr <folder with nvngx_dlssnr.dll>\n"); return 2; }
    ID3D11Device* dev = nullptr; ID3D11DeviceContext* ctx = nullptr; D3D_FEATURE_LEVEL level;
    if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &dev, &level, &ctx))) { printf("no device\n"); return 1; }
    const wchar_t* paths[1] = { argv[1] };
    NVSDK_NGX_FeatureCommonInfo info = {};
    info.PathListInfo.Path = paths; info.PathListInfo.Length = 1;
    info.LoggingInfo.MinimumLoggingLevel = NVSDK_NGX_LOGGING_LEVEL_ON;
    NVSDK_NGX_Result r = NVSDK_NGX_D3D11_Init_with_ProjectID("6f1d6c1e-52a7-4c0b-9d7a-c3a5d0b84e21", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", argv[1], dev, &info, NVSDK_NGX_Version_API);
    printf("init 0x%08X\n", (unsigned)r);
    if (NVSDK_NGX_FAILED(r)) return 1;
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
    for (int feature = 14; feature <= 18; feature++)
    {
        NVSDK_NGX_Parameter* p = nullptr;
        NVSDK_NGX_D3D11_AllocateParameters(&p);
        NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Width", w);
        NVSDK_NGX_Parameter_SetUI(p, "DLSSNR.Height", h);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Width, w);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_Height, h);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutWidth, w);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_OutHeight, h);
        NVSDK_NGX_Parameter_SetI(p, "DLSSNR.DepthInverted", 1);
        NVSDK_NGX_Handle* handle = nullptr;
        r = Create(ctx, feature, p, &handle);
        printf("feature %d: create 0x%08X, handle %p\n", feature, (unsigned)r, (void*)handle);
        if (NVSDK_NGX_SUCCEED(r) && handle != nullptr)
        {
            NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Color", color);
            NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Output", output);
            NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.MVec", motion);
            NVSDK_NGX_Parameter_SetD3d11Resource(p, "DLSSNR.Depth", depth);
            NVSDK_NGX_Parameter_SetF(p, "DLSSNR.Intensity", 1.0f);
            NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalToneStrength", 1.0f);
            NVSDK_NGX_Parameter_SetF(p, "DLSSNR.LocalStructureStrength", 1.0f);
            NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleX", -(float)w);
            NVSDK_NGX_Parameter_SetF(p, "DLSSNR.MVecScaleY", (float)h);
            NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Enabled", 1);
            NVSDK_NGX_Parameter_SetI(p, "DLSSNR.Reset", 1);
            for (int i = 0; i < 3; i++)
            {
                r = Evaluate(ctx, handle, p);
                printf("  evaluate %d: 0x%08X\n", i, (unsigned)r);
            }
            NVSDK_NGX_D3D11_ReleaseFeature(handle);
        }
        NVSDK_NGX_D3D11_DestroyParameters(p);
    }
    NVSDK_NGX_D3D11_Shutdown1(dev);
    return 0;
}
