/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ShakeCursor
{
    /// <summary>
    /// High-resolution images of every cursor shape in the user's current scheme, whatever scheme that is.
    ///
    /// KWin always magnifies the arrow. On Windows the enlarged pointer follows the shape that is actually
    /// showing (arrow, hand, text beam, resize arrows, ...). Shapes are recognised by their shared system
    /// handle, which stays the same while the real cursors are hidden, so the overlay keeps tracking the
    /// shape even though the real pointer is invisible. Cursors set by apps themselves are copied from
    /// their handle.
    /// </summary>
    sealed class CursorCache : IDisposable
    {
        public struct Shape
        {
            public uint Id;           // OCR_* id
            public string Name;       // value name under HKCU\Control Panel\Cursors
            public Shape(uint id, string name) { Id = id; Name = name; }
        }

        public static readonly Shape[] Shapes =
        {
            new Shape(32512, "Arrow"), new Shape(32513, "IBeam"), new Shape(32514, "Wait"),
            new Shape(32515, "Crosshair"), new Shape(32516, "UpArrow"), new Shape(32631, "NWPen"),
            new Shape(32642, "SizeNWSE"), new Shape(32643, "SizeNESW"), new Shape(32644, "SizeWE"),
            new Shape(32645, "SizeNS"), new Shape(32646, "SizeAll"), new Shape(32648, "No"),
            new Shape(32649, "Hand"), new Shape(32650, "AppStarting"), new Shape(32651, "Help"),
            new Shape(32671, "Pin"), new Shape(32672, "Person"),
        };

        // Extra slots that are hidden too but have no entry in the scheme (OCR_SIZE / OCR_ICON).
        static readonly uint[] LegacyIds = { 32640, 32641 };

        readonly Dictionary<IntPtr, CursorImage> m_byHandle = new Dictionary<IntPtr, CursorImage>();
        readonly Dictionary<IntPtr, CursorImage> m_custom = new Dictionary<IntPtr, CursorImage>();
        string m_snapshot;

        public CursorImage Arrow { get; private set; }

        public static IEnumerable<uint> AllIds
        {
            get
            {
                foreach (Shape s in Shapes) yield return s.Id;
                foreach (uint id in LegacyIds) yield return id;
            }
        }

        /// <summary>Reloads all shapes if the scheme changed. Must run while the real cursors are visible.</summary>
        public void Refresh()
        {
            string snapshot = Snapshot();
            if (snapshot == m_snapshot && Arrow != null) return;
            Clear();
            m_snapshot = snapshot;

            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                foreach (Shape s in Shapes)
                {
                    CursorImage img = null;
                    string file = k == null ? null : k.GetValue(s.Name) as string;
                    if (!string.IsNullOrEmpty(file))
                    {
                        file = Environment.ExpandEnvironmentVariables(file);
                        if (File.Exists(file)) img = CursorImage.FromFile(file);
                    }
                    if (img == null) img = CursorImage.FromSystem(s.Id); // scheme uses Windows' built-in shape
                    if (img == null) continue;

                    IntPtr shared = Native.LoadCursor(IntPtr.Zero, new IntPtr(s.Id));
                    if (shared != IntPtr.Zero && !m_byHandle.ContainsKey(shared)) m_byHandle[shared] = img;
                    if (s.Id == 32512) Arrow = img;
                }
            }
        }

        /// <summary>Image for the cursor handle that is currently showing.</summary>
        public CursorImage Lookup(IntPtr hCursor)
        {
            CursorImage img;
            if (m_byHandle.TryGetValue(hCursor, out img)) return img;
            if (m_custom.TryGetValue(hCursor, out img)) return img;

            // A cursor an app made itself: copy it at its native size.
            if (m_custom.Count > 32) ClearCustom();
            img = CursorImage.FromHandle(hCursor);
            if (img != null) m_custom[hCursor] = img;
            return img ?? Arrow;
        }

        public void ClearCustom()
        {
            foreach (CursorImage img in m_custom.Values) img.Dispose();
            m_custom.Clear();
        }

        static string Snapshot()
        {
            var sb = new StringBuilder();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                if (k != null)
                    foreach (string n in k.GetValueNames())
                        sb.Append(n).Append('=').Append(k.GetValue(n)).Append(';');
            }
            return sb.ToString();
        }

        void Clear()
        {
            var seen = new HashSet<CursorImage>();
            foreach (CursorImage img in m_byHandle.Values) if (seen.Add(img)) img.Dispose();
            m_byHandle.Clear();
            ClearCustom();
            Arrow = null;
        }

        public void Dispose() { Clear(); }
    }
}
