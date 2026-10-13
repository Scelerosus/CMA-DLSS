using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CMADlssNr
{
    // The camera's own settings while the plugin runs: motion vectors on, and the jitter DLSS reconstructs from.
    //
    // Motion vectors. The network and DLSS both look for each pixel's last frame along the pipeline's motion vectors.
    // HDRP makes them only if the camera's frame settings say so (for the camera's own motion, and for objects that
    // move by themselves): both are switched on for the camera while the plugin runs, whatever the game has set.
    //
    // Jitter. DLSS makes detail out of a frame that is rendered a little differently each time: the projection is
    // moved by a part of a pixel every frame, and DLSS is told by how much. Without that it has nothing to resolve and
    // what it gives is a blur. HDRP has that jitter already, for its own temporal anti-aliasing: with that mode set on
    // the camera the projection is jittered every frame (HDCamera.taaJitter), and the motion vectors are made without
    // it, which is what DLSS wants. What must not happen is HDRP resolving the jitter itself before DLSS sees the
    // frame. So while DLSS runs the camera is set to temporal anti-aliasing (for the jitter) and its anti-aliasing
    // pass is switched off by a frame setting of the camera's own (for the resolve).
    //
    // Everything is put back as it was when the plugin stops.
    public sealed partial class Capture
    {
        private const ulong AntialiasingBit = 1UL << (91 - 64);
        // (FrameSettingsField.MotionVectors = 10, ObjectMotionVectors = 11.)
        private const ulong MotionBits = (1UL << 10) | (1UL << 11);
        // The game's own effects that can be switched off for the camera (FrameSettingsField): in the first half of the
        // bits subsurface scattering (46); in the second depth of field (81), motion blur (82), bloom (84), chromatic
        // aberration (86), vignette (87), film grain (89).
        private const ulong SssBit = 1UL << 46;
        private const ulong DofBit = 1UL << (81 - 64), BlurBit = 1UL << (82 - 64), BloomBit = 1UL << (84 - 64), ChromaBit = 1UL << (86 - 64), VignetteBit = 1UL << (87 - 64), GrainBit = 1UL << (89 - 64);
        private const ulong EffectBits2 = DofBit | BlurBit | BloomBit | ChromaBit | VignetteBit | GrainBit;
        private ulong m_fxOff1, m_fxOff2;

        // Which of them are to be off now, as the bits of their frame settings.
        private static void EffectsOff(out ulong off1, out ulong off2)
        {
            off1 = Plugin.GameSss.Value ? 0UL : SssBit;
            off2 = (Plugin.GameDof.Value ? 0UL : DofBit) | (Plugin.GameMotionBlur.Value ? 0UL : BlurBit) | (Plugin.GameBloom.Value ? 0UL : BloomBit)
                | (Plugin.GameChromatic.Value ? 0UL : ChromaBit) | (Plugin.GameVignette.Value ? 0UL : VignetteBit) | (Plugin.GameGrain.Value ? 0UL : GrainBit);
        }

        private HDAdditionalCameraData m_jitHd;
        private HDAdditionalCameraData.AntialiasingMode m_jitModeWas;
        private bool m_jitCustomWas, m_jitOn, m_jitFailed, m_jitJitter, m_camOn;
        private ulong m_jitMask1Was, m_jitSet1Was, m_jitMask2Was, m_jitSet2Was;
        private float m_jitterX, m_jitterY, m_jitScaleWas, m_jitBiasWas;
        private int m_jitLogs;

        // Before the frame is rendered: the camera is set (motion vectors; and, with "jitter", a jittered, unresolved
        // frame), or put back.
        private void JitterSet(Camera cam, bool want, bool jitter = false, float bias = 0f)
        {
            if (m_jitFailed)
            {
                return;
            }
            try
            {
                EffectsOff(out ulong off1, out ulong off2);
                if (m_camOn && (!want || cam == null || m_jitHd == null || m_jitHd.gameObject != cam.gameObject || jitter != m_jitJitter || off1 != m_fxOff1 || off2 != m_fxOff2))
                {
                    JitterRestore();
                }
                if (!want || cam == null)
                {
                    return;
                }
                if (!m_camOn)
                {
                    HDAdditionalCameraData hd = cam.GetComponent<HDAdditionalCameraData>();
                    if (hd == null)
                    {
                        return;
                    }
                    m_jitHd = hd;
                    m_jitModeWas = hd.antialiasing;
                    m_jitCustomWas = hd.customRenderingSettings;
                    m_jitScaleWas = hd.taaJitterScale;
                    m_jitBiasWas = hd.materialMipBias;
                    FrameSettingsOverrideMask mask = hd.renderingPathCustomFrameSettingsOverrideMask;
                    FrameSettings set = hd.m_RenderingPathCustomFrameSettings;
                    m_jitMask1Was = mask.mask.data1;
                    m_jitMask2Was = mask.mask.data2;
                    m_jitSet1Was = set.bitDatas.data1;
                    m_jitSet2Was = set.bitDatas.data2;
                    bool motionWas = !m_jitCustomWas || (m_jitMask1Was & MotionBits) != MotionBits || (m_jitSet1Was & MotionBits) == MotionBits;
                    mask.mask.data1 |= MotionBits;
                    set.bitDatas.data1 |= MotionBits;
                    if (jitter)
                    {
                        mask.mask.data2 |= AntialiasingBit;
                        set.bitDatas.data2 &= ~AntialiasingBit;
                    }
                    // (The game's effects that are switched off in the window.)
                    m_fxOff1 = off1;
                    m_fxOff2 = off2;
                    mask.mask.data1 |= off1;
                    set.bitDatas.data1 &= ~off1;
                    mask.mask.data2 |= off2;
                    set.bitDatas.data2 &= ~off2;
                    hd.renderingPathCustomFrameSettingsOverrideMask = mask;
                    hd.m_RenderingPathCustomFrameSettings = set;
                    hd.customRenderingSettings = true;
                    if (jitter)
                    {
                        // (The camera's own scale of the jitter: this game has it at a tenth, which is no jitter to
                        // speak of.)
                        hd.taaJitterScale = 1f;
                        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    }
                    m_camOn = true;
                    m_jitOn = jitter;
                    m_jitJitter = jitter;
                    m_jitLogs = 0;
                    Plugin.ModLog.LogInfo($"Camera: its anti-aliasing was {m_jitModeWas} (its own frame settings {(m_jitCustomWas ? "on" : "off")}, its jitter scaled by {m_jitScaleWas:F2}, its mip bias {m_jitBiasWas:F2}). Motion vectors switched on in its frame settings{(motionWas ? "" : " (the game had them overridden off)")}.{(off1 != 0 || off2 != 0 ? " Of the game's effects switched off:" + (Plugin.GameBloom.Value ? "" : " bloom") + (Plugin.GameSss.Value ? "" : " subsurface scattering") + (Plugin.GameDof.Value ? "" : " depth of field") + (Plugin.GameMotionBlur.Value ? "" : " motion blur") + (Plugin.GameChromatic.Value ? "" : " chromatic aberration") + (Plugin.GameVignette.Value ? "" : " vignette") + (Plugin.GameGrain.Value ? "" : " film grain") + "." : "")}{(jitter ? " Set to temporal anti-aliasing for its jitter, with the anti-aliasing pass itself switched off: DLSS resolves the frame." : "")}");
                }
                if (!m_jitOn)
                {
                    return;
                }
                // (Textures are read as for the screen's size, not the smaller frame's: sharper ones, by NVIDIA's rule,
                // the logarithm of how much smaller the frame is.)
                if (Plugin.MipBias.Value && Mathf.Abs(m_jitHd.materialMipBias - (m_jitBiasWas + bias)) > 0.01f)
                {
                    m_jitHd.materialMipBias = m_jitBiasWas + bias;
                }
                if (m_jitHd.taaJitterScale != 1f)
                {
                    m_jitHd.taaJitterScale = 1f;
                }
                if (m_jitHd.antialiasing != HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing)
                {
                    // (The game has set it anew, its settings were changed: what it set is what is put back later.)
                    m_jitModeWas = m_jitHd.antialiasing;
                    m_jitHd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                }
            }
            catch (Exception ex)
            {
                m_jitFailed = true;
                Plugin.ModLog.LogWarning("Camera: it could not be set (" + ex.Message + "); DLSS runs without jitter (softer).");
                JitterRestore();
            }
        }

        private void JitterRestore()
        {
            try
            {
                if (m_camOn && m_jitHd != null)
                {
                    FrameSettingsOverrideMask mask = m_jitHd.renderingPathCustomFrameSettingsOverrideMask;
                    FrameSettings set = m_jitHd.m_RenderingPathCustomFrameSettings;
                    // (Only the bits that were touched are put back: the game may have changed others meanwhile.)
                    ulong touched1 = MotionBits | SssBit, touched2 = AntialiasingBit | EffectBits2;
                    mask.mask.data1 = (mask.mask.data1 & ~touched1) | (m_jitMask1Was & touched1);
                    mask.mask.data2 = (mask.mask.data2 & ~touched2) | (m_jitMask2Was & touched2);
                    set.bitDatas.data1 = (set.bitDatas.data1 & ~touched1) | (m_jitSet1Was & touched1);
                    set.bitDatas.data2 = (set.bitDatas.data2 & ~touched2) | (m_jitSet2Was & touched2);
                    m_jitHd.renderingPathCustomFrameSettingsOverrideMask = mask;
                    m_jitHd.m_RenderingPathCustomFrameSettings = set;
                    m_jitHd.customRenderingSettings = m_jitCustomWas;
                    if (m_jitOn)
                    {
                        m_jitHd.taaJitterScale = m_jitScaleWas;
                        m_jitHd.materialMipBias = m_jitBiasWas;
                        m_jitHd.antialiasing = m_jitModeWas;
                    }
                    Plugin.ModLog.LogInfo("Camera: its settings are as they were again (anti-aliasing " + m_jitModeWas + ").");
                }
            }
            catch (Exception)
            {
            }
            m_camOn = false;
            m_jitOn = false;
            m_jitHd = null;
            m_jitterX = m_jitterY = 0f;
        }

        // Once the frame is rendered: by how much it was jittered (parts of a pixel of the rendered frame).
        private void JitterRead(Camera cam)
        {
            m_jitterX = m_jitterY = 0f;
            if (!m_jitOn || cam == null)
            {
                return;
            }
            try
            {
                HDCamera hc = HDCamera.GetOrCreate(cam, 0);
                if (hc == null)
                {
                    return;
                }
                Vector4 j = hc.taaJitter;
                m_jitterX = j.x * (Plugin.JitterInvertX.Value ? -1f : 1f);
                m_jitterY = j.y * (Plugin.JitterInvertY.Value ? -1f : 1f);
                if (m_jitLogs < 2)
                {
                    m_jitLogs++;
                    Plugin.ModLog.LogInfo($"Jitter: this frame HDRP moved the projection by ({j.x:F3}, {j.y:F3}) of a pixel (its camera: anti-aliasing {hc.antialiasing}, frame {hc.taaFrameIndex}, {hc.actualWidth} wide); DLSS is told ({m_jitterX:F3}, {m_jitterY:F3}).");
                }
            }
            catch (Exception ex)
            {
                if (m_jitLogs < 4)
                {
                    m_jitLogs = 4;
                    Plugin.ModLog.LogWarning("Jitter: HDRP's jitter could not be read (" + ex.Message + "); DLSS is told there is none.");
                }
            }
        }
    }
}
