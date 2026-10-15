using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Core.Logging.Interpolation;
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

	private const uint GET_MODULE_HANDLE_EX_FLAG_PIN = 1u;

	internal static string IniPath => Path.Combine(Paths.GameRootPath, "dlssg_to_fsr3.ini");

	internal static bool Installed => File.Exists(IniPath);

	// The bridge's FP16 network is rebuilt for Turing and Ampere (RTX 20 and 30). On Ada and Blackwell its GPU code
	// does not load and neural rendering fails to start (NvAPI_D3D12_CreateCuModule failed), so it is asked for on RTX 20
	// and 30 only. ("RTX 2000 Ada" and the like are Ada.)
	private static readonly Regex s_oldRtx = new Regex(@"RTX\s*(20|30)\d\d|TITAN RTX", RegexOptions.IgnoreCase);

	internal static string CardName => SystemInfo.graphicsDeviceName ?? "";

	internal static bool OldRtx => s_oldRtx.IsMatch(CardName) && CardName.IndexOf("Ada", StringComparison.OrdinalIgnoreCase) < 0;

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

	internal static void Start(bool fastNetwork)
	{
		try
		{
			if (Installed)
			{
				Match match = s_key.Match(File.ReadAllText(IniPath));
				ModeAtStart = (match.Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0);
				fastNetwork = fastNetwork && OldRtx;
				if (ModeAtStart > 0 != fastNetwork)
				{
					WriteMode(fastNetwork ? 1 : 0);
				}
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
						Plugin.Info((object)("NGX: " + module2.ModuleName + " pinned in memory (closing and opening a feature again no longer unloads and reloads it)."));
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
