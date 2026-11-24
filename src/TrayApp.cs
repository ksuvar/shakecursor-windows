/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ShakeCursor
{
    /// <summary>Notification-area icon (in the hidden "^" overflow by default) and its menu.</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon m_tray = new NotifyIcon();
        readonly ContextMenuStrip m_menu = new ContextMenuStrip();
        readonly ToolStripMenuItem m_enabled, m_autostart, m_styleBreeze, m_styleWindows, m_stylePrevious, m_uninstall;
        readonly Control m_ui = new Control(); // for marshalling cross-process signals onto the UI thread
        readonly EventWaitHandle m_quitEvent, m_showSettingsEvent;
        readonly RegisteredWaitHandle m_quitWait, m_showWait;

        Config m_config;
        ShakeCursorEffect m_effect;
        SettingsForm m_settings;

        public TrayApp(bool welcome)
        {
            m_ui.CreateControl();
            m_config = Config.Load();

            m_enabled = new ToolStripMenuItem("Shake to find pointer", null, delegate { SetEnabled(!m_config.Enabled); });
            var settings = new ToolStripMenuItem("Settings...", null, delegate { ShowSettings(); });
            settings.Font = new Font(settings.Font, FontStyle.Bold);

            var style = new ToolStripMenuItem("Mouse pointers");
            m_styleBreeze = new ToolStripMenuItem("KDE Breeze", null, delegate { SetStyle(CursorStyle.Breeze); });
            m_styleWindows = new ToolStripMenuItem("Windows default", null, delegate { SetStyle(CursorStyle.WindowsDefault); });
            m_stylePrevious = new ToolStripMenuItem("My previous pointers", null, delegate { SetStyle(CursorStyle.Previous); });
            style.DropDownItems.AddRange(new ToolStripItem[] { m_styleBreeze, m_styleWindows, m_stylePrevious });

            m_autostart = new ToolStripMenuItem("Start with Windows", null, delegate { Installer.Autostart = !Installer.Autostart; });
            var about = new ToolStripMenuItem("About / GitHub", null, delegate { OpenUrl(AppInfo.Url); });
            m_uninstall = new ToolStripMenuItem("Uninstall...", null, delegate { Uninstall(); });
            var quit = new ToolStripMenuItem("Quit", null, delegate { ExitThread(); });

            var header = new ToolStripLabel("Shake Cursor " + AppInfo.Version) { ForeColor = SystemColors.GrayText };
            m_menu.Items.AddRange(new ToolStripItem[]
            {
                header, new ToolStripSeparator(),
                m_enabled, settings, style, m_autostart,
                new ToolStripSeparator(),
                about, m_uninstall, quit,
            });
            m_menu.Opening += delegate { RefreshMenu(); };

            m_tray.Icon = AppInfo.Icon;
            m_tray.Text = "Shake Cursor";
            m_tray.ContextMenuStrip = m_menu;
            m_tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            m_tray.BalloonTipClicked += delegate { ShowSettings(); };
            m_tray.Visible = true;

            // Other processes (installer, second launch) talk to us through named events.
            m_quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.QuitEventName);
            m_quitEvent.Reset();
            m_showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSettingsEventName);
            m_quitWait = ThreadPool.RegisterWaitForSingleObject(m_quitEvent,
                delegate { m_ui.BeginInvoke((MethodInvoker)ExitThread); }, null, Timeout.Infinite, true);
            m_showWait = ThreadPool.RegisterWaitForSingleObject(m_showSettingsEvent,
                delegate { m_ui.BeginInvoke((MethodInvoker)ShowSettings); }, null, Timeout.Infinite, false);

            ApplyConfig();

            if (welcome || !m_config.WelcomeShown)
            {
                m_tray.ShowBalloonTip(8000, "Shake Cursor is running",
                    "Shake the mouse quickly to make the pointer bigger. Click here or the icon in the taskbar's ^ area for settings.",
                    ToolTipIcon.Info);
                m_config.WelcomeShown = true;
                SaveConfig();
            }
        }

        void ApplyConfig()
        {
            if (m_config.Enabled && m_effect == null)
            {
                try { m_effect = new ShakeCursorEffect(m_config); }
                catch (Exception e)
                {
                    if (ShakeCursorEffect.Log != null) ShakeCursorEffect.Log.WriteLine("start failed: " + e);
                    m_tray.ShowBalloonTip(5000, "Shake Cursor", "Could not start: " + e.Message, ToolTipIcon.Error);
                }
            }
            else if (!m_config.Enabled && m_effect != null)
            {
                m_effect.Dispose();
                m_effect = null;
            }
            else if (m_effect != null)
            {
                m_effect.Reconfigure(m_config);
            }
            m_tray.Text = m_config.Enabled ? "Shake Cursor" : "Shake Cursor (paused)";
        }

        void SaveConfig()
        {
            try { m_config.Save(); } catch { }
        }

        void SetEnabled(bool enabled)
        {
            m_config.Enabled = enabled;
            SaveConfig();
            ApplyConfig();
        }

        void SetStyle(CursorStyle style)
        {
            if (m_effect != null) m_effect.Reset();
            try { CursorThemes.Apply(style); }
            catch (Exception e) { MessageBox.Show("Could not change the pointers:\n\n" + e.Message, "Shake Cursor"); }
        }

        void RefreshMenu()
        {
            m_enabled.Checked = m_config.Enabled;
            m_autostart.Checked = Installer.Autostart;
            CursorStyle s = CursorThemes.Current;
            m_styleBreeze.Checked = s == CursorStyle.Breeze;
            m_styleWindows.Checked = s == CursorStyle.WindowsDefault;
            m_stylePrevious.Visible = CursorThemes.HasBackup;
            m_stylePrevious.Text = "My previous pointers (" + CursorThemes.BackupName + ")";
            m_uninstall.Visible = Installer.IsRunningInstalled;
        }

        void ShowSettings()
        {
            if (m_settings != null) { m_settings.Activate(); return; }
            using (m_settings = new SettingsForm(m_config))
            {
                CursorStyle before = CursorThemes.Current;
                if (m_settings.ShowDialog() == DialogResult.OK && m_settings.Result != null)
                {
                    m_config = m_settings.Result;
                    SaveConfig();
                    ApplyConfig();
                    if (Installer.Autostart != m_settings.ChosenAutostart) Installer.Autostart = m_settings.ChosenAutostart;
                    CursorStyle chosen = m_settings.ChosenStyle;
                    if (chosen != CursorStyle.Other && (chosen != before || chosen == CursorStyle.Previous))
                        SetStyle(chosen);
                }
            }
            m_settings = null;
        }

        void Uninstall()
        {
            // Hand over to a fresh process so this one can exit and the folder can be deleted.
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall") { UseShellExecute = false });
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        protected override void ExitThreadCore()
        {
            if (m_effect != null) { m_effect.Dispose(); m_effect = null; }
            m_quitWait.Unregister(null);
            m_showWait.Unregister(null);
            m_tray.Visible = false;
            m_tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
