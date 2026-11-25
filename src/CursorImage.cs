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

        // Animated cursors only: the image of each step (a frame may repeat) and how long it shows.
        public CursorImage[] Frames;
        public int[] StepMs;

        public const int PrefetchSize = 256; // largest size a .cur file can hold

        /// <summary>Loads a .cur/.ani file at high resolution, with every frame for animated cursors.</summary>
        public static CursorImage FromFile(string path)
        {
            IntPtr h = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, PrefetchSize, PrefetchSize, Native.LR_LOADFROMFILE);
            if (h == IntPtr.Zero) return null;
            CursorImage img;
            try { img = FromHandle(h); }
            finally { Native.DestroyCursor(h); }
            if (img != null)
                try { img.LoadFrames(System.IO.File.ReadAllBytes(path)); } catch { /* fall back to the still first frame */ }
            return img;
        }

        /// <summary>Reads the frames of an .ani (RIFF "ACON") file; does nothing for other data.</summary>
        void LoadFrames(byte[] b)
        {
            if (b.Length < 12 || Tag(b, 0) != "RIFF" || Tag(b, 8) != "ACON") return;
            var icons = new System.Collections.Generic.List<CursorImage>();
            int[] rate = null, seq = null;
            int steps = 0, jiffies = 10;
            for (int pos = 12; pos + 8 <= b.Length;)
            {
                string id = Tag(b, pos);
                int len = BitConverter.ToInt32(b, pos + 4), data = pos + 8;
                if (len < 0 || data + len > b.Length) break;
                if (id == "anih" && len >= 36) { steps = BitConverter.ToInt32(b, data + 8); jiffies = BitConverter.ToInt32(b, data + 28); }
                else if (id == "rate") rate = Ints(b, data, len / 4);
                else if (id == "seq ") seq = Ints(b, data, len / 4);
                else if (id == "LIST" && len >= 4 && Tag(b, data) == "fram")
                    for (int p = data + 4; p + 8 <= data + len;)
                    {
                        int l = BitConverter.ToInt32(b, p + 4);
                        if (l < 0 || p + 8 + l > data + len) break;
                        if (Tag(b, p) == "icon") icons.Add(FromIconFile(b, p + 8));
                        p += 8 + l + (l & 1);
                    }
                pos = data + len + (len & 1); // chunks are word aligned
            }
            if (icons.Count < 2 || icons.Contains(null)) { foreach (CursorImage i in icons) if (i != null) i.Dispose(); return; }

            if (steps <= 0) steps = seq != null ? seq.Length : icons.Count;
            Frames = new CursorImage[steps];
            StepMs = new int[steps];
            for (int s = 0; s < steps; s++)
            {
                int f = seq != null && s < seq.Length ? seq[s] : s;
                Frames[s] = icons[Math.Max(0, Math.Min(icons.Count - 1, f))];
                int j = rate != null && s < rate.Length ? rate[s] : jiffies;
                StepMs[s] = Math.Max(16, j * 1000 / 60); // rates are in jiffies (1/60 s)
            }
        }

        /// <summary>Largest image of a .cur/.ico file held in <paramref name="b"/> at <paramref name="at"/>.</summary>
        static CursorImage FromIconFile(byte[] b, int at)
        {
            int count = BitConverter.ToUInt16(b, at + 4), best = -1, bestW = 0;
            for (int i = 0; i < count; i++)
            {
                int w = b[at + 6 + i * 16]; if (w == 0) w = 256;
                if (w > bestW) { bestW = w; best = at + 6 + i * 16; }
            }
            if (best < 0) return null;
            bool isCursor = BitConverter.ToUInt16(b, at + 2) == 2;
            int size = BitConverter.ToInt32(b, best + 8), offset = at + BitConverter.ToInt32(b, best + 12);
            int h = b[best + 1]; if (h == 0) h = 256;

            // Cursor resource data = hotspot (two WORDs) + the image (DIB or PNG).
            var res = new byte[4 + size];
            if (isCursor) Array.Copy(b, best + 4, res, 0, 4);
            Array.Copy(b, offset, res, 4, size);
            IntPtr hc = Native.CreateIconFromResourceEx(res, res.Length, false, 0x00030000, bestW, h, 0);
            if (hc == IntPtr.Zero) return null;
            try { return FromHandle(hc); }
            finally { Native.DestroyCursor(hc); }
        }

        static string Tag(byte[] b, int at) { return System.Text.Encoding.ASCII.GetString(b, at, 4); }

        static int[] Ints(byte[] b, int at, int n)
        {
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = BitConverter.ToInt32(b, at + i * 4);
            return r;
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
            CursorImage img;
            try { img = FromHandle(h); }
            finally { Native.DestroyCursor(h); }

            // Busy shapes are RT_ANICURSOR (21) resources holding the same RIFF data as an .ani file.
            IntPtr info = Native.FindResource(s_user32, new IntPtr(res), new IntPtr(21));
            if (img != null && info != IntPtr.Zero)
            {
                var bytes = new byte[Native.SizeofResource(s_user32, info)];
                Marshal.Copy(Native.LockResource(Native.LoadResource(s_user32, info)), bytes, 0, bytes.Length);
                try { img.LoadFrames(bytes); } catch { }
            }
            return img;
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
            if (Frames != null) foreach (CursorImage f in Frames) f.Dispose(); // repeats are harmless: Dispose is idempotent
            Frames = null;
        }
    }
}
