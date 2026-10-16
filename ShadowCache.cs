using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

internal sealed class ShadowCache
{
	private sealed class Held
	{
		public IntPtr key;

		public Renderer renderer;

		public Transform transform;

		public bool batched;

		public bool wasStatic;

		public bool marked;

		public bool dead;

		public float stillSince;
	}

	private sealed class Kept
	{
		public HDAdditionalLightData light;

		public ShadowUpdateMode mode;

		public bool dynamic;

		public bool movement;

		public bool onPlacement;

		public long room;

		public int checkAt;

		public int misses;

		public bool placed;
	}

	private const float StillFor = 10f;

	private const float BatchedStillFor = 1f;

	private readonly Dictionary<IntPtr, Held> m_held = new Dictionary<IntPtr, Held>();

	private readonly List<Held> m_list = new List<Held>();

	private readonly HashSet<IntPtr> m_skipped = new HashSet<IntPtr>();

	private readonly Dictionary<IntPtr, Kept> m_kept = new Dictionary<IntPtr, Kept>();

	private readonly HashSet<IntPtr> m_noRoom = new HashSet<IntPtr>();

	private int m_next;

	private int m_marked;

	private long m_roomUsed;

	private long m_roomAll = -1L;

	private int m_atlas;

	private float m_scanAt;

	private float m_skippedClearAt;

	private bool m_redraw;

	private bool m_on;

	private bool m_failed;

	private int m_tellings;

	private int m_toldKept = -1;

	private int m_toldNoRoom = -1;

	internal string State = "off";

	private static int s_restart;

	private static RenderPipelineAsset s_restartQuality;

	private static RenderPipelineAsset s_restartDefault;

	internal static string AtlasState = "";

	private readonly List<string> m_lightsSeen = new List<string>();

	internal static bool Restarting => s_restart != 0;

	internal static void Prepare(int want)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected Obj, but got Unknown
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Expected Obj, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected Obj, but got Unknown
		if (want <= 0)
		{
			AtlasState = "the game's";
			return;
		}
		try
		{
			List<RenderPipelineAsset> list = new List<RenderPipelineAsset>();
			HashSet<IntPtr> hashSet = new HashSet<IntPtr>();
			RenderPipelineAsset currentRenderPipeline = GraphicsSettings.currentRenderPipeline;
			if ((Object)currentRenderPipeline != (Object)null && hashSet.Add(((Il2CppObjectBase)currentRenderPipeline).Pointer))
			{
				list.Add(currentRenderPipeline);
			}
			RenderPipelineAsset[] array = new RenderPipelineAsset[2]
			{
				QualitySettings.renderPipeline,
				GraphicsSettings.defaultRenderPipeline
			};
			foreach (RenderPipelineAsset val in array)
			{
				if ((Object)val != (Object)null && hashSet.Add(((Il2CppObjectBase)val).Pointer))
				{
					list.Add(val);
				}
			}
			int num = 0;
			int num2 = 0;
			foreach (RenderPipelineAsset item in list)
			{
				HDRenderPipelineAsset val2 = ((Il2CppObjectBase)item).TryCast<HDRenderPipelineAsset>();
				if (!((Object)val2 == (Object)null))
				{
					RenderPipelineSettings renderPipelineSettings = val2.m_RenderPipelineSettings;
					HDShadowInitParameters hdShadowInitParams = renderPipelineSettings.hdShadowInitParams;
					int cachedPunctualLightShadowAtlas = hdShadowInitParams.cachedPunctualLightShadowAtlas;
					num = Math.Max(num, cachedPunctualLightShadowAtlas);
					if (cachedPunctualLightShadowAtlas < want)
					{
						hdShadowInitParams.cachedPunctualLightShadowAtlas = want;
						renderPipelineSettings.hdShadowInitParams = hdShadowInitParams;
						val2.m_RenderPipelineSettings = renderPipelineSettings;
						num2++;
					}
				}
			}
			if (num2 == 0)
			{
				AtlasState = $"{num}x{num}, the game's";
				return;
			}
			bool flag = RenderPipelineManager.currentPipeline != null;
			if (flag)
			{
				s_restart = 1;
			}
			AtlasState = $"{want}x{want} (the game's {num}x{num})";
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)($"Static shadows: HDRP's atlas for kept shadows is {want}x{want} instead of {num}x{num} (in {num2} pipeline asset(s))" + (flag ? "; HDRP had started already, so it is started again once" : "; HDRP had not started yet") + "."));
			}
		}
		catch (Exception ex)
		{
			AtlasState = "the game's (it could not be changed)";
			Plugin.ModLog.LogWarning((object)("Static shadows: HDRP's atlas for kept shadows could not be made bigger (" + ex.Message + ")."));
		}
	}

	internal static void RestartStep()
	{
		if (s_restart == 0)
		{
			return;
		}
		try
		{
			if (s_restart == 1)
			{
				s_restartQuality = QualitySettings.renderPipeline;
				s_restartDefault = GraphicsSettings.defaultRenderPipeline;
				QualitySettings.renderPipeline = null;
				GraphicsSettings.defaultRenderPipeline = null;
				s_restart = 2;
				return;
			}
			GraphicsSettings.defaultRenderPipeline = s_restartDefault;
			QualitySettings.renderPipeline = s_restartQuality;
			s_restart = 0;
			s_restartQuality = null;
			s_restartDefault = null;
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)("Static shadows: HDRP started again (" + ((RenderPipelineManager.currentPipeline != null) ? "it runs" : "not running yet") + ")."));
			}
		}
		catch (Exception ex)
		{
			s_restart = 0;
			Plugin.ModLog.LogWarning((object)("Static shadows: HDRP could not be started again (" + ex.Message + ")."));
		}
	}

	internal void Step(bool want)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Expected Obj, but got Unknown
		if (s_restart != 0 || RenderPipelineManager.currentPipeline == null || (Object)HDRenderPipeline.currentAsset == (Object)null)
		{
			return;
		}
		if (!want || m_failed)
		{
			if (m_on)
			{
				if (!want && m_kept.Count > 0 && Plugin.VerboseLog.Value)
				{
					Plugin.Info((object)$"Static shadows: off; {m_kept.Count} light(s) draw everything every frame again.");
				}
				Restore();
			}
			if (m_failed)
			{
				State = "off (it failed, see the log)";
			}
			return;
		}
		try
		{
			m_on = true;
			float unscaledTime = Time.unscaledTime;
			if (m_roomAll < 0)
			{
				HDShadowInitParameters hdShadowInitParams = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
				m_atlas = hdShadowInitParams.cachedPunctualLightShadowAtlas;
				m_roomAll = (long)m_atlas * (long)m_atlas;
			}
			if (m_atlas <= 0)
			{
				State = "the game's settings give HDRP no atlas for kept shadows";
				return;
			}
			if (unscaledTime >= m_scanAt)
			{
				m_scanAt = unscaledTime + 5f;
				Scan(unscaledTime);
			}
			Watch(unscaledTime);
			CheckRoom();
			if (m_redraw)
			{
				m_redraw = false;
				foreach (Kept value in m_kept.Values)
				{
					if ((Object)value.light != (Object)null && value.placed)
					{
						value.light.RequestShadowMapRendering();
					}
				}
			}
			State = $"{m_kept.Count} light(s) keep what does not move in their shadows; {m_marked} of {m_held.Count} objects held still" + ((m_noRoom.Count > 0) ? $"; {m_noRoom.Count} light(s) left as they were (no room)" : "");
			Tell();
		}
		catch (Exception ex)
		{
			m_failed = true;
			Plugin.ModLog.LogWarning((object)("Static shadows: failed (" + ex.GetType().Name + ": " + ex.Message + "); lights and objects are as they were again."));
			Restore();
		}
	}

	private void Scan(float now)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected Obj, but got Unknown
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Expected Obj, but got Unknown
		//IL_030b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0316: Expected Obj, but got Unknown
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Expected Obj, but got Unknown
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Invalid comparison between Unknown and I4
		//IL_0353: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bb: Expected Obj, but got Unknown
		//IL_0360: Unknown result type (might be due to invalid IL or missing references)
		//IL_036a: Expected Obj, but got Unknown
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Invalid comparison between Unknown and I4
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Invalid comparison between Unknown and I4
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_0238: Unknown result type (might be due to invalid IL or missing references)
		List<IntPtr> list = null;
		foreach (KeyValuePair<IntPtr, Kept> item in m_kept)
		{
			if ((Object)item.Value.light == (Object)null)
			{
				(list ?? (list = new List<IntPtr>())).Add(item.Key);
			}
		}
		if (list != null)
		{
			foreach (IntPtr item2 in list)
			{
				m_roomUsed -= m_kept[item2].room;
				m_kept.Remove(item2);
			}
		}
		HDShadowInitParameters hdShadowInitParams = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
		Il2CppArrayBase<HDAdditionalLightData> val = Object.FindObjectsOfType<HDAdditionalLightData>();
		int num = 0;
		while (val != null && num < val.Length)
		{
			HDAdditionalLightData val2 = val[num];
			if (!((Object)val2 == (Object)null))
			{
				IntPtr pointer = ((Il2CppObjectBase)val2).Pointer;
				if (!m_kept.ContainsKey(pointer) && !m_noRoom.Contains(pointer))
				{
					HDLightType type = val2.type;
					if ((int)type == 0 || (int)type == 2)
					{
						Light legacyLight = val2.legacyLight;
						if (!((Object)legacyLight == (Object)null) && ((Behaviour)legacyLight).enabled && (int)legacyLight.shadows != 0 && (int)val2.shadowUpdateMode == 0)
						{
							int resolutionFromSettings = val2.GetResolutionFromSettings(type, hdShadowInitParams);
							long num2 = (long)resolutionFromSettings * (long)resolutionFromSettings * (((int)type != 2) ? 1 : 6);
							bool flag = m_roomUsed + num2 <= (long)m_atlas * (long)m_atlas && HDCachedShadowManager.instance.WouldFitInAtlas(val2);
							if (m_lightsSeen.Count < 8)
							{
								m_lightsSeen.Add((((int)type == 2) ? "point " : "spot ") + resolutionFromSettings + (flag ? "" : " (no room)"));
							}
							if (!flag)
							{
								m_noRoom.Add(pointer);
							}
							else
							{
								Kept value = new Kept
								{
									light = val2,
									mode = val2.shadowUpdateMode,
									dynamic = val2.alwaysDrawDynamicShadows,
									movement = val2.updateUponLightMovement,
									onPlacement = val2.onDemandShadowRenderOnPlacement,
									room = num2,
									checkAt = Time.frameCount + 120
								};
								val2.alwaysDrawDynamicShadows = true;
								val2.updateUponLightMovement = true;
								val2.onDemandShadowRenderOnPlacement = true;
								val2.shadowUpdateMode = (ShadowUpdateMode)2;
								m_kept[pointer] = value;
								m_roomUsed += num2;
							}
						}
					}
				}
			}
			num++;
		}
		if (now >= m_skippedClearAt)
		{
			m_skippedClearAt = now + 60f;
			m_skipped.Clear();
		}
		Il2CppArrayBase<MeshRenderer> val3 = Object.FindObjectsOfType<MeshRenderer>();
		int num3 = 0;
		while (val3 != null && num3 < val3.Length)
		{
			MeshRenderer val4 = val3[num3];
			if (!((Object)val4 == (Object)null))
			{
				IntPtr pointer2 = ((Il2CppObjectBase)val4).Pointer;
				if (!m_held.ContainsKey(pointer2) && !m_skipped.Contains(pointer2))
				{
					bool isPartOfStaticBatch = ((Renderer)val4).isPartOfStaticBatch;
					if ((int)((Renderer)val4).shadowCastingMode == 0 || (!isPartOfStaticBatch && (Moving((Component)val4) || ((Object)val4).name.StartsWith("CMA", StringComparison.Ordinal) || StrandHair((Renderer)val4))))
					{
						m_skipped.Add(pointer2);
					}
					else
					{
						Transform transform = ((Component)val4).transform;
						transform.hasChanged = false;
						Held held = new Held
						{
							key = pointer2,
							renderer = (Renderer)val4,
							transform = transform,
							batched = isPartOfStaticBatch,
							wasStatic = ((Renderer)val4).staticShadowCaster,
							stillSince = now
						};
						m_held[pointer2] = held;
						m_list.Add(held);
					}
				}
			}
			num3++;
		}
		if (m_list.Count != m_held.Count)
		{
			m_list.RemoveAll((Held h) => h.dead);
			m_next = 0;
		}
	}

	// Strand hair (the game's GPU hair, HDRP/Hair shaders) moves its strands in the shader while its transform stays still,
	// so the cache took it for something that does not move and kept its shadow: the shadow stayed in the air when the
	// hair swung. It is left to be drawn every frame.
	private static bool StrandHair(Renderer r)
	{
		Il2CppReferenceArray<Material> sharedMaterials = r.sharedMaterials;
		for (int i = 0; sharedMaterials != null && i < sharedMaterials.Length; i++)
		{
			Material val = sharedMaterials[i];
			if ((Object)val != (Object)null && (Object)val.shader != (Object)null && ((Object)val.shader).name.StartsWith("HDRP/Hair", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool Moving(Component c)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected Obj, but got Unknown
		if (!((Object)c.GetComponentInParent<Animator>() != (Object)null))
		{
			return (Object)c.GetComponentInParent<Rigidbody>() != (Object)null;
		}
		return true;
	}

	private void Watch(float now)
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected Obj, but got Unknown
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Expected Obj, but got Unknown
		int count = m_list.Count;
		if (count == 0)
		{
			return;
		}
		int num = (count + 7) / 8;
		for (int i = 0; i < num; i++)
		{
			if (m_next >= count)
			{
				m_next = 0;
			}
			Held held = m_list[m_next++];
			if (held.dead)
			{
				continue;
			}
			Renderer renderer = held.renderer;
			if ((Object)renderer == (Object)null)
			{
				held.dead = true;
				m_held.Remove(held.key);
				if (held.marked)
				{
					held.marked = false;
					m_marked--;
					m_redraw = true;
				}
				continue;
			}
			bool flag = renderer.enabled && ((Component)renderer).gameObject.activeInHierarchy;
			bool hasChanged = held.transform.hasChanged;
			if (hasChanged)
			{
				held.transform.hasChanged = false;
			}
			if (held.marked)
			{
				if (hasChanged || !flag)
				{
					renderer.staticShadowCaster = held.wasStatic;
					held.marked = false;
					m_marked--;
					held.stillSince = now;
					m_redraw = true;
				}
			}
			else if (hasChanged || !flag)
			{
				held.stillSince = now;
			}
			else if (now - held.stillSince >= (held.batched ? 1f : 10f))
			{
				if (!held.batched && Moving((Component)renderer))
				{
					held.stillSince = now;
					continue;
				}
				renderer.staticShadowCaster = true;
				held.marked = true;
				m_marked++;
				m_redraw = true;
			}
		}
	}

	private void CheckRoom()
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected Obj, but got Unknown
		int frameCount = Time.frameCount;
		List<IntPtr> list = null;
		foreach (KeyValuePair<IntPtr, Kept> item in m_kept)
		{
			Kept value = item.Value;
			if (!value.placed && frameCount >= value.checkAt && !((Object)value.light == (Object)null))
			{
				value.checkAt = frameCount + 60;
				if (HDCachedShadowManager.instance.LightHasBeenPlacedInAtlas(value.light))
				{
					value.placed = true;
					m_redraw = true;
				}
				else if (++value.misses >= 3)
				{
					(list ?? (list = new List<IntPtr>())).Add(item.Key);
				}
			}
		}
		if (list == null)
		{
			return;
		}
		foreach (IntPtr item2 in list)
		{
			Kept kept = m_kept[item2];
			Give(kept);
			m_roomUsed -= kept.room;
			m_kept.Remove(item2);
			m_noRoom.Add(item2);
		}
	}

	private static void Give(Kept k)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if ((Object)k.light != (Object)null)
			{
				k.light.shadowUpdateMode = k.mode;
				k.light.alwaysDrawDynamicShadows = k.dynamic;
				k.light.updateUponLightMovement = k.movement;
				k.light.onDemandShadowRenderOnPlacement = k.onPlacement;
			}
		}
		catch (Exception)
		{
		}
	}

	private void Tell()
	{
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0208: Unknown result type (might be due to invalid IL or missing references)
		int count = m_kept.Count;
		int count2 = m_noRoom.Count;
		if ((count == m_toldKept && count2 == m_toldNoRoom) || m_tellings >= 12)
		{
			return;
		}
		int num = 0;
		foreach (Kept value in m_kept.Values)
		{
			if (value.placed)
			{
				num++;
			}
		}
		if (num >= count)
		{
			m_tellings++;
			m_toldKept = count;
			m_toldNoRoom = count2;
			var punctualLightShadowAtlas = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams.punctualLightShadowAtlas;
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)($"Static shadows: {count} spot / point light(s) now keep what does not move in their shadows (their atlas {m_atlas}x{m_atlas}, {((m_roomAll > 0) ? (m_roomUsed * 100 / m_roomAll) : 0)}% full)" + ((count2 > 0) ? $"; {count2} left drawing everything every frame (no room)" : "") + ". Lights with shadows seen: " + ((m_lightsSeen.Count > 0) ? string.Join(", ", m_lightsSeen) : "none") + $"; the atlas for shadows drawn every frame {punctualLightShadowAtlas.shadowAtlasResolution}x{punctualLightShadowAtlas.shadowAtlasResolution}{(punctualLightShadowAtlas.useDynamicViewportRescale ? ", shrinking them to fit" : "")}."));
			}
		}
	}

	internal void Restore()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected Obj, but got Unknown
		foreach (Held item in m_list)
		{
			try
			{
				if (item.marked && (Object)item.renderer != (Object)null)
				{
					item.renderer.staticShadowCaster = item.wasStatic;
				}
			}
			catch (Exception)
			{
			}
		}
		foreach (Kept value in m_kept.Values)
		{
			Give(value);
		}
		m_held.Clear();
		m_list.Clear();
		m_skipped.Clear();
		m_kept.Clear();
		m_noRoom.Clear();
		m_marked = 0;
		m_next = 0;
		m_roomUsed = 0L;
		m_roomAll = -1L;
		m_scanAt = 0f;
		m_redraw = false;
		m_toldKept = -1;
		m_toldNoRoom = -1;
		m_lightsSeen.Clear();
		m_on = false;
		State = "off";
	}
}
