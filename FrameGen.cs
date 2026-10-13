using System;
using UnityEngine;

namespace CMADlssNr
{
    // DLSS frame generation, done by the native half: it hooks the presenting of the game's
    // swap chain and shows frames it makes between the rendered ones. Each rendered frame it is handed the motion
    // vectors and the depth as the pipeline has them (not turned upside down, unlike what the network and DLSS get) and
    // the camera's matrices. What it interpolates is what is on the screen: the picture this plugin puts up, with the
    // game's interface over it.
    //
    // One made frame for each rendered one (x2). The log line "Frame generation:" tells what the native half says.
    public sealed partial class Capture
    {
        private bool m_fgOpened, m_fgOn, m_fgFailed, m_fgHavePrev, m_fgEnabled;
        private int m_fgTotal = 2, m_fgW, m_fgH, m_fgLogs;
        private float m_fgLogAt;
        private Matrix4x4 m_fgPrevVP;
        private string m_fgStatus = "";
        private IntPtr m_fgRegMv, m_fgRegDepth;
        private float m_fgMostAt, m_fgBase;
        private int m_fgMost = 2;
        private readonly float[] m_fgViewToClip = new float[16], m_fgClipToView = new float[16], m_fgClipToPrev = new float[16], m_fgPrevToClip = new float[16], m_fgBasis = new float[12];

        // (As Unity has a matrix in memory: column by column.)
        private static void Fill(float[] dst, Matrix4x4 m)
        {
            dst[0] = m.m00; dst[1] = m.m10; dst[2] = m.m20; dst[3] = m.m30;
            dst[4] = m.m01; dst[5] = m.m11; dst[6] = m.m21; dst[7] = m.m31;
            dst[8] = m.m02; dst[9] = m.m12; dst[10] = m.m22; dst[11] = m.m32;
            dst[12] = m.m03; dst[13] = m.m13; dst[14] = m.m23; dst[15] = m.m33;
        }

        // Generation stands back for that many frames: while the network's or DLSS's features are made anew.
        private int m_fgHoldUntil;

        private void FrameGenHold(int frames)
        {
            m_fgHoldUntil = Mathf.Max(m_fgHoldUntil, Time.frameCount + frames);
            if (m_fgEnabled)
            {
                FrameGenStop();
            }
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

        // Once a frame, when the frame is put together: the generator is handed this frame's data.
        private void FrameGenStep(Camera cam, int w, int h)
        {
            if (!Plugin.FrameGen.Value || m_fgFailed || Time.frameCount < m_fgHoldUntil)
            {
                FrameGenStop();
                return;
            }
            try
            {
                if (!m_fgOpened)
                {
                    int rc = Native.vdn_fg_open(Native.SnippetDir, Texture2D.whiteTexture.GetNativeTexturePtr(), Plugin.VerboseLog.Value ? 1 : 0);
                    if (rc != 0)
                    {
                        m_fgFailed = true;
                        m_fgStatus = "unavailable: " + Native.Text(Native.vdn_fg_status_text());
                        Plugin.ModLog.LogError($"Frame generation: could not be opened ({rc}): {Native.Text(Native.vdn_fg_status_text())}; hooked into this process: {Native.Text(Native.vdn_fg_hook_report())}.");
                        return;
                    }
                    m_fgOpened = true;
                    m_fgLogs = 0;
                    Plugin.ModLog.LogInfo($"Frame generation: opened; the presenting of the swap chain is hooked ({(Native.vdn_fg_hook_installed() == 2 ? "Present and Present1" : "Present only")}); the most it can show for a rendered frame is {Native.vdn_fg_max_multiplier()}; hooked into this process: {Native.Text(Native.vdn_fg_hook_report())}.");
                }
                // (The pipeline's buffers as they were copied out, not turned: only there when the pipeline keeps them
                // as arrays, which it does in this game.)
                if (m_sliceMvec == null || m_sliceDepth == null || cam == null)
                {
                    m_fgStatus = "waiting for motion vectors and depth";
                    return;
                }
                if (w != m_fgW || h != m_fgH)
                {
                    m_fgW = w;
                    m_fgH = h;
                    m_fgHavePrev = false;
                    m_fgRegMv = m_fgRegDepth = IntPtr.Zero;
                    Native.vdn_fg_reset();
                }
                IntPtr mv = Ptr(m_sliceMvec), dp = Ptr(m_sliceDepth);
                if (mv != m_fgRegMv || dp != m_fgRegDepth)
                {
                    Native.vdn_fg_set_textures(mv, dp);
                    m_fgRegMv = mv;
                    m_fgRegDepth = dp;
                }
                Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
                Matrix4x4 vp = proj * cam.worldToCameraMatrix;
                Matrix4x4 prev = m_fgHavePrev ? m_fgPrevVP : vp;
                Matrix4x4 toPrev = prev * vp.inverse;
                Fill(m_fgViewToClip, proj);
                Fill(m_fgClipToView, proj.inverse);
                Fill(m_fgClipToPrev, toPrev);
                Fill(m_fgPrevToClip, toPrev.inverse);
                Transform t = cam.transform;
                Vector3 p = t.position, up = t.up, right = t.right, fwd = t.forward;
                m_fgBasis[0] = p.x; m_fgBasis[1] = p.y; m_fgBasis[2] = p.z;
                m_fgBasis[3] = up.x; m_fgBasis[4] = up.y; m_fgBasis[5] = up.z;
                m_fgBasis[6] = right.x; m_fgBasis[7] = right.y; m_fgBasis[8] = right.z;
                m_fgBasis[9] = fwd.x; m_fgBasis[10] = fwd.y; m_fgBasis[11] = fwd.z;
                float mx = w * (Plugin.FgInvertMotionX.Value ? -1f : 1f), my = h * (Plugin.FgInvertMotionY.Value ? -1f : 1f);
                // (The jitter as DLSS is told it, for buffers that are not turned upside down.)
                Native.vdn_fg_set_frame(m_fgViewToClip, m_fgClipToView, m_fgClipToPrev, m_fgPrevToClip, m_fgBasis, cam.nearClipPlane, cam.farClipPlane, cam.fieldOfView * Mathf.Deg2Rad, cam.aspect,
                    mx, my, m_jitterX, -m_jitterY, Plugin.DepthInverted.Value ? 1 : 0, 1);
                float nowF = Time.unscaledTime;
                if (nowF >= m_fgMostAt)
                {
                    // (Asked once a second: the driver's answer comes after the first frames.)
                    m_fgMostAt = nowF + 1f;
                    m_fgMost = Mathf.Clamp((int)Native.vdn_fg_max_multiplier(), 2, 6);
                    m_fgBase = (float)Native.vdn_fg_base_hz();
                }
                // (One made frame for each rendered one, and no other choice. More was tried three ways (0.5.x, 0.8.5 to
                // 0.8.7): every made frame takes from the rendered ones, 35 a second at x2 became 31 at x3 and 21 at
                // x4 with neural rendering on, and near thirty the native half's pacing switches itself off and on.)
                m_fgTotal = 2;
                Native.vdn_fg_set_pacing(Plugin.FgPacing.Value ? 1 : 0);
                Native.vdn_fg_set_menu_detection(0);
                Native.vdn_fg_set_multiplier(m_fgTotal);
                Native.vdn_fg_enable(1);
                m_fgEnabled = true;
                m_fgPrevVP = vp;
                m_fgHavePrev = true;
                m_fgOn = Native.vdn_fg_generating() != 0;

                float now = Time.unscaledTime;
                if (now >= m_fgLogAt)
                {
                    m_fgStatus = (m_fgOn ? "generating" : "not generating") + ": " + Native.Text(Native.vdn_fg_status_text());
                    if (m_fgLogs < 30)
                    {
                        m_fgLogs++;
                        Plugin.ModLog.LogInfo($"Frame generation: {(m_fgOn ? "generating" : "NOT generating")}, x{m_fgTotal}; ready {Native.vdn_fg_ready()}; shown {Native.vdn_fg_presented()} frame(s), of them made {Native.vdn_fg_interpolated()}, passed through {Native.vdn_fg_passed_through()}; rendered at {Native.vdn_fg_base_hz():F0} a second; {Native.Text(Native.vdn_fg_status_text())}; held back: {Native.Text(Native.vdn_fg_gate_summary())}.");
                    }
                    m_fgLogAt = now + (m_fgLogs < 4 ? 2f : 10f);
                }
            }
            catch (Exception ex)
            {
                m_fgFailed = true;
                m_fgStatus = "failed: " + ex.Message;
                Plugin.ModLog.LogError("Frame generation: failed (" + ex.GetType().Name + ": " + ex.Message + "); switched off.");
                FrameGenStop();
            }
        }

        private void MenuFrameGen(ref float y, float x, float w)
        {
            Check(ref y, x, w, "Frame generation (DLSS-G)", Plugin.FrameGen);
            Check(ref y, x, w, "Even frame pacing", Plugin.FgPacing);
            Line(ref y, x, w);
            Check(ref y, x, w, "NVIDIA Reflex low latency  (" + ReflexState + ")", Plugin.Reflex);
            Check(ref y, x, w, "Reflex boost", Plugin.ReflexBoost);
            GUI.Label(new Rect(x, y, w, 150f), "Shows one made frame between every two rendered ones (x2). Needs DLSS or neural rendering on, an RTX 40 or 50 card and hardware-accelerated GPU scheduling in Windows.\nReflex cuts the delay between the mouse and the screen.\n" + (Plugin.FrameGen.Value ? "Now: " + (m_fgStatus.Length > 0 ? m_fgStatus : "starting") : ""), m_stSmall);
            y += 154f;
        }
    }
}
