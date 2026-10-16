using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using UnityEngine;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal static class Bridge
{
	private static readonly Regex s_key = new Regex("^(\\s*EnableDlssNr\\s*=\\s*)(-?\\d+)", RegexOptions.Multiline);

	private static readonly HashSet<string> s_pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static float s_loadedAt = -100f;

	private static bool s_loaded;

	private const int CU_DEVICE_ATTRIBUTE_COMPUTE_CAPABILITY_MAJOR = 75;

	private const int CU_DEVICE_ATTRIBUTE_COMPUTE_CAPABILITY_MINOR = 76;

	private const uint GET_MODULE_HANDLE_EX_FLAG_PIN = 1u;

	internal static int ComputeCapability { get; private set; }

	internal static bool NativeFp8 => ComputeCapability >= 89;

	internal static string IniPath => Path.Combine(Paths.GameRootPath, "dlssg_to_fsr3.ini");

	internal static bool Installed => File.Exists(IniPath);

	internal static int ModeAtStart { get; private set; } = -1;

	internal static bool FastNetworkLoaded
	{
		get
		{
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime - s_loadedAt < 3f)
			{
				return s_loaded;
			}
			s_loadedAt = unscaledTime;
			s_loaded = false;
			try
			{
				foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
				{
					if (module.ModuleName.IndexOf("nvngx_dlssnr.rebuilt", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						s_loaded = true;
						break;
					}
				}
			}
			catch (Exception)
			{
			}
			return s_loaded;
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetModuleHandleExW(uint flags, string name, out IntPtr module);

	[DllImport("nvcuda", EntryPoint = "cuInit")]
	private static extern int CuInit(uint flags);

	[DllImport("nvcuda", EntryPoint = "cuDeviceGet")]
	private static extern int CuDeviceGet(out int device, int ordinal);

	[DllImport("nvcuda", EntryPoint = "cuDeviceGetAttribute")]
	private static extern int CuDeviceGetAttribute(out int value, int attribute, int device);

	internal static bool FastNetworkWanted(bool fastNetwork)
	{
		if (fastNetwork)
		{
			return !NativeFp8;
		}
		return false;
	}

	private static void ReadComputeCapability()
	{
		try
		{
			if (CuInit(0u) == 0 && CuDeviceGet(out var device, 0) == 0 && CuDeviceGetAttribute(out var value, 75, device) == 0 && CuDeviceGetAttribute(out var value2, 76, device) == 0)
			{
				ComputeCapability = value * 10 + value2;
			}
		}
		catch (Exception ex)
		{
			Plugin.Info((object)("Bridge: the card's compute capability could not be read (" + ex.Message + "); FastNetwork is followed as it is."));
		}
	}

	internal static void Start(bool fastNetwork)
	{
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Expected Obj, but got Unknown
		try
		{
			if (!Installed)
			{
				return;
			}
			ReadComputeCapability();
			Match match = s_key.Match(File.ReadAllText(IniPath));
			ModeAtStart = (match.Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0);
			bool flag = FastNetworkWanted(fastNetwork);
			if (ModeAtStart > 0 == flag)
			{
				return;
			}
			WriteMode(flag ? 1 : 0);
			if (ModeAtStart > 0 && NativeFp8)
			{
				ManualLogSource modLog = Plugin.ModLog;
				bool flag2 = default;
				BepInExWarningLogInterpolatedStringHandler val = new BepInExWarningLogInterpolatedStringHandler(265, 1, out flag2);
				if (flag2)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Bridge: this card (sm_");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(ComputeCapability);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(") has FP8 in hardware, and the nvidia mfg bridge's FP16 network is built for RTX 20 and 30 and does not start on it; EnableDlssNr=0 is written to dlssg_to_fsr3.ini. Neural rendering runs on NVIDIA's own network from the next start of the game.");
				}
				modLog.LogWarning(val);
			}
		}
		catch (Exception ex)
		{
			ModeAtStart = -1;
			Plugin.ModLog.LogWarning((object)("Bridge: dlssg_to_fsr3.ini could not be read (" + ex.Message + ")."));
		}
	}

	internal static void WriteMode(int mode)
	{
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected Obj, but got Unknown
		try
		{
			string text = File.ReadAllText(IniPath);
			string nl = (text.Contains("\r\n") ? "\r\n" : "\n");
			string value = mode.ToString(CultureInfo.InvariantCulture);
			File.WriteAllText(contents: (!s_key.IsMatch(text)) ? Regex.Replace(text, "^\\[Setting\\][^\\n]*\\n", (Match m) => m.Value + "EnableDlssNr=" + value + nl, RegexOptions.Multiline) : s_key.Replace(text, (Match m) => m.Groups[1].Value + value, 1), path: IniPath);
			bool flag = default;
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(102, 1, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Bridge: EnableDlssNr=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(value);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" written to dlssg_to_fsr3.ini (the mod reads it from the next start of the game).");
			}
			Plugin.Info(val);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning((object)("Bridge: dlssg_to_fsr3.ini could not be written (" + ex.Message + ")."));
		}
	}

	internal static void Pin()
	{
		try
		{
			foreach (ProcessModule module2 in Process.GetCurrentProcess().Modules)
			{
				if (module2.ModuleName.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase) && !s_pinned.Contains(module2.FileName))
				{
					if (GetModuleHandleExW(1u, module2.FileName, out var _))
					{
						s_pinned.Add(module2.FileName);
						Plugin.Info((object)("Neural rendering: " + module2.ModuleName + " pinned in memory (switching neural rendering off and on no longer reloads it)."));
						continue;
					}
					Plugin.ModLog.LogWarning((object)("Neural rendering: " + module2.ModuleName + " could not be pinned (win32 error " + Marshal.GetLastWin32Error() + ")."));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning((object)("Neural rendering: the network's module could not be looked up (" + ex.Message + ")."));
		}
	}
}
