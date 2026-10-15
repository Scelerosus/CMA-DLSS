using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

public sealed class Capture : MonoBehaviour
{
	private struct HairWas
	{
		public Renderer renderer;

		public MotionVectorGenerationMode mode;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr QueryInterfaceFn(uint id);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int InitializeFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int SetSleepModeFn(IntPtr device, ref SleepMode mode);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int SleepFn(IntPtr device);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void GetDeviceFn(IntPtr self, out IntPtr device);

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	private struct SleepMode
	{
		public uint version;

		public byte lowLatency;

		public byte boost;

		public uint minimumIntervalUs;

		public byte useMarkers;

		public unsafe fixed byte reserved[31];
	}

	private struct CursorInfo
	{
		public int size;

		public int flags;

		public IntPtr cursor;

		public int x;

		public int y;
	}

	private struct MaskDraw
	{
		public Renderer renderer;

		public int submesh;

		public int region;

		public Material own;

		public int ownPass;

		public string told;
	}

	private sealed class GameEffect
	{
		public string Name;

		public string Told;

		public ConfigEntry<bool> Entry;

		public int Field;

		public Func<VolumeStack, int> Used;

		public string Status = "";
	}

	private static Capture s_me;

	private Il2CppSystem.Action<ScriptableRenderContext, Camera> m_callback;

	private bool m_subscribed;

	private bool m_failed;

	private Camera m_camera;

	private RenderTexture m_target;

	private int m_setFrame = -1;

	private int m_composedFrame = -1;

	private int m_missed;

	private RenderTexture m_color;

	private RenderTexture m_output;

	private RenderTexture m_mvec;

	private RenderTexture m_depth;

	private RenderTexture m_sliceMvec;

	private RenderTexture m_sliceDepth;

	private int m_view = -1;

	private int m_width;

	private int m_height;

	private int m_passes;

	private int m_reset;

	private bool m_initialised;

	private IntPtr m_regColor;

	private IntPtr m_regMvec;

	private IntPtr m_regDepth;

	private IntPtr m_regOutput;

	private IntPtr m_regMask;

	private float m_logAt;

	private float m_fpsFrom;

	private int m_fpsFrames;

	private ulong m_waitWas;

	private bool m_enabledWas;

	private bool m_upscaleWas;

	private long m_composeTicks;

	private int m_logs;

	private string m_sourcesTold = "";

	private int m_presentFrame = -10;

	private GameObject m_screen;

	private Canvas m_screenCanvas;

	private RawImage m_screenImage;

	private bool m_screenFlip;

	private bool m_screenSet;

	private RenderTexture m_mvecBig;

	private RenderTexture m_depthBig;

	private Texture m_shown;

	private bool m_shownFlip;

	private bool m_nrFailed;

	private bool m_fgOpened;

	private bool m_fgOn;

	private bool m_fgFailed;

	private bool m_fgHavePrev;

	private bool m_fgEnabled;

	private int m_fgTotal = 2;

	private int m_fgW;

	private int m_fgH;

	private int m_fgLogs;

	private float m_fgLogAt;

	private Matrix4x4 m_fgPrevVP;

	private string m_fgStatus = "";

	private IntPtr m_fgRegMv;

	private IntPtr m_fgRegDepth;

	private float m_fgMostAt;

	private float m_fgBase;

	private int m_fgMost = 2;

	private readonly float[] m_fgViewToClip = new float[16];

	private readonly float[] m_fgClipToView = new float[16];

	private readonly float[] m_fgClipToPrev = new float[16];

	private readonly float[] m_fgPrevToClip = new float[16];

	private readonly float[] m_fgBasis = new float[12];

	private int m_fgHoldUntil;

	private readonly List<HairWas> m_hair = new List<HairWas>();

	private float m_hairAt;

	private bool m_hairFailed;

	private bool m_hairMotionTold;

	private const ulong AntialiasingBit = 134217728uL;

	private const ulong MotionBits = 3072uL;

	private const ulong SssBit = 70368744177664uL;

	private const ulong DofBit = 131072uL;

	private const ulong BlurBit = 262144uL;

	private const ulong BloomBit = 1048576uL;

	private const ulong ChromaBit = 4194304uL;

	private const ulong VignetteBit = 8388608uL;

	private const ulong GrainBit = 33554432uL;

	private const ulong EffectBits2 = 47579136uL;

	private ulong m_fxOff1;

	private ulong m_fxOff2;

	private HDAdditionalCameraData m_jitHd;

	private HDAdditionalCameraData.AntialiasingMode m_jitModeWas;

	private bool m_jitCustomWas;

	private bool m_jitOn;

	private bool m_jitFailed;

	private bool m_jitJitter;

	private bool m_camOn;

	private ulong m_jitMask1Was;

	private ulong m_jitSet1Was;

	private ulong m_jitMask2Was;

	private ulong m_jitSet2Was;

	private float m_jitterX;

	private float m_jitterY;

	private float m_jitScaleWas;

	private float m_jitBiasWas;

	private int m_jitLogs;

	private static readonly Dictionary<IntPtr, IntPtr> s_ptrs = new Dictionary<IntPtr, IntPtr>();

	private bool m_reflexTried;

	private bool m_reflexOk;

	private bool m_reflexOn;

	private bool m_reflexBoost;

	private IntPtr m_device;

	private SetSleepModeFn m_setSleepMode;

	private SleepFn m_sleep;

	private int m_reflexFails;

	internal string ReflexState = "not started";

	private bool m_pointerMine;

	private bool m_pointerReset;

	private Rect m_menuOnScreen;

	internal const int Regions = 4;

	internal static readonly string[] RegionNames = new string[4] { "Scene", "Skin", "Hair", "Clothing" };

	private static readonly float[] s_regionAlpha = new float[4] { 0f, 1f, 1f, 0.5f };

	private static readonly Regex s_hairName = new Regex("hair|brow|lash|beard|fur|pubic|scalp|pelo|cabell|ceja|pestan|polytail|ponytail|pigtail|braid|bangs|fringe|strand", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex s_skinName = new Regex("skin|body|head|face|arm|leg|torso|nail|std_|pene|penis|genit|vag|anus|tongue|teeth|tooth|gum|eye|cornea|sclera|iris|lip|breast|nipple|mouth", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex s_notHair = new Regex("chair|Chair|CHAIR", RegexOptions.Compiled);

	private static readonly string[] s_depthPasses = new string[4] { "DepthForwardOnly", "DepthOnly", "TransparentDepthPrepass", "ShadowCaster" };

	private Material m_maskHairAll;

	private string m_maskHairTold = "";

	private bool m_maskHairFailed;

	private readonly List<MaskDraw> m_maskDraws = new List<MaskDraw>();

	private readonly Material[] m_maskFill = new Material[4];

	private Material m_maskDepth;

	private Material m_maskZero;

	private Material m_maskSceneMat;

	private Mesh m_quadNear;

	private Mesh m_quadFar;

	private RenderTexture m_maskRaw;

	private RenderTexture m_maskSmall;

	private RenderTexture m_mask;

	private CommandBuffer m_maskCmd;

	private float m_maskScanAt;

	private float m_maskMaxI = 1f;

	private float m_maskMaxS = 1f;

	private bool m_maskFailed;

	private bool m_maskReady;

	private bool m_maskInit;

	private int m_maskToldParts;

	private int m_maskTolds;

	internal string MaskState = "";

	private bool m_menu;

	private bool m_menuDirty;

	private bool m_cursorWas;

	private CursorLockMode m_lockWas;

	private float m_fpsShown;

	private GUIStyle m_stTitle;

	private GUIStyle m_stLabel;

	private GUIStyle m_stSmall;

	private GUIStyle m_stButton;

	private GUIStyle m_stButtonOn;

	private Texture2D m_texPanel;

	private Texture2D m_texButton;

	private Texture2D m_texButtonOn;

	private Texture2D m_texLine;

	private int m_dragId;

	private int m_tab;

	private readonly List<Renderer> m_noMotion = new List<Renderer>();

	private float m_motionAt;

	private int m_motionLogs;

	private bool m_motionFailed;

	private int m_sharpFails;

	private bool m_sharpOff;

	private bool m_sharpTold;

	private RenderTexture m_sharp;

	private RenderTexture m_sharpBlur;

	private RenderTexture m_sharpSolid;

	private Material m_sharpMat;

	private Mesh m_sharpQuad;

	private CommandBuffer m_sharpCmd;

	internal string SharpState = "";

	private bool m_srOpen;

	private bool m_srFailed;

	private bool m_srTold;

	private RenderTexture m_srColor;

	private RenderTexture m_srOut;

	private int m_srRenderW;

	private int m_srRenderH;

	private int m_srOutW;

	private int m_srOutH;

	private int m_srQuality = -1;

	private int m_srModel = -1;

	private int m_srSlot;

	private int m_srReset;

	private int m_srEvalFails;

	private bool m_srAuto;

	private IntPtr m_srRegColor;

	private IntPtr m_srRegMvec;

	private IntPtr m_srRegDepth;

	private IntPtr m_srRegOut;

	private int m_outW;

	private int m_outH;

	private static readonly string[] s_qualities = new string[7] { "Performance", "Balanced", "Quality", "Ultra Performance", "Ultra Quality", "DLAA", "Custom" };

	private int m_srPresetOpen = -1;

	private int m_srSwapAt = -1;

	private bool m_uqTold;

	private int m_oneStepWay = -1;

	private float m_oneStepCheckAt;

	private CommandBuffer m_oneStepCmd;

	private RTHandle m_oneStepMvec;

	private RTHandle m_oneStepDepth;

	private IntPtr m_oneStepMvecFrom;

	private IntPtr m_oneStepDepthFrom;

	internal string OneStepState = "not checked yet";

	private static readonly string[] s_oneStepWays = new string[4] { "none", "HDRP's blit", "HDRP's array blit", "Unity's blit" };

	private readonly HashSet<IntPtr> m_hairSeen = new HashSet<IntPtr>();

	private readonly HashSet<IntPtr> m_motionSeen = new HashSet<IntPtr>();

	private float m_seenClearAt;

	private int m_scanCount;

	private double m_scanMotionSum;

	private double m_scanMotionWorst;

	private double m_scanHairSum;

	private double m_scanHairWorst;

	internal double ScanMotionMs;

	internal double ScanHairMs;

	internal double ScanWorstMs;

	private static GameEffect[] s_effects;

	private GUIStyle m_stStatus;

	private float m_fxStatusAt;

	private int m_camResets;

	private bool m_camQuiet;

	private Camera m_fxToldCam;

	private float m_fxTellAt;

	private readonly ShadowCache m_shadows = new ShadowCache();

	private readonly Dictionary<IntPtr, MaskDraw[]> m_maskKnown = new Dictionary<IntPtr, MaskDraw[]>();

	private static readonly MaskDraw[] s_noDraws = new MaskDraw[0];

	private float m_maskKnownClearAt;

	private int m_maskScanCount;

	private double m_maskScanSum;

	private double m_maskScanWorst;

	internal double ScanMaskMs;

	internal double ScanMaskWorstMs;

	private bool m_sqValid;

	private uint m_sqW;

	private uint m_sqH;

	private uint m_sqQuality;

	private int m_sqResult;

	private uint m_sqRenderW;

	private uint m_sqRenderH;

	private uint m_sqMinW;

	private uint m_sqMinH;

	private uint m_sqMaxW;

	private uint m_sqMaxH;

	private float m_sqSharpness;

	private bool Active
	{
		get
		{
			if ((Plugin.Enabled.Value || Plugin.Upscale.Value) && !m_failed)
			{
				return Native.Loaded;
			}
			return false;
		}
	}

	private bool MaskWanted
	{
		get
		{
			if (Plugin.Mask.Value && !m_maskFailed)
			{
				return Plugin.Enabled.Value;
			}
			return false;
		}
	}

	private bool UpscaleWanted
	{
		get
		{
			if (Plugin.Upscale.Value)
			{
				return !m_srFailed;
			}
			return false;
		}
	}

	private static GameEffect[] Effects
	{
		get
		{
			GameEffect[] array = s_effects;
			if (array == null)
			{
				array = new GameEffect[13]
				{
					Fx("Bloom", "bloom", Plugin.GameBloom, 84, (VolumeStack s) => Level<Bloom>(Vol<Bloom>(s), (Func<Bloom, bool>)((Bloom c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Subsurface scattering (skin)", "subsurface scattering", Plugin.GameSss, 46, null),
					Fx("Depth of field", "depth of field", Plugin.GameDof, 81, (VolumeStack s) => Level<DepthOfField>(Vol<DepthOfField>(s), (Func<DepthOfField, bool>)((DepthOfField c) =>
					{
						//IL_0006: Unknown result type (might be due to invalid IL or missing references)
						//IL_000c: Invalid comparison between Unknown and I4
						return (int)((VolumeParameter<DepthOfFieldMode>)(object)c.focusMode).value > 0;
					}))),
					Fx("Motion blur", "motion blur", Plugin.GameMotionBlur, 82, (VolumeStack s) => Level<MotionBlur>(Vol<MotionBlur>(s), (Func<MotionBlur, bool>)((MotionBlur c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Chromatic aberration", "chromatic aberration", Plugin.GameChromatic, 86, (VolumeStack s) => Level<ChromaticAberration>(Vol<ChromaticAberration>(s), (Func<ChromaticAberration, bool>)((ChromaticAberration c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Vignette", "vignette", Plugin.GameVignette, 87, (VolumeStack s) => Level<Vignette>(Vol<Vignette>(s), (Func<Vignette, bool>)((Vignette c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Film grain", "film grain", Plugin.GameGrain, 89, (VolumeStack s) => Level<FilmGrain>(Vol<FilmGrain>(s), (Func<FilmGrain, bool>)((FilmGrain c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Screen-space reflections", "screen-space reflections", Plugin.GameSsr, 23, (VolumeStack s) => Level<ScreenSpaceReflection>(Vol<ScreenSpaceReflection>(s), (Func<ScreenSpaceReflection, bool>)((ScreenSpaceReflection c) => ((VolumeParameter<bool>)(object)c.enabled).value))),
					Fx("Ambient occlusion", "ambient occlusion", Plugin.GameSsao, 24, (VolumeStack s) => Level<ScreenSpaceAmbientOcclusion>(Vol<ScreenSpaceAmbientOcclusion>(s), (Func<ScreenSpaceAmbientOcclusion, bool>)((ScreenSpaceAmbientOcclusion c) => ((VolumeParameter<float>)(object)c.intensity).value > 0f))),
					Fx("Screen-space GI", "screen-space global illumination", Plugin.GameSsgi, 95, (VolumeStack s) => Level<GlobalIllumination>(Vol<GlobalIllumination>(s), (Func<GlobalIllumination, bool>)((GlobalIllumination c) => ((VolumeParameter<bool>)(object)c.enable).value))),
					Fx("Contact shadows", "contact shadows", Plugin.GameContactShadows, 21, (VolumeStack s) => Level<ContactShadows>(Vol<ContactShadows>(s), (Func<ContactShadows, bool>)((ContactShadows c) => ((VolumeParameter<bool>)(object)c.enable).value))),
					Fx("Volumetric fog", "volumetric fog", Plugin.GameVolumetricFog, 28, (VolumeStack s) => Level<Fog>(Vol<Fog>(s), (Func<Fog, bool>)((Fog c) => ((VolumeParameter<bool>)(object)c.enabled).value && ((VolumeParameter<bool>)(object)c.enableVolumetricFog).value))),
					Fx("Volumetric clouds", "volumetric clouds", Plugin.GameVolumetricClouds, 79, (VolumeStack s) => Level<VolumetricClouds>(Vol<VolumetricClouds>(s), (Func<VolumetricClouds, bool>)((VolumetricClouds c) => ((VolumeParameter<bool>)(object)c.enable).value)))
				};
				s_effects = array;
			}
			return array;
		}
	}

	public Capture(IntPtr pointer)
		: base(pointer)
	{
	}

	private void Awake()
	{
		s_me = this;
	}

	private void Update()
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		ReflexStep();
		PointerStep();
		m_fpsShown = Mathf.Lerp(m_fpsShown, 1f / Mathf.Max(0.0005f, Time.unscaledDeltaTime), 0.05f);
		if ((int)Plugin.MenuKey.Value != 0 && Input.GetKeyDown(Plugin.MenuKey.Value))
		{
			MenuToggle();
		}
		if (Plugin.Enabled.Value != m_enabledWas || Plugin.Upscale.Value != m_upscaleWas)
		{
			m_upscaleWas = Plugin.Upscale.Value;
			m_nrFailed = false;
			m_fgFailed = false;
			m_enabledWas = Plugin.Enabled.Value;
			m_failed = false;
			m_srFailed = false;
			m_jitFailed = false;
			m_missed = 0;
		}
		if (m_menu && m_menuDirty && !Input.GetMouseButton(0))
		{
			MenuSave();
		}
		if ((int)Plugin.ToggleKey.Value != 0 && Input.GetKeyDown(Plugin.ToggleKey.Value))
		{
			Plugin.Enabled.Value = !Plugin.Enabled.Value;
			m_failed = false;
			m_srFailed = false;
			m_missed = 0;
			Plugin.Info((object)("Neural rendering: " + (Plugin.Enabled.Value ? "on" : "off") + "."));
		}
		if ((int)Plugin.QualityKey.Value != 0 && Input.GetKeyDown(Plugin.QualityKey.Value))
		{
			int[] array = new int[5] { 5, 2, 1, 0, 3 };
			int num = Array.IndexOf(array, Plugin.Quality.Value);
			Plugin.Quality.Value = array[(num + 1 + array.Length) % array.Length];
			m_srFailed = false;
			m_logs = 2;
			m_logAt = 0f;
			Plugin.Info((object)("DLSS quality: " + s_qualities[Plugin.Quality.Value] + ((Plugin.Quality.Value == 5) ? " (the size of the screen, no upscaling)" : "") + "."));
		}
	}

	private void LateUpdate()
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected Obj, but got Unknown
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Expected Obj, but got Unknown
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Expected Obj, but got Unknown
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Expected Obj, but got Unknown
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Expected Obj, but got Unknown
		//IL_01bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Expected Obj, but got Unknown
		//IL_02a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Expected Obj, but got Unknown
		//IL_02d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e1: Expected Obj, but got Unknown
		//IL_02b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02be: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c8: Expected Obj, but got Unknown
		//IL_02c8: Expected Obj, but got Unknown
		//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fe: Expected Obj, but got Unknown
		//IL_02fe: Expected Obj, but got Unknown
		try
		{
			ShadowCache.RestartStep();
			GiveBack();
			if (Active)
			{
				ScanStep();
				m_shadows.Step(want: true);
			}
			else
			{
				HairStep(on: false);
				MotionStep(on: false);
				m_shadows.Step(want: false);
			}
			Screen2();
			bool active = Active;
			Camera val = (active ? Camera.main : null);
			if ((Object)val == (Object)null || Screen.width < 64 || Screen.height < 64)
			{
				JitterSet(null, want: false);
				GiveBack();
				if (!active)
				{
					Teardown();
				}
				return;
			}
			if (!m_subscribed)
			{
				m_callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>((System.Delegate)new System.Action<ScriptableRenderContext, Camera>(OnEndCamera));
				RenderPipelineManager.endCameraRendering += m_callback;
				m_subscribed = true;
				Plugin.Info((object)"Capture: listening for the end of each camera's rendering.");
			}
			if (m_setFrame >= 0 && m_composedFrame < m_setFrame)
			{
				if (++m_missed >= 3)
				{
					m_failed = true;
					Plugin.ModLog.LogError((object)"Capture: the render pipeline did not report the end of the main camera's rendering for three frames; the plugin stands back (switching it off and on in the window tries again).");
					GiveBack();
					Teardown();
					return;
				}
			}
			else
			{
				m_missed = 0;
			}
			m_outW = Screen.width;
			m_outH = Screen.height;
			RenderExtent(m_outW, m_outH, out var rw, out var rh);
			if ((Object)m_target == (Object)null || ((Texture)m_target).width != rw || ((Texture)m_target).height != rh)
			{
				if ((Object)m_target != (Object)null)
				{
					m_target.Release();
					Object.Destroy((Object)m_target);
				}
				m_target = new RenderTexture(rw, rh, 24, (RenderTextureFormat)0, (RenderTextureReadWrite)2)
				{
					name = "CMADlssNr.Frame",
					hideFlags = (HideFlags)61,
					filterMode = (FilterMode)1
				};
				m_target.Create();
				_ = Plugin.ModLog;
				bool flag = false;
				BepInExInfoLogInterpolatedStringHandler val2 = new BepInExInfoLogInterpolatedStringHandler(74, 5, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("Capture: the main camera '");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(((Object)val).name);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("' renders into a texture of ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(rw);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(rh);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" (the screen is ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(m_outW);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(m_outH);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(").");
				}
				Plugin.Info(val2);
			}
			if ((Object)m_camera != (Object)null && (Object)m_camera != (Object)val)
			{
				GiveBack();
			}
			if (!((Object)val.targetTexture != (Object)null) || !((Object)val.targetTexture != (Object)m_target))
			{
				JitterSet(val, want: true, Plugin.Jitter.Value && UpscaleWanted && m_srOpen, Mathf.Log((float)rw / (float)Mathf.Max(1, m_outW), 2f));
				val.targetTexture = m_target;
				m_camera = val;
				m_setFrame = Time.frameCount;
			}
		}
		catch (Exception ex)
		{
			Fail("before the frame", ex);
		}
	}

	private static void OnEndCamera(ScriptableRenderContext context, Camera cam)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected Obj, but got Unknown
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected Obj, but got Unknown
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected Obj, but got Unknown
		Capture capture = s_me;
		if (!((Object)capture == (Object)null) && !((Object)cam == (Object)null) && !((Object)capture.m_camera == (Object)null) && !(((Il2CppObjectBase)cam).Pointer != ((Il2CppObjectBase)capture.m_camera).Pointer) && capture.m_setFrame == Time.frameCount)
		{
			try
			{
				long timestamp = Stopwatch.GetTimestamp();
				capture.Compose();
				capture.m_composeTicks += Stopwatch.GetTimestamp() - timestamp;
			}
			catch (Exception ex)
			{
				capture.Fail("composing the frame", ex);
			}
			capture.m_composedFrame = Time.frameCount;
		}
	}

	private void OnGUI()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Invalid comparison between Unknown and I4
		if ((Object)m_camera != (Object)null && Event.current != null && (int)Event.current.type == 7 && m_composedFrame == Time.frameCount)
		{
			GiveBack();
		}
		try
		{
			MenuDraw();
		}
		catch (Exception ex)
		{
			m_menu = false;
			Plugin.ModLog.LogWarning((object)("The settings window failed (" + ex.Message + ") and was closed."));
		}
	}

	[HideFromIl2Cpp]
	private void Fail(string where, Exception ex)
	{
		m_failed = true;
		JitterRestore();
		Plugin.ModLog.LogError((object)("Capture: failed " + where + " (" + ex.GetType().Name + ": " + ex.Message + "); the plugin stands back (switching it off and on in the window tries again)."));
		GiveBack();
	}

	private void GiveBack()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected Obj, but got Unknown
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected Obj, but got Unknown
		//IL_0046: Expected Obj, but got Unknown
		try
		{
			if ((Object)m_camera != (Object)null && (Object)m_target != (Object)null && (Object)m_camera.targetTexture == (Object)m_target)
			{
				m_camera.targetTexture = null;
			}
		}
		catch (Exception)
		{
		}
		m_camera = null;
	}

	private void Screen2()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected Obj, but got Unknown
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected Obj, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Expected Obj, but got Unknown
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Expected Obj, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Expected Obj, but got Unknown
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
		bool flag = (Object)m_shown != (Object)null && Time.frameCount - m_presentFrame <= 2 && Active;
		if ((Object)m_screen == (Object)null)
		{
			if (!flag)
			{
				return;
			}
			m_screen = new GameObject("CMADlssNr.Screen")
			{
				hideFlags = (HideFlags)61
			};
			Object.DontDestroyOnLoad((Object)m_screen);
			m_screenCanvas = m_screen.AddComponent<Canvas>();
			m_screenCanvas.renderMode = (RenderMode)0;
			m_screenCanvas.sortingOrder = -32000;
			GameObject val = new GameObject("Picture")
			{
				hideFlags = (HideFlags)61
			};
			RectTransform val2 = val.AddComponent<RectTransform>();
			((Transform)val2).SetParent(m_screen.transform, false);
			val2.anchorMin = Vector2.zero;
			val2.anchorMax = Vector2.one;
			val2.offsetMin = Vector2.zero;
			val2.offsetMax = Vector2.zero;
			m_screenImage = val.AddComponent<RawImage>();
			((Graphic)m_screenImage).raycastTarget = false;
			((Graphic)m_screenImage).color = Color.white;
			Plugin.Info((object)"Capture: a picture over the whole screen (under the interface) shows what is put together.");
		}
		if (((Behaviour)m_screenCanvas).enabled != flag)
		{
			((Behaviour)m_screenCanvas).enabled = flag;
		}
		if (flag)
		{
			if ((Object)m_screenImage.texture == (Object)null || ((Il2CppObjectBase)m_screenImage.texture).Pointer != ((Il2CppObjectBase)m_shown).Pointer)
			{
				m_screenImage.texture = m_shown;
			}
			if (m_screenFlip != m_shownFlip || !m_screenSet)
			{
				m_screenSet = true;
				m_screenFlip = m_shownFlip;
				m_screenImage.uvRect = (m_shownFlip ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f));
			}
		}
	}

	private void Compose()
	{
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Expected Obj, but got Unknown
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Expected Obj, but got Unknown
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Expected Obj, but got Unknown
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Expected Obj, but got Unknown
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Expected Obj, but got Unknown
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Expected Obj, but got Unknown
		//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0214: Expected Obj, but got Unknown
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Expected Obj, but got Unknown
		//IL_0240: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Expected Obj, but got Unknown
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0282: Expected Obj, but got Unknown
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a1: Expected Obj, but got Unknown
		//IL_02df: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Expected Obj, but got Unknown
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d1: Expected Obj, but got Unknown
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Expected Obj, but got Unknown
		//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0304: Expected Obj, but got Unknown
		int width = ((Texture)m_target).width;
		int height = ((Texture)m_target).height;
		bool value = Plugin.FlipY.Value;
		if (Input.GetKeyDown((KeyCode)290))
		{
			m_dump = true;
		}
		JitterRead(m_camera);
		int num = Mathf.Max(width, m_outW);
		int num2 = Mathf.Max(height, m_outH);
		Buffers(width, height, value);
		bool num3 = Plugin.Enabled.Value && !m_nrFailed;
		RenderTexture val = null;
		if (num3 && Plugin.NrAfter.Value && UpscaleWanted && m_srOpen && !m_srFailed)
		{
			val = Upscaled(width, height, num, num2, (Texture)m_target, value);
		}
		bool flag = (Object)val != (Object)null;
		int w = (flag ? num : width);
		int h = (flag ? num2 : height);
		if (flag)
		{
			if ((Object)m_mvecBig == (Object)null || ((Texture)m_mvecBig).width != num || ((Texture)m_mvecBig).height != num2)
			{
				Drop(ref m_mvecBig);
				Drop(ref m_depthBig);
				m_mvecBig = Make("CMADlssNr.MotionBig", num, num2, (RenderTextureFormat)13, (RenderTextureReadWrite)1);
				m_depthBig = Make("CMADlssNr.DepthBig", num, num2, (RenderTextureFormat)14, (RenderTextureReadWrite)1);
			}
			Graphics.Blit((Texture)m_mvec, m_mvecBig);
			Graphics.Blit((Texture)m_depth, m_depthBig);
		}
		else if ((Object)m_mvecBig != (Object)null)
		{
			Drop(ref m_mvecBig);
			Drop(ref m_depthBig);
		}
		if (num3)
		{
			MaskBuild(m_camera, w, h, value);
		}
		else
		{
			m_maskReady = false;
		}
		bool flag2 = num3 && Ensure(w, h) && (flag ? Run(w, h, (Texture)val, flipFrom: false, m_mvecBig, m_depthBig) : Run(w, h, (Texture)m_target, value, m_mvec, m_depth)) && Native.vdn_ready(m_view) != 0;
		if (m_nrFailed && (m_view >= 0 || (Object)m_color != (Object)null))
		{
			CloseNr();
		}
		if (m_srOpen && !UpscaleWanted)
		{
			TeardownSr();
		}
		Texture final = (Texture)(flag2 ? m_output : m_target);
		bool shownFlip = flag2 & value;
		if (flag)
		{
			final = (Texture)(flag2 ? m_output : val);
			shownFlip = value;
		}
		else if (UpscaleWanted && m_srOpen)
		{
			RenderTexture val2 = (flag2 ? Upscaled(width, height, num, num2, (Texture)m_output, flipFrom: false) : Upscaled(width, height, num, num2, (Texture)m_target, value));
			if ((Object)val2 != (Object)null)
			{
				final = (Texture)val2;
				shownFlip = value;
			}
		}
		m_shown = Sharpened(final);
		m_shownFlip = shownFlip;
		m_presentFrame = Time.frameCount;
		if (m_dump)
		{
			m_dump = false;
			DumpStages((Texture)m_target, flag ? (Texture)val : (((Object)final != (Object)m_target && (Object)final != (Object)m_output) ? final : null), flag2 ? (Texture)m_output : null, m_shown, width, height);
		}
		FrameGenStep(m_camera, width, height);
		Report(flag2);
	}

	// F9: the frame at each stage to BepInEx\CMADlss-dump\<quality>_<render size>_<stage>.png (game frame, DLSS output,
	// network output, what is shown), to see where something in the picture comes from. Orientation as the textures are.
	private bool m_dump;

	private void DumpStages(Texture game, Texture dlss, Texture nr, Texture shown, int w, int h)
	{
		try
		{
			string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "CMADlss-dump");
			System.IO.Directory.CreateDirectory(dir);
			string stem = s_qualities[Mathf.Clamp(Plugin.Quality.Value, 0, 6)].Replace(" ", "") + "_" + w + "x" + h + "_" + Time.frameCount + "_";
			DumpStage(dir, stem + "1game", game);
			DumpStage(dir, stem + "2dlss", dlss);
			DumpStage(dir, stem + "3nr", nr);
			DumpStage(dir, stem + "4shown", shown);
			Plugin.ModLog.LogInfo((object)("Dump: stages written to " + dir + " (" + stem + "*)."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning((object)("Dump: failed (" + ex.Message + ")."));
		}
	}

	private static void DumpStage(string dir, string name, Texture t)
	{
		if ((Object)t == (Object)null)
		{
			return;
		}
		RenderTexture temporary = RenderTexture.GetTemporary(t.width, t.height, 0, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
		RenderTexture active = RenderTexture.active;
		try
		{
			Graphics.Blit(t, temporary);
			RenderTexture.active = temporary;
			Texture2D val = new Texture2D(t.width, t.height, (TextureFormat)4, false);
			val.ReadPixels(new Rect(0f, 0f, (float)t.width, (float)t.height), 0, 0);
			val.Apply();
			System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), (byte[])ImageConversion.EncodeToPNG(val));
			Object.Destroy((Object)val);
		}
		finally
		{
			RenderTexture.active = active;
			RenderTexture.ReleaseTemporary(temporary);
		}
	}

	private static void Blit(Texture from, RenderTexture to, float sx, float sy, bool flip)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		if (flip)
		{
			Graphics.Blit(from, to, new Vector2(sx, 0f - sy), new Vector2(0f, sy));
		}
		else
		{
			Graphics.Blit(from, to, new Vector2(sx, sy), Vector2.zero);
		}
	}

	private void OneStep(int way, Texture from, RenderTexture to, int w, int h, bool flip, bool depth)
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Expected Obj, but got Unknown
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected Obj, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		float num = (float)w / (float)from.width;
		float num2 = (float)h / (float)from.height;
		if (way == 3)
		{
			if (flip)
			{
				Graphics.Blit(from, to, new Vector2(num, 0f - num2), new Vector2(0f, num2), 0, 0);
			}
			else
			{
				Graphics.Blit(from, to, new Vector2(num, num2), Vector2.zero, 0, 0);
			}
			return;
		}
		IntPtr pointer = ((Il2CppObjectBase)from).Pointer;
		RTHandle val = (depth ? m_oneStepDepth : m_oneStepMvec);
		if (val == null)
		{
			val = RTHandles.Alloc(from);
		}
		else if (pointer != (depth ? m_oneStepDepthFrom : m_oneStepMvecFrom))
		{
			val.SetTexture(from);
		}
		if (depth)
		{
			m_oneStepDepth = val;
			m_oneStepDepthFrom = pointer;
		}
		else
		{
			m_oneStepMvec = val;
			m_oneStepMvecFrom = pointer;
		}
		if (m_oneStepCmd == null)
		{
			m_oneStepCmd = new CommandBuffer
			{
				name = "CMADlssNr.OneCopy"
			};
		}
		CommandBuffer oneStepCmd = m_oneStepCmd;
		oneStepCmd.Clear();
		oneStepCmd.SetRenderTarget(new RenderTargetIdentifier((Texture)to));
		Vector4 val2 = (flip ? new Vector4(num, 0f - num2, 0f, num2) : new Vector4(num, num2, 0f, 0f));
		if (way == 1)
		{
			Blitter.BlitTexture(oneStepCmd, val, val2, 0f, false);
		}
		else
		{
			Blitter.BlitTexture(oneStepCmd, val, val2, Blitter.GetBlitMaterial((TextureDimension)5, false), 0);
		}
		Graphics.ExecuteCommandBuffer(oneStepCmd);
	}

	private void OneStepCheck(Texture from, RenderTexture done, int w, int h, bool flip)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		m_oneStepCheckAt = Time.unscaledTime + 5f;
		RenderTexture rt = null;
		try
		{
			float[] array = Readback(done);
			if (!Varied(array))
			{
				OneStepState = "waiting for a view with depth in it, to check against";
				return;
			}
			rt = Make("CMADlssNr.OneCopyCheck", w, h, done.format, (RenderTextureReadWrite)1);
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 1; i < s_oneStepWays.Length; i++)
			{
				string value;
				try
				{
					RenderTexture active = RenderTexture.active;
					RenderTexture.active = rt;
					GL.Clear(false, true, Color.clear);
					RenderTexture.active = active;
					OneStep(i, from, rt, w, h, flip, depth: true);
					int num = Differ(array, Readback(rt));
					if (num <= array.Length / 50)
					{
						m_oneStepWay = i;
						OneStepState = "one copy (" + s_oneStepWays[i] + ")";
						if (Plugin.VerboseLog.Value)
						{
							Plugin.Info((object)("Motion vectors and depth: one copy from now on, by " + s_oneStepWays[i] + " (the same depth as the two" + ((num > 0) ? $", but for {num} of {array.Length} samples" : "") + ")" + ((stringBuilder.Length > 0) ? ("; " + stringBuilder) : "") + "."));
						}
						return;
					}
					value = $"{num} of {array.Length} samples differ";
				}
				catch (Exception ex)
				{
					value = ex.GetType().Name + ": " + ex.Message;
				}
				stringBuilder.Append((stringBuilder.Length > 0) ? "; " : "").Append(s_oneStepWays[i]).Append(": ")
					.Append(value);
			}
			m_oneStepWay = 0;
			OneStepState = "two copies (no one copy gave the same depth)";
			Plugin.Info((object)("Motion vectors and depth: no one copy gives the same depth as the two (" + stringBuilder?.ToString() + "); the two stay."));
		}
		catch (Exception ex2)
		{
			m_oneStepWay = 0;
			OneStepState = "two copies (the check failed)";
			Plugin.ModLog.LogWarning((object)("Motion vectors and depth: one copy could not be checked (" + ex2.Message + "); the two stay."));
		}
		finally
		{
			Drop(ref rt);
		}
	}

	private static float[] Readback(RenderTexture rt)
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected Obj, but got Unknown
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected Obj, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected Obj, but got Unknown
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected Obj, but got Unknown
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		RenderTexture rt2 = Make("CMADlssNr.OneCopyRead", 64, 36, (RenderTextureFormat)11, (RenderTextureReadWrite)1);
		Texture2D val = null;
		RenderTexture active = RenderTexture.active;
		try
		{
			Graphics.Blit((Texture)rt, rt2);
			RenderTexture.active = rt2;
			val = new Texture2D(64, 36, (TextureFormat)20, false, true);
			val.ReadPixels(new Rect(0f, 0f, 64f, 36f), 0, 0, false);
			Il2CppStructArray<Color> pixels = val.GetPixels();
			float[] array = new float[((Il2CppArrayBase<Color>)(object)pixels).Length];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = ((Il2CppArrayBase<Color>)(object)pixels)[i].r;
			}
			return array;
		}
		finally
		{
			RenderTexture.active = active;
			if ((Object)val != (Object)null)
			{
				Object.Destroy((Object)val);
			}
			Drop(ref rt2);
		}
	}

	private static bool Varied(float[] a)
	{
		int num = 0;
		float num2 = float.MaxValue;
		float num3 = float.MinValue;
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != 0f)
			{
				num++;
			}
			num2 = Math.Min(num2, a[i]);
			num3 = Math.Max(num3, a[i]);
		}
		if (num >= a.Length / 10)
		{
			return num3 - num2 > 0.0001f;
		}
		return false;
	}

	private static int Differ(float[] a, float[] b)
	{
		int num = 0;
		for (int i = 0; i < a.Length; i++)
		{
			if (i >= b.Length || !(Math.Abs(a[i] - b[i]) <= 1E-05f + 0.001f * Math.Abs(a[i])))
			{
				num++;
			}
		}
		return num;
	}

	private string Buffer(string name, RenderTexture to, int w, int h, bool flip)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected Obj, but got Unknown
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d0: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Invalid comparison between Unknown and I4
		//IL_02fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Expected Obj, but got Unknown
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Invalid comparison between Unknown and I4
		//IL_0334: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected Obj, but got Unknown
		//IL_0053: Expected Obj, but got Unknown
		//IL_03e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected Obj, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Expected Obj, but got Unknown
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Expected Obj, but got Unknown
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected Obj, but got Unknown
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Expected Obj, but got Unknown
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bf: Expected Obj, but got Unknown
		//IL_01c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d7: Expected Obj, but got Unknown
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		Texture globalTexture = Shader.GetGlobalTexture(name);
		if ((Object)globalTexture != (Object)null && (int)globalTexture.dimension == 5 && globalTexture.width >= w && globalTexture.height >= h)
		{
			try
			{
				bool flag = (Object)to == (Object)m_depth;
				if (Plugin.FastBuffers.Value && m_oneStepWay > 0 && (!Plugin.FrameGen.Value || m_fgFailed))
				{
					OneStep(m_oneStepWay, globalTexture, to, w, h, flip, flag);
					return $"{globalTexture.width}x{globalTexture.height} {globalTexture.graphicsFormat} (array, its first, in one copy)";
				}
				RenderTexture val = (flag ? m_sliceDepth : m_sliceMvec);
				if ((Object)val == (Object)null || ((Texture)val).width != w || ((Texture)val).height != h || val.graphicsFormat != globalTexture.graphicsFormat)
				{
					if ((Object)val != (Object)null)
					{
						Forget((Texture)val);
						val.Release();
						Object.Destroy((Object)val);
					}
					val = new RenderTexture(w, h, 0, globalTexture.graphicsFormat)
					{
						name = "CMADlssNr.Slice",
						hideFlags = (HideFlags)61,
						filterMode = (FilterMode)0
					};
					val.Create();
					if (flag)
					{
						m_sliceDepth = val;
					}
					else
					{
						m_sliceMvec = val;
					}
				}
				Graphics.CopyTexture(globalTexture, 0, 0, 0, 0, w, h, (Texture)val, 0, 0, 0, 0);
				Blit((Texture)val, to, 1f, 1f, flip);
				if (flag && Plugin.FastBuffers.Value && m_oneStepWay < 0 && Time.unscaledTime >= m_oneStepCheckAt)
				{
					OneStepCheck(globalTexture, to, w, h, flip);
				}
				return $"{globalTexture.width}x{globalTexture.height} {globalTexture.graphicsFormat} (array, its first)";
			}
			catch (Exception ex)
			{
				return $"unusable (array {globalTexture.graphicsFormat}: {ex.Message})";
			}
		}
		if ((Object)globalTexture == (Object)null || (int)globalTexture.dimension != 2 || globalTexture.width < w || globalTexture.height < h)
		{
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = to;
			GL.Clear(false, true, Color.clear);
			RenderTexture.active = active;
			if (!((Object)globalTexture == (Object)null))
			{
				return $"unusable ({globalTexture.dimension}, {globalTexture.width}x{globalTexture.height})";
			}
			return "none";
		}
		Blit(globalTexture, to, (float)w / (float)globalTexture.width, (float)h / (float)globalTexture.height, flip);
		return $"{globalTexture.width}x{globalTexture.height} {globalTexture.graphicsFormat}";
	}

	private void Buffers(int w, int h, bool flip)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		if ((Object)m_mvec == (Object)null || ((Texture)m_mvec).width != w || ((Texture)m_mvec).height != h)
		{
			Drop(ref m_mvec);
			Drop(ref m_depth);
			m_mvec = Make("CMADlssNr.Motion", w, h, (RenderTextureFormat)13, (RenderTextureReadWrite)1);
			m_depth = Make("CMADlssNr.Depth", w, h, (RenderTextureFormat)14, (RenderTextureReadWrite)1);
		}
		string text = Buffer("_CameraMotionVectorsTexture", m_mvec, w, h, flip);
		string text2 = Buffer("_CameraDepthTexture", m_depth, w, h, flip);
		string text3 = "motion vectors " + text + ", depth " + text2;
		if (text3 != m_sourcesTold)
		{
			m_sourcesTold = text3;
			Plugin.Info((object)("Capture: the pipeline's buffers: " + text3 + "; the frame is " + w + "x" + h + "."));
		}
	}

	private bool Run(int w, int h, Texture from, bool flipFrom, RenderTexture mvec, RenderTexture depth)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected Obj, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected Obj, but got Unknown
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected Obj, but got Unknown
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected Obj, but got Unknown
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected Obj, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected Obj, but got Unknown
		Blit(from, m_color, 1f, 1f, flipFrom);
		bool flag = m_maskInit && m_maskReady && (Object)m_mask != (Object)null;
		IntPtr intPtr = Ptr((Texture)m_color);
		IntPtr intPtr2 = Ptr((Texture)mvec);
		IntPtr intPtr3 = Ptr((Texture)depth);
		IntPtr intPtr4 = Ptr((Texture)m_output);
		IntPtr intPtr5 = (flag ? Ptr((Texture)m_mask) : IntPtr.Zero);
		if (intPtr != m_regColor || intPtr2 != m_regMvec || intPtr3 != m_regDepth || intPtr4 != m_regOutput || intPtr5 != m_regMask)
		{
			m_regMask = intPtr5;
			int num = Native.vdn_set_textures(m_view, intPtr, intPtr2, intPtr3, intPtr4, intPtr5, 0u, 0u, 0u, 0u);
			if (num != 0)
			{
				Plugin.ModLog.LogError((object)("Neural rendering: the textures were refused (" + num + "): " + Native.Text(Native.vdn_reject_text(m_view))));
				m_nrFailed = true;
				return false;
			}
			m_regColor = intPtr;
			m_regMvec = intPtr2;
			m_regDepth = intPtr3;
			m_regOutput = intPtr4;
			m_reset = 2;
		}
		float mvecScaleX = (float)w * (Plugin.InvertMotionX.Value ? (-1f) : 1f);
		float mvecScaleY = (float)h * (Plugin.InvertMotionY.Value ? (-1f) : 1f);
		float intensity = Mathf.Clamp01(Plugin.Intensity.Value) * (flag ? m_maskMaxI : 1f);
		float localStructure = Mathf.Min(2f, Mathf.Clamp(Plugin.LocalStructure.Value, 0f, 2f) * (flag ? m_maskMaxS : 1f));
		Native.vdn_set_params(m_view, intensity, Mathf.Clamp(Plugin.LocalTone.Value, 0f, 2f), localStructure, -1f, mvecScaleX, mvecScaleY, 0, Mathf.Clamp(Plugin.Style.Value, 0, 2), Plugin.DepthInverted.Value ? 1 : 0, Plugin.UiCorrection.Value ? 1 : 0, flag ? 1 : 0, (m_reset > 0) ? 1 : 0);
		if (m_reset > 0)
		{
			m_reset--;
		}
		GL.IssuePluginEvent(Native.vdn_get_render_event_func(), Native.NrEvent(m_view));
		return true;
	}

	private static RenderTexture Make(string name, int w, int h, RenderTextureFormat format, RenderTextureReadWrite space)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		RenderTexture val = new RenderTexture(w, h, 0, format, space)
		{
			name = name,
			hideFlags = (HideFlags)61,
			filterMode = (FilterMode)0
		};
		val.Create();
		return val;
	}

	private bool Ensure(int w, int h)
	{
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Expected Obj, but got Unknown
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01af: Expected Obj, but got Unknown
		int num = Mathf.Clamp(Plugin.Passes.Value, 1, 4);
		if (m_initialised && m_width == w && m_height == h && m_passes == num && m_maskInit == m_maskReady)
		{
			return true;
		}
		Native.vdn_rebuild_begin();
		try
		{
			FrameGenHold(30);
			ReleaseNr();
			m_width = w;
			m_height = h;
			m_passes = num;
			m_color = Make("CMADlssNr.Color", w, h, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
			m_output = Make("CMADlssNr.Output", w, h, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
			if (m_view < 0)
			{
				m_view = Native.vdn_view_open();
				if (m_view < 0)
				{
					Plugin.ModLog.LogError((object)"Neural rendering: the native half has no free view.");
					m_nrFailed = true;
					return false;
				}
			}
			m_maskInit = m_maskReady;
			int num2 = Native.vdn_view_init(m_view, Native.SnippetPath, (uint)w, (uint)h, (uint)num, m_maskInit ? 1u : 0u, Ptr((Texture)m_color));
			if (num2 != 0)
			{
				Plugin.ModLog.LogError((object)("Neural rendering: could not be started (" + num2 + "): " + Native.Text(Native.vdn_status_text(m_view))));
				m_nrFailed = true;
				return false;
			}
			m_initialised = true;
			Bridge.Pin();
			m_regColor = (m_regMvec = (m_regDepth = (m_regOutput = IntPtr.Zero)));
			m_reset = 2;
			m_logs = 0;
			m_logAt = 0f;
			_ = Plugin.ModLog;
			bool flag = false;
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(35, 4, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Neural rendering: view ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(m_view);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" is up at ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(w);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(h);
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>((num > 1) ? (", " + num + " passes") : "");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(".");
			}
			Plugin.Info(val);
			return true;
		}
		finally
		{
			Native.vdn_rebuild_end();
		}
	}

	private void Report(bool ran)
	{
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Expected Obj, but got Unknown
		float unscaledTime = Time.unscaledTime;
		m_fpsFrames++;
		if (!(unscaledTime < m_logAt) && m_logs < 6)
		{
			float num = unscaledTime - m_fpsFrom;
			float num2 = ((num > 0.2f && m_fpsFrom > 0f) ? ((float)m_fpsFrames / num) : 0f);
			ulong num3 = ((m_view >= 0) ? Native.vdn_ring_wait_micros(m_view) : 0);
			float value = ((m_fpsFrames > 0) ? ((float)(num3 - m_waitWas) / 1000f / (float)m_fpsFrames) : 0f);
			m_waitWas = num3;
			float num4 = ((m_fpsFrames > 0) ? ((float)((double)m_composeTicks * 1000.0 / (double)Stopwatch.Frequency / (double)m_fpsFrames)) : 0f);
			m_composeTicks = 0L;
			m_fpsFrom = unscaledTime;
			m_fpsFrames = 0;
			m_logAt = unscaledTime + 5f;
			m_logs++;
			_ = Plugin.ModLog;
			bool flag = false;
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(140, 9, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Speed: ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(num2, "F0");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" frames a second; rendered ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(((Texture)m_target).width);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(((Texture)m_target).height);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" for a screen of ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(m_outW);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(m_outH);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("; neural rendering ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(ran ? "on" : "off");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("; DLSS ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>((m_srOpen && !m_srFailed) ? (s_qualities[Mathf.Clamp(Plugin.Quality.Value, 0, 6)] + (m_jitOn ? ", jittered" : ", no jitter")) : "off");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("; the plugin's own calls took ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(num4, "F2");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ms a frame of the main thread");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>((m_view >= 0) ? $"; the native half waited {value:F2} ms a frame for the GPU, last result 0x{Native.vdn_last_ngx_result(m_view):X}, {Native.Text(Native.vdn_status_text(m_view))}" : "");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(".");
			}
			Plugin.Info(val);
		}
	}

	private void ReleaseNr()
	{
		Drop(ref m_color);
		Drop(ref m_output);
		m_initialised = false;
		m_regColor = (m_regMvec = (m_regDepth = (m_regOutput = (m_regMask = IntPtr.Zero))));
	}

	private void CloseNr()
	{
		try
		{
			Native.vdn_rebuild_begin();
			try
			{
				if (m_view >= 0)
				{
					Native.vdn_view_close(m_view);
					m_view = -1;
				}
				ReleaseNr();
			}
			finally
			{
				Native.vdn_rebuild_end();
			}
		}
		catch (Exception)
		{
		}
	}

	private void Teardown()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Expected Obj, but got Unknown
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected Obj, but got Unknown
		m_shadows.Step(want: false);
		if (m_view >= 0 || !((Object)m_color == (Object)null) || !((Object)m_mvec == (Object)null) || m_srOpen || m_camOn || m_noMotion.Count != 0 || m_hair.Count != 0)
		{
			m_shown = null;
			FrameGenStop();
			CloseNr();
			TeardownSr();
			JitterRestore();
			HairRestore();
			MotionRestore();
			Drop(ref m_mvec);
			Drop(ref m_depth);
			Drop(ref m_sliceMvec);
			Drop(ref m_mvecBig);
			Drop(ref m_depthBig);
			Drop(ref m_sliceDepth);
			Drop(ref m_sharp);
			Drop(ref m_sharpBlur);
			Drop(ref m_sharpSolid);
			MaskDrop();
			m_sourcesTold = "";
		}
	}

	private static void Fill(float[] dst, Matrix4x4 m)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		dst[0] = m.m00;
		dst[1] = m.m10;
		dst[2] = m.m20;
		dst[3] = m.m30;
		dst[4] = m.m01;
		dst[5] = m.m11;
		dst[6] = m.m21;
		dst[7] = m.m31;
		dst[8] = m.m02;
		dst[9] = m.m12;
		dst[10] = m.m22;
		dst[11] = m.m32;
		dst[12] = m.m03;
		dst[13] = m.m13;
		dst[14] = m.m23;
		dst[15] = m.m33;
	}

	private void FrameGenHold(int frames)
	{
		m_fgHoldUntil = Mathf.Max(m_fgHoldUntil, Time.frameCount + frames);
		if (m_fgEnabled)
		{
			FrameGenStop();
		}
	}

	// Frame generation closed altogether (not only switched off) while DLSS is closed and opened again for another preset:
	// with it open, that swap crashed the game every time (access violation, in the driver's NGX or at an address that
	// belongs to no module); with it off, the same swaps ran clean. It opens again by itself when the hold ends.
	private void FrameGenClose()
	{
		if (!m_fgOpened)
		{
			return;
		}
		try
		{
			Native.vdn_fg_enable(0);
			Native.vdn_fg_close();
		}
		catch (Exception)
		{
		}
		m_fgOpened = false;
		m_fgEnabled = false;
		m_fgOn = false;
		m_fgRegMv = (m_fgRegDepth = IntPtr.Zero);
		m_fgHavePrev = false;
	}

	private void FrameGenStop()
	{
		if (m_fgOpened && m_fgEnabled)
		{
			try
			{
				Native.vdn_fg_enable(0);
			}
			catch (Exception)
			{
			}
		}
		m_fgEnabled = false;
		m_fgOn = false;
		m_fgHavePrev = false;
	}

	private void FrameGenStep(Camera cam, int w, int h)
	{
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Expected Obj, but got Unknown
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Expected Obj, but got Unknown
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Expected Obj, but got Unknown
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Expected Obj, but got Unknown
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Expected Obj, but got Unknown
		//IL_021d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Expected Obj, but got Unknown
		//IL_022e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0238: Expected Obj, but got Unknown
		//IL_026c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_0277: Unknown result type (might be due to invalid IL or missing references)
		//IL_0278: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0284: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_029f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0301: Unknown result type (might be due to invalid IL or missing references)
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_030f: Unknown result type (might be due to invalid IL or missing references)
		//IL_031e: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		//IL_034b: Unknown result type (might be due to invalid IL or missing references)
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0369: Unknown result type (might be due to invalid IL or missing references)
		//IL_0378: Unknown result type (might be due to invalid IL or missing references)
		//IL_0387: Unknown result type (might be due to invalid IL or missing references)
		//IL_0397: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_056f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0576: Expected Obj, but got Unknown
		if (!Plugin.FrameGen.Value || m_fgFailed || Time.frameCount < m_fgHoldUntil)
		{
			FrameGenStop();
			return;
		}
		try
		{
			bool flag = false;
			if (!m_fgOpened)
			{
				int num = Native.vdn_fg_open(Native.SnippetDir, ((Texture)Texture2D.whiteTexture).GetNativeTexturePtr(), Plugin.VerboseLog.Value ? 1 : 0);
				if (num != 0)
				{
					m_fgFailed = true;
					m_fgStatus = "unavailable: " + Native.Text(Native.vdn_fg_status_text());
					ManualLogSource modLog = Plugin.ModLog;
					BepInExErrorLogInterpolatedStringHandler val = new BepInExErrorLogInterpolatedStringHandler(71, 3, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Frame generation: could not be opened (");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(num);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("): ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(Native.Text(Native.vdn_fg_status_text()));
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("; hooked into this process: ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(Native.Text(Native.vdn_fg_hook_report()));
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(".");
					}
					modLog.LogError(val);
					return;
				}
				m_fgOpened = true;
				m_fgLogs = 0;
				_ = Plugin.ModLog;
				BepInExInfoLogInterpolatedStringHandler val2 = new BepInExInfoLogInterpolatedStringHandler(147, 3, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("Frame generation: opened; the presenting of the swap chain is hooked (");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>((Native.vdn_fg_hook_installed() == 2) ? "Present and Present1" : "Present only");
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("); the most it can show for a rendered frame is ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<uint>(Native.vdn_fg_max_multiplier());
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("; hooked into this process: ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(Native.Text(Native.vdn_fg_hook_report()));
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(".");
				}
				Plugin.Info(val2);
			}
			if ((Object)m_sliceMvec == (Object)null || (Object)m_sliceDepth == (Object)null || (Object)cam == (Object)null)
			{
				m_fgStatus = "waiting for motion vectors and depth";
				return;
			}
			if (w != m_fgW || h != m_fgH)
			{
				m_fgW = w;
				m_fgH = h;
				m_fgHavePrev = false;
				m_fgRegMv = (m_fgRegDepth = IntPtr.Zero);
				Native.vdn_fg_reset();
			}
			IntPtr intPtr = Ptr((Texture)m_sliceMvec);
			IntPtr intPtr2 = Ptr((Texture)m_sliceDepth);
			if (intPtr != m_fgRegMv || intPtr2 != m_fgRegDepth)
			{
				Native.vdn_fg_set_textures(intPtr, intPtr2);
				m_fgRegMv = intPtr;
				m_fgRegDepth = intPtr2;
			}
			Matrix4x4 gPUProjectionMatrix = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
			Matrix4x4 val3 = gPUProjectionMatrix * cam.worldToCameraMatrix;
			Matrix4x4 m = (m_fgHavePrev ? m_fgPrevVP : val3) * val3.inverse;
			Fill(m_fgViewToClip, gPUProjectionMatrix);
			Fill(m_fgClipToView, gPUProjectionMatrix.inverse);
			Fill(m_fgClipToPrev, m);
			Fill(m_fgPrevToClip, m.inverse);
			Transform transform = ((Component)cam).transform;
			Vector3 position = transform.position;
			Vector3 up = transform.up;
			Vector3 right = transform.right;
			Vector3 forward = transform.forward;
			m_fgBasis[0] = position.x;
			m_fgBasis[1] = position.y;
			m_fgBasis[2] = position.z;
			m_fgBasis[3] = up.x;
			m_fgBasis[4] = up.y;
			m_fgBasis[5] = up.z;
			m_fgBasis[6] = right.x;
			m_fgBasis[7] = right.y;
			m_fgBasis[8] = right.z;
			m_fgBasis[9] = forward.x;
			m_fgBasis[10] = forward.y;
			m_fgBasis[11] = forward.z;
			float mvScaleX = (float)w * (Plugin.FgInvertMotionX.Value ? (-1f) : 1f);
			float mvScaleY = (float)h * (Plugin.FgInvertMotionY.Value ? (-1f) : 1f);
			Native.vdn_fg_set_frame(m_fgViewToClip, m_fgClipToView, m_fgClipToPrev, m_fgPrevToClip, m_fgBasis, cam.nearClipPlane, cam.farClipPlane, cam.fieldOfView * (MathF.PI / 180f), cam.aspect, mvScaleX, mvScaleY, m_jitterX, 0f - m_jitterY, Plugin.DepthInverted.Value ? 1 : 0, 1);
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime >= m_fgMostAt)
			{
				m_fgMostAt = unscaledTime + 1f;
				m_fgMost = Mathf.Clamp((int)Native.vdn_fg_max_multiplier(), 2, 6);
				m_fgBase = (float)Native.vdn_fg_base_hz();
			}
			m_fgTotal = Mathf.Clamp(Plugin.FgMultiplier.Value, 2, m_fgMost);
			Native.vdn_fg_set_pacing(Plugin.FgPacing.Value ? 1 : 0);
			Native.vdn_fg_set_menu_detection(0);
			Native.vdn_fg_set_multiplier(m_fgTotal);
			Native.vdn_fg_enable(1);
			m_fgEnabled = true;
			m_fgPrevVP = val3;
			m_fgHavePrev = true;
			m_fgOn = Native.vdn_fg_generating() != 0;
			float unscaledTime2 = Time.unscaledTime;
			if (!(unscaledTime2 >= m_fgLogAt))
			{
				return;
			}
			m_fgStatus = (m_fgOn ? "generating" : "not generating") + ": " + Native.Text(Native.vdn_fg_status_text());
			if (m_fgLogs < 30)
			{
				m_fgLogs++;
				_ = Plugin.ModLog;
				BepInExInfoLogInterpolatedStringHandler val4 = new BepInExInfoLogInterpolatedStringHandler(117, 9, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("Frame generation: ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(m_fgOn ? "generating" : "NOT generating");
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(", x");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(m_fgTotal);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("; ready ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(Native.vdn_fg_ready());
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("; shown ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<uint>(Native.vdn_fg_presented());
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" frame(s), of them made ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<uint>(Native.vdn_fg_interpolated());
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(", passed through ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<uint>(Native.vdn_fg_passed_through());
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("; rendered at ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<double>(Native.vdn_fg_base_hz(), "F0");
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" a second; ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(Native.Text(Native.vdn_fg_status_text()));
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("; held back: ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(Native.Text(Native.vdn_fg_gate_summary()));
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(".");
				}
				Plugin.Info(val4);
			}
			m_fgLogAt = unscaledTime2 + ((m_fgLogs < 4) ? 2f : 10f);
		}
		catch (Exception ex)
		{
			m_fgFailed = true;
			m_fgStatus = "failed: " + ex.Message;
			Plugin.ModLog.LogError((object)("Frame generation: failed (" + ex.GetType().Name + ": " + ex.Message + "); switched off."));
			FrameGenStop();
		}
	}

	private void MenuBridge(ref float y, float x, float w)
	{
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		string text;
		if (Bridge.Installed)
		{
			if (Bridge.ModeAtStart <= 0)
			{
				text = "nvidia mfg bridge: its FP16 network is off this session.";
			}
			else
			{
				text = (Bridge.FastNetworkLoaded ? "nvidia mfg bridge: neural rendering runs on its FP16 network now." : "nvidia mfg bridge: its FP16 network loads with neural rendering.");
			}
		}
		else
		{
			text = "The nvidia mfg bridge mod is not beside the game (version.dll and dlssg_to_fsr3.ini).";
		}
		Note(ref y, x, w, text);
		if (!Bridge.OldRtx)
		{
			Note(ref y, x, w, "This video card (" + Bridge.CardName + ") runs the original FP8 network: the FP16 one is for RTX 20 and 30.");
		}
		else
		{
			bool value = Plugin.FastNetwork.Value;
			Check(ref y, x, w, "Neural rendering on the bridge's FP16 network", Plugin.FastNetwork);
			if (value != Plugin.FastNetwork.Value && Bridge.Installed)
			{
				Bridge.WriteMode(Plugin.FastNetwork.Value ? 1 : 0);
			}
			if (Bridge.Installed && Bridge.ModeAtStart > 0 != Plugin.FastNetwork.Value)
			{
				Note(ref y, x, w, "Takes effect after the game is restarted: the mod reads its file at startup.");
			}
		}
		Note(ref y, x, w, "The same picture, about 21 → 30 frames a second on an RTX 3060. Frame generation on RTX 20 and 30 (Frame Gen tab) also comes from this mod; its own neural rendering cannot run here (D3D12 only, and this game is D3D11).");
		Line(ref y, x, w);
		Fit(new Rect(x, y, w, 24f), "Switch on as the game starts", m_stLabel);
		y += 28f;
		Check(ref y, x, w, "DLSS", Plugin.StartDlss);
		Check(ref y, x, w, "Neural rendering", Plugin.StartNeural);
		Check(ref y, x, w, "Frame generation", Plugin.StartFrameGen);
		Note(ref y, x, w, "On: switched on at every start, whatever it was left at. Off: it stays as it was left.");
		Line(ref y, x, w);
		Fit(new Rect(x, y, w, 24f), "Optimization", m_stLabel);
		y += 28f;
		Check(ref y, x, w, "Check each object once (fewer stutters)", Plugin.FastScans);
		Check(ref y, x, w, "Sharpening in 3 passes instead of 4", Plugin.FastSharpening);
		Check(ref y, x, w, "Mask drawn at half size when Soft is on", Plugin.HalfResMask);
		Check(ref y, x, w, "Mask: check each object once", Plugin.FastMaskScan);
		Check(ref y, x, w, "Motion and depth in one copy (FG off)", Plugin.FastBuffers);
		string value2;
		if (Plugin.FastBuffers.Value)
		{
			value2 = ((m_oneStepWay > 0 && Plugin.FrameGen.Value && !m_fgFailed) ? ("two copies while frame generation is on, " + OneStepState + " without") : OneStepState);
		}
		else
		{
			value2 = "two copies";
		}
		Note(ref y, x, w, $"Last object scans: motion {ScanMotionMs:F2} ms, hair {ScanHairMs:F2} ms, mask {ScanMaskMs:F2} ms (worst lately {Math.Max(ScanWorstMs, ScanMaskWorstMs):F2} ms); every 3-5 s. Motion and depth: {value2}. Static shadows: {m_shadows.State} (their atlas {ShadowCache.AtlasState}).");
	}

	private void Note(ref float y, float x, float w, string text)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		float num = m_stSmall.CalcHeight(new GUIContent(text), w);
		GUI.Label(new Rect(x, y, w, num), text, m_stSmall);
		y += num + 6f;
	}

	private void MenuFrameGen(ref float y, float x, float w)
	{
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		Check(ref y, x, w, "Frame generation (DLSS-G)", Plugin.FrameGen);
		Check(ref y, x, w, "Even frame pacing", Plugin.FgPacing);
		int num = Mathf.Max(Mathf.Max(m_fgMost, 2), Mathf.Clamp(Plugin.FgMultiplier.Value, 2, 6));
		string[] array = new string[num - 1];
		int[] array2 = new int[num - 1];
		for (int i = 2; i <= num; i++)
		{
			array[i - 2] = "x" + i;
			array2[i - 2] = i;
		}
		Choice(ref y, x, w, "Frames for each rendered one" + ((m_fgMost > 2) ? $" (up to x{m_fgMost} here)" : ""), array, array2, Plugin.FgMultiplier);
		Line(ref y, x, w);
		Check(ref y, x, w, "NVIDIA Reflex low latency  (" + ReflexState + ")", Plugin.Reflex);
		Check(ref y, x, w, "Reflex boost", Plugin.ReflexBoost);
		GUI.Label(new Rect(x, y, w, 200f), "x2 shows one made frame between rendered ones, x3 two, x4 three. A 60 Hz monitor shows at most 60 frames a second: there more than x2 only means fewer rendered frames and more delay; it is for 120 Hz and faster monitors. Needs DLSS or neural rendering on and hardware-accelerated GPU scheduling; RTX 20 and 30 cards get it from the bridge mod (version.dll).\nReflex cuts the delay between the mouse and the screen.\n" + (Plugin.FrameGen.Value ? ("Now: " + ((m_fgStatus.Length > 0) ? m_fgStatus : "starting")) : ""), m_stSmall);
		y += 204f;
	}

	[HideFromIl2Cpp]
	private bool Unseen(HashSet<IntPtr> seen, Renderer r)
	{
		if (!Plugin.FastScans.Value)
		{
			return true;
		}
		return seen.Add(((Il2CppObjectBase)r).Pointer);
	}

	private void ScanStep()
	{
		EffectsTell();
		float unscaledTime = Time.unscaledTime;
		if (!Plugin.FastScans.Value || unscaledTime >= m_seenClearAt)
		{
			m_hairSeen.Clear();
			m_motionSeen.Clear();
			m_seenClearAt = unscaledTime + 30f;
		}
		float motionAt = m_motionAt;
		float hairAt = m_hairAt;
		long timestamp = Stopwatch.GetTimestamp();
		MotionStep(on: true);
		long timestamp2 = Stopwatch.GetTimestamp();
		HairStep(on: true);
		long timestamp3 = Stopwatch.GetTimestamp();
		bool flag = m_motionAt != motionAt;
		bool flag2 = m_hairAt != hairAt;
		if (!flag && !flag2)
		{
			return;
		}
		double num = 1000.0 / (double)Stopwatch.Frequency;
		if (flag)
		{
			ScanMotionMs = (double)(timestamp2 - timestamp) * num;
			m_scanMotionSum += ScanMotionMs;
			m_scanMotionWorst = Math.Max(m_scanMotionWorst, ScanMotionMs);
		}
		if (flag2)
		{
			ScanHairMs = (double)(timestamp3 - timestamp2) * num;
			m_scanHairSum += ScanHairMs;
			m_scanHairWorst = Math.Max(m_scanHairWorst, ScanHairMs);
		}
		ScanWorstMs = Math.Max(m_scanMotionWorst, m_scanHairWorst);
		if (++m_scanCount >= 20)
		{
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)$"Scans ({(Plugin.FastScans.Value ? "each renderer once" : "every renderer every time")}): motion {m_scanMotionSum / (double)m_scanCount:F2} ms on average, {m_scanMotionWorst:F2} at worst; hair {m_scanHairSum / (double)m_scanCount:F2} ms on average, {m_scanHairWorst:F2} at worst (the last {m_scanCount}).");
			}
			m_scanCount = 0;
			m_scanMotionSum = (m_scanHairSum = 0.0);
			m_scanMotionWorst = (m_scanHairWorst = 0.0);
		}
	}

	private void HairStep(bool on)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected Obj, but got Unknown
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected Obj, but got Unknown
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Expected Obj, but got Unknown
		if (m_hairFailed)
		{
			return;
		}
		try
		{
			if (!on)
			{
				HairRestore();
				return;
			}
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime < m_hairAt)
			{
				return;
			}
			m_hairAt = unscaledTime + 3f;
			for (int num = m_hair.Count - 1; num >= 0; num--)
			{
				if ((Object)m_hair[num].renderer == (Object)null)
				{
					m_hair.RemoveAt(num);
				}
			}
			Il2CppArrayBase<Renderer> val = Object.FindObjectsOfType<Renderer>();
			int num2 = 0;
			bool flag = false;
			while (val != null && num2 < val.Length)
			{
				Renderer val2 = val[num2];
				if (!((Object)val2 == (Object)null) && Unseen(m_hairSeen, val2) && (int)val2.motionVectorGenerationMode != 0 && StrandHair(val2))
				{
					m_hair.Add(new HairWas
					{
						renderer = val2,
						mode = val2.motionVectorGenerationMode
					});
					val2.motionVectorGenerationMode = (MotionVectorGenerationMode)0;
					if (!m_hairMotionTold)
					{
						m_hairMotionTold = true;
						_ = Plugin.ModLog;
						BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(83, 1, out flag);
						if (flag)
						{
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("Hair: '");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(((Object)val2).name);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' is strand hair: it gets the camera's motion vectors while the plugin runs.");
						}
						Plugin.Info(val3);
					}
				}
				num2++;
			}
		}
		catch (Exception ex)
		{
			m_hairFailed = true;
			Plugin.ModLog.LogWarning((object)("Hair: its motion vectors could not be changed (" + ex.Message + "); left as the game has them."));
		}
	}

	private static bool StrandHair(Renderer r)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected Obj, but got Unknown
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected Obj, but got Unknown
		Il2CppReferenceArray<Material> sharedMaterials = r.sharedMaterials;
		int num = 0;
		while (sharedMaterials != null && num < ((Il2CppArrayBase<Material>)(object)sharedMaterials).Length)
		{
			Material val = ((Il2CppArrayBase<Material>)(object)sharedMaterials)[num];
			if ((Object)val != (Object)null && (Object)val.shader != (Object)null && ((Object)val.shader).name.StartsWith("HDRP/Hair", StringComparison.Ordinal))
			{
				return true;
			}
			num++;
		}
		return false;
	}

	private void HairRestore()
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected Obj, but got Unknown
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		m_hairSeen.Clear();
		for (int i = 0; i < m_hair.Count; i++)
		{
			try
			{
				HairWas hairWas = m_hair[i];
				if ((Object)hairWas.renderer != (Object)null && (int)hairWas.renderer.motionVectorGenerationMode == 0)
				{
					hairWas.renderer.motionVectorGenerationMode = hairWas.mode;
				}
			}
			catch (Exception)
			{
			}
		}
		m_hair.Clear();
		m_hairAt = 0f;
	}

	private void EffectsTell()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected Obj, but got Unknown
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected Obj, but got Unknown
		//IL_0038: Expected Obj, but got Unknown
		if (!Plugin.VerboseLog.Value)
		{
			return;
		}
		Camera main = Camera.main;
		if ((Object)main == (Object)null)
		{
			return;
		}
		if ((Object)m_fxToldCam != (Object)main)
		{
			m_fxToldCam = main;
			m_fxTellAt = Time.unscaledTime + 3f;
		}
		else
		{
			if (m_fxTellAt <= 0f || Time.unscaledTime < m_fxTellAt)
			{
				return;
			}
			m_fxTellAt = 0f;
			EffectsStatus();
			Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
			List<string> list = new List<string>();
			GameEffect[] effects = Effects;
			for (int i = 0; i < effects.Length; i++)
			{
				string text = ((effects[i].Status.Length > 0) ? effects[i].Status : "?");
				if (!dictionary.TryGetValue(text, out var value))
				{
					List<string> list2 = (dictionary[text] = new List<string>());
					value = list2;
					list.Add(text);
				}
				value.Add(effects[i].Told);
			}
			StringBuilder stringBuilder = new StringBuilder();
			foreach (string item in list)
			{
				stringBuilder.Append((stringBuilder.Length > 0) ? "; " : "").Append(item).Append(": ")
					.Append(string.Join(", ", dictionary[item]));
			}
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)("Game effects with '" + ((Object)main).name + "': " + stringBuilder?.ToString() + "."));
			}
		}
	}

	private static GameEffect Fx(string name, string told, ConfigEntry<bool> entry, int field, Func<VolumeStack, int> used)
	{
		return new GameEffect
		{
			Name = name,
			Told = told,
			Entry = entry,
			Field = field,
			Used = used
		};
	}

	private static T Vol<T>(VolumeStack stack) where T : VolumeComponent
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected Obj, but got Unknown
		VolumeComponent component = stack.GetComponent(Il2CppType.Of<T>());
		if (!((Object)component == (Object)null))
		{
			return ((Il2CppObjectBase)component).TryCast<T>();
		}
		return default;
	}

	private static int Level<T>(T c, Func<T, bool> used) where T : VolumeComponent
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		if ((Object)(object)c == (Object)null)
		{
			return -1;
		}
		if (!used(c))
		{
			return 0;
		}
		return 1;
	}

	private static void EffectsAll(out ulong all1, out ulong all2)
	{
		all1 = 0uL;
		all2 = 0uL;
		GameEffect[] effects = Effects;
		for (int i = 0; i < effects.Length; i++)
		{
			if (effects[i].Field < 64)
			{
				all1 |= (ulong)(1L << effects[i].Field);
			}
			else
			{
				all2 |= (ulong)(1L << effects[i].Field - 64);
			}
		}
	}

	private static void EffectsOff(out ulong off1, out ulong off2)
	{
		off1 = 0uL;
		off2 = 0uL;
		GameEffect[] effects = Effects;
		for (int i = 0; i < effects.Length; i++)
		{
			if (!effects[i].Entry.Value)
			{
				if (effects[i].Field < 64)
				{
					off1 |= (ulong)(1L << effects[i].Field);
				}
				else
				{
					off2 |= (ulong)(1L << effects[i].Field - 64);
				}
			}
		}
	}

	private static string EffectsOffNames()
	{
		StringBuilder stringBuilder = new StringBuilder();
		GameEffect[] effects = Effects;
		for (int i = 0; i < effects.Length; i++)
		{
			if (!effects[i].Entry.Value)
			{
				stringBuilder.Append((stringBuilder.Length > 0) ? ", " : "").Append(effects[i].Told);
			}
		}
		return stringBuilder.ToString();
	}

	private void EffectsStatus()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected Obj, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected Obj, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		GameEffect[] effects = Effects;
		try
		{
			Camera val = (((Object)m_camera != (Object)null) ? m_camera : Camera.main);
			if ((Object)val == (Object)null)
			{
				for (int i = 0; i < effects.Length; i++)
				{
					effects[i].Status = "";
				}
				return;
			}
			HDCamera orCreate = HDCamera.GetOrCreate(val, 0);
			FrameSettings frameSettings = orCreate.frameSettings;
			VolumeStack volumeStack = orCreate.volumeStack;
			foreach (GameEffect gameEffect in effects)
			{
				int num = -1;
				try
				{
					if (volumeStack != null && gameEffect.Used != null)
					{
						num = gameEffect.Used(volumeStack);
					}
				}
				catch (Exception)
				{
					num = -1;
				}
				bool flag = ((gameEffect.Field < 64) ? (((frameSettings.bitDatas.data1 >> gameEffect.Field) & 1) != 0) : (((frameSettings.bitDatas.data2 >> gameEffect.Field - 64) & 1) != 0));
				ref string status = ref gameEffect.Status;
				string text;
				if (gameEffect.Entry.Value)
				{
					text = ((!flag) ? "HDRP: off" : (num switch
					{
						1 => "in use", 
						0 => "not in scene", 
						_ => "on", 
					}));
				}
				else
				{
					text = ((num == 0) ? "not in scene" : "switched off");
				}
				status = text;
			}
		}
		catch (Exception)
		{
			for (int k = 0; k < effects.Length; k++)
			{
				effects[k].Status = "?";
			}
		}
	}

	private void MenuGame(ref float y, float x, float w)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Expected Obj, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		if (Time.unscaledTime >= m_fxStatusAt)
		{
			m_fxStatusAt = Time.unscaledTime + 0.5f;
			EffectsStatus();
		}
		if (m_stStatus == null)
		{
			m_stStatus = new GUIStyle(m_stSmall)
			{
				alignment = (TextAnchor)5,
				wordWrap = false
			};
		}
		Note(ref y, x, w, "The game's own effects on the main camera. On: as the game has it; off: switched off, whatever the game says. Right: what this scene does with it — \"not in scene\" or \"HDRP: off\" means switching it changes nothing here.");
		GameEffect[] effects = Effects;
		for (int i = 0; i < effects.Length; i++)
		{
			GUI.Label(new Rect(x + 54f, y, w - 54f, 28f), effects[i].Status, m_stStatus);
			Check(ref y, x, w, effects[i].Name, effects[i].Entry);
		}
		if (m_camResets > 0)
		{
			Note(ref y, x, w, $"The game set the camera's frame settings back {m_camResets} time(s); the plugin's were put on top again each time.");
		}
		Note(ref y, x, w, "They are in the frame before DLSS and the network see it, which then carry on whatever they blur or add. A halo round sunlit skin is the game's bloom.");
	}

	private void CamBitsHold()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		if (!m_camOn || (Object)m_jitHd == (Object)null)
		{
			return;
		}
		HDAdditionalCameraData jitHd = m_jitHd;
		FrameSettingsOverrideMask renderingPathCustomFrameSettingsOverrideMask = jitHd.renderingPathCustomFrameSettingsOverrideMask;
		FrameSettings renderingPathCustomFrameSettings = jitHd.m_RenderingPathCustomFrameSettings;
		bool customRenderingSettings = jitHd.customRenderingSettings;
		ulong num = 0xC00 | m_fxOff1;
		ulong num2 = (ulong)(m_jitJitter ? 134217728 : 0) | m_fxOff2;
		ulong num3 = num & ~renderingPathCustomFrameSettingsOverrideMask.mask.data1;
		ulong num4 = num2 & ~renderingPathCustomFrameSettingsOverrideMask.mask.data2;
		ulong num5 = num & (renderingPathCustomFrameSettings.bitDatas.data1 ^ 0xC00);
		ulong num6 = num2 & renderingPathCustomFrameSettings.bitDatas.data2;
		if (!customRenderingSettings || num3 != 0L || num4 != 0L || num5 != 0L || num6 != 0L)
		{
			m_jitMask1Was = (m_jitMask1Was & ~num3) | (renderingPathCustomFrameSettingsOverrideMask.mask.data1 & num3);
			m_jitMask2Was = (m_jitMask2Was & ~num4) | (renderingPathCustomFrameSettingsOverrideMask.mask.data2 & num4);
			m_jitSet1Was = (m_jitSet1Was & ~num5) | (renderingPathCustomFrameSettings.bitDatas.data1 & num5);
			m_jitSet2Was = (m_jitSet2Was & ~num6) | (renderingPathCustomFrameSettings.bitDatas.data2 & num6);
			if (!customRenderingSettings)
			{
				m_jitCustomWas = false;
			}
			renderingPathCustomFrameSettingsOverrideMask.mask.data1 |= num;
			renderingPathCustomFrameSettingsOverrideMask.mask.data2 |= num2;
			renderingPathCustomFrameSettings.bitDatas.data1 = (renderingPathCustomFrameSettings.bitDatas.data1 | 0xC00) & ~m_fxOff1;
			renderingPathCustomFrameSettings.bitDatas.data2 &= ~num2;
			jitHd.renderingPathCustomFrameSettingsOverrideMask = renderingPathCustomFrameSettingsOverrideMask;
			jitHd.m_RenderingPathCustomFrameSettings = renderingPathCustomFrameSettings;
			jitHd.customRenderingSettings = true;
			if (++m_camResets == 1 && Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)"Camera: something set its frame settings back; the plugin's are put on top again (the Game tab counts how often).");
			}
		}
	}

	private void JitterSet(Camera cam, bool want, bool jitter = false, float bias = 0f)
	{
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Expected Obj, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected Obj, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected Obj, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Expected Obj, but got Unknown
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Expected Obj, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected Obj, but got Unknown
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected Obj, but got Unknown
		//IL_0062: Expected Obj, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		//IL_015c: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Expected Obj, but got Unknown
		//IL_00c4: Expected Obj, but got Unknown
		//IL_04a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ae: Invalid comparison between Unknown and I4
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0292: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Expected Obj, but got Unknown
		//IL_030a: Unknown result type (might be due to invalid IL or missing references)
		if (m_jitFailed)
		{
			return;
		}
		try
		{
			EffectsOff(out var off, out var off2);
			if (m_camOn && (!want || (Object)cam == (Object)null || (Object)m_jitHd == (Object)null || (Object)((Component)m_jitHd).gameObject != (Object)((Component)cam).gameObject || jitter != m_jitJitter || off != m_fxOff1 || off2 != m_fxOff2))
			{
				m_camQuiet = want && (Object)cam != (Object)null && (Object)m_jitHd != (Object)null && (Object)((Component)m_jitHd).gameObject == (Object)((Component)cam).gameObject && jitter == m_jitJitter;
				JitterRestore();
			}
			if (!want || (Object)cam == (Object)null)
			{
				return;
			}
			if (!m_camOn)
			{
				HDAdditionalCameraData component = ((Component)cam).GetComponent<HDAdditionalCameraData>();
				if ((Object)component == (Object)null)
				{
					return;
				}
				m_jitHd = component;
				m_jitModeWas = component.antialiasing;
				m_jitCustomWas = component.customRenderingSettings;
				m_jitScaleWas = component.taaJitterScale;
				m_jitBiasWas = component.materialMipBias;
				FrameSettingsOverrideMask renderingPathCustomFrameSettingsOverrideMask = component.renderingPathCustomFrameSettingsOverrideMask;
				FrameSettings renderingPathCustomFrameSettings = component.m_RenderingPathCustomFrameSettings;
				m_jitMask1Was = renderingPathCustomFrameSettingsOverrideMask.mask.data1;
				m_jitMask2Was = renderingPathCustomFrameSettingsOverrideMask.mask.data2;
				m_jitSet1Was = renderingPathCustomFrameSettings.bitDatas.data1;
				m_jitSet2Was = renderingPathCustomFrameSettings.bitDatas.data2;
				bool flag = !m_jitCustomWas || (m_jitMask1Was & 0xC00) != 3072 || (m_jitSet1Was & 0xC00) == 3072;
				renderingPathCustomFrameSettingsOverrideMask.mask.data1 |= 3072uL;
				renderingPathCustomFrameSettings.bitDatas.data1 |= 3072uL;
				if (jitter)
				{
					renderingPathCustomFrameSettingsOverrideMask.mask.data2 |= 134217728uL;
					renderingPathCustomFrameSettings.bitDatas.data2 &= 18446744073575333887uL;
				}
				m_fxOff1 = off;
				m_fxOff2 = off2;
				renderingPathCustomFrameSettingsOverrideMask.mask.data1 |= off;
				renderingPathCustomFrameSettings.bitDatas.data1 &= ~off;
				renderingPathCustomFrameSettingsOverrideMask.mask.data2 |= off2;
				renderingPathCustomFrameSettings.bitDatas.data2 &= ~off2;
				component.renderingPathCustomFrameSettingsOverrideMask = renderingPathCustomFrameSettingsOverrideMask;
				component.m_RenderingPathCustomFrameSettings = renderingPathCustomFrameSettings;
				component.customRenderingSettings = true;
				if (jitter)
				{
					component.taaJitterScale = 1f;
					component.antialiasing = (HDAdditionalCameraData.AntialiasingMode)2;
				}
				m_camOn = true;
				m_jitOn = jitter;
				m_jitJitter = jitter;
				m_jitLogs = (m_camQuiet ? 2 : 0);
				_ = Plugin.ModLog;
				bool flag2 = false;
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(145, 7, out flag2);
				if (flag2)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Camera: its anti-aliasing was ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<HDAdditionalCameraData.AntialiasingMode>(m_jitModeWas);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" (its own frame settings ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(m_jitCustomWas ? "on" : "off");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", its jitter scaled by ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(m_jitScaleWas, "F2");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", its mip bias ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(m_jitBiasWas, "F2");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("). Motion vectors switched on in its frame settings");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(flag ? "" : " (the game had them overridden off)");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(".");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>((off != 0L || off2 != 0L) ? (" Of the game's effects switched off: " + EffectsOffNames() + ".") : "");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(jitter ? " Set to temporal anti-aliasing for its jitter, with the anti-aliasing pass itself switched off: DLSS resolves the frame." : "");
				}
				if (m_camQuiet)
				{
					if (Plugin.VerboseLog.Value)
					{
						Plugin.Info((object)("Camera: of the game's effects switched off now: " + ((off != 0L || off2 != 0L) ? EffectsOffNames() : "none") + "."));
					}
				}
				else
				{
					Plugin.Info(val);
				}
				m_camQuiet = false;
			}
			CamBitsHold();
			if (m_jitOn)
			{
				if (Plugin.MipBias.Value && Mathf.Abs(m_jitHd.materialMipBias - (m_jitBiasWas + bias)) > 0.01f)
				{
					m_jitHd.materialMipBias = m_jitBiasWas + bias;
				}
				if (m_jitHd.taaJitterScale != 1f)
				{
					m_jitHd.taaJitterScale = 1f;
				}
				if ((int)m_jitHd.antialiasing != 2)
				{
					m_jitModeWas = m_jitHd.antialiasing;
					m_jitHd.antialiasing = (HDAdditionalCameraData.AntialiasingMode)2;
				}
			}
		}
		catch (Exception ex)
		{
			m_jitFailed = true;
			Plugin.ModLog.LogWarning((object)("Camera: it could not be set (" + ex.Message + "); DLSS runs without jitter (softer)."));
			JitterRestore();
		}
	}

	private void JitterRestore()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected Obj, but got Unknown
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (m_camOn && (Object)m_jitHd != (Object)null)
			{
				FrameSettingsOverrideMask renderingPathCustomFrameSettingsOverrideMask = m_jitHd.renderingPathCustomFrameSettingsOverrideMask;
				FrameSettings renderingPathCustomFrameSettings = m_jitHd.m_RenderingPathCustomFrameSettings;
				EffectsAll(out var all, out var all2);
				all |= 0xC00;
				all2 |= 0x8000000;
				renderingPathCustomFrameSettingsOverrideMask.mask.data1 = (renderingPathCustomFrameSettingsOverrideMask.mask.data1 & ~all) | (m_jitMask1Was & all);
				renderingPathCustomFrameSettingsOverrideMask.mask.data2 = (renderingPathCustomFrameSettingsOverrideMask.mask.data2 & ~all2) | (m_jitMask2Was & all2);
				renderingPathCustomFrameSettings.bitDatas.data1 = (renderingPathCustomFrameSettings.bitDatas.data1 & ~all) | (m_jitSet1Was & all);
				renderingPathCustomFrameSettings.bitDatas.data2 = (renderingPathCustomFrameSettings.bitDatas.data2 & ~all2) | (m_jitSet2Was & all2);
				m_jitHd.renderingPathCustomFrameSettingsOverrideMask = renderingPathCustomFrameSettingsOverrideMask;
				m_jitHd.m_RenderingPathCustomFrameSettings = renderingPathCustomFrameSettings;
				m_jitHd.customRenderingSettings = m_jitCustomWas;
				if (m_jitOn)
				{
					m_jitHd.taaJitterScale = m_jitScaleWas;
					m_jitHd.materialMipBias = m_jitBiasWas;
					m_jitHd.antialiasing = m_jitModeWas;
				}
				if (!m_camQuiet)
				{
					Plugin.Info((object)("Camera: its settings are as they were again (anti-aliasing " + ((object)m_jitModeWas/*cast due to constrained. prefix*/).ToString() + ")."));
				}
			}
		}
		catch (Exception)
		{
		}
		m_camOn = false;
		m_jitOn = false;
		m_jitHd = null;
		m_jitterX = (m_jitterY = 0f);
	}

	private void JitterRead(Camera cam)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected Obj, but got Unknown
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Expected Obj, but got Unknown
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		m_jitterX = (m_jitterY = 0f);
		if (!m_jitOn || (Object)cam == (Object)null)
		{
			return;
		}
		try
		{
			HDCamera orCreate = HDCamera.GetOrCreate(cam, 0);
			if (orCreate == null)
			{
				return;
			}
			Vector4 taaJitter = orCreate.taaJitter;
			m_jitterX = taaJitter.x * (Plugin.JitterInvertX.Value ? (-1f) : 1f);
			m_jitterY = taaJitter.y * (Plugin.JitterInvertY.Value ? (-1f) : 1f);
			if (m_jitLogs < 2)
			{
				m_jitLogs++;
				_ = Plugin.ModLog;
				bool flag = false;
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(127, 7, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Jitter: this frame HDRP moved the projection by (");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(taaJitter.x, "F3");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(taaJitter.y, "F3");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(") of a pixel (its camera: anti-aliasing ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<HDAdditionalCameraData.AntialiasingMode>(orCreate.antialiasing);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", frame ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(orCreate.taaFrameIndex);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(orCreate.actualWidth);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" wide); DLSS is told (");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(m_jitterX, "F3");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(m_jitterY, "F3");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(").");
				}
				Plugin.Info(val);
			}
		}
		catch (Exception ex)
		{
			if (m_jitLogs < 4)
			{
				m_jitLogs = 4;
				Plugin.ModLog.LogWarning((object)("Jitter: HDRP's jitter could not be read (" + ex.Message + "); DLSS is told there is none."));
			}
		}
	}

	private static IntPtr Ptr(Texture t)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		if ((Object)t == (Object)null)
		{
			return IntPtr.Zero;
		}
		if (!s_ptrs.TryGetValue(((Il2CppObjectBase)t).Pointer, out var value) || value == IntPtr.Zero)
		{
			value = t.GetNativeTexturePtr();
			s_ptrs[((Il2CppObjectBase)t).Pointer] = value;
		}
		return value;
	}

	private static void Forget(Texture t)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		if ((Object)t != (Object)null)
		{
			s_ptrs.Remove(((Il2CppObjectBase)t).Pointer);
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryW(string path);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	private void ReflexOpen()
	{
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Expected Obj, but got Unknown
		m_reflexTried = true;
		try
		{
			IntPtr intPtr = LoadLibraryW("nvapi64.dll");
			IntPtr intPtr2 = ((intPtr != IntPtr.Zero) ? GetProcAddress(intPtr, "nvapi_QueryInterface") : IntPtr.Zero);
			if (intPtr2 == IntPtr.Zero)
			{
				ReflexState = "unavailable (no NVIDIA driver interface)";
				Plugin.Info((object)"Reflex: nvapi64.dll or its entry was not found; not used.");
				return;
			}
			QueryInterfaceFn delegateForFunctionPointer = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(intPtr2);
			IntPtr intPtr3 = delegateForFunctionPointer(22079528u);
			IntPtr intPtr4 = delegateForFunctionPointer(2887559648u);
			IntPtr intPtr5 = delegateForFunctionPointer(2234307026u);
			if (intPtr3 == IntPtr.Zero || intPtr4 == IntPtr.Zero || intPtr5 == IntPtr.Zero)
			{
				ReflexState = "unavailable (the driver has no such functions)";
				_ = Plugin.ModLog;
				bool flag = false;
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(95, 3, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Reflex: the driver did not give its functions (initialise ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(intPtr3 != IntPtr.Zero);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", set sleep mode ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(intPtr4 != IntPtr.Zero);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", sleep ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(intPtr5 != IntPtr.Zero);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("); not used.");
				}
				Plugin.Info(val);
				return;
			}
			int num = Marshal.GetDelegateForFunctionPointer<InitializeFn>(intPtr3)();
			if (num != 0)
			{
				ReflexState = "unavailable (the driver interface did not start)";
				Plugin.Info((object)("Reflex: the driver's interface did not start (" + num + "); not used."));
				return;
			}
			IntPtr nativeTexturePtr = ((Texture)Texture2D.whiteTexture).GetNativeTexturePtr();
			if (nativeTexturePtr == IntPtr.Zero)
			{
				ReflexState = "unavailable (no device)";
				return;
			}
			Marshal.GetDelegateForFunctionPointer<GetDeviceFn>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(nativeTexturePtr), 3 * IntPtr.Size))(nativeTexturePtr, out m_device);
			if (m_device == IntPtr.Zero)
			{
				ReflexState = "unavailable (no device)";
				return;
			}
			m_setSleepMode = Marshal.GetDelegateForFunctionPointer<SetSleepModeFn>(intPtr4);
			m_sleep = Marshal.GetDelegateForFunctionPointer<SleepFn>(intPtr5);
			m_reflexOk = true;
			Plugin.Info((object)"Reflex: the driver's low latency functions were found for the game's device.");
		}
		catch (Exception ex)
		{
			ReflexState = "unavailable (" + ex.Message + ")";
			Plugin.ModLog.LogWarning((object)("Reflex: could not be set up (" + ex.GetType().Name + ": " + ex.Message + "); not used."));
		}
	}

	private unsafe bool ReflexMode(bool on, bool boost)
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected Obj, but got Unknown
		SleepMode mode = new SleepMode
		{
			version = (uint)(sizeof(SleepMode) | 0x10000),
			lowLatency = (on ? ((byte)1) : ((byte)0)),
			boost = (byte)((on & boost) ? 1u : 0u),
			minimumIntervalUs = 0u,
			useMarkers = 0
		};
		int num = m_setSleepMode(m_device, ref mode);
		if (num != 0)
		{
			ReflexState = "refused by the driver (" + num + ")";
			_ = Plugin.ModLog;
			bool flag = false;
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(52, 3, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Reflex: the driver refused the mode (on ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(on);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", boost ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(boost);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("): ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(num);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(".");
			}
			Plugin.Info(val);
			return false;
		}
		return true;
	}

	private void ReflexStep()
	{
		bool flag = Plugin.Reflex.Value && Native.Loaded;
		if (!flag && !m_reflexOn)
		{
			return;
		}
		try
		{
			if (!m_reflexTried)
			{
				ReflexOpen();
			}
			if (!m_reflexOk || m_reflexFails > 3)
			{
				return;
			}
			bool value = Plugin.ReflexBoost.Value;
			if (flag != m_reflexOn || (flag && value != m_reflexBoost))
			{
				if (!ReflexMode(flag, value))
				{
					m_reflexFails++;
					return;
				}
				m_reflexOn = flag;
				m_reflexBoost = value;
				ref string reflexState = ref ReflexState;
				string text;
				if (flag)
				{
					text = (value ? "on, with boost" : "on");
				}
				else
				{
					text = "off";
				}
				reflexState = text;
				Plugin.Info((object)("Reflex: low latency " + ReflexState + "."));
			}
			if (m_reflexOn)
			{
				int num = m_sleep(m_device);
				if (num != 0 && ++m_reflexFails > 3)
				{
					ReflexState = "stopped (the driver's sleep fails: " + num + ")";
					Plugin.Info((object)("Reflex: the driver's sleep keeps failing (" + num + "); no longer called."));
				}
			}
		}
		catch (Exception ex)
		{
			m_reflexFails = 99;
			ReflexState = "stopped (" + ex.Message + ")";
			Plugin.ModLog.LogWarning((object)("Reflex: failed (" + ex.Message + "); no longer used."));
		}
	}

	[DllImport("user32")]
	private static extern bool GetCursorInfo(ref CursorInfo info);

	[DllImport("user32")]
	private static extern int ShowCursor(bool show);

	[DllImport("user32", EntryPoint = "LoadCursorW")]
	private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

	[DllImport("user32", EntryPoint = "SetCursor")]
	private static extern IntPtr SetSystemCursor(IntPtr cursor);

	private void PointerStep()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (!m_pointerReset)
			{
				m_pointerReset = true;
				Cursor.SetCursor((Texture2D)null, Vector2.zero, (CursorMode)0);
			}
			Vector3 mousePosition = Input.mousePosition;
			if (m_menu && m_menuOnScreen.width > 0f && m_menuOnScreen.Contains(new Vector2(mousePosition.x, (float)Screen.height - mousePosition.y)) && !Cursor.visible && Application.isFocused)
			{
				CursorInfo info = new CursorInfo
				{
					size = Marshal.SizeOf<CursorInfo>()
				};
				if (!m_pointerMine || (GetCursorInfo(ref info) && (info.flags & 1) == 0))
				{
					int num = -1;
					int num2 = 0;
					while (num < 0 && num2 < 32)
					{
						num = ShowCursor(show: true);
						num2++;
					}
				}
				SetSystemCursor(LoadCursor(IntPtr.Zero, (IntPtr)32512));
				m_pointerMine = true;
			}
			else
			{
				if (!m_pointerMine)
				{
					return;
				}
				m_pointerMine = false;
				if (!Cursor.visible)
				{
					int num3 = 0;
					int num4 = 0;
					while (num3 >= 0 && num4 < 32)
					{
						num3 = ShowCursor(show: false);
						num4++;
					}
					while (num3 < -1 && num4 < 64)
					{
						num3 = ShowCursor(show: true);
						num4++;
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool IsHair(string name)
	{
		if (s_hairName.IsMatch(name))
		{
			return !s_notHair.IsMatch(name);
		}
		return false;
	}

	private static float MaxOf(ConfigEntry<float>[] region, float most)
	{
		float num = 0f;
		for (int i = 0; i < region.Length; i++)
		{
			num = Mathf.Max(num, Mathf.Clamp(region[i].Value, 0f, most));
		}
		return num;
	}

	private static Material Plain(Shader shader, int src, int dst, int zwrite, int ztest)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected Obj, but got Unknown
		Material val = new Material(shader)
		{
			name = "CMADlssNr.Mask",
			hideFlags = (HideFlags)61
		};
		val.SetInt("_SrcBlend", src);
		val.SetInt("_DstBlend", dst);
		val.SetInt("_ZWrite", zwrite);
		val.SetInt("_ZTest", ztest);
		val.SetInt("_Cull", 0);
		val.SetColor("_Color", Color.white);
		return val;
	}

	internal static Mesh Rectangle(float depth)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		Mesh val = new Mesh
		{
			name = "CMADlssNr.Rectangle",
			hideFlags = (HideFlags)61
		};
		val.vertices = (Il2CppStructArray<Vector3>)(new Vector3[4]
		{
			new Vector3(-1f, -1f, depth),
			new Vector3(-1f, 1f, depth),
			new Vector3(1f, 1f, depth),
			new Vector3(1f, -1f, depth)
		});
		val.uv = (Il2CppStructArray<Vector2>)(new Vector2[4]
		{
			new Vector2(0f, 0f),
			new Vector2(0f, 1f),
			new Vector2(1f, 1f),
			new Vector2(1f, 0f)
		});
		val.colors = (Il2CppStructArray<Color>)(new Color[4]
		{
			Color.white,
			Color.white,
			Color.white,
			Color.white
		});
		val.triangles = (Il2CppStructArray<int>)(new int[6] { 0, 1, 2, 0, 2, 3 });
		val.bounds = new Bounds(Vector3.zero, new Vector3(1000000f, 1000000f, 1000000f));
		return val;
	}

	private bool MaskMaterials()
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected Obj, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected Obj, but got Unknown
		if ((Object)m_maskDepth != (Object)null && (Object)m_quadNear != (Object)null)
		{
			return true;
		}
		Shader val = Shader.Find("Hidden/Internal-Colored");
		if ((Object)val == (Object)null)
		{
			return false;
		}
		m_maskDepth = Plain(val, 0, 1, 1, 4);
		m_maskZero = Plain(val, 0, 0, 0, 4);
		m_maskSceneMat = Plain(val, 1, 0, 0, 4);
		m_maskHairAll = Plain(val, 1, 0, 0, 5);
		for (int i = 1; i < 4; i++)
		{
			m_maskFill[i] = Plain(val, 8, 7, 0, 8);
		}
		m_quadNear = Rectangle(0f);
		m_quadFar = Rectangle(0.9999f);
		return true;
	}

	private void MaskClassify(Renderer val2)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected Obj, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected Obj, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected Obj, but got Unknown
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected Obj, but got Unknown
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Expected Obj, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected Obj, but got Unknown
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Expected Obj, but got Unknown
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Expected Obj, but got Unknown
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Expected Obj, but got Unknown
		//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Expected Obj, but got Unknown
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected Obj, but got Unknown
		//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e4: Expected Obj, but got Unknown
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e2: Expected Obj, but got Unknown
		if (((Object)val2).name.StartsWith("CMA", StringComparison.Ordinal))
		{
			return;
		}
		SkinnedMeshRenderer val3 = ((Il2CppObjectBase)val2).TryCast<SkinnedMeshRenderer>();
		MeshRenderer val4 = (((Object)val3 == (Object)null) ? ((Il2CppObjectBase)val2).TryCast<MeshRenderer>() : null);
		if ((Object)val3 == (Object)null && (Object)val4 == (Object)null)
		{
			return;
		}
		int subMeshCount;
		if ((Object)val3 != (Object)null)
		{
			if ((Object)val3.sharedMesh == (Object)null)
			{
				return;
			}
			subMeshCount = val3.sharedMesh.subMeshCount;
		}
		else
		{
			string text = ((Object)val2).name;
			Transform parent = ((Component)val2).transform.parent;
			int num = 0;
			while ((Object)parent != (Object)null && num < 5)
			{
				text = text + " " + ((Object)parent).name;
				parent = parent.parent;
				num++;
			}
			if (!IsHair(text))
			{
				return;
			}
			MeshFilter component = ((Component)val2).GetComponent<MeshFilter>();
			if ((Object)component == (Object)null || (Object)component.sharedMesh == (Object)null)
			{
				return;
			}
			subMeshCount = component.sharedMesh.subMeshCount;
		}
		Il2CppReferenceArray<Material> sharedMaterials = val2.sharedMaterials;
		int num2 = Mathf.Min(subMeshCount, ((Il2CppArrayBase<Material>)(object)sharedMaterials)?.Length ?? 0);
		for (int i = 0; i < num2; i++)
		{
			Material val5 = ((Il2CppArrayBase<Material>)(object)sharedMaterials)[i];
			if ((Object)val5 == (Object)null)
			{
				continue;
			}
			string name = ((Object)val5).name;
			string name2 = ((Object)val2).name;
			string text2 = name + " " + name2;
			int num3 = (((Object)val4 != (Object)null || IsHair(text2)) ? 2 : (s_skinName.IsMatch(text2) ? 1 : 3));
			if (num3 == 2 && (Object)val3 != (Object)null && val5.renderQueue >= 2500 && ((Object)val5.shader == (Object)null || !((Object)val5.shader).name.StartsWith("HDRP/Hair")))
			{
				num3 = 1;
			}
			MaskDraw item = new MaskDraw
			{
				renderer = val2,
				submesh = i,
				region = num3,
				ownPass = -1,
				told = name + " (" + name2 + ") = " + RegionNames[num3]
			};
			if (num3 == 2)
			{
				try
				{
					for (int j = 0; j < s_depthPasses.Length; j++)
					{
						if (item.ownPass >= 0)
						{
							break;
						}
						if (m_maskHairFailed)
						{
							break;
						}
						int num4 = val5.FindPass(s_depthPasses[j]);
						if (num4 >= 0)
						{
							item.own = val5;
							item.ownPass = num4;
						}
						string text3 = ((Object)val5).name + ": '" + (((Object)val5.shader != (Object)null) ? ((Object)val5.shader).name : "?") + "' pass " + s_depthPasses[j] + ((num4 >= 0) ? " found" : " not there") + "; ";
						if (m_maskHairTold.Length < 1200 && !m_maskHairTold.Contains(text3))
						{
							m_maskHairTold += text3;
							Plugin.Info((object)("Mask: hair's own material for its depth: " + text3));
						}
					}
				}
				catch (Exception ex)
				{
					m_maskHairFailed = true;
					item.own = null;
					item.ownPass = -1;
					Plugin.Info((object)("Mask: the passes of the hair's own material could not be asked for (" + ex.Message + "); hair is drawn as before."));
				}
			}
			m_maskDraws.Add(item);
		}
	}

	private void MaskScanTimed(long t0)
	{
		ScanMaskMs = (double)(Stopwatch.GetTimestamp() - t0) * 1000.0 / (double)Stopwatch.Frequency;
		m_maskScanSum += ScanMaskMs;
		m_maskScanWorst = Math.Max(m_maskScanWorst, ScanMaskMs);
		ScanMaskWorstMs = m_maskScanWorst;
		if (++m_maskScanCount >= 20)
		{
			if (Plugin.VerboseLog.Value)
			{
				Plugin.Info((object)$"Mask scans ({(Plugin.FastMaskScan.Value ? "each renderer looked at once" : "every renderer every time")}): {m_maskScanSum / (double)m_maskScanCount:F2} ms on average, {m_maskScanWorst:F2} at worst (the last {m_maskScanCount}); {m_maskKnown.Count} renderers known.");
			}
			m_maskScanCount = 0;
			m_maskScanSum = 0.0;
			m_maskScanWorst = 0.0;
		}
	}

	private void MaskScan(Camera cam)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected Obj, but got Unknown
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Expected Obj, but got Unknown
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_024d: Expected Obj, but got Unknown
		m_maskDraws.Clear();
		int[] array = new int[4];
		StringBuilder stringBuilder = new StringBuilder();
		bool value = Plugin.FastMaskScan.Value;
		float unscaledTime = Time.unscaledTime;
		if (!value || unscaledTime >= m_maskKnownClearAt)
		{
			m_maskKnown.Clear();
			m_maskKnownClearAt = unscaledTime + 30f;
		}
		long timestamp = Stopwatch.GetTimestamp();
		int num = (((Object)cam != (Object)null) ? cam.cullingMask : (-1));
		Il2CppArrayBase<Renderer> val = Object.FindObjectsOfType<Renderer>();
		int num2 = 0;
		while (val != null && num2 < val.Length)
		{
			Renderer val2 = val[num2];
			if (!((Object)val2 == (Object)null) && val2.enabled && (num & (1 << ((Component)val2).gameObject.layer)) != 0)
			{
				int count = m_maskDraws.Count;
				IntPtr pointer = ((Il2CppObjectBase)val2).Pointer;
				if (value && m_maskKnown.TryGetValue(pointer, out var value2))
				{
					m_maskDraws.AddRange(value2);
				}
				else
				{
					MaskClassify(val2);
					if (value)
					{
						m_maskKnown[pointer] = ((m_maskDraws.Count > count) ? m_maskDraws.GetRange(count, m_maskDraws.Count - count).ToArray() : s_noDraws);
					}
				}
				for (int i = count; i < m_maskDraws.Count; i++)
				{
					array[m_maskDraws[i].region]++;
					if (m_maskToldParts != -1 && stringBuilder.Length < 1500)
					{
						stringBuilder.Append((stringBuilder.Length > 0) ? ", " : "").Append(m_maskDraws[i].told);
					}
				}
			}
			num2++;
		}
		MaskScanTimed(timestamp);
		if (m_maskDraws.Count > 0 && m_maskDraws.Count != m_maskToldParts && m_maskTolds < 2)
		{
			m_maskTolds++;
			m_maskToldParts = m_maskDraws.Count;
			_ = Plugin.ModLog;
			bool flag = false;
			BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(62, 5, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("Mask: ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(m_maskDraws.Count);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" part(s): ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(array[1]);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" skin, ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(array[2]);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" hair, ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(array[3]);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" clothing. By material (mesh): ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<StringBuilder>(stringBuilder);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(".");
			}
			Plugin.Info(val3);
		}
		MaskState = ((m_maskDraws.Count > 0) ? $"{array[1]} skin, {array[2]} hair, {array[3]} clothing parts" : "no characters found");
	}

	private bool Shown(MaskDraw d)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		if ((Object)d.renderer != (Object)null && d.renderer.enabled)
		{
			return d.renderer.isVisible;
		}
		return false;
	}

	private void MaskPaint(Camera cam)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Expected Obj, but got Unknown
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected Obj, but got Unknown
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected Obj, but got Unknown
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_023a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		if (m_maskCmd == null)
		{
			m_maskCmd = new CommandBuffer
			{
				name = "CMADlssNr.Mask"
			};
		}
		Matrix4x4 worldToCameraMatrix = cam.worldToCameraMatrix;
		Matrix4x4 projectionMatrix = cam.projectionMatrix;
		Matrix4x4 identity = Matrix4x4.identity;
		CommandBuffer maskCmd = m_maskCmd;
		maskCmd.Clear();
		maskCmd.SetRenderTarget(new RenderTargetIdentifier((Texture)m_maskRaw));
		maskCmd.ClearRenderTarget(true, true, Color.white);
		bool flag = false;
		for (int i = 0; i < m_maskDraws.Count; i++)
		{
			MaskDraw d = m_maskDraws[i];
			if ((Object)d.own != (Object)null && d.ownPass >= 0 && Shown(d))
			{
				maskCmd.DrawRenderer(d.renderer, d.own, d.submesh, d.ownPass);
				flag = true;
			}
		}
		if (flag && (Object)m_maskHairAll != (Object)null)
		{
			maskCmd.SetViewProjectionMatrices(identity, identity);
			maskCmd.DrawMesh(m_quadFar, identity, m_maskHairAll, 0, 0);
		}
		maskCmd.SetViewProjectionMatrices(worldToCameraMatrix, projectionMatrix);
		for (int j = 0; j < m_maskDraws.Count; j++)
		{
			if (Shown(m_maskDraws[j]))
			{
				maskCmd.DrawRenderer(m_maskDraws[j].renderer, m_maskDepth, m_maskDraws[j].submesh, 0);
			}
		}
		for (int k = 1; k < 4; k++)
		{
			maskCmd.SetViewProjectionMatrices(worldToCameraMatrix, projectionMatrix);
			for (int l = 0; l < m_maskDraws.Count; l++)
			{
				if (m_maskDraws[l].region == k && Shown(m_maskDraws[l]))
				{
					maskCmd.DrawRenderer(m_maskDraws[l].renderer, m_maskZero, m_maskDraws[l].submesh, 0);
				}
			}
			maskCmd.SetViewProjectionMatrices(identity, identity);
			maskCmd.DrawMesh(m_quadNear, identity, m_maskFill[k], 0, 0);
		}
		maskCmd.SetViewProjectionMatrices(identity, identity);
		maskCmd.DrawMesh(m_quadFar, identity, m_maskSceneMat, 0, 0);
		Graphics.ExecuteCommandBuffer(maskCmd);
	}

	private bool MaskBuild(Camera cam, int w, int h, bool flip)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected Obj, but got Unknown
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected Obj, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Expected Obj, but got Unknown
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Expected Obj, but got Unknown
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Expected Obj, but got Unknown
		//IL_02c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Expected Obj, but got Unknown
		//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f5: Expected Obj, but got Unknown
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_0280: Unknown result type (might be due to invalid IL or missing references)
		//IL_0296: Unknown result type (might be due to invalid IL or missing references)
		m_maskReady = false;
		if (!MaskWanted || (Object)cam == (Object)null)
		{
			return false;
		}
		try
		{
			if (!MaskMaterials())
			{
				m_maskFailed = true;
				MaskState = "not available (no plain shader in this game)";
				Plugin.Info((object)"Mask: 'Hidden/Internal-Colored' is not in this game; no mask.");
				return false;
			}
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime >= m_maskScanAt)
			{
				m_maskScanAt = unscaledTime + 3f;
				MaskScan(cam);
			}
			int num;
			int num2;
			if (Plugin.MaskSoft.Value)
			{
				num = (Plugin.HalfResMask.Value ? 1 : 0);
				if (num != 0)
				{
					num2 = Mathf.Max(1, w / 2);
					goto IL_0097;
				}
			}
			else
			{
				num = 0;
			}
			num2 = w;
			goto IL_0097;
			IL_0097:
			int num3 = num2;
			int num4 = ((num != 0) ? Mathf.Max(1, h / 2) : h);
			if ((Object)m_mask == (Object)null || ((Texture)m_mask).width != w || ((Texture)m_mask).height != h)
			{
				Drop(ref m_maskRaw);
				Drop(ref m_maskSmall);
				Drop(ref m_mask);
				m_maskSmall = Make("CMADlssNr.MaskSmall", Mathf.Max(1, w / 3), Mathf.Max(1, h / 3), (RenderTextureFormat)0, (RenderTextureReadWrite)1);
				((Texture)m_maskSmall).filterMode = (FilterMode)1;
				m_mask = Make("CMADlssNr.Mask", w, h, (RenderTextureFormat)0, (RenderTextureReadWrite)1);
			}
			if ((Object)m_maskRaw == (Object)null || ((Texture)m_maskRaw).width != num3 || ((Texture)m_maskRaw).height != num4)
			{
				Drop(ref m_maskRaw);
				m_maskRaw = new RenderTexture(num3, num4, 24, (RenderTextureFormat)0, (RenderTextureReadWrite)1)
				{
					name = "CMADlssNr.MaskRaw",
					hideFlags = (HideFlags)61,
					filterMode = (FilterMode)1
				};
				m_maskRaw.Create();
			}
			m_maskMaxI = MaxOf(Plugin.RegionEffect, 1f);
			m_maskMaxS = MaxOf(Plugin.RegionDetail, 2f);
			Color val = default;
			for (int i = 0; i < 4; i++)
			{
				float num5 = ((m_maskMaxI > 0.001f) ? Mathf.Clamp01(Plugin.RegionEffect[i].Value / m_maskMaxI) : 1f);
				float num6 = ((m_maskMaxS > 0.001f) ? Mathf.Clamp01(Plugin.RegionDetail[i].Value / m_maskMaxS) : 1f);
				val = new Color(Mathf.LinearToGammaSpace(num5), 1f, Mathf.LinearToGammaSpace(num6), s_regionAlpha[i]);
				((i == 0) ? m_maskSceneMat : m_maskFill[i]).SetColor("_Color", val);
				if (i == 2)
				{
					m_maskHairAll.SetColor("_Color", val);
				}
			}
			MaskPaint(cam);
			if (Plugin.MaskSoft.Value)
			{
				Graphics.Blit((Texture)m_maskRaw, m_maskSmall);
				Blit((Texture)m_maskSmall, m_mask, 1f, 1f, flip);
			}
			else
			{
				Blit((Texture)m_maskRaw, m_mask, 1f, 1f, flip);
			}
			m_maskReady = true;
			return true;
		}
		catch (Exception ex)
		{
			m_maskFailed = true;
			MaskState = "failed: " + ex.Message;
			Plugin.ModLog.LogWarning((object)("Mask: failed (" + ex.GetType().Name + ": " + ex.Message + "); the network runs without it."));
			return false;
		}
	}

	private void MaskDrop()
	{
		m_maskKnown.Clear();
		Drop(ref m_maskRaw);
		Drop(ref m_maskSmall);
		Drop(ref m_mask);
		m_maskReady = false;
	}

	private void MenuMask(ref float y, float x, float w)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		Check(ref y, x, w, "Separate settings for skin, hair and clothing", Plugin.Mask);
		Rect val = new Rect(x, y, w, 20f);
		string text;
		if (Plugin.Mask.Value)
		{
			text = ((MaskState.Length > 0) ? MaskState : "starting");
		}
		else
		{
			text = "off: one setting for the whole picture";
		}
		GUI.Label(val, text, m_stSmall);
		y += 22f;
		for (int i = 0; i < 4; i++)
		{
			Slider(ref y, x, w, RegionNames[i] + ": effect", Plugin.RegionEffect[i], 0f, 1f, 10 + i * 2);
			Slider(ref y, x, w, RegionNames[i] + ": detail", Plugin.RegionDetail[i], 0f, 2f, 11 + i * 2);
		}
		Check(ref y, x, w, "Soft edges between regions", Plugin.MaskSoft);
		GUI.Label(new Rect(x, y, w, 60f), "Effect is how much of the network's result is kept on that part of the picture, detail how much fine structure it adds there; both scale Intensity and Local structure of the Neural page.", m_stSmall);
		y += 64f;
	}

	private static Texture2D Flat(Color c)
	{
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected Obj, but got Unknown
		Texture2D val = new Texture2D(1, 1, (TextureFormat)4, false)
		{
			hideFlags = (HideFlags)61
		};
		val.SetPixel(0, 0, c);
		val.Apply();
		return val;
	}

	private void MenuToggle()
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		m_menu = !m_menu;
		if (m_menu)
		{
			m_lockWas = Cursor.lockState;
			m_cursorWas = (int)m_lockWas == 1;
			if (m_cursorWas)
			{
				Cursor.lockState = (CursorLockMode)0;
			}
		}
		else
		{
			if (m_cursorWas)
			{
				Cursor.lockState = m_lockWas;
			}
			m_menuOnScreen = default;
			MenuSave();
		}
	}

	private void MenuSave()
	{
		if (!m_menuDirty)
		{
			return;
		}
		m_menuDirty = false;
		try
		{
			Plugin.Cfg.Save();
		}
		catch (Exception)
		{
		}
	}

	private void MenuStyles()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Expected Obj, but got Unknown
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Expected Obj, but got Unknown
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Expected Obj, but got Unknown
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected Obj, but got Unknown
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Expected Obj, but got Unknown
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		if (m_stTitle == null || !((Object)m_texPanel != (Object)null))
		{
			m_texPanel = Flat(new Color(0.06f, 0.065f, 0.08f, 0.94f));
			m_texButton = Flat(new Color(0.17f, 0.18f, 0.22f, 1f));
			m_texButtonOn = Flat(new Color(0.3f, 0.62f, 0.2f, 1f));
			m_texLine = Flat(new Color(1f, 1f, 1f, 0.12f));
			m_stTitle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 20,
				fontStyle = (FontStyle)1,
				alignment = (TextAnchor)3
			};
			m_stLabel = new GUIStyle(GUI.skin.label)
			{
				fontSize = 15,
				alignment = (TextAnchor)3,
				wordWrap = false
			};
			m_stSmall = new GUIStyle(GUI.skin.label)
			{
				fontSize = 13,
				alignment = (TextAnchor)0,
				wordWrap = true
			};
			m_stButton = new GUIStyle(GUI.skin.label)
			{
				fontSize = 14,
				alignment = (TextAnchor)4,
				wordWrap = false
			};
			m_stButtonOn = new GUIStyle(m_stButton)
			{
				fontStyle = (FontStyle)1
			};
			m_stTitle.normal.textColor = new Color(0.62f, 0.9f, 0.45f);
			m_stLabel.normal.textColor = new Color(0.9f, 0.9f, 0.92f);
			m_stSmall.normal.textColor = new Color(0.62f, 0.64f, 0.7f);
			m_stButton.normal.textColor = new Color(0.85f, 0.86f, 0.9f);
			m_stButtonOn.normal.textColor = Color.white;
		}
	}

	// The text on one line, the font made smaller (down to 9) when it would not fit the box: no text runs out of its button.
	private static void Fit(Rect r, string text, GUIStyle style)
	{
		int fontSize = style.fontSize;
		float x = style.CalcSize(new GUIContent(text)).x;
		float num = r.width - 6f;
		if (x > num && x > 0f)
		{
			style.fontSize = Mathf.Max(9, Mathf.FloorToInt((float)fontSize * num / x));
		}
		GUI.Label(r, text, style);
		style.fontSize = fontSize;
	}

	private bool Clicked(Rect r)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		Event current = Event.current;
		if ((int)current.type == 0 && current.button == 0 && r.Contains(current.mousePosition))
		{
			current.Use();
			return true;
		}
		return false;
	}

	private bool Button(Rect r, string text, bool on)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected Obj, but got Unknown
		if ((int)Event.current.type == 7)
		{
			GUI.DrawTexture(r, (Texture)(on ? m_texButtonOn : m_texButton));
		}
		Fit(r, text, on ? m_stButtonOn : m_stButton);
		return Clicked(r);
	}

	[HideFromIl2Cpp]
	private void Check(ref float y, float x, float w, string text, ConfigEntry<bool> e)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		Rect val = default;
		val = new Rect(x, y + 3f, 44f, 22f);
		if (Button(val, e.Value ? "ON" : "off", e.Value))
		{
			e.Value = !e.Value;
			m_menuDirty = true;
		}
		Fit(new Rect(x + 54f, y, w - 54f, 28f), text, m_stLabel);
		y += 30f;
	}

	[HideFromIl2Cpp]
	private void Choice(ref float y, float x, float w, string text, string[] names, int[] values, ConfigEntry<int> e)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		Fit(new Rect(x, y, w, 24f), text, m_stLabel);
		y += 25f;
		float num = (w - 4f * (float)(names.Length - 1)) / (float)names.Length;
		for (int i = 0; i < names.Length; i++)
		{
			if (Button(new Rect(x + (float)i * (num + 4f), y, num, 26f), names[i], e.Value == values[i]) && e.Value != values[i])
			{
				e.Value = values[i];
				m_menuDirty = true;
				m_srFailed = false;
			}
		}
		y += 34f;
	}

	[HideFromIl2Cpp]
	private void Slider(ref float y, float x, float w, string text, ConfigEntry<float> e, float min, float max, int id)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Invalid comparison between Unknown and I4
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Invalid comparison between Unknown and I4
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Invalid comparison between Unknown and I4
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Expected Obj, but got Unknown
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Expected Obj, but got Unknown
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Invalid comparison between Unknown and I4
		Fit(new Rect(x, y, w - 60f, 24f), text, m_stLabel);
		GUI.Label(new Rect(x + w - 60f, y, 60f, 24f), e.Value.ToString("0.00"), m_stLabel);
		y += 24f;
		Rect val = default;
		val = new Rect(x, y + 5f, w, 10f);
		Rect val2 = default;
		val2 = new Rect(x, y, w, 20f);
		Event current = Event.current;
		if ((int)current.type == 0 && current.button == 0 && val2.Contains(current.mousePosition))
		{
			m_dragId = id;
			current.Use();
		}
		if (m_dragId == id && ((int)current.type == 3 || (int)current.type == 0 || (int)current.type == 12))
		{
			float num = Mathf.Lerp(min, max, Mathf.Clamp01((current.mousePosition.x - x) / w));
			num = Mathf.Round(num * 100f) / 100f;
			if (Mathf.Abs(num - e.Value) > 0.0001f)
			{
				e.Value = num;
				m_menuDirty = true;
			}
		}
		if ((int)current.rawType == 1)
		{
			m_dragId = 0;
		}
		if ((int)current.type == 7)
		{
			GUI.DrawTexture(val, (Texture)m_texButton);
			GUI.DrawTexture(new Rect(x, y + 5f, w * Mathf.InverseLerp(min, max, e.Value), 10f), (Texture)m_texButtonOn);
		}
		y += 26f;
	}

	private void Line(ref float y, float x, float w)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected Obj, but got Unknown
		if ((int)Event.current.type == 7)
		{
			GUI.DrawTexture(new Rect(x, y + 4f, w, 1f), (Texture)m_texLine);
		}
		y += 10f;
	}

	private void MenuDraw()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Invalid comparison between Unknown and I4
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected Obj, but got Unknown
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Expected Obj, but got Unknown
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0660: Unknown result type (might be due to invalid IL or missing references)
		//IL_066f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0693: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_06dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_06fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ec: Invalid comparison between Unknown and I4
		//IL_0487: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f6: Invalid comparison between Unknown and I4
		if (!m_menu)
		{
			return;
		}
		MenuStyles();
		float num = Mathf.Max(1f, (float)Screen.height / 1080f);
		Matrix4x4 matrix = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(num, num, 1f));
		float num2 = 30f;
		float num3 = 30f;
		Rect val = default;
		val = new Rect(num2, num3, 430f, 860f);
		if ((int)Event.current.type == 7)
		{
			GUI.DrawTexture(val, (Texture)m_texPanel);
		}
		float num4 = num2 + 16f;
		float num5 = 398f;
		float num6 = num3 + 10f;
		GUI.Label(new Rect(num4, num6, num5, 30f), "DLSS / Neural Rendering  0.8.3", m_stTitle);
		num6 += 32f;
		bool flag = m_srOpen && !m_srFailed;
		bool flag2 = m_view >= 0 && m_initialised;
		string value;
		if (!Plugin.Enabled.Value && !Plugin.Upscale.Value)
		{
			value = "everything is off";
		}
		else if (m_failed)
		{
			value = "failed, see BepInEx/LogOutput.log";
		}
		else
		{
			value = (((Object)m_target != (Object)null) ? ($"render {((Texture)m_target).width}x{((Texture)m_target).height} -> screen {m_outW}x{m_outH}" + ((!flag) ? "" : ((((Texture)m_target).width < m_outW) ? ", DLSS upscaling" : ", DLAA")) + (flag2 ? ", neural rendering" : "") + (m_fgOn ? (", frame generation x" + m_fgTotal) : "")) : "waiting for the camera");
		}
		GUI.Label(new Rect(num4, num6, num5, 40f), $"{m_fpsShown:0} fps rendered · {value}", m_stSmall);
		num6 += 38f;
		string[] array = new string[6] { "DLSS", "Neural", "Mask", "Frame Gen", "Game", "Bridge" };
		float num7 = (num5 - 20f) / 6f;
		for (int i = 0; i < 6; i++)
		{
			if (Button(new Rect(num4 + (float)i * (num7 + 4f), num6, num7, 28f), array[i], m_tab == i))
			{
				m_tab = i;
			}
		}
		num6 += 38f;
		Line(ref num6, num4, num5);
		if (m_tab == 0)
		{
			Check(ref num6, num4, num5, "DLSS", Plugin.Upscale);
			Choice(ref num6, num4, num5, "Quality", new string[7] { "DLAA", "U.Quality", "Quality", "Balanced", "Perf", "U.Perf", "Custom" }, new int[7] { 5, 4, 2, 1, 0, 3, 6 }, Plugin.Quality);
			if (Plugin.Quality.Value == 6)
			{
				Slider(ref num6, num4, num5, "Render scale (share of the screen's size)", Plugin.RenderScale, 0.33f, 1f, 5);
			}
			if (Plugin.Preset.Value != 0 && !Chosen(Plugin.Preset.Value))
			{
				Plugin.Preset.Value = 0;
			}
			Slider(ref num6, num4, num5, "Sharpness" + ((SharpState.Length > 0) ? ("  (" + SharpState + ")") : ""), Plugin.Sharpness, 0f, 1f, 4);
			Choice(ref num6, num4, num5, "Model preset", new string[6] { "Auto", "K", "M", "L", "E", "F" }, new int[6] { 0, 11, 13, 12, 5, 6 }, Plugin.Preset);
			GUI.Label(new Rect(num4, num6, num5, 166f), "DLAA keeps the full resolution and only anti-aliases; the other qualities render a smaller frame and reconstruct it, for more frames a second.\nModel preset: K is the sharpest and steadiest, M is faster; Auto takes K, and M for Performance and Ultra Performance. E and F are the older, much lighter models for RTX 20 and 30 cards: E for upscaling, F for DLAA and Ultra Performance.\nL is NVIDIA's newest model, made for Ultra Performance (NVIDIA's own pick for it); it is heavier than K and M.", m_stSmall);
			num6 += 170f;
		}
		else if (m_tab == 1)
		{
			Check(ref num6, num4, num5, "Neural rendering (DLSS NR)", Plugin.Enabled);
			Check(ref num6, num4, num5, "After DLSS, at full size (finer, slower)", Plugin.NrAfter);
			Slider(ref num6, num4, num5, "Intensity", Plugin.Intensity, 0f, 1f, 1);
			Slider(ref num6, num4, num5, "Local tone", Plugin.LocalTone, 0f, 2f, 2);
			Slider(ref num6, num4, num5, "Local structure", Plugin.LocalStructure, 0f, 2f, 3);
			Choice(ref num6, num4, num5, "Style", new string[3] { "Default", "Natural", "Cinematic" }, new int[3] { 0, 1, 2 }, Plugin.Style);
			Choice(ref num6, num4, num5, "Passes", new string[3] { "1", "2", "3" }, new int[3] { 1, 2, 3 }, Plugin.Passes);
			GUI.Label(new Rect(num4, num6, num5, 96f), "Intensity is how much of the network's result is shown. Local tone is its relighting, Local structure the fine detail it adds.\nWith DLSS on, the network works on the smaller frame before DLSS; \"After DLSS\" runs it on the full picture instead. Every pass costs the network's whole time again.", m_stSmall);
			num6 += 100f;
		}
		else if (m_tab == 2)
		{
			MenuMask(ref num6, num4, num5);
		}
		else if (m_tab == 4)
		{
			MenuGame(ref num6, num4, num5);
		}
		else if (m_tab == 5)
		{
			MenuBridge(ref num6, num4, num5);
		}
		else
		{
			MenuFrameGen(ref num6, num4, num5);
		}
		GUI.Label(new Rect(num4, num3 + 860f - 30f, num5, 24f), "Window: " + ((object)Plugin.MenuKey.Value).ToString() + " (BepInEx/config/community.cma.dlssnr.cfg)", m_stSmall);
		GUI.matrix = matrix;
		Event current = Event.current;
		Rect val2 = default;
		val2 = (m_menuOnScreen = new Rect(val.x * num, val.y * num, val.width * num, val.height * num));
		if (((int)current.type == 0 || (int)current.type == 1 || (int)current.type == 6) && val2.Contains(current.mousePosition))
		{
			current.Use();
		}
	}

	private void MotionStep(bool on)
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected Obj, but got Unknown
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Expected Obj, but got Unknown
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Expected Obj, but got Unknown
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Invalid comparison between Unknown and I4
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected Obj, but got Unknown
		if (m_motionFailed)
		{
			return;
		}
		try
		{
			if (!on)
			{
				MotionRestore();
				return;
			}
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime < m_motionAt)
			{
				return;
			}
			m_motionAt = unscaledTime + 5f;
			Il2CppArrayBase<Renderer> val = Object.FindObjectsOfType<Renderer>();
			int num = 0;
			string text = "";
			int num2 = 0;
			while (val != null && num2 < val.Length)
			{
				Renderer val2 = val[num2];
				TransparentMotion(val2);
				if (!((Object)val2 == (Object)null) && Unseen(m_motionSeen, val2) && (int)val2.motionVectorGenerationMode == 2)
				{
					Scene scene = ((Component)val2).gameObject.scene;
					if (scene.IsValid())
					{
						val2.motionVectorGenerationMode = (MotionVectorGenerationMode)1;
						SkinnedMeshRenderer val3 = ((Il2CppObjectBase)val2).TryCast<SkinnedMeshRenderer>();
						if ((Object)val3 != (Object)null && !val3.skinnedMotionVectors)
						{
							val3.skinnedMotionVectors = true;
						}
						m_noMotion.Add(val2);
						num++;
						if (text.Length < 160)
						{
							text = text + ((text.Length > 0) ? ", " : "") + ((Object)val2).name;
						}
					}
				}
				num2++;
			}
			bool flag = false;
			if (num > 0 && m_motionLogs < 12)
			{
				m_motionLogs++;
				_ = Plugin.ModLog;
				BepInExInfoLogInterpolatedStringHandler val4 = new BepInExInfoLogInterpolatedStringHandler(103, 5, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("Motion: ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(num);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" renderer(s) were set to write no motion (");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(text);
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>((num > 6) ? ", ..." : "");
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("); they write their own now (");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(m_noMotion.Count);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" in all, of ");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(val?.Length ?? 0);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" renderers).");
				}
				Plugin.Info(val4);
			}
			else if (m_motionLogs == 0)
			{
				m_motionLogs++;
				_ = Plugin.ModLog;
				BepInExInfoLogInterpolatedStringHandler val5 = new BepInExInfoLogInterpolatedStringHandler(50, 1, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("Motion: no renderer of ");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(val?.Length ?? 0);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(" is set to write no motion.");
				}
				Plugin.Info(val5);
			}
		}
		catch (Exception ex)
		{
			m_motionFailed = true;
			Plugin.ModLog.LogWarning((object)("Motion: the renderers could not be gone through (" + ex.Message + ")."));
		}
	}

	// Skin, brows, the scalp cap and the like are materials of the transparent queue (2500-2999) in this game, and those
	// write no motion vectors in HDRP: DLSS has none for them and smears what lies under (the forehead under the scalp
	// cap). Each such material is told to write them while the plugin runs; given back in MotionRestore.
	private readonly List<Material> m_transMotion = new List<Material>();

	private readonly HashSet<int> m_transSeen = new HashSet<int>();

	private void TransparentMotion(Renderer r)
	{
		if ((Object)r == (Object)null || !Plugin.TransparentMotionVec.Value || !m_transSeen.Add(((Object)r).GetInstanceID()))
		{
			return;
		}
		try
		{
			Il2CppReferenceArray<Material> sharedMaterials = r.sharedMaterials;
			for (int i = 0; sharedMaterials != null && i < sharedMaterials.Length; i++)
			{
				Material val = sharedMaterials[i];
				if (!((Object)val == (Object)null) && val.renderQueue >= 2500 && val.renderQueue < 3000 && val.HasProperty("_TransparentWritingMotionVec") && !(val.GetFloat("_TransparentWritingMotionVec") > 0.5f))
				{
					val.SetFloat("_TransparentWritingMotionVec", 1f);
					val.EnableKeyword("_TRANSPARENT_WRITES_MOTION_VEC");
					m_transMotion.Add(val);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void MotionRestore()
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected Obj, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected Obj, but got Unknown
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Invalid comparison between Unknown and I4
		foreach (Material item in m_transMotion)
		{
			if ((Object)item != (Object)null)
			{
				item.SetFloat("_TransparentWritingMotionVec", 0f);
				item.DisableKeyword("_TRANSPARENT_WRITES_MOTION_VEC");
			}
		}
		if (m_transMotion.Count > 0)
		{
			Plugin.Info((object)("Motion: " + m_transMotion.Count + " transparent material(s) wrote motion vectors; given back."));
		}
		m_transMotion.Clear();
		m_transSeen.Clear();
		m_motionSeen.Clear();
		if (m_noMotion.Count == 0)
		{
			return;
		}
		foreach (Renderer item in m_noMotion)
		{
			try
			{
				if ((Object)item != (Object)null && (int)item.motionVectorGenerationMode == 1)
				{
					item.motionVectorGenerationMode = (MotionVectorGenerationMode)2;
				}
			}
			catch (Exception)
			{
			}
		}
		_ = Plugin.ModLog;
		bool flag = false;
		BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(65, 1, out flag);
		if (flag)
		{
			((BepInExLogInterpolatedStringHandler)val).AppendLiteral("Motion: ");
			((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(m_noMotion.Count);
			((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" renderer(s) write no motion again, as the game had them.");
		}
		Plugin.Info(val);
		m_noMotion.Clear();
		m_motionAt = 0f;
	}

	private Texture Sharpened(Texture final)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected Obj, but got Unknown
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Expected Obj, but got Unknown
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Expected Obj, but got Unknown
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected Obj, but got Unknown
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected Obj, but got Unknown
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Expected Obj, but got Unknown
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Expected Obj, but got Unknown
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0239: Unknown result type (might be due to invalid IL or missing references)
		//IL_023e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Expected Obj, but got Unknown
		//IL_025c: Unknown result type (might be due to invalid IL or missing references)
		//IL_026c: Unknown result type (might be due to invalid IL or missing references)
		//IL_026e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Expected Obj, but got Unknown
		//IL_036c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0373: Expected Obj, but got Unknown
		//IL_02c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c9: Expected Obj, but got Unknown
		//IL_0302: Unknown result type (might be due to invalid IL or missing references)
		//IL_030d: Expected Obj, but got Unknown
		float num = Mathf.Clamp01(Plugin.Sharpness.Value);
		if (num < 0.01f || m_sharpOff || (Object)final == (Object)null)
		{
			return final;
		}
		string text = "its material";
		try
		{
			if ((Object)m_sharpMat == (Object)null)
			{
				Material defaultCanvasMaterial = Canvas.GetDefaultCanvasMaterial();
				if ((Object)defaultCanvasMaterial == (Object)null)
				{
					m_sharpOff = true;
					SharpState = "not available in this game";
					Plugin.Info((object)"Sharpening: the interface's material was not found; no sharpening.");
					return final;
				}
				m_sharpMat = new Material(defaultCanvasMaterial)
				{
					name = "CMADlssNr.Sharpen",
					hideFlags = (HideFlags)61
				};
				m_sharpQuad = Rectangle(0f);
				m_sharpCmd = new CommandBuffer
				{
					name = "CMADlssNr.Sharpen"
				};
			}
			text = "its textures";
			int width = final.width;
			int height = final.height;
			if ((Object)m_sharp == (Object)null || ((Texture)m_sharp).width != width || ((Texture)m_sharp).height != height)
			{
				Drop(ref m_sharp);
				Drop(ref m_sharpBlur);
				Drop(ref m_sharpSolid);
				m_sharp = Make("CMADlssNr.Sharp", width, height, (RenderTextureFormat)2, (RenderTextureReadWrite)1);
				((Texture)m_sharp).filterMode = (FilterMode)1;
				m_sharpSolid = Make("CMADlssNr.SharpSolid", width, height, (RenderTextureFormat)22, (RenderTextureReadWrite)1);
				((Texture)m_sharpSolid).filterMode = (FilterMode)1;
				m_sharpBlur = Make("CMADlssNr.SharpBlur", Mathf.Max(1, width / 2), Mathf.Max(1, height / 2), (RenderTextureFormat)22, (RenderTextureReadWrite)1);
				((Texture)m_sharpBlur).filterMode = (FilterMode)1;
			}
			text = "the blur";
			Texture val = final;
			if (!Plugin.FastSharpening.Value)
			{
				Graphics.Blit(final, m_sharpSolid);
				val = (Texture)m_sharpSolid;
			}
			Graphics.Blit(val, m_sharpBlur);
			Graphics.Blit((Texture)m_sharpBlur, m_sharp);
			text = "the frame over the blur";
			m_sharpMat.SetTexture("_MainTex", val);
			m_sharpMat.SetColor("_Color", new Color(1f, 1f, 1f, 1f + num));
			Matrix4x4 identity = Matrix4x4.identity;
			m_sharpCmd.Clear();
			m_sharpCmd.SetRenderTarget(new RenderTargetIdentifier((Texture)m_sharp));
			m_sharpCmd.SetViewProjectionMatrices(identity, identity);
			m_sharpCmd.DrawMesh(m_sharpQuad, identity, m_sharpMat, 0, 0);
			Graphics.ExecuteCommandBuffer(m_sharpCmd);
			m_sharpFails = 0;
			if (!m_sharpTold)
			{
				m_sharpTold = true;
				_ = Plugin.ModLog;
				bool flag = false;
				BepInExInfoLogInterpolatedStringHandler val2 = new BepInExInfoLogInterpolatedStringHandler(54, 4, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("Sharpening: on, ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<float>(num, "F2");
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(", with the interface's shader '");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(((Object)m_sharpMat.shader != (Object)null) ? ((Object)m_sharpMat.shader).name : "?");
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("' at ");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(width);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(height);
					((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(".");
				}
				Plugin.Info(val2);
			}
			return (Texture)m_sharp;
		}
		catch (Exception ex)
		{
			if (++m_sharpFails > 20)
			{
				m_sharpOff = true;
				SharpState = "not available in this game";
				Plugin.Info((object)("Sharpening: keeps failing at " + text + " (" + ex.GetType().Name + ": " + ex.Message + "); switched off."));
			}
			return final;
		}
	}

	private static uint PresetFor(int quality)
	{
		return PresetFor(quality, Plugin.Preset.Value);
	}

	// The preset for a quality given the one the window asked for. A feature that is already open keeps the preset it was
	// opened with until the swap (close, open) takes place: configuring it in place with another preset as well made the
	// game crash when switching to Auto (the in-place rebuild and the close-open followed each other).
	private static uint PresetFor(int quality, int value)
	{
		if (Chosen(value))
		{
			return (uint)value;
		}
		if (quality != 0 && quality != 3)
		{
			return 11u;
		}
		return 13u;
	}

	internal static bool Chosen(int preset)
	{
		if (preset != 5 && preset != 6 && preset != 11 && preset != 12)
		{
			return preset == 13;
		}
		return true;
	}

	internal static string PresetName(uint p)
	{
		if (p < 1 || p > 26)
		{
			return "?";
		}
		return ((char)(65 + p - 1)).ToString();
	}

	private int SrQuery(uint outW, uint outH, uint quality, out uint renderW, out uint renderH, out uint minW, out uint minH, out uint maxW, out uint maxH, out float sharpness)
	{
		if (!m_sqValid || m_sqW != outW || m_sqH != outH || m_sqQuality != quality)
		{
			m_sqResult = Native.vdn_sr_query(outW, outH, quality, out m_sqRenderW, out m_sqRenderH, out m_sqMinW, out m_sqMinH, out m_sqMaxW, out m_sqMaxH, out m_sqSharpness);
			m_sqW = outW;
			m_sqH = outH;
			m_sqQuality = quality;
			m_sqValid = true;
		}
		renderW = m_sqRenderW;
		renderH = m_sqRenderH;
		minW = m_sqMinW;
		minH = m_sqMinH;
		maxW = m_sqMaxW;
		maxH = m_sqMaxH;
		sharpness = m_sqSharpness;
		return m_sqResult;
	}

	// Custom quality: the frame's size as the slider says (a share of the screen's), whatever the presets give. DLSS's
	// Quality mode takes 50 % to 100 % of the screen, Ultra Performance what lies below; the size is held to the range of
	// the mode used. The slider's value counts once it has stood still for 20 frames (a change builds DLSS's feature
	// again, which is not done on every step of a drag).
	private float m_scaleEff = -1f;

	private float m_scaleSeen = -1f;

	private int m_scaleSeenAt;

	private float CustomScale()
	{
		float num = Mathf.Clamp(Plugin.RenderScale.Value, 0.33f, 1f);
		if (m_scaleEff < 0f)
		{
			m_scaleEff = num;
			m_scaleSeen = num;
		}
		if (Mathf.Abs(num - m_scaleSeen) > 0.0005f)
		{
			m_scaleSeen = num;
			m_scaleSeenAt = Time.frameCount;
		}
		else if (m_scaleSeen != m_scaleEff && Time.frameCount - m_scaleSeenAt >= 20)
		{
			m_scaleEff = m_scaleSeen;
		}
		return m_scaleEff;
	}

	private int CustomMode()
	{
		return (CustomScale() >= 0.5f) ? 2 : 3;
	}

	private void RenderExtentCustom(int ow, int oh, out int rw, out int rh)
	{
		rw = ow;
		rh = oh;
		float num = CustomScale();
		if (SrQuery((uint)ow, (uint)oh, (uint)CustomMode(), out var _, out var _, out var minW, out var minH, out var maxW, out var maxH, out var _) != 0)
		{
			return;
		}
		uint num2 = (uint)Mathf.RoundToInt((float)ow * num);
		uint num3 = (uint)Mathf.RoundToInt((float)oh * num);
		if (maxW >= minW && maxW != 0 && maxH >= minH)
		{
			num2 = Math.Clamp(num2, minW, maxW);
			num3 = Math.Clamp(num3, minH, maxH);
		}
		if (num2 >= 64 && num3 >= 64 && num2 <= (uint)ow && num3 <= (uint)oh)
		{
			rw = (int)num2;
			rh = (int)num3;
		}
	}

	private void RenderExtent(int ow, int oh, out int rw, out int rh)
	{
		//IL_0304: Unknown result type (might be due to invalid IL or missing references)
		//IL_030b: Expected Obj, but got Unknown
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Expected Obj, but got Unknown
		rw = ow;
		rh = oh;
		if (!UpscaleWanted)
		{
			return;
		}
		try
		{
			if (m_srOpen && m_srPresetOpen != Plugin.Preset.Value)
			{
				if (m_srSwapAt < 0)
				{
					m_srSwapAt = Time.frameCount + ((m_fgEnabled || m_fgOpened) ? 12 : 0);
					FrameGenHold(40);
					FrameGenClose();
				}
				if (Time.frameCount >= m_srSwapAt)
				{
					m_srSwapAt = -1;
					TeardownSr();
					FrameGenHold(30);
				}
			}
			else
			{
				m_srSwapAt = -1;
			}
			if (!m_srOpen)
			{
				m_srPresetOpen = Plugin.Preset.Value;
				Native.vdn_rebuild_begin();
				int num;
				try
				{
					num = Native.vdn_sr_open(Native.SnippetDir, ((Texture)Texture2D.whiteTexture).GetNativeTexturePtr(), Plugin.VerboseLog.Value ? 1 : 0);
				}
				finally
				{
					Native.vdn_rebuild_end();
				}
				if (num != 0)
				{
					Plugin.ModLog.LogError((object)("DLSS: could not be opened (" + num + "): " + Native.Text(Native.vdn_sr_status_text()) + ". The frame is rendered at the size of the screen."));
					m_srFailed = true;
					return;
				}
				m_srOpen = true;
				Plugin.Info((object)"DLSS: opened.");
				Bridge.Pin();
			}
			if (Plugin.Quality.Value == 5)
			{
				return;
			}
			if (Plugin.Quality.Value == 6)
			{
				RenderExtentCustom(ow, oh, out rw, out rh);
				return;
			}
			int num2 = Mathf.Clamp(Plugin.Quality.Value, 0, 4);
			bool flag = num2 == 4 && !Plugin.NativeUltraQuality.Value;
			int num3 = SrQuery((uint)ow, (uint)oh, flag ? 2u : ((uint)num2), out var renderW, out var renderH, out var minW, out var minH, out var maxW, out var maxH, out var _);
			bool flag2 = false;
			if ((num3 == 0) & flag)
			{
				float num4 = Mathf.Clamp(Plugin.UltraQualityScale.Value, 0.67f, 1f);
				uint num5 = (uint)Mathf.RoundToInt((float)ow * num4);
				uint num6 = (uint)Mathf.RoundToInt((float)oh * num4);
				if (maxW >= renderW && maxH >= renderH)
				{
					num5 = Math.Min(num5, maxW);
					num6 = Math.Min(num6, maxH);
				}
				if (num5 > renderW && num6 > renderH)
				{
					renderW = num5;
					renderH = num6;
				}
				if (!m_uqTold)
				{
					m_uqTold = true;
					_ = Plugin.ModLog;
					BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(73, 8, out flag2);
					if (flag2)
					{
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("DLSS: Ultra Quality: rendered ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(renderW);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(renderH);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" for ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(ow);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(oh);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" (DLSS's Quality mode takes ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(minW);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(minH);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" to ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(maxW);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<uint>(maxH);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(").");
					}
					Plugin.Info(val);
				}
			}
			if (num3 != 0 || renderW < 64 || renderH < 64 || renderW > ow || renderH > oh)
			{
				if (!m_srTold)
				{
					m_srTold = true;
					ManualLogSource modLog = Plugin.ModLog;
					BepInExWarningLogInterpolatedStringHandler val2 = new BepInExWarningLogInterpolatedStringHandler(115, 7, out flag2);
					if (flag2)
					{
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("DLSS: no render extent was given for ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(ow);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(oh);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" at quality ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(num2);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" (result ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<int>(num3);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(", ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<uint>(renderW);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<uint>(renderH);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("): ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(Native.Text(Native.vdn_sr_status_text()));
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(". The frame is rendered at the size of the screen.");
					}
					modLog.LogWarning(val2);
				}
			}
			else
			{
				rw = (int)renderW;
				rh = (int)renderH;
			}
		}
		catch (Exception ex)
		{
			m_srFailed = true;
			Plugin.ModLog.LogError((object)("DLSS: failed while asking for its render extent (" + ex.Message + "). The frame is rendered at the size of the screen."));
		}
	}

	private bool EnsureSr(int rw, int rh, int ow, int oh)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected Obj, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected Obj, but got Unknown
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected Obj, but got Unknown
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Expected Obj, but got Unknown
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Expected Obj, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Expected Obj, but got Unknown
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Expected Obj, but got Unknown
		//IL_02e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ed: Expected Obj, but got Unknown
		int num = Mathf.Clamp(Plugin.Quality.Value, 0, 6);
		int mode = ((num == 6) ? CustomMode() : num);
		int num2 = ((m_srOpen && m_srPresetOpen >= 0) ? m_srPresetOpen : Mathf.Max(0, Plugin.Preset.Value));
		bool value = Plugin.AutoExposure.Value;
		bool flag = false;
		if ((Object)m_srColor == (Object)null || ((Texture)m_srColor).width != rw || ((Texture)m_srColor).height != rh)
		{
			Drop(ref m_srColor);
			m_srColor = Make("CMADlssNr.SrColor", rw, rh, (RenderTextureFormat)2, (RenderTextureReadWrite)1);
			flag = true;
		}
		if ((Object)m_srOut == (Object)null || ((Texture)m_srOut).width != ow || ((Texture)m_srOut).height != oh)
		{
			Drop(ref m_srOut);
			m_srOut = new RenderTexture(ow, oh, 0, (RenderTextureFormat)2, (RenderTextureReadWrite)1)
			{
				name = "CMADlssNr.SrOut",
				hideFlags = (HideFlags)61,
				filterMode = (FilterMode)0,
				enableRandomWrite = true
			};
			m_srOut.Create();
			flag = true;
		}
		IntPtr intPtr = Ptr((Texture)m_srColor);
		IntPtr intPtr2 = Ptr((Texture)m_mvec);
		IntPtr intPtr3 = Ptr((Texture)m_depth);
		IntPtr intPtr4 = Ptr((Texture)m_srOut);
		bool flag2 = flag || m_srRenderW != rw || m_srRenderH != rh || m_srOutW != ow || m_srOutH != oh || m_srQuality != num || m_srModel != num2 || m_srAuto != value;
		if (!flag2 && intPtr == m_srRegColor && intPtr2 == m_srRegMvec && intPtr3 == m_srRegDepth && intPtr4 == m_srRegOut)
		{
			return true;
		}
		Native.vdn_rebuild_begin();
		try
		{
			if (flag2)
			{
				FrameGenHold(30);
				m_srRenderW = rw;
				m_srRenderH = rh;
				m_srOutW = ow;
				m_srOutH = oh;
				m_srQuality = num;
				m_srModel = num2;
				m_srAuto = value;
				Native.vdn_sr_configure((uint)rw, (uint)rh, (uint)ow, (uint)oh, (num == 4 && !Plugin.NativeUltraQuality.Value) ? 2u : ((uint)mode), PresetFor(mode, num2), Plugin.DepthInverted.Value ? 1 : 0, value ? 1 : 0, 0, 1u);
			}
			int num3 = Native.vdn_sr_set_textures(0, intPtr, intPtr2, intPtr3, intPtr4);
			if (num3 != 0)
			{
				Plugin.ModLog.LogError((object)("DLSS: the textures were refused (0x" + num3.ToString("X") + "): " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly."));
				m_srFailed = true;
				return false;
			}
			m_srRegColor = intPtr;
			m_srRegMvec = intPtr2;
			m_srRegDepth = intPtr3;
			m_srRegOut = intPtr4;
			m_srReset = 2;
			if (flag2)
			{
				Bridge.Pin();
				_ = Plugin.ModLog;
				bool flag3 = false;
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(39, 7, out flag3);
				if (flag3)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("DLSS: render ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(rw);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(rh);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" -> screen ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(ow);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(oh);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" (");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(s_qualities[num]);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(", preset ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(PresetName(PresetFor(mode, num2)));
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(Chosen(num2) ? ", chosen" : ", chosen for the quality");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(").");
				}
				Plugin.Info(val);
			}
			return true;
		}
		finally
		{
			Native.vdn_rebuild_end();
		}
	}

	private RenderTexture Upscaled(int rw, int rh, int ow, int oh, Texture from, bool flipFrom)
	{
		if (m_srFailed || !m_srOpen || !EnsureSr(rw, rh, ow, oh))
		{
			return null;
		}
		Blit(from, m_srColor, 1f, 1f, flipFrom);
		float mvScaleX = (float)rw * (Plugin.InvertMotionX.Value ? (-1f) : 1f);
		float mvScaleY = (float)rh * (Plugin.InvertMotionY.Value ? (-1f) : 1f);
		int srSlot = m_srSlot;
		m_srSlot = (m_srSlot + 1) & 7;
		Native.vdn_sr_set_frame(srSlot, 0, m_jitterX, m_jitterY, mvScaleX, mvScaleY, (m_srReset > 0) ? 1 : 0);
		if (m_srReset > 0)
		{
			m_srReset--;
		}
		GL.IssuePluginEvent(Native.vdn_get_render_event_func(), Native.SrEvent(srSlot));
		switch (Native.vdn_sr_error())
		{
		case 1:
			Plugin.ModLog.LogError((object)("DLSS: the feature could not be made: " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly."));
			m_srFailed = true;
			return null;
		case 2:
			if (++m_srEvalFails >= 60)
			{
				Plugin.ModLog.LogError((object)("DLSS: it has failed " + m_srEvalFails + " frames running: " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly."));
				m_srFailed = true;
			}
			return null;
		default:
			if (Native.vdn_sr_ready() == 0)
			{
				return null;
			}
			m_srEvalFails = 0;
			return m_srOut;
		}
	}

	private static void Drop(ref RenderTexture rt)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Expected Obj, but got Unknown
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected Obj, but got Unknown
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected Obj, but got Unknown
		if ((Object)rt != (Object)null)
		{
			Forget((Texture)rt);
			rt.Release();
			Object.Destroy((Object)rt);
		}
		rt = null;
	}

	private void TeardownSr()
	{
		try
		{
			if (m_srOpen)
			{
				Native.vdn_rebuild_begin();
				try
				{
					Native.vdn_sr_close();
				}
				finally
				{
					Native.vdn_rebuild_end();
				}
			}
		}
		catch (Exception)
		{
		}
		m_srOpen = false;
		m_sqValid = false;
		Drop(ref m_srColor);
		Drop(ref m_srOut);
		m_srRegColor = (m_srRegMvec = (m_srRegDepth = (m_srRegOut = IntPtr.Zero)));
		m_srQuality = -1;
		m_srTold = false;
	}
}
