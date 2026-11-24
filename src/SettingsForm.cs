/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later
*/

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ShakeCursor
{
    /// <summary>Settings window, modelled on KDE's System Settings › Accessibility › Shake Cursor page.</summary>
    sealed class SettingsForm : Form
    {
        public Config Result;

        readonly CheckBox m_enabled, m_autostart;
        readonly TrackBar m_sensitivity, m_magnification, m_over, m_max, m_interval;
        readonly ComboBox m_style;
        readonly CursorStyle[] m_styleValues;

        public CursorStyle ChosenStyle
        {
            get { return m_styleValues[Math.Max(0, m_style.SelectedIndex)]; }
        }

        public bool ChosenAutostart { get { return m_autostart.Checked; } }

        public SettingsForm(Config config)
        {
            Text = "Shake Cursor settings";
            Icon = AppInfo.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(14);
            ShowInTaskbar = true;

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            m_enabled = new CheckBox { Text = "Shake the mouse to make the pointer bigger", AutoSize = true, Checked = config.Enabled };
            grid.Controls.Add(m_enabled, 0, 0);
            grid.SetColumnSpan(m_enabled, 3);

            // Sensitivity: KWin's value is the shake factor (lower = triggers more easily). Show it as "easy ... hard".
            m_sensitivity = AddSlider(grid, 1, "Shake needed", 0, 12, (int)Math.Round((config.Sensitivity - 2) * 2),
                v => v <= 3 ? "Light" : v <= 7 ? "Normal" : "Strong");
            m_magnification = AddSlider(grid, 2, "Size on first shake", 3, 12, (int)Math.Round(config.Magnification * 2),
                v => (v / 2.0).ToString("0.#", CultureInfo.CurrentCulture) + "×");
            m_over = AddSlider(grid, 3, "Grows more per shake", 0, 8, (int)Math.Round(config.OverMagnification * 4),
                v => "+" + (v / 4.0).ToString("0.##", CultureInfo.CurrentCulture) + "×");
            m_max = AddSlider(grid, 4, "Largest size", 2, 15, (int)Math.Round(config.MaxMagnification),
                v => v + "×");
            m_interval = AddSlider(grid, 5, "Shake time window", 5, 20, (int)(config.TimeInterval / 100),
                v => (v / 10.0).ToString("0.0", CultureInfo.CurrentCulture) + " s");

            grid.Controls.Add(new Label { Text = "Mouse pointers", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 12, 12, 3) }, 0, 6);
            m_style = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(3, 12, 3, 3) };
            CursorStyle current = CursorThemes.Current;
            if (CursorThemes.HasBackup && current != CursorStyle.Other)
            {
                m_styleValues = new[] { CursorStyle.Breeze, CursorStyle.WindowsDefault, CursorStyle.Previous };
                m_style.Items.AddRange(new object[] { "KDE Breeze", "Windows default", "My previous pointers (" + CursorThemes.BackupName + ")" });
            }
            else
            {
                m_styleValues = new[] { CursorStyle.Breeze, CursorStyle.WindowsDefault, CursorStyle.Other };
                m_style.Items.AddRange(new object[] { "KDE Breeze", "Windows default", "Keep my current pointers" });
            }
            m_style.SelectedIndex = current == CursorStyle.Breeze ? 0 : current == CursorStyle.WindowsDefault ? 1 : 2;
            grid.Controls.Add(m_style, 1, 6);
            grid.SetColumnSpan(m_style, 2);

            m_autostart = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = Installer.Autostart, Margin = new Padding(3, 10, 3, 3) };
            grid.Controls.Add(m_autostart, 0, 7);
            grid.SetColumnSpan(m_autostart, 3);

            var hint = new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3),
                Text = "Tip: changes apply when you press Save - then shake the mouse to try them.",
            };
            grid.Controls.Add(hint, 0, 8);
            grid.SetColumnSpan(hint, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(85, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(85, 0) };
            var defaults = new Button { Text = "Defaults", AutoSize = true, MinimumSize = new Size(85, 0), Margin = new Padding(3, 3, 60, 3) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            buttons.Controls.Add(defaults);
            grid.Controls.Add(buttons, 0, 9);
            grid.SetColumnSpan(buttons, 3);

            Controls.Add(grid);
            AcceptButton = save;
            CancelButton = cancel;

            defaults.Click += delegate
            {
                var d = new Config();
                m_enabled.Checked = true;
                m_sensitivity.Value = (int)((d.Sensitivity - 2) * 2);
                m_magnification.Value = (int)(d.Magnification * 2);
                m_over.Value = (int)(d.OverMagnification * 4);
                m_max.Value = (int)d.MaxMagnification;
                m_interval.Value = (int)(d.TimeInterval / 100);
            };

            save.Click += delegate
            {
                Result = config.Clone();
                Result.Enabled = m_enabled.Checked;
                Result.Sensitivity = 2 + m_sensitivity.Value / 2.0;
                Result.Magnification = m_magnification.Value / 2.0;
                Result.OverMagnification = m_over.Value / 4.0;
                Result.MaxMagnification = Math.Max(m_max.Value, Result.Magnification);
                Result.TimeInterval = m_interval.Value * 100;
            };
        }

        static TrackBar AddSlider(TableLayoutPanel grid, int row, string label, int min, int max, int value, Func<int, string> format)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 12, 3) }, 0, row);
            var bar = new TrackBar
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                TickStyle = TickStyle.None, Width = 240, AutoSize = true,
            };
            var text = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = format(bar.Value) };
            bar.ValueChanged += delegate { text.Text = format(bar.Value); };
            grid.Controls.Add(bar, 1, row);
            grid.Controls.Add(text, 2, row);
            return bar;
        }
    }
}
