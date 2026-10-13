// A test of CmaBridge.dll without the game: a Direct3D 11 device of its own, textures as the plugin makes them, and the
// same calls in the same order. Prints what comes back. Argument: the folder with nvngx_dlss.dll.
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <cstdio>
#include <vector>

extern "C"
{
    __declspec(dllimport) unsigned vdn_abi_version();
    __declspec(dllimport) void* vdn_get_render_event_func();
    __declspec(dllimport) int vdn_sr_open(const wchar_t*, void*, int);
    __declspec(dllimport) int vdn_sr_query(unsigned, unsigned, unsigned, unsigned*, unsigned*, unsigned*, unsigned*, unsigned*, unsigned*, float*);
    __declspec(dllimport) void vdn_sr_configure(unsigned, unsigned, unsigned, unsigned, unsigned, unsigned, int, int, int, unsigned);
    __declspec(dllimport) int vdn_sr_set_textures(int, void*, void*, void*, void*);
    __declspec(dllimport) void vdn_sr_set_frame(int, int, float, float, float, float, int);
    __declspec(dllimport) void vdn_sr_close();
    __declspec(dllimport) int vdn_sr_ready();
    __declspec(dllimport) int vdn_sr_error();
    __declspec(dllimport) const char* vdn_sr_status_text();
}

static ID3D11Texture2D* Make(ID3D11Device* dev, unsigned w, unsigned h, DXGI_FORMAT format, bool uav)
{
    D3D11_TEXTURE2D_DESC d = {};
    d.Width = w;
    d.Height = h;
    d.MipLevels = 1;
    d.ArraySize = 1;
    d.Format = format;
    d.SampleDesc.Count = 1;
    d.Usage = D3D11_USAGE_DEFAULT;
    d.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET | (uav ? D3D11_BIND_UNORDERED_ACCESS : 0);
    ID3D11Texture2D* t = nullptr;
    HRESULT hr = dev->CreateTexture2D(&d, nullptr, &t);
    if (FAILED(hr))
    {
        printf("texture %ux%u format %d failed: 0x%08X\n", w, h, (int)format, (unsigned)hr);
    }
    return t;
}

// The mean of a texture's red, green and blue, read back.
static void Mean(ID3D11Device* dev, ID3D11DeviceContext* ctx, ID3D11Texture2D* tex, const char* name)
{
    D3D11_TEXTURE2D_DESC d;
    tex->GetDesc(&d);
    d.Usage = D3D11_USAGE_STAGING;
    d.BindFlags = 0;
    d.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* copy = nullptr;
    if (FAILED(dev->CreateTexture2D(&d, nullptr, &copy)))
    {
        return;
    }
    ctx->CopyResource(copy, tex);
    D3D11_MAPPED_SUBRESOURCE m;
    if (SUCCEEDED(ctx->Map(copy, 0, D3D11_MAP_READ, 0, &m)))
    {
        double sum[3] = {};
        unsigned n = 0;
        for (unsigned y = 0; y < d.Height; y += 7)
        {
            const unsigned short* row = (const unsigned short*)((const char*)m.pData + (size_t)y * m.RowPitch);
            for (unsigned x = 0; x < d.Width; x += 7)
            {
                for (int c = 0; c < 3; c++)
                {
                    // (Half floats: only told apart from nothing here, by their bits.)
                    sum[c] += row[x * 4 + c];
                }
                n++;
            }
        }
        printf("%s: mean half-float bits r %.0f g %.0f b %.0f over %u samples\n", name, sum[0] / n, sum[1] / n, sum[2] / n, n);
        ctx->Unmap(copy, 0);
    }
    copy->Release();
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2)
    {
        printf("usage: test <folder with nvngx_dlss.dll> [quality 0..5] [preset]\n");
        return 2;
    }
    unsigned quality = argc > 2 ? (unsigned)_wtoi(argv[2]) : 2, preset = argc > 3 ? (unsigned)_wtoi(argv[3]) : 13;
    printf("abi %u\n", vdn_abi_version());
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    D3D_FEATURE_LEVEL level;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &dev, &level, &ctx);
    if (FAILED(hr))
    {
        printf("no device: 0x%08X\n", (unsigned)hr);
        return 1;
    }
    const unsigned ow = 2560, oh = 1440;
    ID3D11Texture2D* any = Make(dev, 4, 4, DXGI_FORMAT_R8G8B8A8_UNORM, false);
    int rc = vdn_sr_open(argv[1], any, 0);
    printf("open %d: %s\n", rc, vdn_sr_status_text());
    if (rc != 0)
    {
        return 1;
    }
    unsigned rw = ow, rh = oh, minW, minH, maxW, maxH;
    float sharp;
    if (quality != 5)
    {
        rc = vdn_sr_query(ow, oh, quality == 4 ? 2 : quality, &rw, &rh, &minW, &minH, &maxW, &maxH, &sharp);
        printf("query %d: render %ux%u (min %ux%u, max %ux%u)\n", rc, rw, rh, minW, minH, maxW, maxH);
    }
    ID3D11Texture2D* color = Make(dev, rw, rh, DXGI_FORMAT_R16G16B16A16_FLOAT, false);
    ID3D11Texture2D* motion = Make(dev, rw, rh, DXGI_FORMAT_R16G16_FLOAT, false);
    ID3D11Texture2D* depth = Make(dev, rw, rh, DXGI_FORMAT_R32_FLOAT, false);
    ID3D11Texture2D* output = Make(dev, ow, oh, DXGI_FORMAT_R16G16B16A16_FLOAT, true);
    // (A colour that is not black, to see it come out the other end.)
    ID3D11RenderTargetView* view = nullptr;
    dev->CreateRenderTargetView(color, nullptr, &view);
    const float grey[4] = { 0.5f, 0.25f, 0.125f, 1.0f }, none[4] = { 0, 0, 0, 0 };
    ctx->ClearRenderTargetView(view, grey);
    view->Release();
    dev->CreateRenderTargetView(output, nullptr, &view);
    ctx->ClearRenderTargetView(view, none);
    view->Release();
    Mean(dev, ctx, output, "output before");

    vdn_sr_configure(rw, rh, ow, oh, quality == 4 ? 2 : quality, preset, 1, 1, 0, 1);
    rc = vdn_sr_set_textures(0, color, motion, depth, output);
    printf("set_textures %d\n", rc);
    typedef void(__stdcall * EventFn)(int);
    EventFn run = (EventFn)vdn_get_render_event_func();
    for (int i = 0; i < 12; i++)
    {
        int slot = i & 7;
        vdn_sr_set_frame(slot, 0, (i & 1) ? 0.25f : -0.25f, (i & 2) ? 0.25f : -0.25f, -(float)rw, (float)rh, i < 2 ? 1 : 0);
        run(0x200 | (slot << 16));
        if (i == 0 || i == 11)
        {
            printf("frame %d: ready %d, error %d: %s\n", i, vdn_sr_ready(), vdn_sr_error(), vdn_sr_status_text());
        }
    }
    ctx->Flush();
    Mean(dev, ctx, output, "output after");
    // (That the game's state is left alone: a marker is set before, and read after.)
    vdn_sr_close();
    printf("closed: %s\n", vdn_sr_status_text());
    return 0;
}
