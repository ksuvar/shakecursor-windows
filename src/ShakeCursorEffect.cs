/*
    SPDX-FileCopyrightText: 2023 Vlad Zahorodnii <vlad.zahorodnii@kde.org>
    SPDX-FileCopyrightText: 2026 ksuvar (Windows port)

    SPDX-License-Identifier: GPL-2.0-or-later

    Port of kwin/src/plugins/shakecursor/shakecursor.{h,cpp}
*/

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShakeCursor
{
    sealed class ShakeCursorEffect : IDisposable
    {
        public static System.IO.TextWriter Log;

        Config m_config;
        readonly ShakeDetector m_shakeDetector = new ShakeDetector();
        readonly CursorCache m_cursors = new CursorCache();
        readonly Timer m_deflateTimer = new Timer();
        readonly Timer m_animationTimer = new Timer();
        readonly Timer m_shapeTimer = new Timer();
        readonly Stopwatch m_animationClock = new Stopwatch();
        readonly Native.LowLevelMouseProc m_hookProc;
        readonly double m_dpiScale;
        IntPtr m_hook;

        const int AnimationDuration = 200; // ignore animation speed, it's not an animation from user perspective
        double m_animationStart = 1.0, m_animationEnd = 1.0;

        CursorOverlay m_cursorItem;
        float m_baseSize = 32;
        int m_x, m_y;
        IntPtr m_shownHandle;

        double m_targetMagnification = 1.0;
        double m_currentMagnification = 1.0;

        public ShakeCursorEffect(Config config)
        {
            m_config = config;
            m_dpiScale = Native.GetDpiForSystem() / 96.0;

            m_deflateTimer.Tick += delegate { m_deflateTimer.Stop(); Deflate(); };
            m_animationTimer.Interval = 8;
            m_animationTimer.Tick += delegate { StepAnimation(); };
            m_shapeTimer.Interval = 50;
            m_shapeTimer.Tick += delegate { FollowShape(false); };

            Reconfigure(config);
            m_cursors.Refresh();

            m_hookProc = HookProc; // keep the delegate alive
            m_hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, m_hookProc, Native.GetModuleHandle(null), 0);
            if (m_hook == IntPtr.Zero)
                throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
            if (Log != null) Log.WriteLine("hook installed, dpi scale {0}", m_dpiScale);
        }

        public void Dispose()
        {
            if (m_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(m_hook); m_hook = IntPtr.Zero; }
            m_animationTimer.Stop();
            m_deflateTimer.Stop();
            m_shapeTimer.Stop();
            Magnify(1.0);
            m_cursors.Dispose();
        }

        public bool IsActive { get { return m_currentMagnification != 1.0; } }

        public void Reconfigure(Config config)
        {
            m_config = config;
            m_shakeDetector.Interval = config.TimeInterval;
            m_shakeDetector.Sensitivity = config.Sensitivity;
        }

        /// <summary>Snaps back to the normal pointer immediately (e.g. before the cursor scheme changes).</summary>
        public void Reset()
        {
            m_animationTimer.Stop();
            m_deflateTimer.Stop();
            m_targetMagnification = 1.0;
            Magnify(1.0);
        }

        void Inflate()
        {
            double magnification;
            if (m_targetMagnification == 1.0)
                magnification = m_config.Magnification;
            else
                magnification = m_targetMagnification + m_config.OverMagnification;

            AnimateTo(Math.Min(magnification, Math.Max(m_config.Magnification, m_config.MaxMagnification)));
        }

        void Deflate()
        {
            AnimateTo(1.0);
        }

        void AnimateTo(double magnification)
        {
            if (m_targetMagnification != magnification)
            {
                m_animationTimer.Stop();

                m_animationStart = m_currentMagnification;
                m_animationEnd = magnification;
                m_animationClock.Restart();
                m_animationTimer.Start();

                m_targetMagnification = magnification;
            }
        }

        static double InOutCubic(double t)
        {
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        void StepAnimation()
        {
            double t = Math.Min(1.0, m_animationClock.ElapsedMilliseconds / (double)AnimationDuration);
            double value = t >= 1.0 ? m_animationEnd : m_animationStart + (m_animationEnd - m_animationStart) * InOutCubic(t);
            Magnify(value);
            if (t >= 1.0) m_animationTimer.Stop();
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == Native.WM_MOUSEMOVE)
            {
                var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                try { PointerMotion(info); }
                catch { /* never let an exception escape a low-level hook */ }
            }
            return Native.CallNextHookEx(m_hook, nCode, wParam, lParam);
        }

        static bool AnyButtonDown()
        {
            return (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0
                || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0
                || (Native.GetAsyncKeyState(Native.VK_MBUTTON) & 0x8000) != 0
                || (Native.GetAsyncKeyState(Native.VK_XBUTTON1) & 0x8000) != 0
                || (Native.GetAsyncKeyState(Native.VK_XBUTTON2) & 0x8000) != 0;
        }

        void PointerMotion(Native.MSLLHOOKSTRUCT ev)
        {
            if (Log != null) Log.WriteLine("{0} move {1},{2} flags={3}", ev.time, ev.pt.X, ev.pt.Y, ev.flags);

            m_x = ev.pt.X;
            m_y = ev.pt.Y;
            if (m_cursorItem != null) m_cursorItem.MoveTo(m_x, m_y);

            if (AnyButtonDown())
            {
                m_shakeDetector.Reset();
                return;
            }

            if (Native.IsPointerConstrained())
                return;

            // KWin works in logical pixels; convert so the 100px threshold means the same thing.
            if (m_shakeDetector.Update(ev.pt.X / m_dpiScale, ev.pt.Y / m_dpiScale, ev.time))
            {
                if (Log != null) Log.WriteLine("shake -> inflate (target {0})", m_targetMagnification);
                Inflate();
                m_deflateTimer.Stop();
                m_deflateTimer.Interval = 2000;
                m_deflateTimer.Start();
            }
        }

        void Magnify(double magnification)
        {
            if (magnification == 1.0)
            {
                m_currentMagnification = 1.0;
                if (m_cursorItem != null)
                {
                    m_shapeTimer.Stop();
                    m_cursorItem.Close();
                    m_cursorItem.Dispose();
                    m_cursorItem = null;
                    ShowCursor();
                }
            }
            else
            {
                m_currentMagnification = magnification;

                if (m_cursorItem == null)
                {
                    // The real cursors are still visible here: refresh the shape images and measure the pointer.
                    m_cursors.Refresh();
                    m_cursors.ClearCustom();
                    m_baseSize = CurrentCursorSize();

                    Native.POINT p;
                    if (Native.GetCursorPos(out p)) { m_x = p.X; m_y = p.Y; }

                    m_cursorItem = new CursorOverlay();
                    m_cursorItem.Show();
                    FollowShape(true);
                    HideCursor();
                    m_shapeTimer.Start();
                    return;
                }
                FollowShape(true);
            }
        }

        /// <summary>Draws the shape that is showing right now; re-renders only when needed.</summary>
        void FollowShape(bool sizeChanged)
        {
            if (m_cursorItem == null) return;

            var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            Native.GetCursorInfo(ref ci);
            if ((ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero)
            {
                // The app under the pointer hid it (video player, game, typing...).
                m_cursorItem.SetHidden(true);
                m_shownHandle = IntPtr.Zero;
                return;
            }

            if (!sizeChanged && ci.hCursor == m_shownHandle) return;
            m_shownHandle = ci.hCursor;
            m_cursorItem.Render(m_cursors.Lookup(ci.hCursor), (float)(m_baseSize * m_currentMagnification), m_x, m_y);
        }

        /// <summary>Size of the pointer as Windows currently draws it (honours the "pointer size" setting).</summary>
        static float CurrentCursorSize()
        {
            var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            IntPtr h = Native.GetCursorInfo(ref ci) && ci.hCursor != IntPtr.Zero
                ? ci.hCursor : Native.LoadCursor(IntPtr.Zero, new IntPtr(32512));
            int w = CursorImage.WidthOf(h);
            return w > 0 ? w : 32;
        }

        // effects->hideCursor(): swap every system cursor for a fully transparent one.
        static void HideCursor()
        {
            byte[] and = new byte[32 * 32 / 8];
            for (int i = 0; i < and.Length; i++) and[i] = 0xFF;
            byte[] xor = new byte[32 * 32 / 8];
            IntPtr blank = Native.CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor);
            foreach (uint id in CursorCache.AllIds)
                Native.SetSystemCursor(Native.CopyIcon(blank), id); // takes ownership of the copy
            Native.DestroyCursor(blank);
        }

        // effects->showCursor()
        static void ShowCursor()
        {
            Native.RestoreSystemCursors();
        }
    }
}
