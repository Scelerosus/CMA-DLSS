using System;
using System.IO;
using System.Runtime.InteropServices;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal static class Presenter
{
	private const string Lib = "CMAPresenter";

	internal const int Abi = 1;

	private static readonly (int rva, byte[] bytes)[] s_uses = new (int, byte[])[4]
	{
		(18589, new byte[6] { 255, 21, 205, 136, 4, 0 }),
		(19123, new byte[6] { 255, 21, 183, 134, 4, 0 }),
		(20285, new byte[7] { 72, 139, 5, 52, 130, 4, 0 }),
		(20485, new byte[7] { 72, 139, 5, 100, 129, 4, 0 })
	};

	internal static IntPtr Module;

	internal static bool Loaded;

	internal static string Why = "not loaded";

	internal static string DllPath => Path.Combine(Native.PluginDir, "nvngx.dll", "CMAPresenter.dll");

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryW(string path);

	internal static void Load(IntPtr bridge)
	{
		try
		{
			if (!File.Exists(DllPath))
			{
				Why = "CMAPresenter.dll is not beside DlssBridge.dll";
				return;
			}
			(int, byte[])[] array = s_uses;
			for (int i = 0; i < array.Length; i++)
			{
				(int, byte[]) tuple = array[i];
				int item = tuple.Item1;
				byte[] item2 = tuple.Item2;
				for (int j = 0; j < item2.Length; j++)
				{
					if (Marshal.ReadByte(bridge, item + j) != item2[j])
					{
						Why = "DlssBridge.dll is of another build (its Present pointers are not where they were)";
						return;
					}
				}
			}
			Module = LoadLibraryW(DllPath);
			if (Module == IntPtr.Zero)
			{
				Why = "CMAPresenter.dll could not be loaded (win32 error " + Marshal.GetLastWin32Error() + ")";
				return;
			}
			if (cp_abi() != 1)
			{
				Why = "CMAPresenter.dll is of another build";
				return;
			}
			cp_open(bridge, 315760u, 315768u);
			Loaded = true;
			Why = "";
		}
		catch (Exception ex)
		{
			Why = "CMAPresenter.dll failed (" + ex.Message + ")";
		}
	}

	[DllImport("CMAPresenter")]
	internal static extern int cp_abi();

	[DllImport("CMAPresenter")]
	internal static extern int cp_open(IntPtr bridge, uint presentRva, uint present1Rva);

	[DllImport("CMAPresenter")]
	internal static extern int cp_tick(int want, int sync, int pace);

	[DllImport("CMAPresenter")]
	internal static extern IntPtr cp_status();

	[DllImport("CMAPresenter")]
	internal static extern int cp_failed();

	[DllImport("CMAPresenter")]
	internal static extern ulong cp_shown();

	[DllImport("CMAPresenter")]
	internal static extern ulong cp_dropped();

	[DllImport("CMAPresenter")]
	internal static extern double cp_delay_ms();

	[DllImport("CMAPresenter")]
	internal static extern double cp_gpu_ms();

	[DllImport("CMAPresenter")]
	internal static extern double cp_present_ms();

	[DllImport("CMAPresenter")]
	internal static extern double cp_spacing_ms();

}
