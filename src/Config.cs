/*
    SPDX-FileCopyrightText: 2026 ksuvar

    SPDX-License-Identifier: GPL-2.0-or-later

    Mirrors kwin/src/plugins/shakecursor/shakecursorconfig.kcfg (same keys, same defaults),
    plus a few Windows-only settings.
*/

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ShakeCursor
{
    sealed class Config
    {
        // KWin settings
        public long TimeInterval = 1000;      // ms of pointer history the detector looks at
        public double Sensitivity = 4;        // trail length / bounding diagonal needed to count as a shake
        public double Magnification = 3;      // size on the first shake
        public double OverMagnification = 1;  // added on every further shake

        // Windows-only
        public bool Enabled = true;
        public double MaxMagnification = 10;  // cap on how big repeated shaking can make the pointer
        public bool WelcomeShown = false;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShakeCursor"); }
        }

        public static string FilePath { get { return Path.Combine(Dir, "shakecursor.ini"); } }

        public Config Clone() { return (Config)MemberwiseClone(); }

        public static Config Load()
        {
            var c = new Config();
            if (!File.Exists(FilePath)) return c;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                double v;
                bool isNum = double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                bool b = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
                switch (key)
                {
                    case "TimeInterval": if (isNum) c.TimeInterval = (long)v; break;
                    case "Sensitivity": if (isNum) c.Sensitivity = v; break;
                    case "Magnification": if (isNum) c.Magnification = v; break;
                    case "OverMagnification": if (isNum) c.OverMagnification = v; break;
                    case "MaxMagnification": if (isNum) c.MaxMagnification = v; break;
                    case "Enabled": c.Enabled = b; break;
                    case "WelcomeShown": c.WelcomeShown = b; break;
                }
            }
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("; Shake Cursor for Windows. The first four keys match KWin's shakecursorconfig.kcfg.");
            sb.AppendLine("[Effect-shakecursor]");
            sb.AppendLine("TimeInterval=" + TimeInterval.ToString(ci));
            sb.AppendLine("Sensitivity=" + Sensitivity.ToString(ci));
            sb.AppendLine("Magnification=" + Magnification.ToString(ci));
            sb.AppendLine("OverMagnification=" + OverMagnification.ToString(ci));
            sb.AppendLine("[Windows]");
            sb.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
            sb.AppendLine("MaxMagnification=" + MaxMagnification.ToString(ci));
            sb.AppendLine("WelcomeShown=" + (WelcomeShown ? "true" : "false"));
            File.WriteAllText(FilePath, sb.ToString());
        }
    }
}
