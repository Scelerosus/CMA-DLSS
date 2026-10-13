using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CMADlssNr
{
    // Sharpening of what is shown.
    //
    // No shader of the plugin's own can be had in this game, so the sharpening is put together from what is there: an
    // unsharp mask. The frame is blurred (copied to half its size and back), and the frame itself is then drawn over
    // the blur with an alpha above one: frame * (1 + k) + blur * (-k), which is the frame plus k times what the blur
    // took away. The shader that draws the frame over the blur is the interface's own (the one every picture of the
    // game's interface is drawn with, so it is certainly there): it multiplies the picture by a colour of its material,
    // whose alpha is set to 1 + k, and blends by that alpha; into a target of floating point numbers, where an alpha
    // above one is not cut off.
    //
    // (The first way tried, the engine's Graphics.DrawTexture, threw "object was garbage collected" in this game every
    // time it was called.)
    public sealed partial class Capture
    {
        private int m_sharpFails;
        private bool m_sharpOff, m_sharpTold;
        private RenderTexture m_sharp, m_sharpBlur, m_sharpSolid;
        private Material m_sharpMat;
        private Mesh m_sharpQuad;
        private CommandBuffer m_sharpCmd;
        internal string SharpState = "";

        // What is to be shown, sharpened by as much as is set (the same texture when that is nothing).
        private Texture Sharpened(Texture final)
        {
            float k = Mathf.Clamp01(Plugin.Sharpness.Value);
            if (k < 0.01f || m_sharpOff || final == null)
            {
                return final;
            }
            string step = "its material";
            try
            {
                if (m_sharpMat == null)
                {
                    Material ui = Canvas.GetDefaultCanvasMaterial();
                    if (ui == null)
                    {
                        m_sharpOff = true;
                        SharpState = "not available in this game";
                        Plugin.ModLog.LogInfo("Sharpening: the interface's material was not found; no sharpening.");
                        return final;
                    }
                    m_sharpMat = new Material(ui) { name = "CMADlssNr.Sharpen", hideFlags = HideFlags.HideAndDontSave };
                    m_sharpQuad = Rectangle(0f);
                    m_sharpCmd = new CommandBuffer { name = "CMADlssNr.Sharpen" };
                }
                step = "its textures";
                int w = final.width, h = final.height;
                if (m_sharp == null || m_sharp.width != w || m_sharp.height != h)
                {
                    Drop(ref m_sharp);
                    Drop(ref m_sharpBlur);
                    Drop(ref m_sharpSolid);
                    m_sharp = Make("CMADlssNr.Sharp", w, h, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    m_sharp.filterMode = FilterMode.Bilinear;
                    // (A format with no alpha of its own: read, its alpha is one, whatever the frame's was.)
                    m_sharpSolid = Make("CMADlssNr.SharpSolid", w, h, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
                    m_sharpSolid.filterMode = FilterMode.Bilinear;
                    m_sharpBlur = Make("CMADlssNr.SharpBlur", Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
                    m_sharpBlur.filterMode = FilterMode.Bilinear;
                }
                step = "the blur";
                Graphics.Blit(final, m_sharpSolid);
                Graphics.Blit(m_sharpSolid, m_sharpBlur);
                Graphics.Blit(m_sharpBlur, m_sharp);
                step = "the frame over the blur";
                m_sharpMat.SetTexture("_MainTex", m_sharpSolid);
                m_sharpMat.SetColor("_Color", new Color(1f, 1f, 1f, 1f + k));
                Matrix4x4 one = Matrix4x4.identity;
                m_sharpCmd.Clear();
                m_sharpCmd.SetRenderTarget(new RenderTargetIdentifier(m_sharp));
                m_sharpCmd.SetViewProjectionMatrices(one, one);
                m_sharpCmd.DrawMesh(m_sharpQuad, one, m_sharpMat, 0, 0);
                Graphics.ExecuteCommandBuffer(m_sharpCmd);
                m_sharpFails = 0;
                if (!m_sharpTold)
                {
                    m_sharpTold = true;
                    Plugin.ModLog.LogInfo($"Sharpening: on, {k:F2}, with the interface's shader '{(m_sharpMat.shader != null ? m_sharpMat.shader.name : "?")}' at {w}x{h}.");
                }
                return m_sharp;
            }
            catch (Exception ex)
            {
                // (One frame without it; given up only when it keeps failing.)
                if (++m_sharpFails > 20)
                {
                    m_sharpOff = true;
                    SharpState = "not available in this game";
                    Plugin.ModLog.LogInfo("Sharpening: keeps failing at " + step + " (" + ex.GetType().Name + ": " + ex.Message + "); switched off.");
                }
                return final;
            }
        }
    }
}
