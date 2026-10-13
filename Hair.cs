using System;
using System.Collections.Generic;
using UnityEngine;

namespace CMADlssNr
{
    // The game's long hair in motion.
    //
    // Its shader ('HDRP/Hair...') makes every strand itself and writes motion vectors of its own that are not to be
    // trusted: with them, whatever their sign, DLSS and the network lose the strands as soon as anything moves, and the
    // hair goes flat with its highlight spread into a glossy patch (seen with the camera swung, while the room and her
    // face stayed sharp). So while the plugin runs the hair writes no motion vectors of its own and gets the camera's:
    // right for a moving camera, a little off where the hair lies on her while she herself moves. Every renderer is
    // put back as it was when the plugin stops.
    public sealed partial class Capture
    {
        private struct HairWas
        {
            public Renderer renderer;
            public MotionVectorGenerationMode mode;
        }

        private readonly List<HairWas> m_hair = new List<HairWas>();
        private float m_hairAt;
        private bool m_hairFailed, m_hairMotionTold;

        // Before the frame (LateUpdate), after MotionStep: that one turns renderers that write no motion to writing
        // their own, this one then takes the hair among them.
        private void HairStep(bool on)
        {
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
                float now = Time.unscaledTime;
                if (now < m_hairAt)
                {
                    return;
                }
                m_hairAt = now + 3f;
                for (int i = m_hair.Count - 1; i >= 0; i--)
                {
                    if (m_hair[i].renderer == null)
                    {
                        m_hair.RemoveAt(i);
                    }
                }
                var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || r.motionVectorGenerationMode == MotionVectorGenerationMode.Camera || !StrandHair(r))
                    {
                        continue;
                    }
                    m_hair.Add(new HairWas { renderer = r, mode = r.motionVectorGenerationMode });
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                    if (!m_hairMotionTold)
                    {
                        m_hairMotionTold = true;
                        Plugin.ModLog.LogInfo($"Hair: '{r.name}' is strand hair: it gets the camera's motion vectors while the plugin runs.");
                    }
                }
            }
            catch (Exception ex)
            {
                m_hairFailed = true;
                Plugin.ModLog.LogWarning("Hair: its motion vectors could not be changed (" + ex.Message + "); left as the game has them.");
            }
        }

        private static bool StrandHair(Renderer r)
        {
            var mats = r.sharedMaterials;
            for (int k = 0; mats != null && k < mats.Length; k++)
            {
                Material m = mats[k];
                if (m != null && m.shader != null && m.shader.name.StartsWith("HDRP/Hair", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // (Before MotionRestore: what that one had turned to writing its own motion is given back to it so.)
        private void HairRestore()
        {
            for (int i = 0; i < m_hair.Count; i++)
            {
                try
                {
                    HairWas h = m_hair[i];
                    if (h.renderer != null && h.renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Camera)
                    {
                        h.renderer.motionVectorGenerationMode = h.mode;
                    }
                }
                catch (Exception)
                {
                }
            }
            m_hair.Clear();
            m_hairAt = 0f;
        }
    }
}
