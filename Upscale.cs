using System;
using UnityEngine;

namespace CMADlssNr
{
    // The network before the upscaler.
    //
    // The network's cost goes with the number of pixels it is run over. So the main camera is made to render a smaller
    // frame (the extent DLSS itself names for the quality that is set), the network is run over that, and DLSS super
    // resolution then makes the frame of the size of the screen out of the network's output, with the same motion
    // vectors and depth. DLSS is told its colour is not HDR then, for what it is handed has
    // been through the tone mapping and the network.
    //
    // The sub-pixel jitter of the projection that DLSS reconstructs detail from is HDRP's own (Jitter.cs).
    public sealed partial class Capture
    {
        private bool m_srOpen, m_srFailed, m_srTold;
        private RenderTexture m_srColor, m_srOut;
        private int m_srRenderW, m_srRenderH, m_srOutW, m_srOutH, m_srQuality = -1, m_srModel = -1, m_srSlot, m_srReset, m_srEvalFails;
        private bool m_srAuto;
        private IntPtr m_srRegColor, m_srRegMvec, m_srRegDepth, m_srRegOut;
        // The size of the screen the frame being rendered is for.
        private int m_outW, m_outH;

        private static readonly string[] s_qualities = { "Performance", "Balanced", "Quality", "Ultra Performance", "Ultra Quality", "DLAA" };

        // The DLSS preset for a quality: the one chosen in the window, or by NVIDIA's rule (K, with M for Performance
        // and below). K (11) and M (13) are the transformer models; E (5) and F (6) are the older convolutional ones,
        // far lighter, for the RTX 20 and 30 cards (E for upscaling, F made for DLAA and Ultra Performance).
        //
        // Never L (12). Making a feature with preset L crashes the game inside the driver's NGX, every time, right
        // after "Using App hint Preset L" in NGX's log: at DLAA (0.4.2, 0.4.3) and at Quality (0.8.0), DLSS 310.9.1,
        // with frame generation on, while K and M are made without trouble. A value of 12 left in the settings file
        // runs K.
        private static uint PresetFor(int quality)
        {
            int chosen = Plugin.Preset.Value;
            if (Chosen(chosen))
            {
                return (uint)chosen;
            }
            return quality == 0 || quality == 3 ? 13u : 11u;
        }

        internal static bool Chosen(int preset)
        {
            return preset == 5 || preset == 6 || preset == 11 || preset == 13;
        }

        internal static string PresetName(uint p)
        {
            return p >= 1 && p <= 26 ? ((char)('A' + p - 1)).ToString() : "?";
        }

        private int m_srPresetOpen = -1, m_srSwapAt = -1;
        private bool m_uqTold;

        // (DLAA too: DLSS is run then at the size of the screen, for its anti-aliasing alone.)
        private bool UpscaleWanted => Plugin.Upscale.Value && !m_srFailed;

        // The extent the camera is to render for a screen of that size: DLSS's own answer for the quality that is set;
        // the size of the screen itself when it is not upscaled.
        private void RenderExtent(int ow, int oh, out int rw, out int rh)
        {
            rw = ow;
            rh = oh;
            if (!UpscaleWanted)
            {
                return;
            }
            try
            {
                // (A preset that was hinted once is kept by NGX for as long as its context lives: for another one the
                // context is made anew.)
                // (Not while frames are being generated: the generator runs in NGX too, from the presenting of the swap
                // chain, and taking DLSS's context down under it crashed the game inside the driver's NGX (the user's
                // run of 0.4.2: preset changed with generation on). So generation is stopped first, a few frames are
                // let pass with the old preset, and only then is the context made anew; generation comes back after.)
                if (m_srOpen && m_srPresetOpen != Plugin.Preset.Value)
                {
                    if (m_srSwapAt < 0)
                    {
                        m_srSwapAt = Time.frameCount + (m_fgEnabled ? 8 : 0);
                        FrameGenHold(40);
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
                    int rc;
                    Native.vdn_rebuild_begin();
                    try
                    {
                        rc = Native.vdn_sr_open(Native.SnippetDir, Texture2D.whiteTexture.GetNativeTexturePtr(), Plugin.VerboseLog.Value ? 1 : 0);
                    }
                    finally
                    {
                        Native.vdn_rebuild_end();
                    }
                    if (rc != 0)
                    {
                        Plugin.ModLog.LogError("DLSS: could not be opened (" + rc + "): " + Native.Text(Native.vdn_sr_status_text()) + ". The frame is rendered at the size of the screen.");
                        m_srFailed = true;
                        return;
                    }
                    m_srOpen = true;
                    Plugin.ModLog.LogInfo("DLSS: opened.");
                }
                if (Plugin.Quality.Value == 5)
                {
                    return;
                }
                int quality = Mathf.Clamp(Plugin.Quality.Value, 0, 4);
                // (Ultra Quality: NVIDIA has no extent of its own for it. The frame is rendered at 77 % each way, which
                // is what the name has meant since DLSS 2, and DLSS is run in its Quality mode on that: it takes any
                // extent between its least and its most for the mode.)
                bool ultraQuality = quality == 4;
                int q = Native.vdn_sr_query((uint)ow, (uint)oh, (uint)(ultraQuality ? 2 : quality), out uint w, out uint h, out uint minW, out uint minH, out uint maxW, out uint maxH, out float _);
                if (q == 0 && ultraQuality)
                {
                    float scale = Mathf.Clamp(Plugin.UltraQualityScale.Value, 0.67f, 1f);
                    uint uw = (uint)Mathf.RoundToInt(ow * scale), uh = (uint)Mathf.RoundToInt(oh * scale);
                    if (maxW >= w && maxH >= h)
                    {
                        uw = Math.Min(uw, maxW);
                        uh = Math.Min(uh, maxH);
                    }
                    if (uw > w && uh > h)
                    {
                        w = uw;
                        h = uh;
                    }
                    if (!m_uqTold)
                    {
                        m_uqTold = true;
                        Plugin.ModLog.LogInfo($"DLSS: Ultra Quality: rendered {w}x{h} for {ow}x{oh} (DLSS's Quality mode takes {minW}x{minH} to {maxW}x{maxH}).");
                    }
                }
                if (q != 0 || w < 64 || h < 64 || w > ow || h > oh)
                {
                    if (!m_srTold)
                    {
                        m_srTold = true;
                        Plugin.ModLog.LogWarning($"DLSS: no render extent was given for {ow}x{oh} at quality {quality} (result {q}, {w}x{h}): {Native.Text(Native.vdn_sr_status_text())}. The frame is rendered at the size of the screen.");
                    }
                    return;
                }
                rw = (int)w;
                rh = (int)h;
            }
            catch (Exception ex)
            {
                m_srFailed = true;
                Plugin.ModLog.LogError("DLSS: failed while asking for its render extent (" + ex.Message + "). The frame is rendered at the size of the screen.");
            }
        }

        private bool EnsureSr(int rw, int rh, int ow, int oh)
        {
            int quality = Mathf.Clamp(Plugin.Quality.Value, 0, 5), model = Mathf.Max(0, Plugin.Preset.Value);
            bool auto = Plugin.AutoExposure.Value;
            bool made = false;
            if (m_srColor == null || m_srColor.width != rw || m_srColor.height != rh)
            {
                Drop(ref m_srColor);
                m_srColor = Make("CMADlssNr.SrColor", rw, rh, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                made = true;
            }
            if (m_srOut == null || m_srOut.width != ow || m_srOut.height != oh)
            {
                Drop(ref m_srOut);
                m_srOut = new RenderTexture(ow, oh, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "CMADlssNr.SrOut", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, enableRandomWrite = true };
                m_srOut.Create();
                made = true;
            }
            IntPtr c = Ptr(m_srColor), m = Ptr(m_mvec), d = Ptr(m_depth), o = Ptr(m_srOut);
            bool changed = made || m_srRenderW != rw || m_srRenderH != rh || m_srOutW != ow || m_srOutH != oh || m_srQuality != quality || m_srModel != model || m_srAuto != auto;
            if (!changed && c == m_srRegColor && m == m_srRegMvec && d == m_srRegDepth && o == m_srRegOut)
            {
                return true;
            }
            Native.vdn_rebuild_begin();
            try
            {
                if (changed)
                {
                    FrameGenHold(30);
                    m_srRenderW = rw;
                    m_srRenderH = rh;
                    m_srOutW = ow;
                    m_srOutH = oh;
                    m_srQuality = quality;
                    m_srModel = model;
                    m_srAuto = auto;
                    Native.vdn_sr_configure((uint)rw, (uint)rh, (uint)ow, (uint)oh, (uint)(quality == 4 ? 2 : quality), PresetFor(quality), Plugin.DepthInverted.Value ? 1 : 0, auto ? 1 : 0, 0, 1);
                }
                int rc = Native.vdn_sr_set_textures(0, c, m, d, o);
                if (rc != 0)
                {
                    Plugin.ModLog.LogError("DLSS: the textures were refused (0x" + rc.ToString("X") + "): " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly.");
                    m_srFailed = true;
                    return false;
                }
                m_srRegColor = c;
                m_srRegMvec = m;
                m_srRegDepth = d;
                m_srRegOut = o;
                m_srReset = 2;
                if (changed)
                {
                    Plugin.ModLog.LogInfo($"DLSS: render {rw}x{rh} -> screen {ow}x{oh} ({s_qualities[quality]}, preset {PresetName(PresetFor(quality))}{(Chosen(Plugin.Preset.Value) ? ", chosen" : ", chosen for the quality")}).");
                }
                return true;
            }
            finally
            {
                Native.vdn_rebuild_end();
            }
        }

        // The network's output made into a frame of the size of the screen. Gives back the texture to show (in the
        // network's own orientation), or null when it could not be done.
        private RenderTexture Upscaled(int rw, int rh, int ow, int oh, Texture from, bool flipFrom)
        {
            if (m_srFailed || !m_srOpen || !EnsureSr(rw, rh, ow, oh))
            {
                return null;
            }
            Blit(from, m_srColor, 1f, 1f, flipFrom);
            float mx = rw * (Plugin.InvertMotionX.Value ? -1f : 1f), my = rh * (Plugin.InvertMotionY.Value ? -1f : 1f);
            int slot = m_srSlot;
            m_srSlot = (m_srSlot + 1) & 7;
            Native.vdn_sr_set_frame(slot, 0, m_jitterX, m_jitterY, mx, my, m_srReset > 0 ? 1 : 0);
            if (m_srReset > 0)
            {
                m_srReset--;
            }
            GL.IssuePluginEvent(Native.vdn_sr_get_render_event_func(), Native.SrEvent(slot));
            switch (Native.vdn_sr_error())
            {
                case 1:
                    Plugin.ModLog.LogError("DLSS: the feature could not be made: " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly.");
                    m_srFailed = true;
                    return null;
                case 2:
                    if (++m_srEvalFails >= 60)
                    {
                        Plugin.ModLog.LogError("DLSS: it has failed " + m_srEvalFails + " frames running: " + Native.Text(Native.vdn_sr_status_text()) + ". The network's output is stretched to the screen plainly.");
                        m_srFailed = true;
                    }
                    return null;
            }
            if (Native.vdn_sr_ready() == 0)
            {
                return null;
            }
            m_srEvalFails = 0;
            return m_srOut;
        }

        private static void Drop(ref RenderTexture rt)
        {
            if (rt != null)
            {
                Forget(rt);
                rt.Release();
                Destroy(rt);
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
            Drop(ref m_srColor);
            Drop(ref m_srOut);
            m_srRegColor = m_srRegMvec = m_srRegDepth = m_srRegOut = IntPtr.Zero;
            m_srQuality = -1;
            m_srTold = false;
        }
    }
}
