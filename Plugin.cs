using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

using IntPtr = System.IntPtr;
using Object = UnityEngine.Object;

namespace CMADlssNr;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Corrupted Modeling Agency.exe")]
public sealed class Plugin : BasePlugin
{
	public const string PluginGuid = "community.cma.dlssnr";

	public const string PluginName = "CMA DLSS NR";

	public const string PluginVersion = "0.1.15";

	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> Enabled;

	internal static ConfigEntry<bool> NrAfter;

	internal static ConfigEntry<bool> UiCorrection;

	internal static ConfigEntry<bool> Upscale;

	internal static ConfigEntry<bool> AutoExposure;

	internal static ConfigEntry<bool> HairOwnMotion;

	internal static ConfigEntry<bool> TransparentMotionVec;

	internal static ConfigEntry<bool> ScalpDepth;

	internal static ConfigEntry<bool> HairTransparentMotion;

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

	internal static ConfigEntry<bool> FgResetOnCuts;

	internal static ConfigEntry<bool> FgFrameCap;

	internal static ConfigEntry<int> FgPacingFloor;

	internal static ConfigEntry<int> FgMotionUnits;

	internal static ConfigEntry<bool> FgOwnPresenter;

	internal static ConfigEntry<bool> Reflex;

	internal static ConfigEntry<bool> ReflexBoost;

	internal static ConfigEntry<bool> ShortQueue;

	internal static ConfigEntry<bool> FluidSemen;

	internal static ConfigEntry<bool> FluidSquirt;

	internal static ConfigEntry<bool> FluidSim;

	internal static ConfigEntry<bool> FluidDrool;

	internal static ConfigEntry<bool> FluidVag;

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

	internal static ConfigEntry<bool> UseShadowCache;

	internal static ConfigFile Cfg;

	internal static readonly ConfigEntry<float>[] RegionEffect = new ConfigEntry<float>[4];

	internal static readonly ConfigEntry<float>[] RegionDetail = new ConfigEntry<float>[4];

	internal static ConfigEntry<float> Intensity;

	internal static ConfigEntry<float> LocalTone;

	internal static ConfigEntry<float> LocalStructure;

	internal static ConfigEntry<float> UltraQualityScale;

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
		//IL_0a5f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a65: Expected Obj, but got Unknown
		//IL_0a0b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a11: Expected Obj, but got Unknown
		//IL_0b18: Unknown result type (might be due to invalid IL or missing references)
		ModLog = ((BasePlugin)this).Log;
		Cfg = ((BasePlugin)this).Config;
		((BasePlugin)this).Config.SaveOnConfigSet = false;
		Enabled = ((BasePlugin)this).Config.Bind<bool>("General", "Enabled", true, "Run neural rendering (independent of DLSS: either runs without the other).");
		NrAfter = ((BasePlugin)this).Config.Bind<bool>("General", "NeuralAfterDlss", true, "Run neural rendering after DLSS, on the full-size picture, instead of before it on the small frame: finer, and as costly as at DLAA.");
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
		Quality = ((BasePlugin)this).Config.Bind<int>("DLSS", "Quality", 5, "0 Performance, 1 Balanced, 2 Quality, 3 Ultra Performance, 4 Ultra Quality (left out: it makes the skin soft; it runs as Quality), 5 DLAA (the size of the screen: no upscaling).");
		if (Quality.Value == 4 || Quality.Value == 6)
		{
			Quality.Value = 2;
		}
		Preset = ((BasePlugin)this).Config.Bind<int>("DLSS", "Preset", 13, "The DLSS model preset: 0 by the quality (K, with M for Performance and Ultra Performance); 11 K, 13 M; the older, lighter models for RTX 20 and 30 cards: 5 E, 6 F. Preset L is not offered: it crashes the driver in this game.");
		UltraQualityScale = ((BasePlugin)this).Config.Bind<float>("DLSS", "UltraQualityScale", 0.77f, "Ultra Quality: how much of the screen's size the frame is rendered at each way, 0.67 (the same as Quality) to 1.");
		Sharpness = ((BasePlugin)this).Config.Bind<float>("DLSS", "Sharpen", 0.3f, "Sharpening of what is shown (an unsharp mask after everything else), 0 to 1.");
		AutoExposure = ((BasePlugin)this).Config.Bind<bool>("DLSS", "AutoExposure", true, "Let DLSS work out the exposure itself.");
		ScalpDepth = ((BasePlugin)this).Config.Bind<bool>("DLSS", "ScalpDepth", true, "The scalp cap (the see-through layer of hair roots on the forehead) writes depth while the plugin runs (ZWrite and the depth prepass), so DLSS does not keep old history on the skin under it (dark stripes on the forehead with preset K). Off: as the game has it. May change how the hairline looks; if it does, switch it off.");
		TransparentMotionVec = ((BasePlugin)this).Config.Bind<bool>("DLSS", "TransparentMotionVectors", true, "Make the game's skin, brows, scalp cap and other transparent-queue materials write motion vectors while the plugin runs (they write none, and DLSS then keeps old history on them: soft, streaky skin, most with preset K). Off: as the game has them.");
		HairOwnMotion = ((BasePlugin)this).Config.Bind<bool>("DLSS", "HairOwnMotion", true, "Strand hair (the game's GPU hair, HDRP/Hair shaders) writes its own motion vectors, so DLSS, neural rendering and frame generation see it move with the head; with only the camera's, hair trailed behind a turning head. Off: the camera's motion only, as the plugin did before (for hair whose own motion vectors come out wrong: streaks or shimmer in it).");
		HairTransparentMotion = ((BasePlugin)this).Config.Bind<bool>("DLSS", "HairTransparentMotion", true, "The see-through parts of strand hair (its soft edges, drawn transparent) write motion vectors too: HDRP's 'transparent writes motion vectors' on the hair's transparent materials and on the camera. Without it they take the motion of what lies behind them, and with the camera moving that differs, so the hair's edges trailed. Works only if the game's hair shader was built with it; the DLSS tab shows what the hair's materials have.");
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
		FgResetOnCuts = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "ResetOnCuts", true, "When the camera jumps to another shot (a cut in a conversation, another camera, a new scene), frame generation, DLSS and neural rendering start afresh for that frame: no made frame mixes the two shots, and DLSS keeps no trail of the old one. A jump is a move or turn far beyond the camera's own pace of late, so a fast pan is not taken for one.");
		FgFrameCap = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "EvenRenderedFrames", true, "While frame generation is on, have Reflex hold the rendered frames to the monitor's refresh divided by the multiplier (30 a second at 60 Hz and x2), evenly spaced, so that each made frame falls halfway between two rendered ones. Only with the game's VSync on: with it off the frames go out as fast as they are made (more than the refresh, with tearing) and are not held.");
		FgPacingFloor = ((BasePlugin)this).Config.Bind<int>("FrameGen", "PacingFloor", 10, "The fewest rendered frames a second at which the made frames are still spaced between the rendered ones. Below it the bridge sends each made frame out right before the rendered one, and with VSync off it is then on the screen for a moment only: the counter goes up, the motion is no smoother. The bridge's own is 30, which an RTX 3060 with neural rendering hardly reaches; 10 to 30.");
		FgMotionUnits = ((BasePlugin)this).Config.Bind<int>("FrameGen", "MotionUnits", 0, "How the motion vectors are scaled for DLSS-G. 0: as a fraction of the screen, the way NVIDIA's Streamline hands them to DLSS-G (HDRP keeps them so already). 1: in pixels, as DLSS upscaling wants them, which is how the plugin gave them before 0.8.8 (then DLSS-G saw motion some thousand times too large).");
		FgOwnPresenter = ((BasePlugin)this).Config.Bind<bool>("FrameGen", "OwnPresenter", true, "Show frame generation's frames through the plugin's own presenter (CMAPresenter.dll): a window of its own laid over the game's picture, on a D3D11 device of its own, which shows each frame once the GPU has finished it and spaces them evenly, with tearing, without holding the game back. Only with the game's VSync off: with VSync on the bridge shows the frames, each on a refresh of its own (Unity then takes a frame's time from the screen's refreshes, and with the presenter the game's world ran too slowly). Off: the bridge shows the frames always, as before 0.9.0.");
		Reflex = ((BasePlugin)this).Config.Bind<bool>("Latency", "Reflex", true, "NVIDIA Reflex low latency mode for the game's device, through the driver.");
		ReflexBoost = ((BasePlugin)this).Config.Bind<bool>("Latency", "ReflexBoost", false, "Reflex with boost: the GPU is kept from slowing its clock, for a little less delay at more power.");
		ShortQueue = ((BasePlugin)this).Config.Bind<bool>("Latency", "ShortQueue", true, "Let the game queue at most one frame for the GPU (Unity's maxQueuedFrames 1 instead of the game's own, 2): each frame reaches the screen a frame sooner, which the mouse feels. Frames were seen finished on the GPU some 45 ms after the game handed them over. The GPU may idle a little between frames. Only while the game's VSync is off: with VSync the frame rate fell and the camera lagged behind the mouse with it.");
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
		FluidSemen = ((BasePlugin)this).Config.Bind<bool>("Fluids", "GpuSemen", true, "Leave the game's semen on the GPU as the game has it (false: switched off, the game's older semen is used). New shots only; what is already there stays.");
		FluidSquirt = ((BasePlugin)this).Config.Bind<bool>("Fluids", "GpuSquirt", true, "Leave the game's squirt on the GPU as the game has it (false: switched off). New squirts only.");
		FluidSim = ((BasePlugin)this).Config.Bind<bool>("Fluids", "FluidSimulation", true, "Leave the game's simulated fluid on the GPU (SPH; saliva and her fluid run on it) as the game has it (false: switched off).");
		FluidDrool = ((BasePlugin)this).Config.Bind<bool>("Fluids", "Saliva", true, "Leave the game's saliva during oral as the game has it (false: switched off).");
		FluidVag = ((BasePlugin)this).Config.Bind<bool>("Fluids", "HerFluid", true, "Leave the game's vaginal fluid as the game has it (false: switched off).");
		FlipY = ((BasePlugin)this).Config.Bind<bool>("Orientation", "FlipY", true, "Hand every texture over upside down, and turn the result back: the way up the network and DLSS expect.");
		InvertMotionX = ((BasePlugin)this).Config.Bind<bool>("Orientation", "InvertMotionX", true, "Negate the motion vectors' X (the game stores current minus previous, NGX wants previous minus current).");
		InvertMotionY = ((BasePlugin)this).Config.Bind<bool>("Orientation", "InvertMotionY", false, "Negate the motion vectors' Y. Off while FlipY is on (the flip does it already).");
		DepthInverted = ((BasePlugin)this).Config.Bind<bool>("Orientation", "DepthInverted", SystemInfo.usesReversedZBuffer, "The depth that is handed over is reversed (near 1, far 0).");
		FastNetwork = ((BasePlugin)this).Config.Bind<bool>("Bridge", "FastNetwork", true, "Run neural rendering on the FP16 rebuild of the network that the nvidia mfg bridge mod (a proxy DLL beside the game) hands over: the same picture, faster. Kept in the mod's dlssg_to_fsr3.ini as EnableDlssNr, which the mod reads when the game starts. For RTX 20 and 30 only: on RTX 40 and 50 (FP8 in hardware) the rebuild does not start, so EnableDlssNr is kept at 0 there and NVIDIA's own network runs, whatever this says.");
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
		UseShadowCache = ((BasePlugin)this).Config.Bind<bool>("Optimization", "ShadowCache", false, "Shadow cache (faster, but can leave stale shadows): spot and point lights remember the shadows of objects that have not moved for a while and redraw only what moves, which saves GPU time. Known problem: a shadow can stay where something has moved away (seen with her hair). Off (the default): every shadow is drawn every frame, as in the game.");
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
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(PluginName);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(PluginVersion);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(": the native half is not there; nothing is run.");
			}
			log.LogError(val);
			return;
		}
		Fluids.Init();
		((BasePlugin)this).AddComponent<Capture>();
		_ = ((BasePlugin)this).Log;
		BepInExInfoLogInterpolatedStringHandler val2 = new BepInExInfoLogInterpolatedStringHandler(82, 6, out flag);
		if (flag)
		{
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(PluginName);
			((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" ");
			((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(PluginVersion);
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
