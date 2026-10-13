using System;
using System.Collections.Generic;
using UnityEngine;

namespace CMADlssNr
{
    // Motion vectors for everything.
    //
    // A renderer can be set to write "no motion" (MotionVectorGenerationMode.ForceNoMotion). HDRP then writes a mark
    // into the motion vector buffer in its place, the number 2 in X, which its own passes read as "nothing known here".
    // The network and DLSS are handed that buffer as it is (no shader of the plugin's can be had in this game to clean
    // it): to them 2 is a motion of two screens' width, so wherever such a renderer is on the screen they look for the
    // last frame's picture in the wrong place, find nothing, and start anew every frame. What is left there is the bare
    // small frame stretched: soft, and trembling with every change of it. (The buffer was measured to hold that 2 in
    // this game.)
    //
    // So while the plugin runs, every renderer that is set so is set to write its own motion instead, and set back when
    // the plugin stops.
    public sealed partial class Capture
    {
        private readonly List<Renderer> m_noMotion = new List<Renderer>();
        private float m_motionAt;
        private int m_motionLogs;
        private bool m_motionFailed;

        private void MotionStep(bool on)
        {
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
                float now = Time.unscaledTime;
                if (now < m_motionAt)
                {
                    return;
                }
                m_motionAt = now + 5f;
                // (Those that are on in the scene: found faster than every renderer there is.)
                var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int found = 0;
                string first = "";
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || r.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion || !r.gameObject.scene.IsValid())
                    {
                        continue;
                    }
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    SkinnedMeshRenderer skinned = r.TryCast<SkinnedMeshRenderer>();
                    if (skinned != null && !skinned.skinnedMotionVectors)
                    {
                        skinned.skinnedMotionVectors = true;
                    }
                    m_noMotion.Add(r);
                    found++;
                    if (first.Length < 160)
                    {
                        first += (first.Length > 0 ? ", " : "") + r.name;
                    }
                }
                if (found > 0 && m_motionLogs < 12)
                {
                    m_motionLogs++;
                    Plugin.ModLog.LogInfo($"Motion: {found} renderer(s) were set to write no motion ({first}{(found > 6 ? ", ..." : "")}); they write their own now ({m_noMotion.Count} in all, of {(all != null ? all.Length : 0)} renderers).");
                }
                else if (m_motionLogs == 0)
                {
                    m_motionLogs++;
                    Plugin.ModLog.LogInfo($"Motion: no renderer of {(all != null ? all.Length : 0)} is set to write no motion.");
                }
            }
            catch (Exception ex)
            {
                m_motionFailed = true;
                Plugin.ModLog.LogWarning("Motion: the renderers could not be gone through (" + ex.Message + ").");
            }
        }

        private void MotionRestore()
        {
            if (m_noMotion.Count == 0)
            {
                return;
            }
            foreach (Renderer r in m_noMotion)
            {
                try
                {
                    if (r != null && r.motionVectorGenerationMode == MotionVectorGenerationMode.Object)
                    {
                        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    }
                }
                catch (Exception)
                {
                }
            }
            Plugin.ModLog.LogInfo($"Motion: {m_noMotion.Count} renderer(s) write no motion again, as the game had them.");
            m_noMotion.Clear();
            m_motionAt = 0f;
        }
    }
}
