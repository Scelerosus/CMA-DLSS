using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace CMADlssNr
{
    // The frame, taken from the game and given back.
    //
    // Each frame, before the game renders (LateUpdate), the main camera is given a texture of the plugin's to render
    // into instead of the screen. When the render pipeline has finished that camera (its endCameraRendering), the
    // frame is in that texture, finished: tone-mapped, as it would have gone to the screen. It is copied to the
    // network's input with the pipeline's motion vectors and depth, the network is run on the render thread (the native
    // half's render event), and its output is drawn to the screen. The camera is then given the screen back, so that
    // everything the game does with it between frames (picking with the mouse) is as it was.
    //
    // HDRP keeps its buffers in textures that may be larger than the frame, with the frame in their corner: what is
    // copied out of them is that corner.
    public sealed partial class Capture : MonoBehaviour
    {
        public Capture(IntPtr pointer) : base(pointer)
        {
        }

        private static Capture s_me;
        private Il2CppSystem.Action<ScriptableRenderContext, Camera> m_callback;
        private bool m_subscribed, m_failed;

        // The camera that renders into the plugin's texture this frame, and that texture.
        private Camera m_camera;
        private RenderTexture m_target;
        private int m_setFrame = -1, m_composedFrame = -1, m_missed;

        // The network's textures (of the size of the frame), and its view in the native half.
        private RenderTexture m_color, m_output, m_mvec, m_depth, m_sliceMvec, m_sliceDepth;
        private int m_view = -1, m_width, m_height, m_passes, m_reset;
        private bool m_initialised;
        private IntPtr m_regColor, m_regMvec, m_regDepth, m_regOutput, m_regMask;

        private float m_logAt, m_fpsFrom;
        private int m_fpsFrames;
        private ulong m_waitWas;
        private bool m_enabledWas, m_upscaleWas;
        private long m_composeTicks;
        private int m_logs;
        private string m_sourcesTold = "";

        private void Awake()
        {
            s_me = this;
        }

        private void Update()
        {
            ReflexStep();
            PointerStep();
            m_fpsShown = Mathf.Lerp(m_fpsShown, 1f / Mathf.Max(0.0005f, Time.unscaledDeltaTime), 0.05f);
            if (Plugin.MenuKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.MenuKey.Value))
            {
                MenuToggle();
            }
            if (Plugin.Enabled.Value != m_enabledWas || Plugin.Upscale.Value != m_upscaleWas)
            {
                m_upscaleWas = Plugin.Upscale.Value;
                m_nrFailed = false;
                m_fgFailed = false;
                // (Switched in the window: whatever failed before is tried again.)
                m_enabledWas = Plugin.Enabled.Value;
                m_failed = false;
                m_srFailed = false;
                m_jitFailed = false;
                m_missed = 0;
            }
            if (m_menu && m_menuDirty && !Input.GetMouseButton(0))
            {
                MenuSave();
            }
            if (Plugin.ToggleKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.ToggleKey.Value))
            {
                Plugin.Enabled.Value = !Plugin.Enabled.Value;
                m_failed = false;
                m_srFailed = false;
                m_missed = 0;
                Plugin.ModLog.LogInfo("Neural rendering: " + (Plugin.Enabled.Value ? "on" : "off") + ".");
            }
            if (Plugin.QualityKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.QualityKey.Value))
            {
                // (From the size of the screen down: DLAA, Quality, Balanced, Performance, Ultra Performance.)
                int[] order = { 5, 2, 1, 0, 3 };
                int at = Array.IndexOf(order, Plugin.Quality.Value);
                Plugin.Quality.Value = order[(at + 1 + order.Length) % order.Length];
                m_srFailed = false;
                m_logs = 2;
                m_logAt = 0f;
                Plugin.ModLog.LogInfo("DLSS quality: " + s_qualities[Plugin.Quality.Value] + (Plugin.Quality.Value == 5 ? " (the size of the screen, no upscaling)" : "") + ".");
            }
        }

        private void LateUpdate()
        {
            try
            {
                // (Should the interface's drawing not have come round to it.)
                GiveBack();
                // (Put back in the other order than they are set: see HairRestore.)
                if (Active)
                {
                    MotionStep(true);
                    HairStep(true);
                }
                else
                {
                    HairStep(false);
                    MotionStep(false);
                }
                Screen2();
                bool want = Active;
                Camera cam = want ? Camera.main : null;
                if (cam == null || Screen.width < 64 || Screen.height < 64)
                {
                    JitterSet(null, false);
                    GiveBack();
                    if (!want)
                    {
                        Teardown();
                    }
                    return;
                }
                if (!m_subscribed)
                {
                    m_callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(new Action<ScriptableRenderContext, Camera>(OnEndCamera));
                    RenderPipelineManager.add_endCameraRendering(m_callback);
                    m_subscribed = true;
                    Plugin.ModLog.LogInfo("Capture: listening for the end of each camera's rendering.");
                }
                // (The frame before was rendered into the texture and never put on the screen: the screen would stay
                // black. After three such frames it is given up, with the screen given back.)
                if (m_setFrame >= 0 && m_composedFrame < m_setFrame)
                {
                    if (++m_missed >= 3)
                    {
                        m_failed = true;
                        Plugin.ModLog.LogError("Capture: the render pipeline did not report the end of the main camera's rendering for three frames; the plugin stands back (switching it off and on in the window tries again).");
                        GiveBack();
                        Teardown();
                        return;
                    }
                }
                else
                {
                    m_missed = 0;
                }
                m_outW = Screen.width;
                m_outH = Screen.height;
                RenderExtent(m_outW, m_outH, out int w, out int h);
                if (m_target == null || m_target.width != w || m_target.height != h)
                {
                    if (m_target != null)
                    {
                        m_target.Release();
                        Destroy(m_target);
                    }
                    m_target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CMADlssNr.Frame", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                    m_target.Create();
                    Plugin.ModLog.LogInfo($"Capture: the main camera '{cam.name}' renders into a texture of {w}x{h} (the screen is {m_outW}x{m_outH}).");
                }
                if (m_camera != null && m_camera != cam)
                {
                    GiveBack();
                }
                // (Not a camera that renders into a texture of the game's own.)
                if (cam.targetTexture != null && cam.targetTexture != m_target)
                {
                    return;
                }
                JitterSet(cam, true, Plugin.Jitter.Value && UpscaleWanted && m_srOpen, Mathf.Log(w / (float)Mathf.Max(1, m_outW), 2f));
                cam.targetTexture = m_target;
                m_camera = cam;
                m_setFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                Fail("before the frame", ex);
            }
        }

        private static void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            Capture me = s_me;
            if (me == null || cam == null || me.m_camera == null || cam.Pointer != me.m_camera.Pointer || me.m_setFrame != Time.frameCount)
            {
                return;
            }
            try
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                me.Compose();
                me.m_composeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            }
            catch (Exception ex)
            {
                me.Fail("composing the frame", ex);
            }
            me.m_composedFrame = Time.frameCount;
            // (The camera keeps the plugin's texture until everything of this frame is rendered: see OnGUI.)
        }

        // The camera is given the screen back only once the whole frame is rendered. Unity renders the cameras that
        // have a texture first and those that render to the screen after: given back at the end of its own rendering
        // (as it was up to 0.2.2), the main camera was found among the latter and rendered a second time, to the
        // screen, at the screen's full size. That was the picture that covered what was drawn there, the frame index
        // going up by two, and the frames a second that would not rise however small the frame. The interface's own
        // drawing comes after all cameras: that is where it is given back, so that between frames (picking with the
        // mouse) the camera is as the game expects it.
        private void OnGUI()
        {
            if (m_camera != null && Event.current != null && Event.current.type == EventType.Repaint && m_composedFrame == Time.frameCount)
            {
                GiveBack();
            }
            try
            {
                MenuDraw();
            }
            catch (Exception ex)
            {
                m_menu = false;
                Plugin.ModLog.LogWarning("The settings window failed (" + ex.Message + ") and was closed.");
            }
        }

        private void Fail(string where, Exception ex)
        {
            m_failed = true;
            JitterRestore();
            Plugin.ModLog.LogError("Capture: failed " + where + " (" + ex.GetType().Name + ": " + ex.Message + "); the plugin stands back (switching it off and on in the window tries again).");
            GiveBack();
        }

        private void GiveBack()
        {
            try
            {
                if (m_camera != null && m_target != null && m_camera.targetTexture == m_target)
                {
                    m_camera.targetTexture = null;
                }
            }
            catch (Exception)
            {
            }
            m_camera = null;
        }

        private int m_presentFrame = -10;
        private GameObject m_screen;
        private Canvas m_screenCanvas;
        private UnityEngine.UI.RawImage m_screenImage;

        // The picture over the whole screen, under everything else that is drawn over the screen: shown for as long as
        // frames are being put together.
        private bool m_screenFlip, m_screenSet;

        private void Screen2()
        {
            bool on = m_shown != null && Time.frameCount - m_presentFrame <= 2 && Active;
            if (m_screen == null)
            {
                if (!on)
                {
                    return;
                }
                m_screen = new GameObject("CMADlssNr.Screen") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(m_screen);
                m_screenCanvas = m_screen.AddComponent<Canvas>();
                m_screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                m_screenCanvas.sortingOrder = -32000;
                var child = new GameObject("Picture") { hideFlags = HideFlags.HideAndDontSave };
                RectTransform rect = child.AddComponent<RectTransform>();
                rect.SetParent(m_screen.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                m_screenImage = child.AddComponent<UnityEngine.UI.RawImage>();
                m_screenImage.raycastTarget = false;
                m_screenImage.color = Color.white;
                Plugin.ModLog.LogInfo("Capture: a picture over the whole screen (under the interface) shows what is put together.");
            }
            if (m_screenCanvas.enabled != on)
            {
                m_screenCanvas.enabled = on;
            }
            if (on)
            {
                if (m_screenImage.texture == null || m_screenImage.texture.Pointer != m_shown.Pointer)
                {
                    m_screenImage.texture = m_shown;
                }
                if (m_screenFlip != m_shownFlip || !m_screenSet)
                {
                    m_screenSet = true;
                    m_screenFlip = m_shownFlip;
                    m_screenImage.uvRect = m_shownFlip ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
                }
            }
        }

        // What is shown over the screen this frame, and whether it is upside down (in the network's orientation).
        private RenderTexture m_mvecBig, m_depthBig;
        private Texture m_shown;
        private bool m_shownFlip, m_nrFailed;

        private bool Active => (Plugin.Enabled.Value || Plugin.Upscale.Value) && !m_failed && Native.Loaded;

        // The frame is in the plugin's texture: the network is run over it if that is on, DLSS after it if that is on
        // (either without the other), and what comes out is what the picture over the screen shows.
        private void Compose()
        {
            int w = m_target.width, h = m_target.height;
            bool flip = Plugin.FlipY.Value;
            JitterRead(m_camera);
            int ow = Mathf.Max(w, m_outW), oh = Mathf.Max(h, m_outH);
            Buffers(w, h, flip);

            bool nrWant = Plugin.Enabled.Value && !m_nrFailed;
            // The other order, as asked for: DLSS first, on the game's frame, and the network over DLSS's full-size
            // result. The network then works on every pixel of the screen (it costs as much as at DLAA), and what it
            // makes is not passed through DLSS afterwards. Its motion vectors and depth are the small ones made large.
            RenderTexture first = null;
            if (nrWant && Plugin.NrAfter.Value && UpscaleWanted && m_srOpen && !m_srFailed)
            {
                first = Upscaled(w, h, ow, oh, m_target, flip);
            }
            bool after = first != null;
            int nw = after ? ow : w, nh = after ? oh : h;
            if (after)
            {
                if (m_mvecBig == null || m_mvecBig.width != ow || m_mvecBig.height != oh)
                {
                    Drop(ref m_mvecBig);
                    Drop(ref m_depthBig);
                    m_mvecBig = Make("CMADlssNr.MotionBig", ow, oh, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
                    m_depthBig = Make("CMADlssNr.DepthBig", ow, oh, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                }
                Graphics.Blit(m_mvec, m_mvecBig);
                Graphics.Blit(m_depth, m_depthBig);
            }
            else if (m_mvecBig != null)
            {
                Drop(ref m_mvecBig);
                Drop(ref m_depthBig);
            }
            if (nrWant)
            {
                MaskBuild(m_camera, nw, nh, flip);
            }
            else
            {
                m_maskReady = false;
            }
            bool nr = nrWant && Ensure(nw, nh)
                && (after ? Run(nw, nh, first, false, m_mvecBig, m_depthBig) : Run(nw, nh, m_target, flip, m_mvec, m_depth))
                && Native.vdn_ready(m_view) != 0;
            if (!nrWant && (m_view >= 0 || m_color != null))
            {
                CloseNr();
            }
            if (m_srOpen && !UpscaleWanted)
            {
                TeardownSr();
            }
            Texture final = nr ? m_output : m_target;
            bool finalFlip = nr && flip;
            if (after)
            {
                final = nr ? (Texture)m_output : first;
                finalFlip = flip;
            }
            else if (UpscaleWanted && m_srOpen)
            {
                // (DLSS is handed the network's output; or, with the network off, the frame itself.)
                RenderTexture up = nr ? Upscaled(w, h, ow, oh, m_output, false) : Upscaled(w, h, ow, oh, m_target, flip);
                if (up != null)
                {
                    final = up;
                    finalFlip = flip;
                }
            }
            // (Shown as it is, with no copy in between: the picture over the screen turns it the right way up.)
            m_shown = Sharpened(final);
            m_shownFlip = finalFlip;
            m_presentFrame = Time.frameCount;
            FrameGenStep(m_camera, w, h);
            Report(nr);
        }

        // A copy of the corner of a texture (all of it with 1, 1), upside down or not.
        private static void Blit(Texture from, RenderTexture to, float sx, float sy, bool flip)
        {
            if (flip)
            {
                Graphics.Blit(from, to, new Vector2(sx, -sy), new Vector2(0f, sy));
            }
            else
            {
                Graphics.Blit(from, to, new Vector2(sx, sy), Vector2.zero);
            }
        }

        // One of the pipeline's buffers into one of the network's textures: the frame's corner of it.
        private string Buffer(string name, RenderTexture to, int w, int h, bool flip)
        {
            Texture from = Shader.GetGlobalTexture(name);
            // (HDRP keeps its buffers as arrays of textures, one to an eye: the plain copy cannot read those. The
            // frame's corner of the first is copied out as it is, into a texture of the same format, and that is what
            // is handed on.)
            if (from != null && from.dimension == TextureDimension.Tex2DArray && from.width >= w && from.height >= h)
            {
                try
                {
                    bool depthSlot = to == m_depth;
                    RenderTexture slice = depthSlot ? m_sliceDepth : m_sliceMvec;
                    if (slice == null || slice.width != w || slice.height != h || slice.graphicsFormat != from.graphicsFormat)
                    {
                        if (slice != null)
                        {
                            Forget(slice);
                            slice.Release();
                            Destroy(slice);
                        }
                        slice = new RenderTexture(w, h, 0, from.graphicsFormat) { name = "CMADlssNr.Slice", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                        slice.Create();
                        if (depthSlot) m_sliceDepth = slice; else m_sliceMvec = slice;
                    }
                    Graphics.CopyTexture(from, 0, 0, 0, 0, w, h, slice, 0, 0, 0, 0);
                    Blit(slice, to, 1f, 1f, flip);
                    return $"{from.width}x{from.height} {from.graphicsFormat} (array, its first)";
                }
                catch (Exception ex)
                {
                    return $"unusable (array {from.graphicsFormat}: {ex.Message})";
                }
            }
            if (from == null || from.dimension != TextureDimension.Tex2D || from.width < w || from.height < h)
            {
                RenderTexture keep = RenderTexture.active;
                RenderTexture.active = to;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = keep;
                return from == null ? "none" : $"unusable ({from.dimension}, {from.width}x{from.height})";
            }
            Blit(from, to, w / (float)from.width, h / (float)from.height, flip);
            return $"{from.width}x{from.height} {from.graphicsFormat}";
        }

        // The pipeline's motion vectors and depth, copied out for the network and for DLSS (both read the same).
        private void Buffers(int w, int h, bool flip)
        {
            if (m_mvec == null || m_mvec.width != w || m_mvec.height != h)
            {
                Drop(ref m_mvec);
                Drop(ref m_depth);
                m_mvec = Make("CMADlssNr.Motion", w, h, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
                m_depth = Make("CMADlssNr.Depth", w, h, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            }
            string mv = Buffer("_CameraMotionVectorsTexture", m_mvec, w, h, flip);
            string depth = Buffer("_CameraDepthTexture", m_depth, w, h, flip);
            string sources = "motion vectors " + mv + ", depth " + depth;
            if (sources != m_sourcesTold)
            {
                m_sourcesTold = sources;
                Plugin.ModLog.LogInfo("Capture: the pipeline's buffers: " + sources + "; the frame is " + w + "x" + h + ".");
            }
        }

        private bool Run(int w, int h, Texture from, bool flipFrom, RenderTexture mvec, RenderTexture depth)
        {
            Blit(from, m_color, 1f, 1f, flipFrom);
            // (Registered anew should Unity have made any of the textures anew.)
            bool masked = m_maskInit && m_maskReady && m_mask != null;
            IntPtr c = Ptr(m_color), m = Ptr(mvec), d = Ptr(depth), o = Ptr(m_output), k = masked ? Ptr(m_mask) : IntPtr.Zero;
            if (c != m_regColor || m != m_regMvec || d != m_regDepth || o != m_regOutput || k != m_regMask)
            {
                m_regMask = k;
                int rc = Native.vdn_set_textures(m_view, c, m, d, o, k, 0, 0, 0, 0);
                if (rc != 0)
                {
                    Plugin.ModLog.LogError("Neural rendering: the textures were refused (" + rc + "): " + Native.Text(Native.vdn_reject_text(m_view)));
                    m_nrFailed = true;
                    return false;
                }
                m_regColor = c;
                m_regMvec = m;
                m_regDepth = d;
                m_regOutput = o;
                m_reset = 2;
            }
            float mx = w * (Plugin.InvertMotionX.Value ? -1f : 1f), my = h * (Plugin.InvertMotionY.Value ? -1f : 1f);
            // (With the mask the feature is handed the most any region is set to; the mask gives each its part of it.)
            float intensity = Mathf.Clamp01(Plugin.Intensity.Value) * (masked ? m_maskMaxI : 1f);
            float structure = Mathf.Min(2f, Mathf.Clamp(Plugin.LocalStructure.Value, 0f, 2f) * (masked ? m_maskMaxS : 1f));
            Native.vdn_set_params(m_view, intensity, Mathf.Clamp(Plugin.LocalTone.Value, 0f, 2f), structure, -1f,
                mx, my, 0, Mathf.Clamp(Plugin.Style.Value, 0, 2), Plugin.DepthInverted.Value ? 1 : 0, Plugin.UiCorrection.Value ? 1 : 0, masked ? 1 : 0, m_reset > 0 ? 1 : 0);
            if (m_reset > 0)
            {
                m_reset--;
            }
            GL.IssuePluginEvent(Native.vdn_get_render_event_func(), Native.NrEvent(m_view));
            return true;
        }

        private static RenderTexture Make(string name, int w, int h, RenderTextureFormat format, RenderTextureReadWrite space)
        {
            var rt = new RenderTexture(w, h, 0, format, space) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            rt.Create();
            return rt;
        }

        private bool Ensure(int w, int h)
        {
            int passes = Mathf.Clamp(Plugin.Passes.Value, 1, 4);
            if (m_initialised && m_width == w && m_height == h && m_passes == passes && m_maskInit == m_maskReady)
            {
                return true;
            }
            Native.vdn_rebuild_begin();
            try
            {
                FrameGenHold(30);
                ReleaseNr();
                m_width = w;
                m_height = h;
                m_passes = passes;
                m_color = Make("CMADlssNr.Color", w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                m_output = Make("CMADlssNr.Output", w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (m_view < 0)
                {
                    m_view = Native.vdn_view_open();
                    if (m_view < 0)
                    {
                        Plugin.ModLog.LogError("Neural rendering: the native half has no free view.");
                        m_nrFailed = true;
                        return false;
                    }
                }
                m_maskInit = m_maskReady;
                int rc = Native.vdn_view_init(m_view, Native.SnippetPath, (uint)w, (uint)h, (uint)passes, m_maskInit ? 1u : 0u, Ptr(m_color));
                if (rc != 0)
                {
                    Plugin.ModLog.LogError("Neural rendering: could not be started (" + rc + "): " + Native.Text(Native.vdn_status_text(m_view)));
                    m_nrFailed = true;
                    return false;
                }
                m_initialised = true;
                m_regColor = m_regMvec = m_regDepth = m_regOutput = IntPtr.Zero;
                m_reset = 2;
                m_logs = 0;
                m_logAt = 0f;
                Plugin.ModLog.LogInfo($"Neural rendering: view {m_view} is up at {w}x{h}{(passes > 1 ? ", " + passes + " passes" : "")}.");
                return true;
            }
            finally
            {
                Native.vdn_rebuild_end();
            }
        }

        private void Report(bool ran)
        {
            float now = Time.unscaledTime;
            m_fpsFrames++;
            if (now < m_logAt || m_logs >= 6)
            {
                return;
            }
            // (How fast the frames come, over the time since the last line; and how long the native half waited for the
            // GPU in that time.)
            float span = now - m_fpsFrom;
            float fps = span > 0.2f && m_fpsFrom > 0f ? m_fpsFrames / span : 0f;
            ulong waited = m_view >= 0 ? Native.vdn_ring_wait_micros(m_view) : 0;
            float waitMs = m_fpsFrames > 0 ? (waited - m_waitWas) / 1000f / m_fpsFrames : 0f;
            m_waitWas = waited;
            float composeMs = m_fpsFrames > 0 ? (float)(m_composeTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / m_fpsFrames) : 0f;
            m_composeTicks = 0;
            m_fpsFrom = now;
            m_fpsFrames = 0;
            m_logAt = now + 5f;
            m_logs++;
            Plugin.ModLog.LogInfo($"Speed: {fps:F0} frames a second; rendered {m_target.width}x{m_target.height} for a screen of {m_outW}x{m_outH}; neural rendering {(ran ? "on" : "off")}; DLSS {(m_srOpen && !m_srFailed ? s_qualities[Mathf.Clamp(Plugin.Quality.Value, 0, 5)] + (m_jitOn ? ", jittered" : ", no jitter") : "off")}; the plugin's own calls took {composeMs:F2} ms a frame of the main thread{(m_view >= 0 ? $"; the native half waited {waitMs:F2} ms a frame for the GPU, last result 0x{Native.vdn_last_ngx_result(m_view):X}, {Native.Text(Native.vdn_status_text(m_view))}" : "")}.");
        }

        private void ReleaseNr()
        {
            Drop(ref m_color);
            Drop(ref m_output);
            m_initialised = false;
            m_regColor = m_regMvec = m_regDepth = m_regOutput = m_regMask = IntPtr.Zero;
        }

        // The network alone is closed (DLSS may go on without it).
        private void CloseNr()
        {
            try
            {
                Native.vdn_rebuild_begin();
                try
                {
                    if (m_view >= 0)
                    {
                        Native.vdn_view_close(m_view);
                        m_view = -1;
                    }
                    ReleaseNr();
                }
                finally
                {
                    Native.vdn_rebuild_end();
                }
            }
            catch (Exception)
            {
            }
        }

        private void Teardown()
        {
            if (m_view < 0 && m_color == null && m_mvec == null && !m_srOpen && !m_camOn && m_noMotion.Count == 0 && m_hair.Count == 0)
            {
                return;
            }
            m_shown = null;
            FrameGenStop();
            CloseNr();
            TeardownSr();
            JitterRestore();
            HairRestore();
            MotionRestore();
            Drop(ref m_mvec);
            Drop(ref m_depth);
            Drop(ref m_sliceMvec);
            Drop(ref m_mvecBig);
            Drop(ref m_depthBig);
            Drop(ref m_sliceDepth);
            Drop(ref m_sharp);
            Drop(ref m_sharpBlur);
            Drop(ref m_sharpSolid);
            MaskDrop();
            m_sourcesTold = "";
        }
    }
}
