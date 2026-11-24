/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShakeCursor
{
    /// <summary>Per-user install (no admin): %LOCALAPPDATA%\Programs\ShakeCursor, Start menu, autostart, Apps &amp; features.</summary>
    static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShakeCursor";
        public const string RunName = "ShakeCursor";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ShakeCursor"); }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, "ShakeCursor.exe"); } }

        static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Shake Cursor.lnk"); }
        }

        public static bool IsRunningInstalled
        {
            get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase); }
        }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        // ---- autostart --------------------------------------------------------------------------------

        public static bool Autostart
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        // ---- install ----------------------------------------------------------------------------------

        public sealed class Options
        {
            public bool UseBreeze = true;
            public bool Autostart = true;
            public bool StartMenu = true;
        }

        public static void Install(Options o)
        {
            StopRunningInstances();

            Directory.CreateDirectory(InstallDir);
            if (!IsRunningInstalled)
                File.Copy(Application.ExecutablePath, InstalledExe, true);

            // Clean up the pre-1.0 location if it exists.
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null && (k.GetValue(RunName) as string ?? "").IndexOf(@"\Programs\ShakeCursor\", StringComparison.OrdinalIgnoreCase) < 0)
                    k.DeleteValue(RunName, false);

            if (o.UseBreeze) CursorThemes.Apply(CursorStyle.Breeze);
            else CursorThemes.ExtractBreeze(); // available later from the tray menu

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (o.Autostart) k.SetValue(RunName, "\"" + InstalledExe + "\"");
                else k.DeleteValue(RunName, false);
            }

            if (o.StartMenu) CreateShortcut(ShortcutPath, InstalledExe);
            else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Shake Cursor");
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", AppInfo.Author);
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --silent");
                k.SetValue("URLInfoAbout", AppInfo.Url);
                k.SetValue("HelpLink", AppInfo.Url);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024) + 100, RegistryValueKind.DWord);
            }
        }

        public static void Uninstall(bool silent)
        {
            if (!silent && MessageBox.Show("Remove Shake Cursor?\n\nIf it changed your mouse pointers to KDE Breeze, your previous pointers are put back.",
                    "Uninstall Shake Cursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            StopRunningInstances();

            if (CursorThemes.Current == CursorStyle.Breeze)
            {
                if (CursorThemes.HasBackup) CursorThemes.RestoreBackup();
                else CursorThemes.Apply(CursorStyle.WindowsDefault);
            }
            CursorThemes.RemoveTraces();
            Native.RestoreSystemCursors();

            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(RunName, false);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey); } catch (ArgumentException) { }
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
            try { if (Directory.Exists(Config.Dir)) Directory.Delete(Config.Dir, true); } catch { }

            // The exe can't delete itself while running; let cmd do it once we've exited.
            if (Directory.Exists(InstallDir))
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"")
                { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
                Process.Start(psi);
            }

            if (!silent)
                MessageBox.Show("Shake Cursor has been removed.", "Shake Cursor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Asks other copies to quit cleanly (so they restore the real cursor), then makes sure they are gone.</summary>
        public static void StopRunningInstances()
        {
            try
            {
                using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, Program.QuitEventName))
                {
                    quit.Set();
                    int self = Process.GetCurrentProcess().Id;
                    foreach (Process p in Process.GetProcessesByName("ShakeCursor"))
                    {
                        if (p.Id == self) continue;
                        if (!p.WaitForExit(3000)) { try { p.Kill(); p.WaitForExit(2000); } catch { } }
                    }
                    quit.Reset();
                }
            }
            catch { }
            Native.RestoreSystemCursors();
        }

        static void CreateShortcut(string lnk, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Shake the mouse to find the pointer" });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    /// <summary>The window shown when someone double-clicks the downloaded exe.</summary>
    sealed class InstallForm : Form
    {
        readonly CheckBox m_breeze, m_autostart, m_startMenu;
        public Installer.Options Result;
        public bool RunPortable;

        public InstallForm()
        {
            bool update = Installer.IsInstalled;
            Text = update ? "Update Shake Cursor" : "Install Shake Cursor";
            Icon = AppInfo.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(16);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };

            var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
            header.Controls.Add(new PictureBox { Image = AppInfo.Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(40, 40), Margin = new Padding(0, 0, 10, 0) });
            var title = new Label { AutoSize = true, Text = "Shake Cursor " + AppInfo.Version, Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
            header.Controls.Add(title);
            root.Controls.Add(header);

            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 12),
                Text = "KDE Plasma's \"shake to find the pointer\" for Windows. Shake the mouse quickly and the pointer grows; " +
                       "stop and it shrinks back. It runs quietly in the notification area (^) of the taskbar.",
            });

            m_breeze = new CheckBox
            {
                AutoSize = true, Checked = CursorThemes.Current == CursorStyle.Breeze || !update,
                Text = "Use the KDE Breeze mouse pointers",
            };
            root.Controls.Add(m_breeze);
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 8),
                Text = "Untick to keep your current pointers. Shaking works with any pointer set, and you can switch later from the tray menu.",
            });
            m_autostart = new CheckBox { AutoSize = true, Checked = !update || Installer.Autostart, Text = "Start with Windows" };
            root.Controls.Add(m_autostart);
            m_startMenu = new CheckBox { AutoSize = true, Checked = true, Text = "Add to the Start menu" };
            root.Controls.Add(m_startMenu);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            var install = new Button { Text = update ? "Update" : "Install", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(90, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 0) };
            var portable = new LinkLabel { Text = "Just try it without installing", AutoSize = true, Margin = new Padding(0, 8, 24, 0) };
            portable.LinkClicked += delegate { RunPortable = true; DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(install);
            buttons.Controls.Add(portable);
            root.Controls.Add(buttons);

            AcceptButton = install;
            CancelButton = cancel;
            Controls.Add(root);

            install.Click += delegate
            {
                Result = new Installer.Options { UseBreeze = m_breeze.Checked, Autostart = m_autostart.Checked, StartMenu = m_startMenu.Checked };
            };
        }
    }
}
