/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShakeCursor
{
    static class Program
    {
        public const string QuitEventName = @"Local\ShakeCursor.Quit";
        public const string ShowSettingsEventName = @"Local\ShakeCursor.ShowSettings";
        const string MutexName = @"Local\ShakeCursor.Instance";

        static bool Has(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }

        [STAThread]
        static int Main(string[] args)
        {
            // Per-monitor v2: hook coordinates and the overlay both use physical pixels.
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Has(args, "--debug"))
            {
                var w = new System.IO.StreamWriter(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shakecursor.log"), false);
                w.AutoFlush = true;
                ShakeCursorEffect.Log = w;
            }

            bool silent = Has(args, "--silent");

            if (Has(args, "--uninstall"))
            {
                Installer.Uninstall(silent);
                return 0;
            }

            // Run from anywhere other than the install folder: act as the installer.
            if (!Installer.IsRunningInstalled && !Has(args, "--portable"))
            {
                Installer.Options options;
                if (silent || Has(args, "--install"))
                {
                    options = new Installer.Options
                    {
                        UseBreeze = !Has(args, "--keep-cursors"),
                        Autostart = !Has(args, "--no-autostart"),
                        StartMenu = !Has(args, "--no-start-menu"),
                    };
                }
                else
                {
                    using (var form = new InstallForm())
                    {
                        form.ShowDialog();
                        if (form.RunPortable) return RunTray(true);
                        if (form.Result == null) return 1;
                        options = form.Result;
                    }
                }

                try { Installer.Install(options); }
                catch (Exception e)
                {
                    if (!silent) MessageBox.Show("Installation failed:\n\n" + e.Message, "Shake Cursor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
                Process.Start(new ProcessStartInfo(Installer.InstalledExe, "--welcome") { UseShellExecute = false });
                return 0;
            }

            return RunTray(Has(args, "--welcome"));
        }

        static int RunTray(bool welcome)
        {
            // If a previous instance died while the pointer was hidden, bring it back.
            Native.RestoreSystemCursors();

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: open its settings instead of starting a second copy.
                    try { using (var ev = EventWaitHandle.OpenExisting(ShowSettingsEventName)) ev.Set(); } catch { }
                    return 0;
                }

                Application.ThreadException += delegate { Native.RestoreSystemCursors(); };
                AppDomain.CurrentDomain.UnhandledException += delegate { Native.RestoreSystemCursors(); };
                AppDomain.CurrentDomain.ProcessExit += delegate { Native.RestoreSystemCursors(); };
                SystemEvents.SessionEnding += delegate { Native.RestoreSystemCursors(); };

                Application.Run(new TrayApp(welcome));
                Native.RestoreSystemCursors();
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
