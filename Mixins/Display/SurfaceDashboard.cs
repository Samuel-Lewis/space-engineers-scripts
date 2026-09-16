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

            // One instrument readout on the line under the subtitle: LABEL value.
            public struct Readout
            {
                public string Label;
                public string Value;

                public Readout(string label, string value)
                {
                    Label = label;
                    Value = value;
                }
            }

            // One row of DrawTable. Colour null uses the default foreground.
            public struct Row
            {
                public string[] Cells;
                public Color? Colour;

                public Row(string[] cells, Color? colour)
                {
                    Cells = cells;
                    Colour = colour;
                }
            }

            // One plotted contact for DrawRadar. X is metres to the right, Y metres up
            // the screen; the caller has already projected and rotated the offset.
            public struct Contact
            {
                public double X;
                public double Y;
                public string Label;
                public Color Colour;

                public Contact(double x, double y, string label, Color colour)
                {
                    X = x;
                    Y = y;
                    Label = label;
                    Colour = colour;
                }
            }

            const string Font = "Debug";
            const double PageSeconds = 5;
            const double WarningBand = 0.3;
            // Auto-fit never zooms in past this radius, so a lone close contact is
            // not pinned to the edge of the plot.
            const double MinimumRadarRadius = 50;
            readonly List<IMyTextSurface> surfaces = new List<IMyTextSurface>();
            readonly StringBuilder measurement = new StringBuilder();
            readonly List<string> wrapped = new List<string>();
            readonly Color background = new Color(8, 12, 18);
            readonly Color panel = new Color(16, 24, 34);
            readonly Color foreground = new Color(225, 234, 244);
            readonly Color muted = new Color(120, 138, 160);
            readonly Color track = new Color(30, 42, 56);
            readonly Color goodColour = new Color(90, 190, 230);
            readonly Color warningColour = new Color(255, 160, 40);
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
                    Prepare(surface);
                    surfaces.Add(surface);
                }
            }

            // Switches a surface to sprite mode. Callers drawing individual surfaces with
            // the single-surface methods below must call this once per surface first.
            public void Prepare(IMyTextSurface surface)
            {
                surface.ContentType = ContentType.SCRIPT;
                surface.Script = "";
                surface.ScriptBackgroundColor = background;
            }

            // blinkSeconds matches IMyLightingBlock.BlinkIntervalSeconds: 0 is solid,
            // otherwise the lamp is lit for the first half of each interval.
            public void Draw(string title, string state, Color lampColour, float blinkSeconds,
                string subtitle, List<Gauge> gauges, List<string> issues, double elapsedSeconds)
            {
                for (int i = 0; i < surfaces.Count; i++)
                    DrawStatus(surfaces[i], title, state, lampColour, blinkSeconds, subtitle, null, float.NaN, gauges, issues, elapsedSeconds);
            }

            // Single-surface form of Draw for callers that manage their own surface list.
            // readouts (optional) is an instrument line between the subtitle and the gauges;
            // headingDegrees (NaN to skip) adds a compass tape under it.
            public void DrawStatus(IMyTextSurface surface, string title, string state, Color lampColour, float blinkSeconds,
                string subtitle, List<Readout> readouts, float headingDegrees, List<Gauge> gauges, List<string> issues, double elapsedSeconds)
            {
                bool lit = blinkSeconds <= 0 || elapsedSeconds % blinkSeconds < blinkSeconds / 2;
                DrawSurface(surface, title, state, lampColour, lit, subtitle, readouts, headingDegrees, gauges, issues, elapsedSeconds);
            }

            // Header plus a centred message, for empty or misconfigured screens.
            public void DrawMessage(IMyTextSurface surface, string title, string message, string detail)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 18f * unit;
                float scale = .9f * unit;
                float width = size.X - 2 * padding;
                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    Rect(frame, origin + size / 2f, size, background);
                    DrawHeader(frame, surface, title, origin.X + padding, origin.Y + padding, width, scale);
                    Vector2 centre = origin + size / 2f;
                    float messageScale = scale * 1.3f;
                    Vector2 messageSize = Measure(surface, message, messageScale);
                    Text(frame, Fit(surface, message, width, messageScale),
                        new Vector2(centre.X, centre.Y - messageSize.Y), messageScale, foreground, TextAlignment.CENTER);
                    wrapped.Clear();
                    Wrap(surface, detail ?? "", width, scale, wrapped);
                    float y = centre.Y + 6f * unit;
                    float lineHeight = Measure(surface, "Ag", scale).Y;
                    for (int i = 0; i < wrapped.Count && y + lineHeight <= origin.Y + size.Y - padding; i++)
                    {
                        Text(frame, wrapped[i], new Vector2(centre.X, y), scale, muted, TextAlignment.CENTER);
                        y += lineHeight;
                    }
                }
            }

            // Header, subtitle and a column-aligned table. Columns shrink to the widest
            // cell and the text scales down until every column fits. Rows beyond the
            // surface page on the PageSeconds cycle.
            public void DrawTable(IMyTextSurface surface, string title, string subtitle,
                string[] headers, List<Row> rows, double elapsedSeconds)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1 || headers == null) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 18f * unit;
                float left = origin.X + padding;
                float right = origin.X + size.X - padding;
                float width = right - left;
                float bottom = origin.Y + size.Y - padding;
                float scale = .9f * unit;
                int columns = headers.Length;
                int rowCount = rows == null ? 0 : rows.Count;
                float gap = 12f * unit;

                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    Rect(frame, origin + size / 2f, size, background);
                    float y = DrawHeader(frame, surface, title, left, origin.Y + padding, width, scale);
                    Text(frame, Fit(surface, subtitle, width, scale), new Vector2(left, y), scale, muted, TextAlignment.LEFT);
                    y += Measure(surface, "Ag", scale).Y + 10f * unit;

                    // Natural column widths, then shrink the text until they fit. The first
                    // column absorbs any remaining slack or truncation.
                    float[] widths = new float[columns];
                    float textScale = scale;
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        float total = gap * (columns - 1);
                        for (int c = 0; c < columns; c++)
                        {
                            widths[c] = Measure(surface, headers[c], textScale).X;
                            for (int r = 0; r < rowCount; r++)
                                if (rows[r].Cells != null && c < rows[r].Cells.Length)
                                    widths[c] = Math.Max(widths[c], Measure(surface, rows[r].Cells[c], textScale).X);
                            total += widths[c];
                        }
                        if (total <= width || textScale <= scale * .6f)
                        {
                            widths[0] = Math.Max(20f * unit, widths[0] + (width - total));
                            break;
                        }
                        textScale -= scale * .08f;
                    }

                    float lineHeight = Measure(surface, "Ag", textScale).Y;
                    float rowHeight = lineHeight + 6f * unit;
                    float x = left;
                    for (int c = 0; c < columns; c++)
                    {
                        Text(frame, Fit(surface, headers[c], widths[c], textScale), new Vector2(x, y), textScale, muted, TextAlignment.LEFT);
                        x += widths[c] + gap;
                    }
                    y += lineHeight + 4f * unit;
                    Rect(frame, new Vector2(left + width / 2, y), new Vector2(width, 2f * unit), track);
                    y += 6f * unit;

                    int visible = Math.Max(0, (int)((bottom - y) / rowHeight));
                    if (visible == 0) return;
                    if (rowCount == 0)
                    {
                        Text(frame, "No contacts", new Vector2(left, y), textScale, muted, TextAlignment.LEFT);
                        return;
                    }
                    bool overflow = rowCount > visible;
                    int pageRows = Math.Max(1, visible - (overflow ? 1 : 0));
                    int pages = Math.Max(1, (rowCount + pageRows - 1) / pageRows);
                    int page = (int)(Math.Max(0, elapsedSeconds) / PageSeconds % pages);
                    int first = page * pageRows;
                    int end = Math.Min(first + pageRows, rowCount);
                    for (int r = first; r < end; r++)
                    {
                        Row row = rows[r];
                        Color colour = row.Colour ?? foreground;
                        x = left;
                        for (int c = 0; c < columns && row.Cells != null && c < row.Cells.Length; c++)
                        {
                            Text(frame, Fit(surface, row.Cells[c], widths[c], textScale), new Vector2(x, y), textScale, colour, TextAlignment.LEFT);
                            x += widths[c] + gap;
                        }
                        y += rowHeight;
                    }
                    if (overflow && visible > 1)
                        Text(frame, "Page " + (page + 1) + "/" + pages, new Vector2(right, bottom - lineHeight),
                            textScale, muted, TextAlignment.RIGHT);
                }
            }

            // Self-centred plot. The radius auto-fits the farthest contact (never below
            // MinimumRadarRadius). headingRadians rotates the centre marker clockwise
            // from screen-up; pass NaN to draw a plain dot instead.
            public void DrawRadar(IMyTextSurface surface, string title, string subtitle,
                List<Contact> contacts, float headingRadians)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 18f * unit;
                float left = origin.X + padding;
                float width = size.X - 2 * padding;
                float scale = .9f * unit;
                float labelScale = scale * .7f;
                int count = contacts == null ? 0 : contacts.Count;

                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    Rect(frame, origin + size / 2f, size, background);
                    float y = DrawHeader(frame, surface, title, left, origin.Y + padding, width, scale);
                    Text(frame, Fit(surface, subtitle, width, scale), new Vector2(left, y), scale, muted, TextAlignment.LEFT);
                    y += Measure(surface, "Ag", scale).Y + 8f * unit;

                    float bottom = origin.Y + size.Y - padding;
                    float labelHeight = Measure(surface, "Ag", labelScale).Y;
                    // Leave room under the plot for a label on a contact at the edge.
                    float plotRadius = Math.Min(width, bottom - y - labelHeight) / 2;
                    if (plotRadius < 20f * unit) return;
                    Vector2 centre = new Vector2(origin.X + size.X / 2, y + plotRadius);

                    double radius = MinimumRadarRadius;
                    for (int i = 0; i < count; i++)
                        radius = Math.Max(radius, Math.Sqrt(contacts[i].X * contacts[i].X + contacts[i].Y * contacts[i].Y));
                    radius *= 1.05;

                    Sprite(frame, "Circle", centre, new Vector2(plotRadius * 2, plotRadius * 2), panel);
                    Sprite(frame, "CircleHollow", centre, new Vector2(plotRadius * 2, plotRadius * 2), track);
                    Sprite(frame, "CircleHollow", centre, new Vector2(plotRadius, plotRadius), track);
                    Rect(frame, centre, new Vector2(plotRadius * 2, 1f * unit), track);
                    Rect(frame, centre, new Vector2(1f * unit, plotRadius * 2), track);
                    Text(frame, Range(radius), new Vector2(origin.X + size.X - padding, y), labelScale, muted, TextAlignment.RIGHT);
                    Text(frame, Range(radius / 2), new Vector2(centre.X + 4f * unit, centre.Y - plotRadius / 2 - labelHeight), labelScale, muted, TextAlignment.LEFT);

                    float dot = 10f * unit;
                    for (int i = 0; i < count; i++)
                    {
                        Contact contact = contacts[i];
                        Vector2 position = centre + new Vector2((float)(contact.X / radius * plotRadius), (float)(-contact.Y / radius * plotRadius));
                        Sprite(frame, "Circle", position, new Vector2(dot, dot), contact.Colour);
                        Text(frame, contact.Label ?? "", new Vector2(position.X, position.Y + dot / 2 + 1f * unit), labelScale, contact.Colour, TextAlignment.CENTER);
                    }

                    float marker = 16f * unit;
                    if (float.IsNaN(headingRadians))
                        Sprite(frame, "Circle", centre, new Vector2(marker * .7f, marker * .7f), foreground);
                    else
                    {
                        MySprite sprite = new MySprite(SpriteType.TEXTURE, "Triangle", centre, new Vector2(marker, marker), foreground);
                        sprite.RotationOrScale = headingRadians;
                        frame.Add(sprite);
                    }
                }
            }

            static string Range(double metres)
            {
                return metres >= 1000 ? (metres / 1000).ToString("0.0") + " km" : metres.ToString("0") + " m";
            }

            // Draws the muted uppercase title and returns the y just below it.
            float DrawHeader(MySpriteDrawFrame frame, IMyTextSurface surface, string title, float left, float y, float width, float scale)
            {
                float unit = scale / .9f;
                float lineHeight = Math.Max(Measure(surface, "Ag", scale).Y, 26f * unit);
                Text(frame, Fit(surface, (title ?? "").ToUpperInvariant(), width, scale * .75f),
                    new Vector2(left, y), scale * .75f, muted, TextAlignment.LEFT);
                return y + lineHeight * .75f + 10f * unit;
            }

            void DrawSurface(IMyTextSurface surface, string title, string state, Color lampColour,
                bool lit, string subtitle, List<Readout> readouts, float headingDegrees, List<Gauge> gauges, List<string> issues, double elapsedSeconds)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 18f * unit;
                float left = origin.X + padding;
                float right = origin.X + size.X - padding;
                float width = right - left;
                float scale = .9f * unit;
                float lineHeight = Math.Max(Measure(surface, "Ag", scale).Y, 26f * unit);
                float y = origin.Y + padding;
                int issueCount = issues == null ? 0 : issues.Count;
                int gaugeCount = gauges == null ? 0 : gauges.Count;

                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    Rect(frame, origin + size / 2f, size, background);

                    // Header
                    Text(frame, Fit(surface, (title ?? "").ToUpperInvariant(), width, scale * .75f),
                        new Vector2(left, y), scale * .75f, muted, TextAlignment.LEFT);
                    y += lineHeight * .75f + 10f * unit;

                    // Lamp and state. The glow is the widest part, so it sets the column
                    // and the row so nothing spills past the padding.
                    float lamp = 44f * unit;
                    float glow = lamp * 1.7f;
                    Vector2 lampCentre = new Vector2(left + glow / 2, y + glow / 2);
                    Color lampDim = new Color(lampColour.R / 5, lampColour.G / 5, lampColour.B / 5);
                    if (lit)
                    {
                        Sprite(frame, "Circle", lampCentre, new Vector2(glow, glow),
                            new Color(lampColour.R, lampColour.G, lampColour.B, 28));
                        Sprite(frame, "Circle", lampCentre, new Vector2(lamp * 1.3f, lamp * 1.3f),
                            new Color(lampColour.R, lampColour.G, lampColour.B, 70));
                    }
                    Sprite(frame, "Circle", lampCentre, new Vector2(lamp, lamp), lit ? lampColour : lampDim);
                    Sprite(frame, "CircleHollow", lampCentre, new Vector2(lamp, lamp),
                        lit ? foreground : new Color(60, 70, 84));

                    float stateX = left + glow + 12f * unit;
                    float stateScale = scale * 2.4f;
                    while (stateScale > scale && Measure(surface, state, stateScale).X > right - stateX)
                        stateScale -= scale * .1f;
                    Vector2 stateSize = Measure(surface, state, stateScale);
                    Text(frame, Fit(surface, state, right - stateX, stateScale),
                        new Vector2(stateX, lampCentre.Y - stateSize.Y / 2), stateScale, foreground, TextAlignment.LEFT);
                    y += glow + 6f * unit;

                    Text(frame, Fit(surface, subtitle, width, scale), new Vector2(left, y), scale, muted, TextAlignment.LEFT);
                    y += lineHeight + 12f * unit;

                    float bottom = origin.Y + size.Y - padding;

                    // Instrument line: LABEL value pairs separated by hairline ticks, wrapping
                    // when the line is full. Hairlines above and below frame it.
                    int readoutCount = readouts == null ? 0 : readouts.Count;
                    if (readoutCount > 0 && y + lineHeight * 1.5f <= bottom)
                    {
                        float labelScale = scale * .65f;
                        float labelDrop = (lineHeight - Measure(surface, "Ag", labelScale).Y) / 2;
                        float gap = 14f * unit;
                        Rect(frame, new Vector2(left + width / 2, y), new Vector2(width, 1f * unit), track);
                        y += 6f * unit;
                        float x = left;
                        for (int i = 0; i < readoutCount; i++)
                        {
                            string label = (readouts[i].Label ?? "").ToUpperInvariant();
                            string value = readouts[i].Value ?? "";
                            float labelWidth = Measure(surface, label, labelScale).X;
                            float valueWidth = Measure(surface, value, scale).X;
                            float pairWidth = labelWidth + 6f * unit + valueWidth;
                            if (x > left && x + pairWidth > right)
                            {
                                x = left;
                                y += lineHeight + 4f * unit;
                                if (y + lineHeight > bottom) break;
                            }
                            if (x > left)
                                Rect(frame, new Vector2(x - gap / 2, y + lineHeight / 2), new Vector2(1f * unit, lineHeight * .7f), muted);
                            Text(frame, label, new Vector2(x, y + labelDrop), labelScale, muted, TextAlignment.LEFT);
                            Text(frame, Fit(surface, value, right - x - labelWidth - 6f * unit, scale),
                                new Vector2(x + labelWidth + 6f * unit, y), scale, foreground, TextAlignment.LEFT);
                            x += pairWidth + gap;
                        }
                        y += lineHeight + 6f * unit;
                        Rect(frame, new Vector2(left + width / 2, y), new Vector2(width, 1f * unit), track);
                        y += 8f * unit;
                    }

                    // Compass tape: +/-60 degrees around the heading, ticks every 10, labels
                    // every 30, a caret at the centre.
                    if (!float.IsNaN(headingDegrees))
                    {
                        float tickScale = scale * .6f;
                        float tapeHeight = Measure(surface, "Ag", tickScale).Y + 14f * unit;
                        if (y + tapeHeight <= bottom)
                        {
                            float centreX = left + width / 2;
                            float perDegree = width / 120f;
                            float baseY = y + tapeHeight - 2f * unit;
                            float heading = ((headingDegrees % 360) + 360) % 360;
                            Rect(frame, new Vector2(centreX, baseY), new Vector2(width, 1f * unit), muted);
                            int firstTick = (int)Math.Floor((heading - 60) / 10) * 10;
                            for (int degree = firstTick; degree <= heading + 60; degree += 10)
                            {
                                float x = centreX + (degree - heading) * perDegree;
                                if (x < left || x > right) continue;
                                bool major = degree % 30 == 0;
                                float tick = (major ? 10f : 5f) * unit;
                                Rect(frame, new Vector2(x, baseY - tick / 2), new Vector2(1f * unit, tick), major ? foreground : muted);
                                if (!major) continue;
                                int shown = ((degree % 360) + 360) % 360;
                                string label = shown == 0 ? "N" : shown == 90 ? "E" : shown == 180 ? "S" : shown == 270 ? "W" : shown.ToString("000");
                                Text(frame, label, new Vector2(x, y), tickScale, shown % 90 == 0 ? foreground : muted, TextAlignment.CENTER);
                            }
                            MySprite caret = new MySprite(SpriteType.TEXTURE, "Triangle", new Vector2(centreX, baseY + 5f * unit),
                                new Vector2(10f * unit, 8f * unit), goodColour);
                            caret.RotationOrScale = (float)Math.PI;
                            frame.Add(caret);
                            y += tapeHeight + 10f * unit;
                        }
                    }

                    // Gauges share one grid: icon column, label column, bar, value column.
                    float rowHeight = lineHeight + 8f * unit;
                    float valueColumn = 0;
                    for (int i = 0; i < gaugeCount; i++)
                        valueColumn = Math.Max(valueColumn, Measure(surface, gauges[i].Text ?? "", scale).X);
                    valueColumn += 12f * unit;
                    int reserve = issueCount > 0 ? 3 : 1;
                    int gaugeRows = Math.Min(gaugeCount, Math.Max(0, (int)((bottom - y) / rowHeight) - reserve));
                    for (int i = 0; i < gaugeRows; i++)
                    {
                        DrawGauge(frame, surface, gauges[i], left, right, y, rowHeight, valueColumn, unit, scale);
                        y += rowHeight;
                    }
                    y += 8f * unit;

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
                float left, float right, float y, float rowHeight, float valueColumn, float unit, float scale)
            {
                float centreY = y + rowHeight / 2;
                float textY = centreY - Measure(surface, "Ag", scale).Y / 2;
                float icon = 24f * unit;
                float x = left;
                if (!string.IsNullOrEmpty(gauge.Icon))
                    Sprite(frame, gauge.Icon, new Vector2(x + icon / 2, centreY), new Vector2(icon, icon), foreground);
                x += icon + 10f * unit;

                float labelWidth = 130f * unit;
                Text(frame, Fit(surface, gauge.Label, labelWidth - 8f * unit, scale),
                    new Vector2(x, textY), scale, foreground, TextAlignment.LEFT);
                x += labelWidth;

                Color fill = GaugeColour(gauge);
                Text(frame, gauge.Text ?? "", new Vector2(right, textY), scale,
                    gauge.Fraction < 0 ? foreground : fill, TextAlignment.RIGHT);

                if (gauge.Fraction < 0) return;
                float barWidth = right - valueColumn - x;
                if (barWidth < 20f * unit) return;
                float barHeight = 12f * unit;
                double fraction = Math.Min(1, Math.Max(0, gauge.Fraction));
                Rect(frame, new Vector2(x + barWidth / 2, centreY), new Vector2(barWidth, barHeight), track);
                float fillWidth = (float)(barWidth * fraction);
                if (fillWidth > 0)
                    Rect(frame, new Vector2(x + fillWidth / 2, centreY), new Vector2(fillWidth, barHeight), fill);
                if (gauge.Threshold > 0 && gauge.Threshold < 1)
                    Rect(frame, new Vector2(x + (float)(barWidth * gauge.Threshold), centreY),
                        new Vector2(2f * unit, barHeight + 6f * unit), foreground);
            }

            // Red below the threshold. Orange in the first WarningBand of the room above it,
            // so a 45% threshold turns orange below 45 + 0.3 * 55 = 61.5%.
            Color GaugeColour(Gauge gauge)
            {
                if (gauge.Threshold < 0) return goodColour;
                if (gauge.Fraction < gauge.Threshold) return errorColour;
                if (gauge.Fraction < gauge.Threshold + WarningBand * (1 - gauge.Threshold)) return warningColour;
                return goodColour;
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
