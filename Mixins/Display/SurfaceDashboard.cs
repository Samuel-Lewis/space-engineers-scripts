using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        // Discovery and update scheduling belong to the caller. Any LCD or cockpit
        // surface can be supplied; elapsedSeconds drives the lamp blink and issue paging.
        public sealed class SurfaceDashboard
        {
            // A gauge with Fraction below zero renders as a plain text row.
            public struct Gauge
            {
                public string Label;
                public string Icon;
                public double Fraction;
                public double Threshold;
                public string Text;

                public Gauge(string label, string icon, double fraction, double threshold, string text)
                {
                    Label = label;
                    Icon = icon;
                    Fraction = fraction;
                    Threshold = threshold;
                    Text = text;
                }
            }

            const string Font = "Debug";
            const double PageSeconds = 5;
            readonly List<IMyTextSurface> surfaces = new List<IMyTextSurface>();
            readonly StringBuilder measurement = new StringBuilder();
            readonly List<string> wrapped = new List<string>();
            readonly Color background = new Color(8, 12, 18);
            readonly Color panel = new Color(16, 24, 34);
            readonly Color foreground = new Color(225, 234, 244);
            readonly Color muted = new Color(120, 138, 160);
            readonly Color track = new Color(30, 42, 56);
            readonly Color goodColour = new Color(90, 190, 230);
            readonly Color errorColour = new Color(255, 90, 70);

            public int SurfaceCount { get { return surfaces.Count; } }

            public void SetSurfaces(List<IMyTextSurface> selectedSurfaces)
            {
                bool unchanged = selectedSurfaces != null && surfaces.Count == selectedSurfaces.Count;
                for (int i = 0; unchanged && i < surfaces.Count; i++)
                    unchanged = surfaces[i] == selectedSurfaces[i];
                if (unchanged) return;
                surfaces.Clear();
                if (selectedSurfaces == null) return;
                for (int i = 0; i < selectedSurfaces.Count; i++)
                {
                    IMyTextSurface surface = selectedSurfaces[i];
                    if (surface == null || surfaces.Contains(surface)) continue;
                    surface.ContentType = ContentType.SCRIPT;
                    surface.Script = "";
                    surface.ScriptBackgroundColor = background;
                    surfaces.Add(surface);
                }
            }

            // blinkSeconds matches IMyLightingBlock.BlinkIntervalSeconds: 0 is solid,
            // otherwise the lamp is lit for the first half of each interval.
            public void Draw(string title, string state, Color lampColour, float blinkSeconds,
                string subtitle, List<Gauge> gauges, List<string> issues, double elapsedSeconds)
            {
                bool lit = blinkSeconds <= 0 || elapsedSeconds % blinkSeconds < blinkSeconds / 2;
                for (int i = 0; i < surfaces.Count; i++)
                    DrawSurface(surfaces[i], title, state, lampColour, lit, subtitle, gauges, issues, elapsedSeconds);
            }

            void DrawSurface(IMyTextSurface surface, string title, string state, Color lampColour,
                bool lit, string subtitle, List<Gauge> gauges, List<string> issues, double elapsedSeconds)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 16f * unit;
                float left = origin.X + padding;
                float right = origin.X + size.X - padding;
                float width = right - left;
                float scale = .7f * unit;
                float lineHeight = Math.Max(Measure(surface, "Ag", scale).Y, 22f * unit);
                float y = origin.Y + padding;
                int issueCount = issues == null ? 0 : issues.Count;

                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    Rect(frame, origin + size / 2f, size, background);

                    // Header
                    Text(frame, Fit(surface, (title ?? "").ToUpperInvariant(), width, scale * .8f),
                        new Vector2(left, y), scale * .8f, muted, TextAlignment.LEFT);
                    y += lineHeight * .8f + 8f * unit;

                    // Lamp and state
                    float lamp = 40f * unit;
                    Vector2 lampCentre = new Vector2(left + lamp / 2, y + lamp / 2);
                    Color lampDim = new Color(lampColour.R / 5, lampColour.G / 5, lampColour.B / 5);
                    if (lit)
                    {
                        Sprite(frame, "Circle", lampCentre, new Vector2(lamp * 1.9f, lamp * 1.9f),
                            new Color(lampColour.R, lampColour.G, lampColour.B, 28));
                        Sprite(frame, "Circle", lampCentre, new Vector2(lamp * 1.35f, lamp * 1.35f),
                            new Color(lampColour.R, lampColour.G, lampColour.B, 70));
                    }
                    Sprite(frame, "Circle", lampCentre, new Vector2(lamp, lamp), lit ? lampColour : lampDim);
                    Sprite(frame, "CircleHollow", lampCentre, new Vector2(lamp, lamp),
                        lit ? foreground : new Color(60, 70, 84));

                    float stateScale = scale * 1.7f;
                    float stateX = left + lamp + 16f * unit;
                    Vector2 stateSize = Measure(surface, state, stateScale);
                    Text(frame, Fit(surface, state, right - stateX, stateScale),
                        new Vector2(stateX, lampCentre.Y - stateSize.Y / 2), stateScale, foreground, TextAlignment.LEFT);
                    y += lamp + 8f * unit;

                    Text(frame, Fit(surface, subtitle, width, scale), new Vector2(left, y), scale, muted, TextAlignment.LEFT);
                    y += lineHeight + 10f * unit;

                    // Gauges
                    float bottom = origin.Y + size.Y - padding;
                    float rowHeight = lineHeight + 6f * unit;
                    int gaugeCount = gauges == null ? 0 : gauges.Count;
                    int reserve = issueCount > 0 ? 3 : 1;
                    int gaugeRows = Math.Min(gaugeCount, Math.Max(0, (int)((bottom - y) / rowHeight) - reserve));
                    for (int i = 0; i < gaugeRows; i++)
                    {
                        DrawGauge(frame, surface, gauges[i], left, right, y, rowHeight, unit, scale);
                        y += rowHeight;
                    }
                    y += 6f * unit;

                    // Issues
                    int rows = (int)((bottom - y) / lineHeight);
                    if (rows < 1) return;
                    if (issueCount == 0)
                    {
                        Text(frame, "No active issues", new Vector2(left, y), scale, muted, TextAlignment.LEFT);
                        return;
                    }

                    Text(frame, "ISSUES (" + issueCount + ")", new Vector2(left, y), scale, errorColour, TextAlignment.LEFT);
                    y += lineHeight;
                    rows--;
                    if (rows < 1) return;

                    wrapped.Clear();
                    for (int i = 0; i < issueCount; i++)
                        Wrap(surface, (i + 1) + ". " + issues[i], width, scale, wrapped);
                    bool overflow = wrapped.Count > rows;
                    int pageRows = Math.Max(1, rows - (overflow ? 1 : 0));
                    int pages = Math.Max(1, (wrapped.Count + pageRows - 1) / pageRows);
                    int page = (int)(Math.Max(0, elapsedSeconds) / PageSeconds % pages);
                    int first = page * pageRows;
                    int end = Math.Min(first + pageRows, wrapped.Count);
                    for (int i = first; i < end; i++)
                    {
                        Text(frame, wrapped[i], new Vector2(left, y), scale, errorColour, TextAlignment.LEFT);
                        y += lineHeight;
                    }
                    if (overflow && rows > 1)
                        Text(frame, "Page " + (page + 1) + "/" + pages, new Vector2(right, bottom - lineHeight),
                            scale, muted, TextAlignment.RIGHT);
                }
            }

            void DrawGauge(MySpriteDrawFrame frame, IMyTextSurface surface, Gauge gauge,
                float left, float right, float y, float rowHeight, float unit, float scale)
            {
                float centreY = y + rowHeight / 2;
                float icon = 20f * unit;
                float x = left;
                if (!string.IsNullOrEmpty(gauge.Icon))
                {
                    Sprite(frame, gauge.Icon, new Vector2(x + icon / 2, centreY), new Vector2(icon, icon), foreground);
                    x += icon + 8f * unit;
                }

                float labelWidth = 110f * unit;
                Text(frame, Fit(surface, gauge.Label, labelWidth, scale),
                    new Vector2(x, centreY - Measure(surface, "Ag", scale).Y / 2), scale, foreground, TextAlignment.LEFT);
                x += labelWidth;

                float valueWidth = Measure(surface, gauge.Text ?? "", scale).X + 8f * unit;
                Text(frame, gauge.Text ?? "", new Vector2(right, centreY - Measure(surface, "Ag", scale).Y / 2),
                    scale, foreground, TextAlignment.RIGHT);

                if (gauge.Fraction < 0) return;
                float barWidth = right - valueWidth - x;
                if (barWidth < 20f * unit) return;
                float barHeight = 10f * unit;
                double fraction = Math.Min(1, Math.Max(0, gauge.Fraction));
                Color fill = gauge.Threshold >= 0 && gauge.Fraction < gauge.Threshold ? errorColour : goodColour;
                Rect(frame, new Vector2(x + barWidth / 2, centreY), new Vector2(barWidth, barHeight), track);
                float fillWidth = (float)(barWidth * fraction);
                if (fillWidth > 0)
                    Rect(frame, new Vector2(x + fillWidth / 2, centreY), new Vector2(fillWidth, barHeight), fill);
                if (gauge.Threshold > 0 && gauge.Threshold < 1)
                    Rect(frame, new Vector2(x + (float)(barWidth * gauge.Threshold), centreY),
                        new Vector2(2f * unit, barHeight + 6f * unit), foreground);
            }

            void Rect(MySpriteDrawFrame frame, Vector2 centre, Vector2 size, Color colour)
            {
                Sprite(frame, "SquareSimple", centre, size, colour);
            }

            void Sprite(MySpriteDrawFrame frame, string texture, Vector2 centre, Vector2 size, Color colour)
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, texture, centre, size, colour));
            }

            void Text(MySpriteDrawFrame frame, string text, Vector2 position, float scale, Color colour, TextAlignment alignment)
            {
                MySprite sprite = MySprite.CreateText(text ?? "", Font, colour, scale, alignment);
                sprite.Position = position;
                frame.Add(sprite);
            }

            Vector2 Measure(IMyTextSurface surface, string text, float scale)
            {
                measurement.Clear();
                measurement.Append(text ?? "");
                return surface.MeasureStringInPixels(measurement, Font, scale);
            }

            string Fit(IMyTextSurface surface, string text, float width, float scale)
            {
                text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
                if (Measure(surface, text, scale).X <= width) return text;
                float ellipsisWidth = Measure(surface, "...", scale).X;
                int length = FittingLength(surface, text, Math.Max(0, width - ellipsisWidth), scale);
                return text.Substring(0, length).TrimEnd() + "...";
            }

            int FittingLength(IMyTextSurface surface, string text, float width, float scale)
            {
                int low = 0;
                int high = text.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (Measure(surface, text.Substring(0, middle), scale).X <= width) low = middle;
                    else high = middle - 1;
                }
                return low;
            }

            void Wrap(IMyTextSurface surface, string text, float width, float scale, List<string> lines)
            {
                string[] paragraphs = (text ?? "").Replace("\r", "").Split('\n');
                for (int i = 0; i < paragraphs.Length; i++)
                {
                    string remaining = paragraphs[i].Trim();
                    if (remaining.Length == 0) { lines.Add(""); continue; }
                    while (remaining.Length > 0)
                    {
                        int length = FittingLength(surface, remaining, width, scale);
                        if (length >= remaining.Length) { lines.Add(remaining); break; }
                        length = Math.Max(1, length);
                        int space = remaining.LastIndexOf(' ', length - 1, length);
                        if (space > 0) length = space;
                        lines.Add(remaining.Substring(0, length).TrimEnd());
                        remaining = remaining.Substring(length).TrimStart();
                    }
                }
            }
        }
    }
}
