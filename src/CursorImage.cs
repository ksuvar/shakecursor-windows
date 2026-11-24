/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShakeCursor
{
    /// <summary>A cursor converted to a straight-alpha ARGB bitmap plus its hotspot.</summary>
    sealed class CursorImage : IDisposable
    {
        public Bitmap Bitmap;
        public PointF Hotspot; // in Bitmap pixels

        public const int PrefetchSize = 256; // largest size a .cur file can hold

        /// <summary>Loads a .cur/.ani file at high resolution (first frame for animated cursors).</summary>
        public static CursorImage FromFile(string path)
        {
            IntPtr h = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, PrefetchSize, PrefetchSize, Native.LR_LOADFROMFILE);
            if (h == IntPtr.Zero) return null;
            try { return FromHandle(h); }
            finally { Native.DestroyCursor(h); }
        }

        // OCR_* id -> cursor resource in user32.dll (what Windows shows when a scheme entry is empty).
        static readonly uint[,] User32Resources =
        {
            { 32512, 100 }, { 32513, 101 }, { 32514, 102 }, { 32515, 103 }, { 32516, 104 }, { 32642, 105 },
            { 32643, 106 }, { 32644, 107 }, { 32645, 108 }, { 32646, 109 }, { 32648, 110 }, { 32650, 111 },
            { 32651, 112 }, { 32631, 113 }, { 32649, 114 }, { 32671, 117 }, { 32672, 118 },
        };
        static IntPtr s_user32;

        /// <summary>Loads one of Windows' built-in cursors (OCR_* id) at high resolution.</summary>
        public static CursorImage FromSystem(uint ocrId)
        {
            int res = 0;
            for (int i = 0; i < User32Resources.GetLength(0); i++)
                if (User32Resources[i, 0] == ocrId) res = (int)User32Resources[i, 1];
            if (res == 0) return null;
            if (s_user32 == IntPtr.Zero) s_user32 = Native.LoadLibraryEx("user32.dll", IntPtr.Zero, 2 /*AS_DATAFILE*/);
            IntPtr h = Native.LoadImage(s_user32, new IntPtr(res), Native.IMAGE_CURSOR, PrefetchSize, PrefetchSize, 0);
            if (h == IntPtr.Zero) return null;
            try { return FromHandle(h); }
            finally { Native.DestroyCursor(h); }
        }

        /// <summary>Copies the pixels of any cursor handle (the handle is not destroyed).</summary>
        public static CursorImage FromHandle(IntPtr hCursor)
        {
            Native.ICONINFO info;
            if (!Native.GetIconInfo(hCursor, out info)) return null;
            try
            {
                int w, h;
                byte[] px;
                bool[] inverted = null;
                if (info.hbmColor != IntPtr.Zero)
                {
                    px = ReadBits(info.hbmColor, out w, out h);
                    bool hasAlpha = false;
                    for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { hasAlpha = true; break; }
                    if (!hasAlpha)
                    {
                        // Old-style colour cursor: AND mask decides transparency; colour under a set mask bit inverts.
                        int mw, mh;
                        byte[] mask = ReadBits(info.hbmMask, out mw, out mh);
                        inverted = new bool[w * h];
                        for (int i = 0; i < px.Length; i += 4)
                        {
                            bool and = i < mask.Length && mask[i] != 0;
                            bool xor = px[i] != 0 || px[i + 1] != 0 || px[i + 2] != 0;
                            if (and && xor) { inverted[i / 4] = true; px[i] = px[i + 1] = px[i + 2] = 0; }
                            px[i + 3] = (byte)(and && !xor ? 0 : 255);
                        }
                    }
                }
                else
                {
                    // Monochrome cursor: the mask bitmap holds the AND plane on top and the XOR plane below.
                    int mw, mh;
                    byte[] mask = ReadBits(info.hbmMask, out mw, out mh);
                    w = mw; h = mh / 2;
                    px = new byte[w * h * 4];
                    int half = w * h * 4;
                    inverted = new bool[w * h];
                    for (int i = 0; i < half; i += 4)
                    {
                        bool and = mask[i] != 0, xor = mask[i + half] != 0;
                        byte v = (byte)(!and && xor ? 255 : 0);       // white where AND=0,XOR=1
                        byte a = (byte)(and && !xor ? 0 : 255);       // transparent where AND=1,XOR=0
                        px[i] = px[i + 1] = px[i + 2] = v; px[i + 3] = a; // inverted pixels drawn black...
                        inverted[i / 4] = and && xor;
                    }
                }

                // ...with a white outline, so an "invert" cursor (like the text beam) stays visible on dark backgrounds.
                if (inverted != null)
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int p = (y * w + x) * 4;
                            if (px[p + 3] != 0) continue;
                            bool near = false;
                            for (int dy = -1; dy <= 1 && !near; dy++)
                                for (int dx = -1; dx <= 1 && !near; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    near = nx >= 0 && ny >= 0 && nx < w && ny < h && inverted[ny * w + nx];
                                }
                            if (near) { px[p] = px[p + 1] = px[p + 2] = 255; px[p + 3] = 255; }
                        }
                }

                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++)
                    Marshal.Copy(px, y * w * 4, d.Scan0 + y * d.Stride, w * 4);
                bmp.UnlockBits(d);
                return new CursorImage { Bitmap = bmp, Hotspot = new PointF(info.xHotspot, info.yHotspot) };
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) Native.DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) Native.DeleteObject(info.hbmMask);
            }
        }

        /// <summary>Width in pixels of a cursor handle as Windows draws it.</summary>
        public static int WidthOf(IntPtr hCursor)
        {
            Native.ICONINFO info;
            if (!Native.GetIconInfo(hCursor, out info)) return 0;
            try
            {
                Native.BITMAP b;
                Native.GetObject(info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask, Marshal.SizeOf(typeof(Native.BITMAP)), out b);
                return b.bmWidth;
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) Native.DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) Native.DeleteObject(info.hbmMask);
            }
        }

        static byte[] ReadBits(IntPtr hbm, out int width, out int height)
        {
            Native.BITMAP b;
            Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.BITMAP)), out b);
            width = b.bmWidth; height = b.bmHeight;
            var bi = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = b.bmWidth, biHeight = -b.bmHeight, biPlanes = 1, biBitCount = 32,
            };
            byte[] bits = new byte[b.bmWidth * b.bmHeight * 4];
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            Native.GetDIBits(dc, hbm, 0, (uint)b.bmHeight, bits, ref bi, 0);
            Native.ReleaseDC(IntPtr.Zero, dc);
            return bits;
        }

        public void Dispose()
        {
            if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
        }
    }
}
