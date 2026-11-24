/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ShakeCursor
{
    /// <summary>
    /// Click-through, top-most, per-pixel-alpha window that draws the magnified cursor.
    /// Plays the role of KWin's ShakeCursorItem in the overlay scene.
    /// </summary>
    sealed class CursorOverlay : Form
    {
        CursorImage m_image;
        float m_hotX, m_hotY;
        bool m_hidden;

        public CursorOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Shake Cursor overlay";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                            | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public CursorImage Image { get { return m_image; } }

        /// <summary>Re-renders <paramref name="image"/> so that it is <paramref name="size"/> pixels wide, hotspot at (x, y).</summary>
        public void Render(CursorImage image, float size, int x, int y)
        {
            m_image = image;
            if (image == null || image.Bitmap == null) { SetHidden(true); return; }

            float scale = size / image.Bitmap.Width;
            int w = Math.Max(1, (int)Math.Ceiling(image.Bitmap.Width * scale));
            int h = Math.Max(1, (int)Math.Ceiling(image.Bitmap.Height * scale));
            m_hotX = image.Hotspot.X * scale;
            m_hotY = image.Hotspot.Y * scale;

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    using (var attrs = new ImageAttributes())
                    {
                        attrs.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
                        g.DrawImage(image.Bitmap, new Rectangle(0, 0, w, h),
                            0, 0, image.Bitmap.Width, image.Bitmap.Height, GraphicsUnit.Pixel, attrs);
                    }
                }
                Push(bmp, x, y);
            }
            SetHidden(false);
        }

        public void SetHidden(bool hidden)
        {
            if (hidden == m_hidden || !IsHandleCreated) { m_hidden = hidden; return; }
            m_hidden = hidden;
            Native.ShowWindow(Handle, hidden ? 0 /*SW_HIDE*/ : 4 /*SW_SHOWNOACTIVATE*/);
        }

        /// <summary>Moves the overlay so the hotspot follows the pointer, without re-rendering.</summary>
        public void MoveTo(int x, int y)
        {
            if (!IsHandleCreated) return;
            const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOREDRAW = 0x8;
            Native.SetWindowPos(Handle, IntPtr.Zero, (int)Math.Round(x - m_hotX), (int)Math.Round(y - m_hotY), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOREDRAW);
        }

        void Push(Bitmap bmp, int x, int y)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0)); // premultiplied 32bpp DIB
            IntPtr old = Native.SelectObject(memDc, hBitmap);
            try
            {
                var size = new Native.SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new Native.POINT();
                var dst = new Native.POINT { X = (int)Math.Round(x - m_hotX), Y = (int)Math.Round(y - m_hotY) };
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA,
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
