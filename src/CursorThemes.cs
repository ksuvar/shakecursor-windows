/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ShakeCursor
{
    enum CursorStyle { Breeze, WindowsDefault, Previous, Other }

    /// <summary>
    /// Switches the Windows cursor scheme between the bundled KDE Breeze cursors, the Windows default,
    /// and whatever the user had before Shake Cursor touched it.
    /// </summary>
    static class CursorThemes
    {
        public const string BreezeSchemeName = "KDE Breeze (Shake Cursor)";
        const string CursorsKey = @"Control Panel\Cursors";
        const string BackupKey = @"Software\ShakeCursor\PreviousCursors";

        static readonly string[,] BreezeFiles =
        {
            { "Arrow", "default.cur" }, { "Help", "help.cur" }, { "AppStarting", "progress.ani" },
            { "Wait", "wait.ani" }, { "Crosshair", "crosshair.cur" }, { "IBeam", "text.cur" },
            { "NWPen", "pencil.cur" }, { "No", "not-allowed.cur" }, { "SizeNS", "size_ver.cur" },
            { "SizeWE", "size_hor.cur" }, { "SizeNWSE", "size_fdiag.cur" }, { "SizeNESW", "size_bdiag.cur" },
            { "SizeAll", "all-scroll.cur" }, { "UpArrow", "center_ptr.cur" }, { "Hand", "pointer.cur" },
            // Breeze has no pin/person pointers; borrow Windows' own (absolute %SystemRoot% path, see Apply).
            { "Pin", "%SystemRoot%\\cursors\\aero_pin.cur" }, { "Person", "%SystemRoot%\\cursors\\aero_person.cur" },
        };

        static readonly string[,] WindowsFiles =
        {
            { "Arrow", "aero_arrow.cur" }, { "Help", "aero_helpsel.cur" }, { "AppStarting", "aero_working.ani" },
            { "Wait", "aero_busy.ani" }, { "Crosshair", "" }, { "IBeam", "" },
            { "NWPen", "aero_pen.cur" }, { "No", "aero_unavail.cur" }, { "SizeNS", "aero_ns.cur" },
            { "SizeWE", "aero_ew.cur" }, { "SizeNWSE", "aero_nwse.cur" }, { "SizeNESW", "aero_nesw.cur" },
            { "SizeAll", "aero_move.cur" }, { "UpArrow", "aero_up.cur" }, { "Hand", "aero_link.cur" },
            { "Pin", "aero_pin.cur" }, { "Person", "aero_person.cur" },
        };

        public static string BreezeDir
        {
            get { return Path.Combine(Installer.InstallDir, "cursors", "Breeze"); }
        }

        public static CursorStyle Current
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CursorsKey))
                {
                    string name = k == null ? null : k.GetValue("") as string;
                    if (name == BreezeSchemeName) return CursorStyle.Breeze;
                    if (name == "Windows Default") return CursorStyle.WindowsDefault;
                    return CursorStyle.Other;
                }
            }
        }

        public static bool HasBackup
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKey))
                    return k != null && k.ValueCount > 0;
            }
        }

        /// <summary>Name of the scheme saved before Shake Cursor first changed the cursors.</summary>
        public static string BackupName
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKey))
                {
                    string n = k == null ? null : k.GetValue("") as string;
                    return string.IsNullOrEmpty(n) ? "custom" : n;
                }
            }
        }

        public static void Apply(CursorStyle style)
        {
            if (style == CursorStyle.Other) return;
            if (style == CursorStyle.Previous) { RestoreBackup(); return; }

            Backup();
            bool breeze = style == CursorStyle.Breeze;
            if (breeze) ExtractBreeze();
            string dir = breeze ? BreezeDir : @"%SystemRoot%\cursors";
            string[,] files = breeze ? BreezeFiles : WindowsFiles;

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CursorsKey))
            {
                var parts = new List<string>();
                for (int i = 0; i < files.GetLength(0); i++)
                {
                    string f = files[i, 1];
                    string value = f.Length == 0 ? "" : f.StartsWith("%") ? f : Path.Combine(dir, f);
                    k.SetValue(files[i, 0], value, RegistryValueKind.ExpandString);
                }
                k.SetValue("", breeze ? BreezeSchemeName : "Windows Default");
                k.SetValue("Scheme Source", breeze ? 1 : 2, RegistryValueKind.DWord);

                if (breeze)
                {
                    // List it in Mouse Properties > Pointers too, in the order that dialog expects.
                    string[] order = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
                                       "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };
                    foreach (string n in order) parts.Add(k.GetValue(n) as string ?? "");
                    using (RegistryKey s = Registry.CurrentUser.CreateSubKey(CursorsKey + @"\Schemes"))
                        s.SetValue(BreezeSchemeName, string.Join(",", parts.ToArray()));
                }
            }
            Reload();
        }

        /// <summary>Puts back the cursors the user had before installing, and forgets the Breeze scheme.</summary>
        public static void RestoreBackup()
        {
            using (RegistryKey b = Registry.CurrentUser.OpenSubKey(BackupKey))
            {
                if (b == null || b.ValueCount == 0) return;
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CursorsKey))
                {
                    foreach (string n in b.GetValueNames())
                        k.SetValue(n, b.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), b.GetValueKind(n));
                }
            }
            Reload();
        }

        public static void RemoveTraces()
        {
            using (RegistryKey s = Registry.CurrentUser.OpenSubKey(CursorsKey + @"\Schemes", true))
                if (s != null) s.DeleteValue(BreezeSchemeName, false);
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ShakeCursor"); } catch (ArgumentException) { }
        }

        static void Backup()
        {
            if (HasBackup) return; // keep the very first state
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CursorsKey))
            using (RegistryKey b = Registry.CurrentUser.CreateSubKey(BackupKey))
            {
                if (k == null) return;
                foreach (string n in k.GetValueNames())
                {
                    if (n == "CursorBaseSize") continue; // the pointer size setting is not part of a scheme
                    b.SetValue(n, k.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), k.GetValueKind(n));
                }
            }
        }

        public static void ExtractBreeze()
        {
            Directory.CreateDirectory(BreezeDir);
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("Breeze.")) continue;
                string target = Path.Combine(BreezeDir, res.Substring("Breeze.".Length));
                using (Stream src = asm.GetManifestResourceStream(res))
                using (FileStream dst = File.Create(target))
                    src.CopyTo(dst);
            }
        }

        static void Reload()
        {
            Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }
    }
}
