using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CMADlssNr
{
    // The native half: DlssBridge.dll does all the work with NVIDIA's NGX (it opens a D3D12 session of its own beside
    // the game's D3D11 device, shares the textures it is handed, and runs the features on the render thread when its
    // render event is issued). This file is its interface; only what this plugin uses.
    //
    // Layout of the plugin folder: nvngx.dll\DlssBridge.dll (a folder of that name), and
    // ngx\nvngx_dlssnr.dll, ngx\nvngx_dlss.dll (NVIDIA's own; its logs are written there too).
    internal static class Native
    {
        private const string Lib = "DlssBridge";
        // DLSS super resolution under a name of its own: the mod's own native library (native\src\bridge.cpp, set
        // aside for now) would be loaded under it. Both names are the one library.
        private const string LibSr = "CmaBridge";
        internal const uint Abi = 4;

        private static IntPtr s_module, s_own = IntPtr.Zero;
        internal static bool Loaded, Own = false;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint AbiVersionFn();

        internal static string PluginDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        internal static string SnippetDir => Path.Combine(PluginDir, "ngx");
        internal static string SnippetPath => Path.Combine(SnippetDir, "nvngx_dlssnr.dll");

        internal static bool Load()
        {
            string path = Path.Combine(PluginDir, "nvngx.dll", "DlssBridge.dll");
            if (!File.Exists(path))
            {
                Plugin.ModLog.LogError("DlssBridge.dll is missing: expected at " + path);
                return false;
            }
            if (!File.Exists(SnippetPath))
            {
                Plugin.ModLog.LogError("nvngx_dlssnr.dll is missing: expected at " + SnippetPath);
                return false;
            }
            s_module = LoadLibraryW(path);
            if (s_module == IntPtr.Zero)
            {
                Plugin.ModLog.LogError("DlssBridge.dll could not be loaded from " + path + " (win32 error " + Marshal.GetLastWin32Error() + ").");
                return false;
            }
            // (Its functions are found in the module that was just loaded, wherever the runtime would look for it.)
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, search) => name == Lib ? s_module : name == LibSr ? (Own ? s_own : s_module) : IntPtr.Zero);
            uint abi = 0;
            try
            {
                if (NativeLibrary.TryGetExport(s_module, "vdn_abi_version", out IntPtr fn))
                {
                    abi = Marshal.GetDelegateForFunctionPointer<AbiVersionFn>(fn)();
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLog.LogWarning("The native ABI version could not be read (" + ex.Message + ").");
            }
            if (abi != Abi)
            {
                Plugin.ModLog.LogError($"DlssBridge.dll is of another build (its ABI is {abi}, this plugin is written for {Abi}). Nothing is run.");
                return false;
            }
            Loaded = true;
            return true;
        }

        // ---- neural rendering ----
        [DllImport(Lib)] internal static extern int vdn_view_open();
        [DllImport(Lib, CharSet = CharSet.Unicode)] internal static extern int vdn_view_init(int view, string snippetPath, uint width, uint height, uint passes, uint wantMask, IntPtr anyUnityTexture);
        [DllImport(Lib)] internal static extern void vdn_view_close(int view);
        [DllImport(Lib)] internal static extern void vdn_shutdown();
        [DllImport(Lib)] internal static extern int vdn_set_textures(int view, IntPtr color, IntPtr mvec, IntPtr depth, IntPtr output, IntPtr mask, uint srcW, uint srcH, uint baseX, uint baseY);
        [DllImport(Lib)] internal static extern void vdn_set_params(int view, float intensity, float localTone, float localStructure, float skinStructure, float mvecScaleX, float mvecScaleY, int renderPreset, int style, int depthInverted, int uiCorrection, int controlMask, int reset);
        [DllImport(Lib)] internal static extern IntPtr vdn_get_render_event_func();
        [DllImport(Lib)] internal static extern uint vdn_frames_evaluated(int view);
        [DllImport(Lib)] internal static extern uint vdn_frames_skipped(int view);
        [DllImport(Lib)] internal static extern int vdn_ready(int view);
        [DllImport(Lib)] internal static extern IntPtr vdn_status_text(int view);
        [DllImport(Lib)] internal static extern uint vdn_last_ngx_result(int view);
        [DllImport(Lib)] internal static extern IntPtr vdn_reject_text(int view);
        [DllImport(Lib)] internal static extern uint vdn_ring_waits(int view);
        [DllImport(Lib)] internal static extern uint vdn_ring_timeouts(int view);
        [DllImport(Lib)] internal static extern ulong vdn_ring_wait_micros(int view);
        [DllImport(Lib)] internal static extern ulong vdn_ring_wait_worst_micros(int view);
        [DllImport(Lib, EntryPoint = "vdn_rebuild_begin")] private static extern void RebuildBeginFirst();
        [DllImport(Lib, EntryPoint = "vdn_rebuild_end")] private static extern void RebuildEndFirst();
        [DllImport(LibSr, EntryPoint = "vdn_rebuild_begin")] private static extern void RebuildBeginOwn();
        [DllImport(LibSr, EntryPoint = "vdn_rebuild_end")] private static extern void RebuildEndOwn();
        [DllImport(LibSr, EntryPoint = "vdn_get_render_event_func")] internal static extern IntPtr vdn_sr_get_render_event_func();

        // While features are made anew the render thread is kept out: of both libraries, when there are two.
        internal static void vdn_rebuild_begin()
        {
            RebuildBeginFirst();
            if (Own)
            {
                RebuildBeginOwn();
            }
        }

        internal static void vdn_rebuild_end()
        {
            if (Own)
            {
                RebuildEndOwn();
            }
            RebuildEndFirst();
        }

        // ---- DLSS super resolution (for the next step: the reduced render) ----
        [DllImport(LibSr, CharSet = CharSet.Unicode)] internal static extern int vdn_sr_open(string snippetDir, IntPtr anyUnityTexture, int verboseLog);
        [DllImport(LibSr)] internal static extern int vdn_sr_query(uint outW, uint outH, uint quality, out uint renderW, out uint renderH, out uint minW, out uint minH, out uint maxW, out uint maxH, out float sharpness);
        [DllImport(LibSr)] internal static extern void vdn_sr_configure(uint renderW, uint renderH, uint outW, uint outH, uint quality, uint preset, int depthInverted, int autoExposure, int isHdr, uint views);
        [DllImport(LibSr)] internal static extern int vdn_sr_set_textures(int view, IntPtr color, IntPtr mvec, IntPtr depth, IntPtr output);
        [DllImport(LibSr)] internal static extern void vdn_sr_set_frame(int slot, int view, float jitterX, float jitterY, float mvScaleX, float mvScaleY, int reset);
        [DllImport(LibSr)] internal static extern void vdn_sr_close();
        [DllImport(LibSr)] internal static extern int vdn_sr_ready();
        [DllImport(LibSr)] internal static extern int vdn_sr_error();
        [DllImport(LibSr)] internal static extern IntPtr vdn_sr_status_text();

        // ---- DLSS frame generation ----
        [DllImport(Lib, CharSet = CharSet.Unicode)] internal static extern int vdn_fg_open(string snippetDir, IntPtr anyUnityTexture, int verboseLog);
        [DllImport(Lib)] internal static extern void vdn_fg_enable(int on);
        [DllImport(Lib)] internal static extern void vdn_fg_set_pacing(int on);
        [DllImport(Lib)] internal static extern void vdn_fg_set_menu_detection(int on);
        [DllImport(Lib)] internal static extern void vdn_fg_set_multiplier(int total);
        [DllImport(Lib)] internal static extern uint vdn_fg_max_multiplier();
        [DllImport(Lib)] internal static extern int vdn_fg_generating();
        [DllImport(Lib)] internal static extern void vdn_fg_reset();
        [DllImport(Lib)] internal static extern int vdn_fg_set_textures(IntPtr mvec, IntPtr depth);
        [DllImport(Lib)] internal static extern void vdn_fg_set_frame(float[] viewToClip, float[] clipToView, float[] clipToPrevClip, float[] prevClipToClip, float[] basis, float nearZ, float farZ, float fovRadians, float aspect, float mvScaleX, float mvScaleY, float jitterX, float jitterY, int depthInverted, int cameraMotionIncluded);
        [DllImport(Lib)] internal static extern void vdn_fg_close();
        [DllImport(Lib)] internal static extern int vdn_fg_ready();
        [DllImport(Lib)] internal static extern int vdn_fg_hook_installed();
        [DllImport(Lib)] internal static extern IntPtr vdn_fg_status_text();
        [DllImport(Lib)] internal static extern uint vdn_fg_interpolated();
        [DllImport(Lib)] internal static extern uint vdn_fg_presented();
        [DllImport(Lib)] internal static extern uint vdn_fg_passed_through();
        [DllImport(Lib)] internal static extern double vdn_fg_base_hz();
        [DllImport(Lib)] internal static extern IntPtr vdn_fg_gate_summary();
        [DllImport(Lib)] internal static extern IntPtr vdn_fg_hook_report();

        internal static string Text(IntPtr p)
        {
            try
            {
                return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) ?? "" : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        internal static int NrEvent(int view) => 0x100 | view;
        internal static int SrEvent(int slot) => 0x200 | ((slot & 7) << 16);
    }
}
