using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal static class Fluids
{
	internal sealed class Switch
	{
		internal string Name;

		internal ConfigEntry<bool> Entry;

		internal string Namespace;

		internal string Class;

		internal IntPtr Field;

		internal bool Missing;

		internal bool Seen;

		internal int Was = -1;

		internal int Now = -1;
	}

	internal static Switch[] All = new Switch[0];

	private static IntPtr s_image;

	private static bool s_imageTried;

	private static float s_at;

	internal static void Init()
	{
		All = new Switch[5]
		{
			new Switch
			{
				Name = "Semen on the GPU",
				Entry = Plugin.FluidSemen,
				Namespace = "Assets.NewFeatures.GpuSemen",
				Class = "GpuSemenConfig"
			},
			new Switch
			{
				Name = "Squirt on the GPU",
				Entry = Plugin.FluidSquirt,
				Namespace = "Assets.NewFeatures.GpuSquirt",
				Class = "GpuSquirtConfig"
			},
			new Switch
			{
				Name = "Simulated fluid (saliva, hers)",
				Entry = Plugin.FluidSim,
				Namespace = "Assets.NewFeatures.GpuFluidSim",
				Class = "GpuFluidSimConfig"
			},
			new Switch
			{
				Name = "Saliva during oral",
				Entry = Plugin.FluidDrool,
				Namespace = "Assets.NewFeatures.OralDrool",
				Class = "OralDroolConfig"
			},
			new Switch
			{
				Name = "Her fluid",
				Entry = Plugin.FluidVag,
				Namespace = "Assets.NewFeatures.VagFluid",
				Class = "VagFluidConfig"
			}
		};
		for (int i = 0; i < All.Length; i++)
		{
			All[i].Seen = All[i].Entry.Value;
		}
	}

	private static IntPtr Image()
	{
		if (s_imageTried)
		{
			return s_image;
		}
		s_imageTried = true;
		IntPtr intPtr = IL2CPP.il2cpp_domain_get();
		string[] array = new string[2] { "SMA.Features", "SMA.Features.dll" };
		for (int i = 0; i < array.Length; i++)
		{
			if (!(s_image == IntPtr.Zero))
			{
				break;
			}
			IntPtr intPtr2 = Marshal.StringToHGlobalAnsi(array[i]);
			try
			{
				IntPtr intPtr3 = IL2CPP.il2cpp_domain_assembly_open(intPtr, intPtr2);
				if (intPtr3 != IntPtr.Zero)
				{
					s_image = IL2CPP.il2cpp_assembly_get_image(intPtr3);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr2);
			}
		}
		return s_image;
	}

	private static string Find(Switch s)
	{
		IntPtr intPtr = Image();
		if (intPtr == IntPtr.Zero)
		{
			return "SMA.Features is not in this game";
		}
		IntPtr intPtr2 = IL2CPP.il2cpp_class_from_name(intPtr, s.Namespace, s.Class);
		if (intPtr2 == IntPtr.Zero)
		{
			return s.Class + " is not in this game";
		}
		IntPtr intPtr3 = IL2CPP.il2cpp_class_get_field_from_name(intPtr2, "Enabled");
		if (intPtr3 == IntPtr.Zero)
		{
			return s.Class + ".Enabled is not in this game";
		}
		int num = IL2CPP.il2cpp_field_get_flags(intPtr3);
		IntPtr intPtr4 = IL2CPP.il2cpp_field_get_type(intPtr3);
		if ((num & 0x10) == 0 || (num & 0x40) != 0 || intPtr4 == IntPtr.Zero || IL2CPP.il2cpp_type_get_type(intPtr4) != 2)
		{
			return s.Class + ".Enabled is not a static switch here";
		}
		IL2CPP.il2cpp_runtime_class_init(intPtr2);
		s.Field = intPtr3;
		return null;
	}

	private unsafe static bool Get(Switch s)
	{
		byte b = 0;
		IL2CPP.il2cpp_field_static_get_value(s.Field, (void*)(&b));
		return b != 0;
	}

	private unsafe static void Set(Switch s, bool on)
	{
		byte b = (on ? ((byte)1) : ((byte)0));
		IL2CPP.il2cpp_field_static_set_value(s.Field, (void*)(&b));
	}

	internal static void Step(float now, bool look)
	{
		if (now < 5f)
		{
			return;
		}
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < All.Length; i++)
		{
			flag |= All[i].Entry.Value != All[i].Seen;
			flag2 |= !All[i].Missing && (!All[i].Entry.Value || All[i].Was >= 0);
		}
		if (!flag && (now < s_at || (!flag2 && !look)))
		{
			return;
		}
		s_at = now + (look ? 0.5f : 1f);
		for (int j = 0; j < All.Length; j++)
		{
			Switch obj = All[j];
			obj.Seen = obj.Entry.Value;
			if (obj.Missing)
			{
				continue;
			}
			try
			{
				if (!(obj.Field == IntPtr.Zero))
				{
					goto IL_013c;
				}
				string text = Find(obj);
				if (text == null)
				{
					goto IL_013c;
				}
				obj.Missing = true;
				Plugin.Info((object)("Fluids: " + obj.Name + " left alone: " + text + "."));
				goto end_IL_00d8;
				IL_013c:
				bool flag3 = Get(obj);
				if (!obj.Entry.Value)
				{
					if (flag3)
					{
						obj.Was = 1;
						Set(obj, on: false);
						flag3 = false;
						Plugin.Info((object)("Fluids: " + obj.Name + " kept off."));
					}
					else if (obj.Was < 0)
					{
						obj.Was = 0;
					}
				}
				else if (obj.Was >= 0)
				{
					if (obj.Was == 1 && !flag3)
					{
						Set(obj, on: true);
						flag3 = true;
					}
					obj.Was = -1;
					Plugin.Info((object)("Fluids: " + obj.Name + " left to the game again."));
				}
				obj.Now = (flag3 ? 1 : 0);
				end_IL_00d8:;
			}
			catch (Exception ex)
			{
				obj.Missing = true;
				Plugin.Info((object)("Fluids: " + obj.Name + " left alone (" + ex.GetType().Name + ": " + ex.Message + ")."));
			}
		}
	}

	internal static string State(Switch s)
	{
		if (s.Missing)
		{
			return "not in this game";
		}
		if (s.Now < 0)
		{
			return "";
		}
		if (s.Now == 1)
		{
			return "on";
		}
		if (!s.Entry.Value)
		{
			return "kept off";
		}
		return "off (the game's)";
	}
}
