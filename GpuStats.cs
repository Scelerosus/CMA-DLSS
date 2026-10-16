using System;
using System.Runtime.InteropServices;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal static class GpuStats
{
	private struct PstatesInfo
	{
		public uint Version;

		public uint Flags;

		public unsafe fixed uint Utilization[16];
	}

	private struct AdapterDesc1
	{
		public unsafe fixed char Description[128];

		public uint VendorId;

		public uint DeviceId;

		public uint SubSysId;

		public uint Revision;

		public UIntPtr DedicatedVideoMemory;

		public UIntPtr DedicatedSystemMemory;

		public UIntPtr SharedSystemMemory;

		public uint LuidLow;

		public int LuidHigh;

		public uint Flags;
	}

	private struct MemoryInfo
	{
		public ulong Budget;

		public ulong CurrentUsage;

		public ulong AvailableForReservation;

		public ulong CurrentReservation;
	}

	private static IntPtr s_gpu;

	private static IntPtr s_pstates;

	private static bool s_nvTried;

	internal static string NvWhy = "";

	private static IntPtr s_adapter;

	private static bool s_tried;

	internal static ulong Dedicated;

	internal static string Why = "";

	[DllImport("dxgi.dll")]
	private unsafe static extern int CreateDXGIFactory1(Guid* riid, IntPtr* factory);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadLibraryW(string path);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	private unsafe static bool NvOpen()
	{
		if (s_pstates != IntPtr.Zero)
		{
			return true;
		}
		if (s_nvTried)
		{
			return false;
		}
		s_nvTried = true;
		try
		{
			IntPtr intPtr = LoadLibraryW("nvapi64.dll");
			IntPtr intPtr2 = ((intPtr != IntPtr.Zero) ? GetProcAddress(intPtr, "nvapi_QueryInterface") : IntPtr.Zero);
			if (intPtr2 == IntPtr.Zero)
			{
				NvWhy = "no NVIDIA driver interface";
				return false;
			}
			delegate* unmanaged[Cdecl]<uint, IntPtr> obj = (delegate* unmanaged[Cdecl]<uint, IntPtr>)(void*)intPtr2;
			IntPtr intPtr3 = obj(22079528u);
			IntPtr intPtr4 = obj(3853292063u);
			IntPtr intPtr5 = obj(1625215725u);
			if (intPtr3 == IntPtr.Zero || intPtr4 == IntPtr.Zero || intPtr5 == IntPtr.Zero)
			{
				NvWhy = "the driver has no such functions";
				return false;
			}
			if (((delegate* unmanaged[Cdecl]<int>)(void*)intPtr3)() != 0)
			{
				NvWhy = "the driver interface did not start";
				return false;
			}
			IntPtr* ptr = stackalloc IntPtr[64];
			uint num = 0u;
			if (((delegate* unmanaged[Cdecl]<IntPtr*, uint*, int>)(void*)intPtr4)(ptr, &num) != 0 || num == 0)
			{
				NvWhy = "no NVIDIA card";
				return false;
			}
			s_gpu = *ptr;
			s_pstates = intPtr5;
			return true;
		}
		catch (Exception ex)
		{
			NvWhy = ex.Message;
			return false;
		}
	}

	internal unsafe static int Load()
	{
		if (!NvOpen())
		{
			return -1;
		}
		PstatesInfo pstatesInfo = new PstatesInfo
		{
			Version = (uint)(sizeof(PstatesInfo) | 0x10000)
		};
		if (((delegate* unmanaged[Cdecl]<IntPtr, PstatesInfo*, int>)(void*)s_pstates)(s_gpu, &pstatesInfo) != 0 || (pstatesInfo.Utilization[0] & 1) == 0)
		{
			return -1;
		}
		return (int)pstatesInfo.Utilization[1];
	}

	private unsafe static IntPtr Method(IntPtr obj, int index)
	{
		return *(IntPtr*)((nint)(*(IntPtr*)(void*)obj) + (nint)index * (nint)sizeof(IntPtr));
	}

	private unsafe static void Release(IntPtr obj)
	{
		if (obj != IntPtr.Zero)
		{
			((delegate* unmanaged<IntPtr, uint>)(void*)Method(obj, 2))(obj);
		}
	}

	private unsafe static bool Open()
	{
		if (s_adapter != IntPtr.Zero)
		{
			return true;
		}
		if (s_tried)
		{
			return false;
		}
		s_tried = true;
		IntPtr zero = IntPtr.Zero;
		try
		{
			Guid guid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
			int num = CreateDXGIFactory1(&guid, &zero);
			if (num < 0 || zero == IntPtr.Zero)
			{
				Why = $"no DXGI factory (0x{num:X8})";
				return false;
			}
			Guid guid2 = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
			IntPtr intPtr = IntPtr.Zero;
			ulong num2 = 0uL;
			for (uint num3 = 0u; num3 < 16; num3++)
			{
				IntPtr zero2 = IntPtr.Zero;
				if (((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)(void*)Method(zero, 12))(zero, num3, &zero2) < 0 || zero2 == IntPtr.Zero)
				{
					break;
				}
				AdapterDesc1 adapterDesc = default;
				bool flag = true;
				if (((delegate* unmanaged<IntPtr, AdapterDesc1*, int>)(void*)Method(zero2, 10))(zero2, &adapterDesc) >= 0)
				{
					flag = (adapterDesc.Flags & 2) != 0;
				}
				ulong num4 = adapterDesc.DedicatedVideoMemory.ToUInt64();
				if (!flag && num4 > num2)
				{
					Release(intPtr);
					intPtr = zero2;
					num2 = num4;
				}
				else
				{
					Release(zero2);
				}
			}
			if (intPtr == IntPtr.Zero)
			{
				Why = "no video card found";
				return false;
			}
			IntPtr zero3 = IntPtr.Zero;
			num = ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)(void*)Method(intPtr, 0))(intPtr, &guid2, &zero3);
			Release(intPtr);
			if (num < 0 || zero3 == IntPtr.Zero)
			{
				Why = "Windows gives no video memory budget here";
				return false;
			}
			s_adapter = zero3;
			Dedicated = num2;
			return true;
		}
		catch (Exception ex)
		{
			Why = ex.Message;
			return false;
		}
		finally
		{
			Release(zero);
		}
	}

	internal unsafe static bool Read(int group, out ulong usage, out ulong budget)
	{
		usage = 0uL;
		budget = 0uL;
		if (!Open())
		{
			return false;
		}
		MemoryInfo memoryInfo = default;
		if (((delegate* unmanaged<IntPtr, uint, int, MemoryInfo*, int>)(void*)Method(s_adapter, 14))(s_adapter, 0u, group, &memoryInfo) < 0)
		{
			return false;
		}
		usage = memoryInfo.CurrentUsage;
		budget = memoryInfo.Budget;
		return true;
	}
}
