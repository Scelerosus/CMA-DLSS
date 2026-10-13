using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace CMADlssNr
{
    // The mask: the network is told where the characters are, and how much of itself to be on each part of the frame.
    //
    // Left to itself the network finds skin in the finished frame by guessing. The game knows which pixels are whose:
    // every skinned mesh in the scene is a character's body, hair or clothing. Each frame those meshes are drawn once
    // more into a picture of the frame, with the main camera's own view, and the native half takes that picture as the
    // feature's control mask: red is how much of the intensity survives there, green of the local tone, blue of the
    // local structure (each as a part of the most that any region is set to, which is what the feature itself is
    // handed), and alpha says what it is: 1 a body, 0.5 clothing, 0 the scene.
    //
    // How it is drawn. The only plain shader this game has is the one the engine draws lines with, and that one
    // multiplies by the colours stored in a mesh's own points: drawn with it in a flat colour, the body came out in the
    // patches of whatever its meshes keep there (the first version of this, seen in the pictures the user saved). So
    // nothing is ever drawn in that shader's own colour. Its blending is used instead, which ignores what it puts out:
    //   1. every part of every character is drawn for its depth alone (blend: nothing of the source, all of what is
    //      there);
    //   2. for each region in turn, its parts are drawn again where they are nearest, writing zero (blend: nothing of
    //      either), which leaves alpha 0 exactly on that region; then one rectangle over the whole picture in the
    //      region's colour, blended by the alpha that is there (the colour where it is 0, untouched where it is 1): the
    //      region now has its colour and alpha 1 again. The last region (clothing) is given its alpha of 0.5;
    //   3. a rectangle at the far end of the depth puts the scene's colour and alpha 0 wherever no character is.
    // The rectangles' own points are white, so there the shader does put out the colour it is told.
    //
    // The parts are told apart by the names of their materials. Nothing of the scene hides a character in the mask (a
    // character behind a table is marked through it); the mask is softened so that regions fade into one another.
    public sealed partial class Capture
    {
        internal const int Regions = 4;
        internal static readonly string[] RegionNames = { "Scene", "Skin", "Hair", "Clothing" };
        private static readonly float[] s_regionAlpha = { 0f, 1f, 1f, 0.5f };
        private static readonly Regex s_hairName = new Regex("hair|brow|lash|beard|fur|pubic|scalp|pelo|cabell|ceja|pestan|polytail|ponytail|pigtail|braid|bangs|fringe|strand", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex s_skinName = new Regex("skin|body|head|face|arm|leg|torso|nail|std_|pene|penis|genit|vag|anus|tongue|teeth|tooth|gum|eye|cornea|sclera|iris|lip|breast|nipple|mouth", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private struct MaskDraw
        {
            public Renderer renderer;
            public int submesh, region;
            // (Of hair: its own material and that material's pass that draws depth alone, when it has one.)
            public Material own;
            public int ownPass;
        }

        // "Chair", and whatever else has "hair" in its name without being any (seen: the office chairs came out as hair).
        private static readonly Regex s_notHair = new Regex("chair|Chair|CHAIR", RegexOptions.Compiled);
        private static readonly string[] s_depthPasses = { "DepthForwardOnly", "DepthOnly", "TransparentDepthPrepass", "ShadowCaster" };
        private Material m_maskHairAll;
        private string m_maskHairTold = "";
        private bool m_maskHairFailed;

        private static bool IsHair(string name)
        {
            return s_hairName.IsMatch(name) && !s_notHair.IsMatch(name);
        }

        private readonly List<MaskDraw> m_maskDraws = new List<MaskDraw>();
        private readonly Material[] m_maskFill = new Material[Regions];
        private Material m_maskDepth, m_maskZero, m_maskSceneMat;
        private Mesh m_quadNear, m_quadFar;
        private RenderTexture m_maskRaw, m_maskSmall, m_mask;
        private CommandBuffer m_maskCmd;
        private float m_maskScanAt, m_maskMaxI = 1f, m_maskMaxS = 1f;
        private bool m_maskFailed, m_maskReady, m_maskInit;
        private int m_maskToldParts, m_maskTolds;
        internal string MaskState = "";

        private bool MaskWanted => Plugin.Mask.Value && !m_maskFailed && Plugin.Enabled.Value;

        // The most any region is set to: what the feature itself is handed, the mask giving each region its part of it.
        private static float MaxOf(ConfigEntry<float>[] region, float most)
        {
            float m = 0f;
            for (int i = 0; i < region.Length; i++)
            {
                m = Mathf.Max(m, Mathf.Clamp(region[i].Value, 0f, most));
            }
            return m;
        }

        // The engine's line shader, set to blend and test as told (BlendMode and CompareFunction by their numbers).
        private static Material Plain(Shader shader, int src, int dst, int zwrite, int ztest)
        {
            var m = new Material(shader) { name = "CMADlssNr.Mask", hideFlags = HideFlags.HideAndDontSave };
            m.SetInt("_SrcBlend", src);
            m.SetInt("_DstBlend", dst);
            m.SetInt("_ZWrite", zwrite);
            m.SetInt("_ZTest", ztest);
            m.SetInt("_Cull", 0);
            m.SetColor("_Color", Color.white);
            return m;
        }

        // A rectangle over the whole picture, given as the picture's own corners, at a depth (-1 nearest, 1 farthest);
        // its points white.
        internal static Mesh Rectangle(float depth)
        {
            var mesh = new Mesh { name = "CMADlssNr.Rectangle", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-1f, -1f, depth), new Vector3(-1f, 1f, depth), new Vector3(1f, 1f, depth), new Vector3(1f, -1f, depth) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            return mesh;
        }

        private bool MaskMaterials()
        {
            if (m_maskDepth != null && m_quadNear != null)
            {
                return true;
            }
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }
            // (Blend: 0 zero, 1 one, 7 the alpha that is there, 8 one less that. Test: 4 nearer or as near, 8 always.)
            m_maskDepth = Plain(shader, 0, 1, 1, 4);
            m_maskZero = Plain(shader, 0, 0, 0, 4);
            m_maskSceneMat = Plain(shader, 1, 0, 0, 4);
            // (Test 5: where what is there is nearer than this.)
            m_maskHairAll = Plain(shader, 1, 0, 0, 5);
            for (int i = 1; i < Regions; i++)
            {
                m_maskFill[i] = Plain(shader, 8, 7, 0, 8);
            }
            m_quadNear = Rectangle(0f);
            m_quadFar = Rectangle(0.9999f);
            return true;
        }

        private void MaskScan(Camera cam)
        {
            m_maskDraws.Clear();
            int[] count = new int[Regions];
            var names = new System.Text.StringBuilder();
            int layers = cam != null ? cam.cullingMask : -1;
            var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; all != null && i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || (layers & (1 << r.gameObject.layer)) == 0 || r.name.StartsWith("CMA", StringComparison.Ordinal))
                {
                    continue;
                }
                // Skinned meshes are characters and what they wear; of the meshes that are not, only hair is taken
                // (it is often kept as plain meshes hung on the head).
                SkinnedMeshRenderer skinned = r.TryCast<SkinnedMeshRenderer>();
                MeshRenderer plain = skinned == null ? r.TryCast<MeshRenderer>() : null;
                if (skinned == null && plain == null)
                {
                    continue;
                }
                int subMeshes;
                if (skinned != null)
                {
                    if (skinned.sharedMesh == null)
                    {
                        continue;
                    }
                    subMeshes = skinned.sharedMesh.subMeshCount;
                }
                else
                {
                    // (A plain mesh is taken when it hangs on a character: when something above it, a few steps up, is
                    // named as hair or as a head, or when it is under the same root as an animated figure and named as
                    // hair itself.)
                    string above = r.name;
                    Transform up = r.transform.parent;
                    for (int step = 0; up != null && step < 5; step++)
                    {
                        above += " " + up.name;
                        up = up.parent;
                    }
                    if (!IsHair(above))
                    {
                        continue;
                    }
                    MeshFilter filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }
                    subMeshes = filter.sharedMesh.subMeshCount;
                }
                var mats = r.sharedMaterials;
                int subs = Mathf.Min(subMeshes, mats != null ? mats.Length : 0);
                for (int s = 0; s < subs; s++)
                {
                    Material m = mats[s];
                    if (m == null)
                    {
                        continue;
                    }
                    string name = m.name + " " + r.name;
                    int region = plain != null || IsHair(name) ? 2 : s_skinName.IsMatch(name) ? 1 : 3;
                    // (Hair that is a see-through picture laid on her skin, the cap under her hair, brows and lashes,
                    // is drawn here as the whole of its mesh: the cap reaches down over her forehead, and the hair's
                    // settings changed her forehead. Those are skin; hair is what is hair all through.)
                    if (region == 2 && skinned != null && m.renderQueue >= 2500 && (m.shader == null || !m.shader.name.StartsWith("HDRP/Hair")))
                    {
                        region = 1;
                    }
                    var draw = new MaskDraw { renderer = r, submesh = s, region = region, ownPass = -1 };
                    if (region == 2)
                    {
                        // Her long hair is drawn by a shader that makes its strands out of next to nothing: with the
                        // plain shader they have no width and nothing of them shows (the pictures: only the cap under
                        // them was marked). So hair is also drawn with its own material, by the pass of it that writes
                        // depth alone; see MaskPaint.
                        // (Guarded by itself: a call not tried in this game before must not take the mask down with it.)
                        try
                        {
                            for (int k = 0; k < s_depthPasses.Length && draw.ownPass < 0 && !m_maskHairFailed; k++)
                            {
                                int pass = m.FindPass(s_depthPasses[k]);
                                if (pass >= 0)
                                {
                                    draw.own = m;
                                    draw.ownPass = pass;
                                }
                                string told = m.name + ": '" + (m.shader != null ? m.shader.name : "?") + "' pass " + s_depthPasses[k] + (pass >= 0 ? " found" : " not there") + "; ";
                                if (m_maskHairTold.Length < 1200 && !m_maskHairTold.Contains(told))
                                {
                                    m_maskHairTold += told;
                                    Plugin.ModLog.LogInfo("Mask: hair's own material for its depth: " + told);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            m_maskHairFailed = true;
                            draw.own = null;
                            draw.ownPass = -1;
                            Plugin.ModLog.LogInfo("Mask: the passes of the hair's own material could not be asked for (" + ex.Message + "); hair is drawn as before.");
                        }
                    }
                    m_maskDraws.Add(draw);
                    count[region]++;
                    if (m_maskToldParts != -1 && names.Length < 1500)
                    {
                        names.Append(names.Length > 0 ? ", " : "").Append(m.name).Append(" (").Append(r.name).Append(") = ").Append(RegionNames[region]);
                    }
                }
            }
            // (Told again when the number of parts changes: she is dressed and her hair is put on after the first look.)
            if (m_maskDraws.Count > 0 && m_maskDraws.Count != m_maskToldParts && m_maskTolds < 2)
            {
                m_maskTolds++;
                m_maskToldParts = m_maskDraws.Count;
                Plugin.ModLog.LogInfo($"Mask: {m_maskDraws.Count} part(s): {count[1]} skin, {count[2]} hair, {count[3]} clothing. By material (mesh): {names}.");
            }
            MaskState = m_maskDraws.Count > 0 ? $"{count[1]} skin, {count[2]} hair, {count[3]} clothing parts" : "no characters found";
        }

        private bool Shown(MaskDraw d)
        {
            return d.renderer != null && d.renderer.enabled && d.renderer.isVisible;
        }

        // The mask drawn into m_maskRaw with the colours the materials have now.
        private void MaskPaint(Camera cam)
        {
            if (m_maskCmd == null)
            {
                m_maskCmd = new CommandBuffer { name = "CMADlssNr.Mask" };
            }
            Matrix4x4 view = cam.worldToCameraMatrix, proj = cam.projectionMatrix, one = Matrix4x4.identity;
            CommandBuffer cmd = m_maskCmd;
            cmd.Clear();
            cmd.SetRenderTarget(new RenderTargetIdentifier(m_maskRaw));
            cmd.ClearRenderTarget(true, true, Color.white);
            // 0. Hair by its own shader: its depth first (HDRP's shaders take the camera from the pipeline's own
            // numbers, which are still this camera's), then the hair's colour wherever anything is nearer than the far
            // end, which at this point is hair alone. What of it lies behind her body is painted over further down.
            bool ownHair = false;
            for (int i = 0; i < m_maskDraws.Count; i++)
            {
                MaskDraw h = m_maskDraws[i];
                if (h.own != null && h.ownPass >= 0 && Shown(h))
                {
                    cmd.DrawRenderer(h.renderer, h.own, h.submesh, h.ownPass);
                    ownHair = true;
                }
            }
            if (ownHair && m_maskHairAll != null)
            {
                cmd.SetViewProjectionMatrices(one, one);
                cmd.DrawMesh(m_quadFar, one, m_maskHairAll, 0, 0);
            }
            // 1. Depth of everything of the characters.
            cmd.SetViewProjectionMatrices(view, proj);
            for (int i = 0; i < m_maskDraws.Count; i++)
            {
                if (Shown(m_maskDraws[i]))
                {
                    cmd.DrawRenderer(m_maskDraws[i].renderer, m_maskDepth, m_maskDraws[i].submesh, 0);
                }
            }
            // 2. Each region: zero where it is nearest, then its colour where the alpha is zero.
            for (int region = 1; region < Regions; region++)
            {
                cmd.SetViewProjectionMatrices(view, proj);
                for (int i = 0; i < m_maskDraws.Count; i++)
                {
                    if (m_maskDraws[i].region == region && Shown(m_maskDraws[i]))
                    {
                        cmd.DrawRenderer(m_maskDraws[i].renderer, m_maskZero, m_maskDraws[i].submesh, 0);
                    }
                }
                cmd.SetViewProjectionMatrices(one, one);
                cmd.DrawMesh(m_quadNear, one, m_maskFill[region], 0, 0);
            }
            // 3. The scene: wherever nothing of a character is.
            cmd.SetViewProjectionMatrices(one, one);
            cmd.DrawMesh(m_quadFar, one, m_maskSceneMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        // Each frame, before the network is run: the mask for this frame. False when there is none to hand over.
        private bool MaskBuild(Camera cam, int w, int h, bool flip)
        {
            m_maskReady = false;
            if (!MaskWanted || cam == null)
            {
                return false;
            }
            try
            {
                if (!MaskMaterials())
                {
                    m_maskFailed = true;
                    MaskState = "not available (no plain shader in this game)";
                    Plugin.ModLog.LogInfo("Mask: 'Hidden/Internal-Colored' is not in this game; no mask.");
                    return false;
                }
                float now = Time.unscaledTime;
                if (now >= m_maskScanAt)
                {
                    m_maskScanAt = now + 3f;
                    MaskScan(cam);
                }
                if (m_maskRaw == null || m_maskRaw.width != w || m_maskRaw.height != h)
                {
                    Drop(ref m_maskRaw);
                    Drop(ref m_maskSmall);
                    Drop(ref m_mask);
                    m_maskRaw = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "CMADlssNr.MaskRaw", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                    m_maskRaw.Create();
                    m_maskSmall = Make("CMADlssNr.MaskSmall", Mathf.Max(1, w / 3), Mathf.Max(1, h / 3), RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    m_maskSmall.filterMode = FilterMode.Bilinear;
                    m_mask = Make("CMADlssNr.Mask", w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                }
                // What each region is set to, as a part of the most any is set to. (A material's colour is taken as one
                // for the eye and made linear on the way to the shader: handed over the other way round, the numbers
                // arrive as they are.)
                m_maskMaxI = MaxOf(Plugin.RegionEffect, 1f);
                m_maskMaxS = MaxOf(Plugin.RegionDetail, 2f);
                for (int i = 0; i < Regions; i++)
                {
                    float r = m_maskMaxI > 0.001f ? Mathf.Clamp01(Plugin.RegionEffect[i].Value / m_maskMaxI) : 1f;
                    float b = m_maskMaxS > 0.001f ? Mathf.Clamp01(Plugin.RegionDetail[i].Value / m_maskMaxS) : 1f;
                    var c = new Color(Mathf.LinearToGammaSpace(r), 1f, Mathf.LinearToGammaSpace(b), s_regionAlpha[i]);
                    (i == 0 ? m_maskSceneMat : m_maskFill[i]).SetColor("_Color", c);
                    if (i == 2)
                    {
                        m_maskHairAll.SetColor("_Color", c);
                    }
                }
                MaskPaint(cam);
                // Softened (made small and large again), and turned the way everything the network is handed is.
                if (Plugin.MaskSoft.Value)
                {
                    Graphics.Blit(m_maskRaw, m_maskSmall);
                    Blit(m_maskSmall, m_mask, 1f, 1f, flip);
                }
                else
                {
                    Blit(m_maskRaw, m_mask, 1f, 1f, flip);
                }
                m_maskReady = true;
                return true;
            }
            catch (Exception ex)
            {
                m_maskFailed = true;
                MaskState = "failed: " + ex.Message;
                Plugin.ModLog.LogWarning("Mask: failed (" + ex.GetType().Name + ": " + ex.Message + "); the network runs without it.");
                return false;
            }
        }

        private void MaskDrop()
        {
            Drop(ref m_maskRaw);
            Drop(ref m_maskSmall);
            Drop(ref m_mask);
            m_maskReady = false;
        }

        private void MenuMask(ref float y, float x, float w)
        {
            Check(ref y, x, w, "Separate settings for skin, hair and clothing", Plugin.Mask);
            GUI.Label(new Rect(x, y, w, 20f), Plugin.Mask.Value ? (MaskState.Length > 0 ? MaskState : "starting") : "off: one setting for the whole picture", m_stSmall);
            y += 22f;
            for (int i = 0; i < Regions; i++)
            {
                Slider(ref y, x, w, RegionNames[i] + ": effect", Plugin.RegionEffect[i], 0f, 1f, 10 + i * 2);
                Slider(ref y, x, w, RegionNames[i] + ": detail", Plugin.RegionDetail[i], 0f, 2f, 11 + i * 2);
            }
            Check(ref y, x, w, "Soft edges between regions", Plugin.MaskSoft);
            GUI.Label(new Rect(x, y, w, 60f), "Effect is how much of the network's result is kept on that part of the picture, detail how much fine structure it adds there; both scale Intensity and Local structure of the Neural page.", m_stSmall);
            y += 64f;
        }
    }
}
