/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Shake Cursor")]
[assembly: AssemblyDescription("KDE Plasma's Shake Cursor effect for Windows")]
[assembly: AssemblyProduct("Shake Cursor")]
[assembly: AssemblyCompany("ksuvar")]
[assembly: AssemblyCopyright("GPL-2.0-or-later. Based on KWin's shakecursor by Vlad Zahorodnii.")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace ShakeCursor
{
    static class AppInfo
    {
        public const string Version = "1.1.1";
        public const string Author = "ksuvar";
        public const string Url = "https://github.com/ksuvar/shakecursor-windows";

        static Icon s_icon;
        public static Icon Icon
        {
            get
            {
                if (s_icon == null)
                {
                    try { s_icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    if (s_icon == null) s_icon = SystemIcons.Application;
                }
                return s_icon;
            }
        }
    }
}
