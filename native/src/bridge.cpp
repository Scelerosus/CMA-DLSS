// CmaBridge: the native half of CMA DLSS NR, written for this mod.
//
// It keeps the interface of the library it replaces (the vdn_* functions, ABI 4), so that the managed half need not
// know which of the two it has loaded.
//
// Stage 1: DLSS super resolution (and DLAA), run on the game's own Direct3D 11 device. Neural rendering and frame
// generation are not in this library yet: their functions are here and say so.
//
// Threads. The managed half calls from the game's main thread; the work with the device's context is done on Unity's
// render thread, in the render event. Everything shared is behind one lock, which the managed half also takes itself
// around a rebuild (vdn_rebuild_begin / vdn_rebuild_end).

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_1.h>
#include <d3d12.h>
#include <mutex>
#include <cstdio>
#include <cstdarg>
#include <cstring>

#include "nvsdk_ngx.h"
#include "nvsdk_ngx_helpers.h"

#define VDN_API extern "C" __declspec(dllexport)

namespace
{
    // The mod's own name to NGX (a project id: any GUID that stays the same).
    const char* const kProjectId = "6f1d6c1e-52a7-4c0b-9d7a-c3a5d0b84e21";
    const char* const kEngineVersion = "1.0";

    std::recursive_mutex g_lock;
    wchar_t g_dir[MAX_PATH] = L"";
    const wchar_t* g_paths[1] = { g_dir };

    // ---- the log (ngx\cmabridge.log beside NVIDIA's own) ----
    FILE* g_log = nullptr;
    int g_logLines = 0;

    void Log(const char* format, ...)
    {
        std::lock_guard<std::recursive_mutex> hold(g_lock);
        if (g_log == nullptr || g_logLines > 4000)
        {
            return;
        }
        g_logLines++;
        SYSTEMTIME t;
        GetLocalTime(&t);
        fprintf(g_log, "[%02d:%02d:%02d.%03d] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
        va_list args;
        va_start(args, format);
        vfprintf(g_log, format, args);
        va_end(args);
        fputc('\n', g_log);
        fflush(g_log);
    }

    void OpenLog(const wchar_t* dir)
    {
        if (g_log != nullptr || dir == nullptr)
        {
            return;
        }
        wchar_t path[MAX_PATH];
        swprintf_s(path, L"%s\\cmabridge.log", dir);
        _wfopen_s(&g_log, path, L"w");
        Log("CmaBridge, stage 1 (DLSS on Direct3D 11), built " __DATE__ " " __TIME__);
    }

    template <typename T> void Drop(T*& p)
    {
        if (p != nullptr)
        {
            p->Release();
            p = nullptr;
        }
    }

    // ---- DLSS super resolution ----
    struct Frame
    {
        float jitterX, jitterY, mvScaleX, mvScaleY;
        int reset;
    };

    struct SuperResolution
    {
        bool open = false;
        ID3D11Device* device = nullptr;
        ID3D11DeviceContext* context = nullptr;
        ID3D11DeviceContext1* context1 = nullptr;
        // A state of the context kept for NGX alone: Unity's own is swapped out while NGX draws and back after, so
        // that nothing NGX sets is left where Unity thinks it knows what is set.
        ID3DDeviceContextState* state = nullptr;

        NVSDK_NGX_Parameter* capabilities = nullptr;
        NVSDK_NGX_Parameter* parameters = nullptr;
        NVSDK_NGX_Handle* feature = nullptr;

        // What is wanted (the main thread), and which of it the feature was made for (the render thread).
        unsigned renderW = 0, renderH = 0, outW = 0, outH = 0, quality = 0, preset = 0;
        int depthInverted = 0, autoExposure = 0, hdr = 0;
        unsigned wanted = 0, made = 0;
        bool configured = false;

        ID3D11Resource* color = nullptr;
        ID3D11Resource* motion = nullptr;
        ID3D11Resource* depth = nullptr;
        ID3D11Resource* output = nullptr;

        Frame frames[8] = {};
        // 0 none, 1 the feature could not be made, 2 it could not be run.
        int error = 0;
        unsigned evaluated = 0;
        unsigned result = 0;
        bool crashed = false;
        char status[512] = "not opened";
    };

    SuperResolution g_sr;

    void Status(const char* format, ...)
    {
        va_list args;
        va_start(args, format);
        vsnprintf(g_sr.status, sizeof(g_sr.status), format, args);
        va_end(args);
        Log("DLSS: %s", g_sr.status);
    }

    void Take(ID3D11Resource*& slot, void* texture)
    {
        ID3D11Resource* next = static_cast<ID3D11Resource*>(texture);
        if (next == slot)
        {
            return;
        }
        if (next != nullptr)
        {
            next->AddRef();
        }
        Drop(slot);
        slot = next;
    }

    void ReleaseFeature()
    {
        if (g_sr.feature != nullptr)
        {
            NVSDK_NGX_D3D11_ReleaseFeature(g_sr.feature);
            g_sr.feature = nullptr;
        }
    }

    // The calls into NGX that draw: made with NGX's own state on the context, and with a guard round them, so that a
    // fault inside the driver ends DLSS and not the game.
    NVSDK_NGX_Result CreateGuarded(NVSDK_NGX_DLSS_Create_Params* create)
    {
        __try
        {
            return NGX_D3D11_CREATE_DLSS_EXT(g_sr.context, &g_sr.feature, g_sr.parameters, create);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            g_sr.crashed = true;
            return NVSDK_NGX_Result_Fail;
        }
    }

    NVSDK_NGX_Result EvaluateGuarded(NVSDK_NGX_D3D11_DLSS_Eval_Params* eval)
    {
        __try
        {
            return NGX_D3D11_EVALUATE_DLSS_EXT(g_sr.context, g_sr.feature, g_sr.parameters, eval);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            g_sr.crashed = true;
            return NVSDK_NGX_Result_Fail;
        }
    }

    void SetPreset(unsigned preset)
    {
        NVSDK_NGX_Parameter* p = g_sr.parameters;
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_DLAA, preset);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraQuality, preset);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Quality, preset);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Balanced, preset);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Performance, preset);
        NVSDK_NGX_Parameter_SetUI(p, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraPerformance, preset);
    }

    // On the render thread.
    void RunSuperResolution(int slot)
    {
        std::lock_guard<std::recursive_mutex> hold(g_lock);
        SuperResolution& s = g_sr;
        if (!s.open || !s.configured || s.crashed || s.color == nullptr || s.output == nullptr || s.motion == nullptr || s.depth == nullptr)
        {
            return;
        }
        ID3DDeviceContextState* unity = nullptr;
        if (s.context1 != nullptr && s.state != nullptr)
        {
            s.context1->SwapDeviceContextState(s.state, &unity);
        }
        if (s.made != s.wanted)
        {
            s.made = s.wanted;
            ReleaseFeature();
            SetPreset(s.preset);
            NVSDK_NGX_DLSS_Create_Params create = {};
            create.Feature.InWidth = s.renderW;
            create.Feature.InHeight = s.renderH;
            create.Feature.InTargetWidth = s.outW;
            create.Feature.InTargetHeight = s.outH;
            create.Feature.InPerfQualityValue = static_cast<NVSDK_NGX_PerfQuality_Value>(s.quality);
            create.InFeatureCreateFlags = NVSDK_NGX_DLSS_Feature_Flags_MVLowRes
                | (s.depthInverted ? NVSDK_NGX_DLSS_Feature_Flags_DepthInverted : 0)
                | (s.autoExposure ? NVSDK_NGX_DLSS_Feature_Flags_AutoExposure : 0)
                | (s.hdr ? NVSDK_NGX_DLSS_Feature_Flags_IsHDR : 0);
            create.InEnableOutputSubrects = false;
            NVSDK_NGX_Result r = CreateGuarded(&create);
            s.result = static_cast<unsigned>(r);
            if (s.crashed)
            {
                s.feature = nullptr;
                s.error = 1;
                Status("the driver faulted while making the feature (%ux%u -> %ux%u, quality %u, preset %u); DLSS is off until the game is started again", s.renderW, s.renderH, s.outW, s.outH, s.quality, s.preset);
            }
            else if (NVSDK_NGX_FAILED(r) || s.feature == nullptr)
            {
                s.feature = nullptr;
                s.error = 1;
                Status("the feature could not be made: 0x%08X (%ux%u -> %ux%u, quality %u, preset %u)", static_cast<unsigned>(r), s.renderW, s.renderH, s.outW, s.outH, s.quality, s.preset);
            }
            else
            {
                s.error = 0;
                Status("feature made: %ux%u -> %ux%u, quality %u, preset %u, flags 0x%X", s.renderW, s.renderH, s.outW, s.outH, s.quality, s.preset, create.InFeatureCreateFlags);
            }
        }
        if (s.feature != nullptr && !s.crashed)
        {
            const Frame& f = s.frames[slot & 7];
            NVSDK_NGX_D3D11_DLSS_Eval_Params eval = {};
            eval.Feature.pInColor = s.color;
            eval.Feature.pInOutput = s.output;
            eval.pInDepth = s.depth;
            eval.pInMotionVectors = s.motion;
            eval.InJitterOffsetX = f.jitterX;
            eval.InJitterOffsetY = f.jitterY;
            eval.InRenderSubrectDimensions.Width = s.renderW;
            eval.InRenderSubrectDimensions.Height = s.renderH;
            eval.InReset = f.reset;
            eval.InMVScaleX = f.mvScaleX;
            eval.InMVScaleY = f.mvScaleY;
            NVSDK_NGX_Result r = EvaluateGuarded(&eval);
            s.result = static_cast<unsigned>(r);
            if (s.crashed)
            {
                s.error = 2;
                Status("the driver faulted while running the feature; DLSS is off until the game is started again");
            }
            else if (NVSDK_NGX_FAILED(r))
            {
                if (s.error != 2)
                {
                    Status("the feature could not be run: 0x%08X", static_cast<unsigned>(r));
                }
                s.error = 2;
            }
            else
            {
                if (s.evaluated == 0)
                {
                    Status("running (%ux%u -> %ux%u)", s.renderW, s.renderH, s.outW, s.outH);
                }
                s.error = 0;
                s.evaluated++;
            }
        }
        if (unity != nullptr)
        {
            s.context1->SwapDeviceContextState(unity, nullptr);
            unity->Release();
        }
    }

    void CloseSuperResolution()
    {
        SuperResolution& s = g_sr;
        if (!s.open)
        {
            return;
        }
        ReleaseFeature();
        if (s.parameters != nullptr)
        {
            NVSDK_NGX_D3D11_DestroyParameters(s.parameters);
            s.parameters = nullptr;
        }
        if (s.capabilities != nullptr)
        {
            NVSDK_NGX_D3D11_DestroyParameters(s.capabilities);
            s.capabilities = nullptr;
        }
        NVSDK_NGX_D3D11_Shutdown1(s.device);
        Drop(s.color);
        Drop(s.motion);
        Drop(s.depth);
        Drop(s.output);
        Drop(s.state);
        Drop(s.context1);
        Drop(s.context);
        Drop(s.device);
        s.open = false;
        s.configured = false;
        s.evaluated = 0;
        s.error = 0;
        s.wanted = s.made = 0;
        Status("closed");
    }

    const char kNotYet[] = "not in the mod's own native library yet";

    void __stdcall OnRenderEvent(int id)
    {
        if ((id & 0xF00) == 0x200)
        {
            RunSuperResolution((id >> 16) & 7);
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// General
// ---------------------------------------------------------------------------------------------------------------------
VDN_API unsigned vdn_abi_version() { return 4; }
VDN_API void* vdn_get_render_event_func() { return reinterpret_cast<void*>(&OnRenderEvent); }
VDN_API void vdn_rebuild_begin() { g_lock.lock(); }
VDN_API void vdn_rebuild_end() { g_lock.unlock(); }

VDN_API void vdn_shutdown()
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    CloseSuperResolution();
}

// ---------------------------------------------------------------------------------------------------------------------
// DLSS super resolution
// ---------------------------------------------------------------------------------------------------------------------
VDN_API int vdn_sr_open(const wchar_t* snippetDir, void* anyUnityTexture, int verboseLog)
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    SuperResolution& s = g_sr;
    if (s.open)
    {
        return 0;
    }
    if (snippetDir == nullptr || anyUnityTexture == nullptr)
    {
        return -1;
    }
    wcsncpy_s(g_dir, snippetDir, _TRUNCATE);
    OpenLog(g_dir);
    static_cast<ID3D11Resource*>(anyUnityTexture)->GetDevice(&s.device);
    if (s.device == nullptr)
    {
        Status("the game's device could not be had from its texture");
        return -2;
    }
    NVSDK_NGX_FeatureCommonInfo info = {};
    info.PathListInfo.Path = g_paths;
    info.PathListInfo.Length = 1;
    info.LoggingInfo.MinimumLoggingLevel = verboseLog ? NVSDK_NGX_LOGGING_LEVEL_VERBOSE : NVSDK_NGX_LOGGING_LEVEL_ON;
    NVSDK_NGX_Result r = NVSDK_NGX_D3D11_Init_with_ProjectID(kProjectId, NVSDK_NGX_ENGINE_TYPE_CUSTOM, kEngineVersion, g_dir, s.device, &info, NVSDK_NGX_Version_API);
    if (NVSDK_NGX_FAILED(r))
    {
        Status("NGX could not be started: 0x%08X", static_cast<unsigned>(r));
        Drop(s.device);
        return -3;
    }
    r = NVSDK_NGX_D3D11_GetCapabilityParameters(&s.capabilities);
    int available = 0;
    if (NVSDK_NGX_SUCCEED(r) && s.capabilities != nullptr)
    {
        NVSDK_NGX_Parameter_GetI(s.capabilities, NVSDK_NGX_Parameter_SuperSampling_Available, &available);
    }
    if (!available)
    {
        int why = 0, driver = 0;
        if (s.capabilities != nullptr)
        {
            NVSDK_NGX_Parameter_GetI(s.capabilities, NVSDK_NGX_Parameter_SuperSampling_FeatureInitResult, &why);
            NVSDK_NGX_Parameter_GetI(s.capabilities, NVSDK_NGX_Parameter_SuperSampling_NeedsUpdatedDriver, &driver);
            NVSDK_NGX_D3D11_DestroyParameters(s.capabilities);
            s.capabilities = nullptr;
        }
        Status("DLSS is not available here (capabilities 0x%08X, feature 0x%08X%s)", static_cast<unsigned>(r), static_cast<unsigned>(why), driver ? ", the driver is too old" : "");
        NVSDK_NGX_D3D11_Shutdown1(s.device);
        Drop(s.device);
        return -4;
    }
    r = NVSDK_NGX_D3D11_AllocateParameters(&s.parameters);
    if (NVSDK_NGX_FAILED(r) || s.parameters == nullptr)
    {
        Status("NGX gave no parameters: 0x%08X", static_cast<unsigned>(r));
        NVSDK_NGX_D3D11_DestroyParameters(s.capabilities);
        s.capabilities = nullptr;
        NVSDK_NGX_D3D11_Shutdown1(s.device);
        Drop(s.device);
        return -5;
    }
    s.device->GetImmediateContext(&s.context);
    ID3D11Device1* device1 = nullptr;
    if (s.context != nullptr && SUCCEEDED(s.context->QueryInterface(__uuidof(ID3D11DeviceContext1), reinterpret_cast<void**>(&s.context1)))
        && SUCCEEDED(s.device->QueryInterface(__uuidof(ID3D11Device1), reinterpret_cast<void**>(&device1))))
    {
        D3D_FEATURE_LEVEL level = s.device->GetFeatureLevel();
        HRESULT hr = device1->CreateDeviceContextState(0, &level, 1, D3D11_SDK_VERSION, __uuidof(ID3D11Device1), nullptr, &s.state);
        if (FAILED(hr))
        {
            s.state = nullptr;
            Log("DLSS: no context state of its own could be made (0x%08X): NGX draws with the game's", static_cast<unsigned>(hr));
        }
    }
    Drop(device1);
    s.open = true;
    s.crashed = false;
    s.error = 0;
    s.evaluated = 0;
    Status("opened (own context state: %s)", s.state != nullptr ? "yes" : "no");
    return 0;
}

VDN_API int vdn_sr_query(unsigned outW, unsigned outH, unsigned quality, unsigned* renderW, unsigned* renderH, unsigned* minW, unsigned* minH, unsigned* maxW, unsigned* maxH, float* sharpness)
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    unsigned w = 0, h = 0, loW = 0, loH = 0, hiW = 0, hiH = 0;
    float sharp = 0.0f;
    int rc = -1;
    if (g_sr.open && g_sr.capabilities != nullptr)
    {
        NVSDK_NGX_Result r = NGX_DLSS_GET_OPTIMAL_SETTINGS(g_sr.capabilities, outW, outH, static_cast<NVSDK_NGX_PerfQuality_Value>(quality), &w, &h, &hiW, &hiH, &loW, &loH, &sharp);
        rc = NVSDK_NGX_FAILED(r) ? static_cast<int>(r) : 0;
    }
    if (renderW) *renderW = w;
    if (renderH) *renderH = h;
    if (minW) *minW = loW;
    if (minH) *minH = loH;
    if (maxW) *maxW = hiW;
    if (maxH) *maxH = hiH;
    if (sharpness) *sharpness = sharp;
    return rc;
}

VDN_API void vdn_sr_configure(unsigned renderW, unsigned renderH, unsigned outW, unsigned outH, unsigned quality, unsigned preset, int depthInverted, int autoExposure, int isHdr, unsigned /*views*/)
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    SuperResolution& s = g_sr;
    s.renderW = renderW;
    s.renderH = renderH;
    s.outW = outW;
    s.outH = outH;
    s.quality = quality;
    s.preset = preset;
    s.depthInverted = depthInverted;
    s.autoExposure = autoExposure;
    s.hdr = isHdr;
    s.configured = true;
    s.wanted++;
    s.evaluated = 0;
    s.error = 0;
}

VDN_API int vdn_sr_set_textures(int /*view*/, void* color, void* motion, void* depth, void* output)
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    if (!g_sr.open)
    {
        return -1;
    }
    Take(g_sr.color, color);
    Take(g_sr.motion, motion);
    Take(g_sr.depth, depth);
    Take(g_sr.output, output);
    return 0;
}

VDN_API void vdn_sr_set_frame(int slot, int /*view*/, float jitterX, float jitterY, float mvScaleX, float mvScaleY, int reset)
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    Frame& f = g_sr.frames[slot & 7];
    f.jitterX = jitterX;
    f.jitterY = jitterY;
    f.mvScaleX = mvScaleX;
    f.mvScaleY = mvScaleY;
    f.reset = reset;
}

VDN_API void vdn_sr_close()
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    CloseSuperResolution();
}

VDN_API int vdn_sr_ready()
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    return g_sr.open && g_sr.evaluated > 0 && g_sr.error == 0 ? 1 : 0;
}

VDN_API int vdn_sr_error()
{
    std::lock_guard<std::recursive_mutex> hold(g_lock);
    return g_sr.error;
}

VDN_API const char* vdn_sr_status_text() { return g_sr.status; }

// ---------------------------------------------------------------------------------------------------------------------
// Neural rendering: not in this library yet.
// ---------------------------------------------------------------------------------------------------------------------
VDN_API int vdn_view_open() { return -1; }
VDN_API int vdn_view_init(int, const wchar_t*, unsigned, unsigned, unsigned, unsigned, void*) { return -1; }
VDN_API void vdn_view_close(int) {}
VDN_API int vdn_set_textures(int, void*, void*, void*, void*, void*, unsigned, unsigned, unsigned, unsigned) { return -1; }
VDN_API void vdn_set_params(int, float, float, float, float, float, float, int, int, int, int, int, int) {}
VDN_API unsigned vdn_frames_evaluated(int) { return 0; }
VDN_API unsigned vdn_frames_skipped(int) { return 0; }
VDN_API int vdn_ready(int) { return 0; }
VDN_API const char* vdn_status_text(int) { return kNotYet; }
VDN_API unsigned vdn_last_ngx_result(int) { return 0; }
VDN_API const char* vdn_reject_text(int) { return kNotYet; }
VDN_API unsigned vdn_ring_waits(int) { return 0; }
VDN_API unsigned vdn_ring_timeouts(int) { return 0; }
VDN_API unsigned long long vdn_ring_wait_micros(int) { return 0; }
VDN_API unsigned long long vdn_ring_wait_worst_micros(int) { return 0; }

// ---------------------------------------------------------------------------------------------------------------------
// Frame generation: not in this library yet.
// ---------------------------------------------------------------------------------------------------------------------
VDN_API int vdn_fg_open(const wchar_t*, void*, int) { return -1; }
VDN_API void vdn_fg_enable(int) {}
VDN_API void vdn_fg_set_pacing(int) {}
VDN_API void vdn_fg_set_menu_detection(int) {}
VDN_API void vdn_fg_set_multiplier(int) {}
VDN_API unsigned vdn_fg_max_multiplier() { return 2; }
VDN_API int vdn_fg_generating() { return 0; }
VDN_API void vdn_fg_reset() {}
VDN_API int vdn_fg_set_textures(void*, void*) { return -1; }
VDN_API void vdn_fg_set_frame(const float*, const float*, const float*, const float*, const float*, float, float, float, float, float, float, float, float, int, int) {}
VDN_API void vdn_fg_close() {}
VDN_API int vdn_fg_ready() { return 0; }
VDN_API int vdn_fg_hook_installed() { return 0; }
VDN_API const char* vdn_fg_status_text() { return kNotYet; }
VDN_API unsigned vdn_fg_interpolated() { return 0; }
VDN_API unsigned vdn_fg_presented() { return 0; }
VDN_API unsigned vdn_fg_passed_through() { return 0; }
VDN_API double vdn_fg_base_hz() { return 0.0; }
VDN_API const char* vdn_fg_gate_summary() { return ""; }
VDN_API const char* vdn_fg_hook_report() { return ""; }
