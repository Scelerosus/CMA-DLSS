using System;
using BepInEx.Configuration;
using UnityEngine;

namespace CMADlssNr
{
    // The settings window: everything the keys did, and the settings that had to be edited in the file. One key opens
    // it (the others are unset by default: the game has its own on them).
    public sealed partial class Capture
    {
        private bool m_menu, m_menuDirty, m_cursorWas;
        private CursorLockMode m_lockWas;
        private float m_fpsShown;
        private GUIStyle m_stTitle, m_stLabel, m_stSmall, m_stButton, m_stButtonOn;
        private Texture2D m_texPanel, m_texButton, m_texButtonOn, m_texLine;
        private int m_dragId;

        private static Texture2D Flat(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void MenuToggle()
        {
            m_menu = !m_menu;
            if (m_menu)
            {
                // (Only a pointer that is locked to the middle of the screen is let go while the window is open.)
                m_lockWas = Cursor.lockState;
                m_cursorWas = m_lockWas == CursorLockMode.Locked;
                if (m_cursorWas)
                {
                    Cursor.lockState = CursorLockMode.None;
                }
            }
            else
            {
                if (m_cursorWas)
                {
                    Cursor.lockState = m_lockWas;
                }
                m_menuOnScreen = default;
                MenuSave();
            }
        }

        private void MenuSave()
        {
            if (!m_menuDirty)
            {
                return;
            }
            m_menuDirty = false;
            try
            {
                Plugin.Cfg.Save();
            }
            catch (Exception)
            {
            }
        }

        private void MenuStyles()
        {
            if (m_stTitle != null && m_texPanel != null)
            {
                return;
            }
            m_texPanel = Flat(new Color(0.06f, 0.065f, 0.08f, 0.94f));
            m_texButton = Flat(new Color(0.17f, 0.18f, 0.22f, 1f));
            m_texButtonOn = Flat(new Color(0.30f, 0.62f, 0.20f, 1f));
            m_texLine = Flat(new Color(1f, 1f, 1f, 0.12f));
            m_stTitle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            m_stLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            m_stSmall = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = true };
            m_stButton = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            m_stButtonOn = new GUIStyle(m_stButton) { fontStyle = FontStyle.Bold };
            m_stTitle.normal.textColor = new Color(0.62f, 0.9f, 0.45f);
            m_stLabel.normal.textColor = new Color(0.9f, 0.9f, 0.92f);
            m_stSmall.normal.textColor = new Color(0.62f, 0.64f, 0.7f);
            m_stButton.normal.textColor = new Color(0.85f, 0.86f, 0.9f);
            m_stButtonOn.normal.textColor = Color.white;
        }

        private bool Clicked(Rect r)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        private bool Button(Rect r, string text, bool on)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, on ? m_texButtonOn : m_texButton);
            }
            GUI.Label(r, text, on ? m_stButtonOn : m_stButton);
            return Clicked(r);
        }

        private void Check(ref float y, float x, float w, string text, ConfigEntry<bool> e)
        {
            Rect box = new Rect(x, y + 3f, 44f, 22f);
            if (Button(box, e.Value ? "ON" : "off", e.Value))
            {
                e.Value = !e.Value;
                m_menuDirty = true;
            }
            GUI.Label(new Rect(x + 54f, y, w - 54f, 28f), text, m_stLabel);
            y += 30f;
        }

        private void Choice(ref float y, float x, float w, string text, string[] names, int[] values, ConfigEntry<int> e)
        {
            GUI.Label(new Rect(x, y, w, 24f), text, m_stLabel);
            y += 25f;
            float bw = (w - 4f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                if (Button(new Rect(x + i * (bw + 4f), y, bw, 26f), names[i], e.Value == values[i]) && e.Value != values[i])
                {
                    e.Value = values[i];
                    m_menuDirty = true;
                    m_srFailed = false;
                }
            }
            y += 34f;
        }

        private void Slider(ref float y, float x, float w, string text, ConfigEntry<float> e, float min, float max, int id)
        {
            GUI.Label(new Rect(x, y, w - 60f, 24f), text, m_stLabel);
            GUI.Label(new Rect(x + w - 60f, y, 60f, 24f), e.Value.ToString("0.00"), m_stLabel);
            y += 24f;
            Rect bar = new Rect(x, y + 5f, w, 10f);
            Rect hit = new Rect(x, y, w, 20f);
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && hit.Contains(ev.mousePosition))
            {
                m_dragId = id;
                ev.Use();
            }
            if (m_dragId == id && (ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown || ev.type == EventType.Used))
            {
                float v = Mathf.Lerp(min, max, Mathf.Clamp01((ev.mousePosition.x - x) / w));
                v = Mathf.Round(v * 100f) / 100f;
                if (Mathf.Abs(v - e.Value) > 0.0001f)
                {
                    e.Value = v;
                    m_menuDirty = true;
                }
            }
            if (ev.rawType == EventType.MouseUp)
            {
                m_dragId = 0;
            }
            if (ev.type == EventType.Repaint)
            {
                GUI.DrawTexture(bar, m_texButton);
                GUI.DrawTexture(new Rect(x, y + 5f, w * Mathf.InverseLerp(min, max, e.Value), 10f), m_texButtonOn);
            }
            y += 26f;
        }

        private void Line(ref float y, float x, float w)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(x, y + 4f, w, 1f), m_texLine);
            }
            y += 10f;
        }

        private int m_tab;

        private void MenuDraw()
        {
            if (!m_menu)
            {
                return;
            }
            MenuStyles();
            float k = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            const float W = 430f, H = 760f;
            float x0 = 30f, y0 = 30f;
            Rect panel = new Rect(x0, y0, W, H);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(panel, m_texPanel);
            }
            float x = x0 + 16f, w = W - 32f, y = y0 + 10f;
            GUI.Label(new Rect(x, y, w, 30f), "DLSS / Neural Rendering  " + Plugin.PluginVersion, m_stTitle);
            y += 32f;
            bool dlss = m_srOpen && !m_srFailed, nr = m_view >= 0 && m_initialised;
            string state = !Plugin.Enabled.Value && !Plugin.Upscale.Value ? "everything is off"
                : m_failed ? "failed, see BepInEx/LogOutput.log"
                : m_target != null ? $"render {m_target.width}x{m_target.height} -> screen {m_outW}x{m_outH}"
                    + (dlss ? (m_target.width < m_outW ? ", DLSS upscaling" : ", DLAA") : "") + (nr ? ", neural rendering" : "")
                    + (m_fgOn ? ", frame generation x" + m_fgTotal : "")
                : "waiting for the camera";
            GUI.Label(new Rect(x, y, w, 40f), $"{m_fpsShown:0} fps rendered · {state}", m_stSmall);
            y += 38f;

            // The pages.
            string[] tabs = { "DLSS", "Neural", "Mask", "Frame Gen", "Game" };
            float tw = (w - 16f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                if (Button(new Rect(x + i * (tw + 4f), y, tw, 28f), tabs[i], m_tab == i))
                {
                    m_tab = i;
                }
            }
            y += 38f;
            Line(ref y, x, w);

            if (m_tab == 0)
            {
                Check(ref y, x, w, "DLSS", Plugin.Upscale);
                Choice(ref y, x, w, "Quality", new[] { "DLAA", "U.Quality", "Quality", "Balanced", "Perf", "U.Perf" }, new[] { 5, 4, 2, 1, 0, 3 }, Plugin.Quality);
                if (Plugin.Preset.Value != 0 && !Chosen(Plugin.Preset.Value))
                {
                    Plugin.Preset.Value = 0;
                }
                Slider(ref y, x, w, "Sharpness" + (SharpState.Length > 0 ? "  (" + SharpState + ")" : ""), Plugin.Sharpness, 0f, 1f, 4);
                Choice(ref y, x, w, "Model preset", new[] { "Auto", "K", "M", "E", "F" }, new[] { 0, 11, 13, 5, 6 }, Plugin.Preset);
                GUI.Label(new Rect(x, y, w, 132f), "DLAA keeps the full resolution and only anti-aliases; the other qualities render a smaller frame and reconstruct it, for more frames a second.\nModel preset: K is the sharpest and steadiest, M is faster; Auto takes K, and M for Performance and Ultra Performance. E and F are the older, much lighter models for RTX 20 and 30 cards: E for upscaling, F for DLAA and Ultra Performance.", m_stSmall);
                y += 136f;
            }
            else if (m_tab == 1)
            {
                Check(ref y, x, w, "Neural rendering (DLSS NR)", Plugin.Enabled);
                Check(ref y, x, w, "After DLSS, at full size (finer, slower)", Plugin.NrAfter);
                Slider(ref y, x, w, "Intensity", Plugin.Intensity, 0f, 1f, 1);
                Slider(ref y, x, w, "Local tone", Plugin.LocalTone, 0f, 2f, 2);
                Slider(ref y, x, w, "Local structure", Plugin.LocalStructure, 0f, 2f, 3);
                Choice(ref y, x, w, "Style", new[] { "Default", "Natural", "Cinematic" }, new[] { 0, 1, 2 }, Plugin.Style);
                Choice(ref y, x, w, "Passes", new[] { "1", "2", "3" }, new[] { 1, 2, 3 }, Plugin.Passes);
                GUI.Label(new Rect(x, y, w, 96f), "Intensity is how much of the network's result is shown. Local tone is its relighting, Local structure the fine detail it adds.\nWith DLSS on, the network works on the smaller frame before DLSS; \"After DLSS\" runs it on the full picture instead. Every pass costs the network's whole time again.", m_stSmall);
                y += 100f;
            }
            else if (m_tab == 2)
            {
                MenuMask(ref y, x, w);
            }
            else if (m_tab == 4)
            {
                GUI.Label(new Rect(x, y, w, 62f), "The game's own effects while DLSS or neural rendering runs. They are in the frame before DLSS and the network see it, which then carry on whatever they blur or add.", m_stSmall);
                y += 66f;
                Check(ref y, x, w, "Bloom (the glow round bright things)", Plugin.GameBloom);
                Check(ref y, x, w, "Subsurface scattering (light inside skin)", Plugin.GameSss);
                Check(ref y, x, w, "Depth of field", Plugin.GameDof);
                Check(ref y, x, w, "Motion blur", Plugin.GameMotionBlur);
                Check(ref y, x, w, "Chromatic aberration", Plugin.GameChromatic);
                Check(ref y, x, w, "Vignette", Plugin.GameVignette);
                Check(ref y, x, w, "Film grain", Plugin.GameGrain);
                GUI.Label(new Rect(x, y, w, 60f), "On leaves an effect as the game has it; off switches it off whatever the game's own settings say. A halo round sunlit skin is the game's bloom.", m_stSmall);
                y += 64f;
            }
            else
            {
                MenuFrameGen(ref y, x, w);
            }
            GUI.Label(new Rect(x, y0 + H - 30f, w, 24f), "Window: " + Plugin.MenuKey.Value + " (BepInEx/config/community.cma.dlssnr.cfg)", m_stSmall);
            GUI.matrix = keep;
            // (Clicks in the window do not go on to the game's own interface under it.)
            Event e = Event.current;
            Rect onScreen = new Rect(panel.x * k, panel.y * k, panel.width * k, panel.height * k);
            m_menuOnScreen = onScreen;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) && onScreen.Contains(e.mousePosition))
            {
                e.Use();
            }
        }
    }
}
