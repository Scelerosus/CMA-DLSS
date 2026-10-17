using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal static class Native
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate uint AbiVersionFn();

	private const string Lib = "DlssBridge";

	internal const uint Abi = 4u;

	private static IntPtr s_module;

	internal static bool Loaded;

	private static readonly byte[] s_floorLoad = new byte[8] { 242, 15, 16, 53, 21, 246, 2, 0 };

	private static double s_floorWritten = 30.0;

	internal static string PluginDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

	internal static string SnippetDir => Path.Combine(PluginDir, "ngx");

	internal static string SnippetPath => Path.Combine(SnippetDir, "nvngx_dlssnr.dll");

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryW(string path);

	[DllImport("kernel32", SetLastError = true)]
	private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

	internal static string SetPacingFloor(double hz)
	{
		try
		{
			if (s_module == IntPtr.Zero)
			{
				return "the native half is not loaded";
			}
			for (int i = 0; i < s_floorLoad.Length; i++)
			{
				if (Marshal.ReadByte(s_module, 18267 + i) != s_floorLoad[i])
				{
					return "DlssBridge.dll is of another build (the pacing code is not where it was)";
				}
			}
			IntPtr intPtr = s_module + 212344;
			double num = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(intPtr));
			if (num != 30.0 && num != s_floorWritten)
			{
				return "DlssBridge.dll is of another build (its pacing floor is " + num + ")";
			}
			if (!VirtualProtect(intPtr, (UIntPtr)8u, 4u, out var old))
			{
				return "the page could not be made writable (win32 error " + Marshal.GetLastWin32Error() + ")";
			}
			Marshal.WriteInt64(intPtr, BitConverter.DoubleToInt64Bits(hz));
			VirtualProtect(intPtr, (UIntPtr)8u, old, out var _);
			s_floorWritten = hz;
			return null;
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}

	internal static bool Load()
	{
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Expected Obj, but got Unknown
		string text = Path.Combine(PluginDir, "nvngx.dll", "DlssBridge.dll");
		if (!File.Exists(text))
		{
			Plugin.ModLog.LogError((object)("DlssBridge.dll is missing: expected at " + text));
			return false;
		}
		if (!File.Exists(SnippetPath))
		{
			Plugin.ModLog.LogError((object)("nvngx_dlssnr.dll is missing: expected at " + SnippetPath));
			return false;
		}
		s_module = LoadLibraryW(text);
		if (s_module == IntPtr.Zero)
		{
			Plugin.ModLog.LogError((object)("DlssBridge.dll could not be loaded from " + text + " (win32 error " + Marshal.GetLastWin32Error() + ")."));
			return false;
		}
		NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (string name, Assembly assembly, DllImportSearchPath? search) =>
		{
			IntPtr result = ((!(name == "DlssBridge")) ? ((!(name == "CMAPresenter")) ? IntPtr.Zero : Presenter.Module) : s_module);
			return result;
		});
		uint num = 0u;
		try
		{
			if (NativeLibrary.TryGetExport(s_module, "vdn_abi_version", out var address))
			{
				num = Marshal.GetDelegateForFunctionPointer<AbiVersionFn>(address)();
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning((object)("The native ABI version could not be read (" + ex.Message + ")."));
		}
		if (num != 4)
		{
			ManualLogSource modLog = Plugin.ModLog;
			bool flag = false;
			BepInExErrorLogInterpolatedStringHandler val = new BepInExErrorLogInterpolatedStringHandler(94, 2, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("DlssBridge.dll is of another build (its ABI is ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(num);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", this plugin is written for ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(4u);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("). Nothing is run.");
			}
			modLog.LogError(val);
			return false;
		}
		Loaded = true;
		Presenter.Load(s_module);
		if (!Presenter.Loaded)
		{
			Plugin.ModLog.LogWarning((object)("Frame generation's own presenter is not available: " + Presenter.Why + "; the bridge shows the frames itself."));
		}
		return true;
	}

	[DllImport("DlssBridge")]
	internal static extern int vdn_view_open();

	[DllImport("DlssBridge", CharSet = CharSet.Unicode)]
	internal static extern int vdn_view_init(int view, string snippetPath, uint width, uint height, uint passes, uint wantMask, IntPtr anyUnityTexture);

	[DllImport("DlssBridge")]
	internal static extern void vdn_view_close(int view);

	[DllImport("DlssBridge")]
	internal static extern int vdn_set_textures(int view, IntPtr color, IntPtr mvec, IntPtr depth, IntPtr output, IntPtr mask, uint srcW, uint srcH, uint baseX, uint baseY);

	[DllImport("DlssBridge")]
	internal static extern void vdn_set_params(int view, float intensity, float localTone, float localStructure, float skinStructure, float mvecScaleX, float mvecScaleY, int renderPreset, int style, int depthInverted, int uiCorrection, int controlMask, int reset);

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_get_render_event_func();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_frames_evaluated(int view);

	[DllImport("DlssBridge")]
	internal static extern uint vdn_frames_skipped(int view);

	[DllImport("DlssBridge")]
	internal static extern int vdn_ready(int view);

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_status_text(int view);

	[DllImport("DlssBridge")]
	internal static extern uint vdn_last_ngx_result(int view);

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_reject_text(int view);

	[DllImport("DlssBridge")]
	internal static extern uint vdn_ring_waits(int view);

	[DllImport("DlssBridge")]
	internal static extern uint vdn_ring_timeouts(int view);

	[DllImport("DlssBridge")]
	internal static extern ulong vdn_ring_wait_micros(int view);

	[DllImport("DlssBridge")]
	internal static extern ulong vdn_ring_wait_worst_micros(int view);

	[DllImport("DlssBridge")]
	internal static extern void vdn_rebuild_begin();

	[DllImport("DlssBridge")]
	internal static extern void vdn_rebuild_end();

	[DllImport("DlssBridge", CharSet = CharSet.Unicode)]
	internal static extern int vdn_sr_open(string snippetDir, IntPtr anyUnityTexture, int verboseLog);

	[DllImport("DlssBridge")]
	internal static extern int vdn_sr_query(uint outW, uint outH, uint quality, out uint renderW, out uint renderH, out uint minW, out uint minH, out uint maxW, out uint maxH, out float sharpness);

	[DllImport("DlssBridge")]
	internal static extern void vdn_sr_configure(uint renderW, uint renderH, uint outW, uint outH, uint quality, uint preset, int depthInverted, int autoExposure, int isHdr, uint views);

	[DllImport("DlssBridge")]
	internal static extern int vdn_sr_set_textures(int view, IntPtr color, IntPtr mvec, IntPtr depth, IntPtr output);

	[DllImport("DlssBridge")]
	internal static extern void vdn_sr_set_frame(int slot, int view, float jitterX, float jitterY, float mvScaleX, float mvScaleY, int reset);

	[DllImport("DlssBridge")]
	internal static extern void vdn_sr_close();

	[DllImport("DlssBridge")]
	internal static extern int vdn_sr_ready();

	[DllImport("DlssBridge")]
	internal static extern int vdn_sr_error();

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_sr_status_text();

	[DllImport("DlssBridge", CharSet = CharSet.Unicode)]
	internal static extern int vdn_fg_open(string snippetDir, IntPtr anyUnityTexture, int verboseLog);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_enable(int on);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_set_pacing(int on);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_set_menu_detection(int on);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_set_multiplier(int total);

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_max_multiplier();

	[DllImport("DlssBridge")]
	internal static extern int vdn_fg_generating();

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_reset();

	[DllImport("DlssBridge")]
	internal static extern int vdn_fg_set_textures(IntPtr mvec, IntPtr depth);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_set_frame(float[] viewToClip, float[] clipToView, float[] clipToPrevClip, float[] prevClipToClip, float[] basis, float nearZ, float farZ, float fovRadians, float aspect, float mvScaleX, float mvScaleY, float jitterX, float jitterY, int depthInverted, int cameraMotionIncluded);

	[DllImport("DlssBridge")]
	internal static extern void vdn_fg_close();

	[DllImport("DlssBridge")]
	internal static extern int vdn_fg_ready();

	[DllImport("DlssBridge")]
	internal static extern int vdn_fg_hook_installed();

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_fg_status_text();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_interpolated();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_presented();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_passed_through();

	[DllImport("DlssBridge")]
	internal static extern double vdn_fg_base_hz();

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_fg_gate_summary();

	[DllImport("DlssBridge")]
	internal static extern IntPtr vdn_fg_hook_report();

	[DllImport("DlssBridge")]
	internal static extern double vdn_fg_gpu_ms();

	[DllImport("DlssBridge")]
	internal static extern double vdn_fg_hook_ms();

	[DllImport("DlssBridge")]
	internal static extern double vdn_fg_unpaced_hz();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_lock_missed();

	[DllImport("DlssBridge")]
	internal static extern uint vdn_fg_stale_camera();

	internal static string Text(IntPtr p)
	{
		try
		{
			return (p != IntPtr.Zero) ? (Marshal.PtrToStringAnsi(p) ?? "") : "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	internal static int NrEvent(int view)
	{
		return 0x100 | view;
	}

	internal static int SrEvent(int slot)
	{
		return 0x200 | ((slot & 7) << 16);
	}
}
