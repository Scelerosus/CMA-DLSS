using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

[BepInPlugin("community.cma.dlssnr", "CMA DLSS NR", "0.8.3")]
[BepInProcess("Corrupted Modeling Agency.exe")]
public sealed class Plugin : BasePlugin
{
	public const string PluginGuid = "community.cma.dlssnr";

	public const string PluginName = "CMA DLSS NR";

	public const string PluginVersion = "0.8.3";

	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> Enabled;

	internal static ConfigEntry<bool> NrAfter;

	internal static ConfigEntry<bool> UiCorrection;

	internal static ConfigEntry<bool> Upscale;

	internal static ConfigEntry<bool> AutoExposure;

	internal static ConfigEntry<bool> VerboseLog;

	internal static ConfigEntry<bool> Jitter;

	internal static ConfigEntry<bool> JitterInvertX;

	internal static ConfigEntry<bool> JitterInvertY;

	internal static ConfigEntry<bool> MipBias;

	internal static ConfigEntry<bool> FrameGen;

	internal static ConfigEntry<bool> FgPacing;

	internal static ConfigEntry<int> FgMultiplier;

	internal static ConfigEntry<bool> FgInvertMotionX;

	internal static ConfigEntry<bool> FgInvertMotionY;

	internal static ConfigEntry<bool> Reflex;

	internal static ConfigEntry<bool> ReflexBoost;

	internal static ConfigEntry<bool> Mask;

	internal static ConfigEntry<bool> MaskSoft;

	internal static ConfigEntry<bool> GameBloom;

	internal static ConfigEntry<bool> GameSss;

	internal static ConfigEntry<bool> GameDof;

	internal static ConfigEntry<bool> GameMotionBlur;

	internal static ConfigEntry<bool> GameChromatic;

	internal static ConfigEntry<bool> GameVignette;

	internal static ConfigEntry<bool> GameGrain;

	internal static ConfigEntry<bool> GameSsr;

	internal static ConfigEntry<bool> GameSsao;

	internal static ConfigEntry<bool> GameSsgi;

	internal static ConfigEntry<bool> GameContactShadows;

	internal static ConfigEntry<bool> GameVolumetricFog;

	internal static ConfigEntry<bool> GameVolumetricClouds;

	internal static ConfigEntry<bool> FlipY;

	internal static ConfigEntry<bool> InvertMotionX;

	internal static ConfigEntry<bool> InvertMotionY;

	internal static ConfigEntry<bool> DepthInverted;

	internal static ConfigEntry<KeyCode> MenuKey;

	internal static ConfigEntry<KeyCode> ToggleKey;

	internal static ConfigEntry<KeyCode> QualityKey;

	internal static ConfigEntry<bool> FastNetwork;

	internal static ConfigEntry<bool> StartDlss;

	internal static ConfigEntry<bool> StartNeural;

	internal static ConfigEntry<bool> StartFrameGen;

	internal static ConfigEntry<bool> FastScans;

	internal static ConfigEntry<bool> FastSharpening;

	internal static ConfigEntry<bool> HalfResMask;

	internal static ConfigEntry<bool> FastMaskScan;

	internal static ConfigEntry<bool> FastBuffers;

	internal static ConfigEntry<int> StaticShadowsAtlas;

	internal static ConfigFile Cfg;

	internal static readonly ConfigEntry<float>[] RegionEffect = new ConfigEntry<float>[4];

	internal static readonly ConfigEntry<float>[] RegionDetail = new ConfigEntry<float>[4];

	internal static ConfigEntry<float> Intensity;

	internal static ConfigEntry<float> LocalTone;

	internal static ConfigEntry<float> LocalStructure;

	internal static ConfigEntry<float> UltraQualityScale;

	internal static ConfigEntry<bool> NativeUltraQuality;

	internal static ConfigEntry<float> RenderScale;

	internal static ConfigEntry<bool> TransparentMotionVec;

	internal static ConfigEntry<float> Sharpness;

	internal static ConfigEntry<int> Style;

	internal static ConfigEntry<int> Passes;

	internal static ConfigEntry<int> Quality;

	internal static ConfigEntry<int> Preset;

	internal static void Info(object data)
	{
		if (VerboseLog != null && VerboseLog.Value)
		{
			ModLog.LogInfo(data);
		}
	}

	internal static void Info(BepInExInfoLogInterpolatedStringHandler logHandler)
	{
		if (VerboseLog != null && VerboseLog.Value)
		{
			ModLog.LogInfo(logHandler);
		}
	}

	public override void Load()
	{
		//IL_08b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_08bf: Expected Obj, but got Unknown
		//IL_086a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0870: Expected Obj, but got Unknown
		//IL_0972: Unknown result type (might be due to invalid IL or missing references)
		ModLog = ((BasePlugin)this).Log;
		Cfg = ((BasePlugin)this).Config;
		((BasePlugin)this).Config.SaveOnConfigSet = false;
		Enabled = ((BasePlugin)this).Config.Bind<bool>("General", "Enabled", true, "Run neural rendering (independent of DLSS: either runs without the other).");
		NrAfter = ((BasePlugin)this).Config.Bind<bool>("General", "NeuralAfterDlss", false, "Run neural rendering after DLSS, on the full-size picture, instead of before it on the small frame: finer, and as costly as at DLAA.");
		Intensity = ((BasePlugin)this).Config.Bind<float>("General", "Intensity", 1f, "How much of the network's result is shown, 0 to 1.");
		Style = ((BasePlugin)this).Config.Bind<int>("General", "Style", 0, "0 Default, 1 Natural, 2 Cinematic.");
		Passes = ((BasePlugin)this).Config.Bind<int>("General", "Passes", 1, "How many times the network is run over the frame, 1 to 3 (each pass costs its whole time and memory again).");
		UiCorrection = ((BasePlugin)this).Config.Bind<bool>("General", "InterfaceCorrection", true, "The network's own hint that there is interface (lettering) in the frame, to be left alone. The game draws its subtitles and some menus as part of the frame.");
		LocalTone = ((BasePlugin)this).Config.Bind<float>("Strength", "LocalTone", 1f, "Strength of the network's relighting, 0 to 2; 1 is neutral.");
		LocalStructure = ((BasePlugin)this).Config.Bind<float>("Strength", "LocalStructure", 1f, "Strength of the fine detail the network adds, 0 to 2; 1 is neutral.");
		MenuKey = ((BasePlugin)this).Config.Bind<KeyCode>("Keys", "Menu", (KeyCode)278, "Key that opens the settings window.");
		ToggleKey = ((BasePlugin)this).Config.Bind<KeyCode>("Keys", "Toggle", (KeyCode)0, "Key that switches neural rendering on and off (none by default: it is in the window).");
		QualityKey = ((BasePlugin)this).Config.Bind<KeyCode>("Keys", "Quality", (KeyCode)0, "Key that goes through the DLSS qualities: DLAA, Quality, Balanced, Performance, Ultra Performance (none by default).");
		Upscale = ((BasePlugin)this).Config.Bind<bool>("DLSS", "Upscale", true, "Run DLSS: anti-aliasing at DLAA, and below it a smaller frame reconstructed to the size of the screen.");
		Quality = ((BasePlugin)this).Config.Bind<int>("DLSS", "Quality", 5, "0 Performance, 1 Balanced, 2 Quality, 3 Ultra Performance, 4 Ultra Quality, 5 DLAA (the size of the screen: no upscaling), 6 Custom (RenderScale).");
		Preset = ((BasePlugin)this).Config.Bind<int>("DLSS", "Preset", 13, "The DLSS model preset: 0 by the quality (K, with M for Performance and Ultra Performance); 11 K, 13 M; the older, lighter models for RTX 20 and 30 cards: 5 E, 6 F. Preset L is not offered: it crashes the driver in this game.");
		TransparentMotionVec = ((BasePlugin)this).Config.Bind<bool>("DLSS", "TransparentMotionVectors", true, "Make the game's skin, brows, scalp cap and other transparent-queue materials write motion vectors while the plugin runs (they write none, and DLSS then smears what lies under them). Off: as the game has them.");
		RenderScale = ((BasePlugin)this).Config.Bind<float>("DLSS", "RenderScale", 0.75f, "Quality 'Custom': the frame is rendered at this share of the screen's size (0.33 to 1), whatever the presets say. Held to the range DLSS takes (Quality mode 0.5 to 1, below it Ultra Performance).");
		NativeUltraQuality = ((BasePlugin)this).Config.Bind<bool>("DLSS", "NativeUltraQuality", false, "Ultra Quality as DLSS's own Ultra Quality mode. Off (the default): DLSS's Quality mode fed a larger frame (UltraQualityScale), which made the skin blotchy.");
		UltraQualityScale = ((BasePlugin)this).Config.Bind<float>("DLSS", "UltraQualityScale", 0.77f, "Ultra Quality: how much of the screen's size the frame is rendered at each way, 0.67 (the same as Quality) to 1.");
		Sharpness = ((BasePlugin)this).Config.Bind<float>("DLSS", "Sharpen", 0.3f, "Sharpening of what is shown (an unsharp mask after everything else), 0 to 1.");
		AutoExposure = ((BasePlugin)this).Config.Bind<bool>("DLSS", "AutoExposure", true, "Let DLSS work out the exposure itself.");
		MipBias = ((BasePlugin)this).Config.Bind<bool>("DLSS", "SharperTextures", true, "While the frame is rendered smaller, read textures as sharp as for the size of the screen (a negative mip bias on the camera, by NVIDIA's rule).");
		Jitter = ((BasePlugin)this).Config.Bind<bool>("DLSS", "ProjectionJitter", true, "Give DLSS a jittered frame to reconstruct from (HDRP's own jitter, with its own anti-aliasing pass switched off). Off: DLSS works on what the game's anti-aliasing gives, which is a blur.");
		JitterInvertX = ((BasePlugin)this).Config.Bind<bool>("DLSS", "JitterNegateX", false, "Negate the X of HDRP's jitter when it is told to DLSS. Off is right in this game (found by trying all four pairs of signs).");
		JitterInvertY = ((BasePlugin)this).Config.Bind<bool>("DLSS", "JitterNegateY", true, "Negate the Y of HDRP's jitter when it is told to DLSS. On is right in this game, with FlipY on.");
		VerboseLog = ((BasePlugin)this).Config.Bind<bool>("DLSS", "VerboseLog", false, "The plugin's info lines in BepInEx\\LogOutput.log, and neural rendering's NGX log (off: only warnings and errors are written).");
		FrameGen = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "Enabled", false, "DLSS frame generation: one made frame is shown between every two rendered ones. It hooks the presenting of the game's swap chain. Needs an RTX 40 or 50 card and hardware-accelerated GPU scheduling.");
		FgPacing = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "Pacing", true, "Space the made frames evenly between the rendered ones.");
		FgMultiplier = ((BasePlugin)this).Config.Bind<int>("FrameGen", "Multiplier", 2, "Frames shown for each rendered one: 2 shows one made frame between rendered ones, 3 two, 4 three (up to what the driver or the bridge mod allows). A 60 Hz monitor shows at most 60 frames a second, so more than 2 only lowers the rendered frames there; it is for 120 Hz and faster monitors.");
		FgInvertMotionX = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "InvertMotionX", true, "Negate the motion vectors' X for frame generation.");
		FgInvertMotionY = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "InvertMotionY", true, "Negate the motion vectors' Y for frame generation (on: it reads the buffers as they are, not turned upside down).");
		Reflex = ((BasePlugin)this).Config.Bind<bool>("Latency", "Reflex", true, "NVIDIA Reflex low latency mode for the game's device, through the driver.");
		ReflexBoost = ((BasePlugin)this).Config.Bind<bool>("Latency", "ReflexBoost", false, "Reflex with boost: the GPU is kept from slowing its clock, for a little less delay at more power.");
		Mask = ((BasePlugin)this).Config.Bind<bool>("Mask", "Enabled", false, "Tell the network where skin, hair and clothing are (a mask drawn from the characters' meshes), and set how much of itself it is on each.");
		MaskSoft = ((BasePlugin)this).Config.Bind<bool>("Mask", "Soft", true, "Soften the mask so that regions fade into one another.");
		for (int i = 0; i < 4; i++)
		{
			RegionEffect[i] = ((BasePlugin)this).Config.Bind<float>("Mask", Capture.RegionNames[i] + "Effect", 1f, "How much of the network's result is kept on this region, as a part of Intensity, 0 to 1.");
			RegionDetail[i] = ((BasePlugin)this).Config.Bind<float>("Mask", Capture.RegionNames[i] + "Detail", 1f, "How much fine detail the network adds on this region, as a multiple of Local structure, 0 to 2.");
		}
		GameBloom = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "Bloom", true, "Leave the game's bloom as it is (false: switched off for the main camera while the plugin runs).");
		GameSss = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "SubsurfaceScattering", true, "Leave the game's subsurface scattering (skin) as it is.");
		GameDof = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "DepthOfField", true, "Leave the game's depth of field as it is.");
		GameMotionBlur = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "MotionBlur", true, "Leave the game's motion blur as it is.");
		GameChromatic = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "ChromaticAberration", true, "Leave the game's chromatic aberration as it is.");
		GameVignette = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "Vignette", true, "Leave the game's vignette as it is.");
		GameGrain = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "FilmGrain", true, "Leave the game's film grain as it is.");
		GameSsr = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "ScreenSpaceReflections", true, "Leave the game's screen-space reflections as they are.");
		GameSsao = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "AmbientOcclusion", true, "Leave the game's screen-space ambient occlusion as it is.");
		GameSsgi = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "ScreenSpaceGlobalIllumination", true, "Leave the game's screen-space global illumination as it is.");
		GameContactShadows = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "ContactShadows", true, "Leave the game's contact shadows as they are.");
		GameVolumetricFog = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "VolumetricFog", true, "Leave the game's volumetric fog and light shafts as they are.");
		GameVolumetricClouds = ((BasePlugin)this).Config.Bind<bool>("GameEffects", "VolumetricClouds", true, "Leave the game's volumetric clouds as they are.");
		FlipY = ((BasePlugin)this).Config.Bind<bool>("Orientation", "FlipY", true, "Hand every texture over upside down, and turn the result back: the way up the network and DLSS expect.");
		InvertMotionX = ((BasePlugin)this).Config.Bind<bool>("Orientation", "InvertMotionX", true, "Negate the motion vectors' X (the game stores current minus previous, NGX wants previous minus current).");
		InvertMotionY = ((BasePlugin)this).Config.Bind<bool>("Orientation", "InvertMotionY", false, "Negate the motion vectors' Y. Off while FlipY is on (the flip does it already).");
		DepthInverted = ((BasePlugin)this).Config.Bind<bool>("Orientation", "DepthInverted", SystemInfo.usesReversedZBuffer, "The depth that is handed over is reversed (near 1, far 0).");
		FastNetwork = ((BasePlugin)this).Config.Bind<bool>("Bridge", "FastNetwork", true, "Run neural rendering on the FP16 rebuild of the network that the nvidia mfg bridge mod (a proxy DLL beside the game) hands over: the same picture, faster. Kept in the mod's dlssg_to_fsr3.ini as EnableDlssNr, which the mod reads when the game starts.");
		StartDlss = ((BasePlugin)this).Config.Bind<bool>("Startup", "Dlss", true, "Switch DLSS on as the game starts, whatever it was left at.");
		StartNeural = ((BasePlugin)this).Config.Bind<bool>("Startup", "NeuralRendering", true, "Switch neural rendering on as the game starts, whatever it was left at.");
		StartFrameGen = ((BasePlugin)this).Config.Bind<bool>("Startup", "FrameGeneration", true, "Switch frame generation on as the game starts, whatever it was left at.");
		if (StartDlss.Value)
		{
			Upscale.Value = true;
		}
		if (StartNeural.Value)
		{
			Enabled.Value = true;
		}
		if (StartFrameGen.Value)
		{
			FrameGen.Value = true;
		}
		FastScans = ((BasePlugin)this).Config.Bind<bool>("Optimization", "FastScans", true, "Go through the scene's renderers (hair, and objects that write no motion vectors) checking each one once, not every one again every few seconds; everything is checked afresh every 30 s.");
		FastSharpening = ((BasePlugin)this).Config.Bind<bool>("Optimization", "FastSharpening", true, "Sharpen in three full-screen passes instead of four (the same picture: the frame is blurred and laid over its blur straight from where it is, without a copy first).");
		HalfResMask = ((BasePlugin)this).Config.Bind<bool>("Optimization", "HalfResMask", true, "With the mask's Soft on, draw the mask at half the frame's width and height. It is made a third of the size and spread out again afterwards anyway, so it looks the same; the characters' meshes are drawn several times for it every frame.");
		FastMaskScan = ((BasePlugin)this).Config.Bind<bool>("Optimization", "FastMaskScan", true, "When the mask looks for the characters (every 3 s), look at each renderer once and remember what its parts are (skin, hair or clothing), instead of reading every renderer's names and materials again; everything is looked at afresh every 30 s.");
		FastBuffers = ((BasePlugin)this).Config.Bind<bool>("Optimization", "FastBuffers", true, "With frame generation off, take the motion vectors and the depth out of HDRP's texture arrays in one copy instead of two. The one copy is checked once against the two; if it does not give the same, the two stay. With frame generation on the two stay anyway (DLSS-G takes the first).");
		StaticShadowsAtlas = ((BasePlugin)this).Config.Bind<int>("Optimization", "StaticShadowsAtlas", 8192, "The size of HDRP's atlas for kept shadows (spot and point lights keep the shadows of what does not move, and draw only what moves every frame), set as the game starts; 0 leaves the game's. The game has 4096, in which not even one point light at 2048 a side (6 x 2048 x 2048) fits. 8192 takes 128-256 MB of video memory.");
		ShadowCache.Prepare(StaticShadowsAtlas.Value);
		Bridge.Start(FastNetwork.Value);
		bool flag = false;
		if (!Native.Load())
		{
			ManualLogSource log = ((BasePlugin)this).Log;
			BepInExErrorLogInterpolatedStringHandler val = new BepInExErrorLogInterpolatedStringHandler(48, 2, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>("CMA DLSS NR");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>("0.8.3");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(": the native half is not there; nothing is run.");
			}
			log.LogError(val);
			return;
		}
		((BasePlugin)this).AddComponent<Capture>();
		_ = ((BasePlugin)this).Log;
		BepInExInfoLogInterpolatedStringHandler val2 = new BepInExInfoLogInterpolatedStringHandler(82, 6, out flag);
		if (flag)
		{
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>("CMA DLSS NR");
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>("0.8.3");
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" loaded: neural rendering ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(Enabled.Value ? "on" : "off");
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(", DLSS ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(Upscale.Value ? "on" : "off");
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(", frame generation ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(FrameGen.Value ? "on" : "off");
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("; ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<KeyCode>(MenuKey.Value);
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" opens the settings window.");
		}
		Info(val2);
	}
}
