using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace CMADlssNr
{
    // DLSS, DLSS neural rendering and DLSS frame generation for Corrupted Modeling Agency (the native half that talks
    // to NVIDIA's NGX is DlssBridge.dll, Native.cs).
    //
    // The main camera is made to render into a texture of the plugin's instead of the screen, smaller than the screen
    // when DLSS upscales. When it has, the frame is handed to the network and to DLSS with the game's motion vectors
    // and depth, in either order, and what comes back is put on the screen under the game's interface (Capture.cs).
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Corrupted Modeling Agency.exe")]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "community.cma.dlssnr";
        public const string PluginName = "CMA DLSS NR";
        public const string PluginVersion = "0.8.8";

        internal static ManualLogSource ModLog;

        internal static ConfigEntry<bool> Enabled, NrAfter, UiCorrection, Upscale, AutoExposure, VerboseLog, Jitter, JitterInvertX, JitterInvertY, MipBias;
        internal static ConfigEntry<bool> FrameGen, FgPacing, FgInvertMotionX, FgInvertMotionY, Reflex, ReflexBoost, Mask, MaskSoft;
        internal static ConfigEntry<bool> GameBloom, GameSss, GameDof, GameMotionBlur, GameChromatic, GameVignette, GameGrain;
        internal static ConfigEntry<bool> FlipY, InvertMotionX, InvertMotionY, DepthInverted;
        internal static ConfigEntry<KeyCode> MenuKey, ToggleKey, QualityKey;
        internal static ConfigFile Cfg;
        internal static readonly ConfigEntry<float>[] RegionEffect = new ConfigEntry<float>[Capture.Regions], RegionDetail = new ConfigEntry<float>[Capture.Regions];
        internal static ConfigEntry<float> Intensity, LocalTone, LocalStructure, UltraQualityScale, Sharpness;
        internal static ConfigEntry<int> Style, Passes, Quality, Preset;

        public override void Load()
        {
            ModLog = Log;
            Cfg = Config;
            // (The window changes values while a slider is dragged: the file is written when it is let go.)
            Config.SaveOnConfigSet = false;
            Enabled = Config.Bind("General", "Enabled", true, "Run neural rendering (independent of DLSS: either runs without the other).");
            NrAfter = Config.Bind("General", "NeuralAfterDlss", false, "Run neural rendering after DLSS, on the full-size picture, instead of before it on the small frame: finer, and as costly as at DLAA.");
            Intensity = Config.Bind("General", "Intensity", 1f, "How much of the network's result is shown, 0 to 1.");
            Style = Config.Bind("General", "Style", 0, "0 Default, 1 Natural, 2 Cinematic.");
            Passes = Config.Bind("General", "Passes", 1, "How many times the network is run over the frame, 1 to 3 (each pass costs its whole time and memory again).");
            UiCorrection = Config.Bind("General", "InterfaceCorrection", true, "The network's own hint that there is interface (lettering) in the frame, to be left alone. The game draws its subtitles and some menus as part of the frame.");
            LocalTone = Config.Bind("Strength", "LocalTone", 1f, "Strength of the network's relighting, 0 to 2; 1 is neutral.");
            LocalStructure = Config.Bind("Strength", "LocalStructure", 1f, "Strength of the fine detail the network adds, 0 to 2; 1 is neutral.");

            MenuKey = Config.Bind("Keys", "Menu", KeyCode.Insert, "Key that opens the settings window.");
            ToggleKey = Config.Bind("Keys", "Toggle", KeyCode.None, "Key that switches neural rendering on and off (none by default: it is in the window).");
            QualityKey = Config.Bind("Keys", "Quality", KeyCode.None, "Key that goes through the DLSS qualities: DLAA, Quality, Balanced, Performance, Ultra Performance (none by default).");

            Upscale = Config.Bind("DLSS", "Upscale", true, "Run DLSS: anti-aliasing at DLAA, and below it a smaller frame reconstructed to the size of the screen.");
            Quality = Config.Bind("DLSS", "Quality", 5, "0 Performance, 1 Balanced, 2 Quality, 3 Ultra Performance, 4 Ultra Quality, 5 DLAA (the size of the screen: no upscaling).");
            Preset = Config.Bind("DLSS", "Preset", 13, "The DLSS model preset: 0 by the quality (K, with M for Performance and Ultra Performance); 11 K, 13 M; the older, lighter models for RTX 20 and 30 cards: 5 E, 6 F. Preset L is not offered: it crashes the driver in this game.");
            UltraQualityScale = Config.Bind("DLSS", "UltraQualityScale", 0.77f, "Ultra Quality: how much of the screen's size the frame is rendered at each way, 0.67 (the same as Quality) to 1.");
            Sharpness = Config.Bind("DLSS", "Sharpen", 0.3f, "Sharpening of what is shown (an unsharp mask after everything else), 0 to 1.");
            AutoExposure = Config.Bind("DLSS", "AutoExposure", true, "Let DLSS work out the exposure itself.");
            MipBias = Config.Bind("DLSS", "SharperTextures", true, "While the frame is rendered smaller, read textures as sharp as for the size of the screen (a negative mip bias on the camera, by NVIDIA's rule).");
            Jitter = Config.Bind("DLSS", "ProjectionJitter", true, "Give DLSS a jittered frame to reconstruct from (HDRP's own jitter, with its own anti-aliasing pass switched off). Off: DLSS works on what the game's anti-aliasing gives, which is a blur.");
            JitterInvertX = Config.Bind("DLSS", "JitterNegateX", false, "Negate the X of HDRP's jitter when it is told to DLSS. Off is right in this game (found by trying all four pairs of signs).");
            JitterInvertY = Config.Bind("DLSS", "JitterNegateY", true, "Negate the Y of HDRP's jitter when it is told to DLSS. On is right in this game, with FlipY on.");
            VerboseLog = Config.Bind("DLSS", "VerboseLog", false, "NGX's own log, to ngx\\nvngx.log.");

            FrameGen = Config.Bind("FrameGen", "Enabled", false, "DLSS frame generation: one made frame is shown between every two rendered ones (x2). It hooks the presenting of the game's swap chain. Needs an RTX 40 or 50 card and hardware-accelerated GPU scheduling.");
            FgPacing = Config.Bind("FrameGen", "Pacing", true, "Space the made frames evenly between the rendered ones.");
            FgInvertMotionX = Config.Bind("FrameGen", "InvertMotionX", true, "Negate the motion vectors' X for frame generation.");
            FgInvertMotionY = Config.Bind("FrameGen", "InvertMotionY", true, "Negate the motion vectors' Y for frame generation (on: it reads the buffers as they are, not turned upside down).");
            Reflex = Config.Bind("Latency", "Reflex", true, "NVIDIA Reflex low latency mode for the game's device, through the driver.");
            ReflexBoost = Config.Bind("Latency", "ReflexBoost", false, "Reflex with boost: the GPU is kept from slowing its clock, for a little less delay at more power.");

            Mask = Config.Bind("Mask", "Enabled", false, "Tell the network where skin, hair and clothing are (a mask drawn from the characters' meshes), and set how much of itself it is on each.");
            MaskSoft = Config.Bind("Mask", "Soft", true, "Soften the mask so that regions fade into one another.");
            for (int i = 0; i < Capture.Regions; i++)
            {
                RegionEffect[i] = Config.Bind("Mask", Capture.RegionNames[i] + "Effect", 1f, "How much of the network's result is kept on this region, as a part of Intensity, 0 to 1.");
                RegionDetail[i] = Config.Bind("Mask", Capture.RegionNames[i] + "Detail", 1f, "How much fine detail the network adds on this region, as a multiple of Local structure, 0 to 2.");
            }

            GameBloom = Config.Bind("GameEffects", "Bloom", true, "Leave the game's bloom as it is (false: switched off for the main camera while the plugin runs).");
            GameSss = Config.Bind("GameEffects", "SubsurfaceScattering", true, "Leave the game's subsurface scattering (skin) as it is.");
            GameDof = Config.Bind("GameEffects", "DepthOfField", true, "Leave the game's depth of field as it is.");
            GameMotionBlur = Config.Bind("GameEffects", "MotionBlur", true, "Leave the game's motion blur as it is.");
            GameChromatic = Config.Bind("GameEffects", "ChromaticAberration", true, "Leave the game's chromatic aberration as it is.");
            GameVignette = Config.Bind("GameEffects", "Vignette", true, "Leave the game's vignette as it is.");
            GameGrain = Config.Bind("GameEffects", "FilmGrain", true, "Leave the game's film grain as it is.");

            // How the game's buffers are handed over. Right for this game as they are: found by measuring.
            FlipY = Config.Bind("Orientation", "FlipY", true, "Hand every texture over upside down, and turn the result back: the way up the network and DLSS expect.");
            InvertMotionX = Config.Bind("Orientation", "InvertMotionX", true, "Negate the motion vectors' X (the game stores current minus previous, NGX wants previous minus current).");
            InvertMotionY = Config.Bind("Orientation", "InvertMotionY", false, "Negate the motion vectors' Y. Off while FlipY is on (the flip does it already).");
            DepthInverted = Config.Bind("Orientation", "DepthInverted", SystemInfo.usesReversedZBuffer, "The depth that is handed over is reversed (near 1, far 0).");

            if (!Native.Load())
            {
                Log.LogError($"{PluginName} {PluginVersion}: the native half is not there; nothing is run.");
                return;
            }
            AddComponent<Capture>();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded: neural rendering {(Enabled.Value ? "on" : "off")}, DLSS {(Upscale.Value ? "on" : "off")}, frame generation {(FrameGen.Value ? "on" : "off")}; {MenuKey.Value} opens the settings window.");
        }
    }
}
