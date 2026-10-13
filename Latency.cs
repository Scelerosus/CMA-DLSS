using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CMADlssNr
{
    // Less delay between the hand and the screen.
    //
    // 1. Native texture pointers are asked of Unity once for each texture, not every frame: asking makes the main
    //    thread wait for the render thread, and with frame generation holding that thread back to pace its frames the
    //    wait was ten milliseconds a frame (measured in the user's run of 0.4.0).
    // 2. NVIDIA Reflex low latency, through the driver's own interface (nvapi64.dll): the game's device is put in low
    //    latency mode, and at the start of every frame the driver is let hold the game back for just as long as it
    //    takes for that frame not to wait in a queue behind the GPU. This game does not do it itself. Frame generation
    //    always costs about a frame of delay; this takes back what can be taken back.
    // 3. The pointer over the plugin's window (see PointerStep).
    public sealed partial class Capture
    {
        // ---- native pointers, asked once ----
        private static readonly Dictionary<IntPtr, IntPtr> s_ptrs = new Dictionary<IntPtr, IntPtr>();

        private static IntPtr Ptr(Texture t)
        {
            if (t == null)
            {
                return IntPtr.Zero;
            }
            if (!s_ptrs.TryGetValue(t.Pointer, out IntPtr p) || p == IntPtr.Zero)
            {
                p = t.GetNativeTexturePtr();
                s_ptrs[t.Pointer] = p;
            }
            return p;
        }

        private static void Forget(Texture t)
        {
            if (t != null)
            {
                s_ptrs.Remove(t.Pointer);
            }
        }

        // ---- Reflex ----
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr QueryInterfaceFn(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetSleepModeFn(IntPtr device, ref SleepMode mode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SleepFn(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceFn(IntPtr self, out IntPtr device);

        // NV_SET_SLEEP_MODE_PARAMS_V1 of nvapi.h.
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private unsafe struct SleepMode
        {
            public uint version;
            public byte lowLatency;
            public byte boost;
            public uint minimumIntervalUs;
            public byte useMarkers;
            public fixed byte reserved[31];
        }

        private bool m_reflexTried, m_reflexOk, m_reflexOn, m_reflexBoost;
        private int m_reflexLogs;
        private IntPtr m_device;
        private SetSleepModeFn m_setSleepMode;
        private SleepFn m_sleep;
        private int m_reflexFails;
        internal string ReflexState = "not started";

        private unsafe void ReflexOpen()
        {
            m_reflexTried = true;
            try
            {
                IntPtr lib = LoadLibraryW("nvapi64.dll");
                IntPtr qi = lib != IntPtr.Zero ? GetProcAddress(lib, "nvapi_QueryInterface") : IntPtr.Zero;
                if (qi == IntPtr.Zero)
                {
                    ReflexState = "unavailable (no NVIDIA driver interface)";
                    Plugin.ModLog.LogInfo("Reflex: nvapi64.dll or its entry was not found; not used.");
                    return;
                }
                var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(qi);
                IntPtr init = query(0x0150E828u), set = query(0xAC1CA9E0u), sleep = query(0x852CD1D2u);
                if (init == IntPtr.Zero || set == IntPtr.Zero || sleep == IntPtr.Zero)
                {
                    ReflexState = "unavailable (the driver has no such functions)";
                    Plugin.ModLog.LogInfo($"Reflex: the driver did not give its functions (initialise {(init != IntPtr.Zero)}, set sleep mode {(set != IntPtr.Zero)}, sleep {(sleep != IntPtr.Zero)}); not used.");
                    return;
                }
                int rc = Marshal.GetDelegateForFunctionPointer<InitializeFn>(init)();
                if (rc != 0)
                {
                    ReflexState = "unavailable (the driver interface did not start)";
                    Plugin.ModLog.LogInfo("Reflex: the driver's interface did not start (" + rc + "); not used.");
                    return;
                }
                // The game's device, from any texture of it (ID3D11DeviceChild::GetDevice, the fourth entry).
                IntPtr tex = Texture2D.whiteTexture.GetNativeTexturePtr();
                if (tex == IntPtr.Zero)
                {
                    ReflexState = "unavailable (no device)";
                    return;
                }
                IntPtr vtable = Marshal.ReadIntPtr(tex);
                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceFn>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                getDevice(tex, out m_device);
                if (m_device == IntPtr.Zero)
                {
                    ReflexState = "unavailable (no device)";
                    return;
                }
                m_setSleepMode = Marshal.GetDelegateForFunctionPointer<SetSleepModeFn>(set);
                m_sleep = Marshal.GetDelegateForFunctionPointer<SleepFn>(sleep);
                m_reflexOk = true;
                Plugin.ModLog.LogInfo("Reflex: the driver's low latency functions were found for the game's device.");
            }
            catch (Exception ex)
            {
                ReflexState = "unavailable (" + ex.Message + ")";
                Plugin.ModLog.LogWarning("Reflex: could not be set up (" + ex.GetType().Name + ": " + ex.Message + "); not used.");
            }
        }

        private unsafe bool ReflexMode(bool on, bool boost)
        {
            var mode = new SleepMode { version = (uint)sizeof(SleepMode) | (1u << 16), lowLatency = (byte)(on ? 1 : 0), boost = (byte)(on && boost ? 1 : 0), minimumIntervalUs = 0u, useMarkers = 0 };
            int rc = m_setSleepMode(m_device, ref mode);
            if (rc != 0)
            {
                ReflexState = "refused by the driver (" + rc + ")";
                Plugin.ModLog.LogInfo($"Reflex: the driver refused the mode (on {on}, boost {boost}): {rc}.");
                return false;
            }
            return true;
        }

        // At the start of every frame (Update): the mode is kept as set, and the driver is let hold the frame back.
        private void ReflexStep()
        {
            bool want = Plugin.Reflex.Value && Native.Loaded;
            if (!want && !m_reflexOn)
            {
                return;
            }
            try
            {
                if (!m_reflexTried)
                {
                    ReflexOpen();
                }
                if (!m_reflexOk || m_reflexFails > 3)
                {
                    return;
                }
                bool boost = Plugin.ReflexBoost.Value;
                if (want != m_reflexOn || (want && boost != m_reflexBoost))
                {
                    if (!ReflexMode(want, boost))
                    {
                        m_reflexFails++;
                        return;
                    }
                    m_reflexOn = want;
                    m_reflexBoost = boost;
                    ReflexState = want ? (boost ? "on, with boost" : "on") : "off";
                    if (m_reflexLogs < 12)
                    {
                        m_reflexLogs++;
                        Plugin.ModLog.LogInfo("Reflex: low latency " + ReflexState + ".");
                    }
                }
                if (m_reflexOn)
                {
                    int rc = m_sleep(m_device);
                    if (rc != 0 && ++m_reflexFails > 3)
                    {
                        ReflexState = "stopped (the driver's sleep fails: " + rc + ")";
                        Plugin.ModLog.LogInfo("Reflex: the driver's sleep keeps failing (" + rc + "); no longer called.");
                    }
                }
            }
            catch (Exception ex)
            {
                m_reflexFails = 99;
                ReflexState = "stopped (" + ex.Message + ")";
                Plugin.ModLog.LogWarning("Reflex: failed (" + ex.Message + "); no longer used.");
            }
        }

        // ---- the pointer ----
        // This game hides the system's pointer for good and draws one of its own, in its interface (the log of 0.6.4:
        // "the game has it hidden, Confined", always). The plugin's window is drawn over the game's interface, so over
        // the window the game's pointer is covered: that was the pointer that "vanished". (And everything tried before
        // was aimed at the wrong pointer: showing the system's one beside the game's gave two, and bringing it back by
        // force after generation was switched left it on the screen for good.)
        //
        // So: the game's pointer is left alone, and Unity's own switch for the system's pointer is not touched. Only
        // while the mouse is over the plugin's window is the system's pointer asked for from Windows itself, and it is
        // sent away again the moment the mouse leaves the window or the window closes. One pointer at any time.
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int size, flags;
            public IntPtr cursor;
            public int x, y;
        }

        [DllImport("user32")]
        private static extern bool GetCursorInfo(ref CursorInfo info);

        [DllImport("user32")]
        private static extern int ShowCursor(bool show);

        [DllImport("user32", EntryPoint = "LoadCursorW")]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

        [DllImport("user32", EntryPoint = "SetCursor")]
        private static extern IntPtr SetSystemCursor(IntPtr cursor);

        private bool m_pointerMine, m_pointerReset;
        private Rect m_menuOnScreen;

        // Every frame (Update).
        private void PointerStep()
        {
            try
            {
                if (!m_pointerReset)
                {
                    // (Should an earlier version have left the engine drawing a pointer of its own.)
                    m_pointerReset = true;
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                }
                Vector3 mouse = Input.mousePosition;
                bool over = m_menu && m_menuOnScreen.width > 0f && m_menuOnScreen.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
                // (Only where the game itself has the system's pointer hidden: where it shows it, there is nothing to do.)
                bool want = over && !Cursor.visible && Application.isFocused;
                if (want)
                {
                    var info = new CursorInfo { size = Marshal.SizeOf<CursorInfo>() };
                    if (!m_pointerMine || (GetCursorInfo(ref info) && (info.flags & 1) == 0))
                    {
                        int count = -1, asked = 0;
                        while (count < 0 && asked < 32)
                        {
                            count = ShowCursor(true);
                            asked++;
                        }
                    }
                    SetSystemCursor(LoadCursor(IntPtr.Zero, (IntPtr)32512));
                    m_pointerMine = true;
                }
                else if (m_pointerMine)
                {
                    m_pointerMine = false;
                    if (!Cursor.visible)
                    {
                        // Back to hidden, as the game has it: the count to just under zero.
                        int count = 0, asked = 0;
                        while (count >= 0 && asked < 32)
                        {
                            count = ShowCursor(false);
                            asked++;
                        }
                        while (count < -1 && asked < 64)
                        {
                            count = ShowCursor(true);
                            asked++;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
