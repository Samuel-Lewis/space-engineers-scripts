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
        // surface can be supplied; elapsedSeconds drives overflow pagination.
        public sealed class SurfaceDashboard
        {
            const string Font = "Debug";
            const double PageSeconds = 5;
            readonly List<IMyTextSurface> surfaces = new List<IMyTextSurface>();
            readonly StringBuilder measurement = new StringBuilder();
            readonly Color background = new Color(10, 16, 24);
            readonly Color foreground = new Color(225, 234, 244);
            readonly Color muted = new Color(145, 162, 184);
            readonly Color errorColour = new Color(255, 120, 110);

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

            public void Draw(string title, string stage, Color indicatorColour,
                string subtitle, List<string> details, List<string> errors,
                double elapsedSeconds)
            {
                for (int i = 0; i < surfaces.Count; i++)
                    DrawSurface(surfaces[i], title, stage, indicatorColour,
                        subtitle, details, errors, elapsedSeconds);
            }

            void DrawSurface(IMyTextSurface surface, string title, string stage,
                Color indicatorColour, string subtitle, List<string> details,
                List<string> errors, double elapsedSeconds)
            {
                Vector2 size = surface.SurfaceSize;
                if (size.X < 1 || size.Y < 1) return;
                Vector2 origin = (surface.TextureSize - size) / 2f;
                float unit = Math.Min(size.X / 512f, size.Y / 256f);
                float padding = 20f * unit;
                float width = size.X - padding * 2;
                float scale = .72f * unit;
                float lineHeight = Math.Max(Measure(surface, "Ag", scale).Y, 24f * unit);
                Vector2 position = origin + new Vector2(padding, padding);
                int errorCount = errors == null ? 0 : errors.Count;

                using (MySpriteDrawFrame frame = surface.DrawFrame())
                {
                    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                        origin + size / 2f, size, background));
                    Text(frame, Fit(surface, title, width, scale), position, scale, muted);
                    position.Y += lineHeight + 12f * unit;

                    float square = 26f * unit;
                    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                        position + new Vector2(square / 2, square / 2 + 3f * unit),
                        new Vector2(square, square), indicatorColour));
                    Text(frame, Fit(surface, stage, width - square - 14f * unit, scale * 1.5f),
                        position + new Vector2(square + 14f * unit, 0), scale * 1.5f, foreground);
                    position.Y += lineHeight * 1.5f + 10f * unit;
                    Text(frame, Fit(surface, subtitle, width, scale), position, scale, muted);
                    position.Y += lineHeight + 16f * unit;

                    float bottom = origin.Y + size.Y - padding;
                    int rows = Math.Max(0, (int)((bottom - position.Y) / lineHeight));
                    // Always reserve room for the error state, even with many checks.
                    int detailRows = Math.Min(details == null ? 0 : details.Count,
                        Math.Max(0, rows - (errorCount > 0 ? 4 : 2)));
                    if (errorCount > 0) detailRows = Math.Min(detailRows, rows / 2);
                    for (int i = 0; i < detailRows; i++)
                    {
                        string detail = details[i];
                        if (i == detailRows - 1 && details.Count > detailRows)
                            detail = "+ " + (details.Count - i) + " more checks";
                        Text(frame, Fit(surface, detail, width, scale), position, scale, foreground);
                        position.Y += lineHeight;
                    }
                    rows -= detailRows;
                    if (rows < 1) return;

                    if (errorCount == 0)
                    {
                        Text(frame, "No active issues", position, scale, muted);
                        return;
                    }

                    Text(frame, "ISSUES (" + errorCount + ")", position, scale, errorColour);
                    position.Y += lineHeight;
                    rows--;
                    if (rows < 1) return;

                    var wrapped = new List<string>();
                    for (int i = 0; i < errorCount; i++)
                        Wrap(surface, (i + 1) + ". " + errors[i], width, scale, wrapped);
                    bool overflow = wrapped.Count > rows;
                    int pageRows = Math.Max(1, rows - (overflow ? 1 : 0));
                    int pages = Math.Max(1, (wrapped.Count + pageRows - 1) / pageRows);
                    int page = (int)(Math.Max(0, elapsedSeconds) / PageSeconds % pages);
                    int first = page * pageRows;
                    int end = Math.Min(first + pageRows, wrapped.Count);
                    for (int i = first; i < end; i++)
                    {
                        Text(frame, wrapped[i], position, scale, errorColour);
                        position.Y += lineHeight;
                    }
                    if (overflow && rows > 1)
                    {
                        string footer = "Issues page " + (page + 1) + "/" + pages;
                        Text(frame, Fit(surface, footer, width, scale),
                            new Vector2(position.X, bottom - lineHeight), scale, muted);
                    }
                }
            }

            void Text(MySpriteDrawFrame frame, string text, Vector2 position, float scale, Color colour)
            {
                MySprite sprite = MySprite.CreateText(text ?? "", Font, colour, scale, TextAlignment.LEFT);
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
