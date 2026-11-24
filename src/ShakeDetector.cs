/*
    SPDX-FileCopyrightText: 2023 Vlad Zahorodnii <vlad.zahorodnii@kde.org>
    SPDX-FileCopyrightText: 2026 ksuvar (Windows port)

    SPDX-License-Identifier: GPL-2.0-or-later

    Port of kwin/src/plugins/shakecursor/shakedetector.{h,cpp}
*/

using System;
using System.Collections.Generic;

namespace ShakeCursor
{
    /// <summary>
    /// The ShakeDetector type provides a way to detect pointer shake gestures.
    ///
    /// Shake gestures are detected by comparing the length of the trail of the cursor within past N milliseconds
    /// with the length of the diagonal of the bounding rectangle of the trail. If the trail is longer
    /// than the diagonal by certain preconfigured factor, it's assumed that the user shook the pointer.
    /// </summary>
    sealed class ShakeDetector
    {
        struct HistoryItem
        {
            public double X, Y;
            public long Timestamp; // milliseconds
        }

        readonly List<HistoryItem> m_history = new List<HistoryItem>();

        public long Interval = 1000;
        public double Sensitivity = 4;

        static bool SameSign(double a, double b)
        {
            const double tolerance = 1;
            // movements less than tolerance count as movements in any direction
            return (a >= -tolerance && b >= -tolerance) || (a <= tolerance && b <= tolerance);
        }

        public void Reset()
        {
            m_history.Clear();
        }

        public bool Update(double x, double y, long timestamp)
        {
            // Prune the old entries in the history.
            int it = 0;
            for (; it < m_history.Count; ++it)
            {
                if (timestamp - m_history[it].Timestamp < Interval)
                    break;
            }
            if (it != 0)
                m_history.RemoveRange(0, it);

            if (m_history.Count >= 2)
            {
                HistoryItem last = m_history[m_history.Count - 1];
                HistoryItem prev = m_history[m_history.Count - 2];
                if (SameSign(last.X - prev.X, x - last.X) && SameSign(last.Y - prev.Y, y - last.Y))
                {
                    m_history[m_history.Count - 1] = new HistoryItem { X = x, Y = y, Timestamp = timestamp };
                    return false;
                }
            }

            m_history.Add(new HistoryItem { X = x, Y = y, Timestamp = timestamp });

            double left = m_history[0].X;
            double top = m_history[0].Y;
            double right = m_history[0].X;
            double bottom = m_history[0].Y;
            double distance = 0;

            for (int i = 1; i < m_history.Count; ++i)
            {
                // Compute the length of the mouse path.
                double deltaX = m_history[i].X - m_history[i - 1].X;
                double deltaY = m_history[i].Y - m_history[i - 1].Y;
                distance += Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                // Compute the bounds of the mouse path.
                left = Math.Min(left, m_history[i].X);
                top = Math.Min(top, m_history[i].Y);
                right = Math.Max(right, m_history[i].X);
                bottom = Math.Max(bottom, m_history[i].Y);
            }

            double boundsWidth = right - left;
            double boundsHeight = bottom - top;
            double diagonal = Math.Sqrt(boundsWidth * boundsWidth + boundsHeight * boundsHeight);
            if (diagonal < 100)
                return false;

            double shakeFactor = distance / diagonal;
            if (shakeFactor > Sensitivity)
            {
                m_history.Clear();
                return true;
            }

            return false;
        }
    }
}
