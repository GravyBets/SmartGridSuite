using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// A quiet, top-down utility-site environment and clearly differentiated
    /// defensive effects. No decorative full-size radar circles are used.
    /// All visual assets are local WPF vectors, independent of game logic.
    /// </summary>
    internal static class TowerDefenseEnvironmentArt
    {
        private static readonly Brush FieldInk = Gradient("#183B47", "#0D2639", 112);
        private static readonly Brush FieldEdge = Ink("#396777");
        private static readonly Brush DeepTurf = Ink("#193C46");
        private static readonly Brush TurfLight = Ink("#204A4B");
        private static readonly Brush Trees = Ink("#255651");
        private static readonly Brush TreeHighlight = Ink("#367360");
        private static readonly Brush Stone = Ink("#68777C");
        private static readonly Brush StoneShadow = Ink("#344A54");
        private static readonly Brush Road = Ink("#3A4B53");
        private static readonly Brush RoadEdge = Ink("#647176");
        private static readonly Brush RoadCenter = Ink("#8B8980");
        private static readonly Brush Yard = Ink("#334B56");
        private static readonly Brush YardEdge = Ink("#546775");
        private static readonly Brush Cyan = Ink("#65E5F2");
        private static readonly Brush Purple = Ink("#C69BFA");
        private static readonly Brush Gold = Ink("#F7D275");
        private static readonly Brush Red = Ink("#F87683");
        private static readonly Brush White = Ink("#EAF3FA");
        private static readonly Brush SoftText = Ink("#A3C3D1");
        private static readonly Brush Badge = Gradient("#142D43", "#0C1C30", 0);
        private static readonly Brush BoltFill = Ink("#F1B94C");
        private static readonly Brush ShieldFill = Ink("#67D2EE");
        private static readonly Brush RfFill = Ink("#AD91EA");

        private static readonly Pen BorderPen = Stroke(FieldEdge, 1.5);
        private static readonly Pen AccentPen = Stroke(Cyan, 1.6);
        private static readonly Pen TurfPen = Stroke(TurfLight, 1.3);
        private static readonly Pen RoadOuter = Stroke(RoadEdge, 34);
        private static readonly Pen RoadInner = Stroke(Road, 29);
        private static readonly Pen RoadDivider = Dashed(RoadCenter, 1.2, 3, 10);
        private static readonly Pen YardPen = Stroke(YardEdge, 1.2);
        private static readonly Pen FencePen = Stroke(White, 2.0);
        private static readonly Pen FenceInnerPen = Stroke(YardEdge, 0.9);
        private static readonly Pen FenceBrokenPen = Dashed(Red, 2, 4, 3);
        private static readonly Pen SecurityPulsePen = Stroke(Cyan, 2.6);
        private static readonly Pen RfPulsePen = Stroke(Purple, 2.7);
        private static readonly Pen LightningPen = Stroke(Gold, 2.5);
        private static readonly Pen LightningWarning = Stroke(Red, 1.5);
        private static readonly Pen FencePostPen = Stroke(White, 0.6);
        private static readonly Pen GoldBadgePen = Stroke(Gold, 1.2);
        private static readonly Pen CyanBadgePen = Stroke(Cyan, 1.2);
        private static readonly Pen PurpleBadgePen = Stroke(Purple, 1.2);
        private static readonly Pen BoltGlyphPen = Stroke(BoltFill, 2.4);
        private static readonly Pen ShieldGlyphPen = Stroke(ShieldFill, 2.4);
        private static readonly Pen RfGlyphPen = Stroke(RfFill, 2.4);

        private static readonly Geometry YardShape = Shape(
            "M 308,216 L 469,216 L 503,252 L 503,359 " +
            "L 472,394 L 297,394 L 267,361 L 267,250 Z");
        private static readonly Geometry AccessRoute = Shape(
            "M -13,528 C 118,527 136,469 209,454 " +
            "C 273,444 293,414 331,372");
        private static readonly Geometry EastTurf = Shape(
            "M 545,0 L 768,0 L 768,160 " +
            "Q 715,220 654,198 Q 596,168 545,0 Z");
        private static readonly Geometry WestTurf = Shape(
            "M 0,18 Q 80,4 114,77 Q 134,137 200,151 " +
            "L 169,236 L 0,213 Z");
        private static readonly Geometry LightningGlyph = Shape(
            "M 10,0 L 0,14 L 8,14 L 4,25 L 20,9 L 11,9 Z");
        private static readonly Geometry ShieldGlyph = Shape(
            "M 10,0 L 20,4 L 18,16 Q 15,23 10,26 " +
            "Q 5,23 2,16 L 0,4 Z");
        private static readonly Geometry RfGlyph = Shape(
            "M 3,13 L 6,13 M 10,5 Q 20,13 10,21 " +
            "M 14,1 Q 29,13 14,25");

        internal static void DrawBackground(DrawingContext dc, Rect field)
        {
            dc.PushTransform(new TranslateTransform(field.Left, field.Top));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0,
                field.Width, field.Height)));

            dc.DrawRectangle(FieldInk, null, new Rect(0, 0,
                field.Width, field.Height));
            dc.DrawGeometry(DeepTurf, null, EastTurf);
            dc.DrawGeometry(DeepTurf, null, WestTurf);

            // A gravel maintenance access route rather than a gameplay path:
            // enemies still approach from arbitrary bearings.
            dc.DrawGeometry(null, RoadOuter, AccessRoute);
            dc.DrawGeometry(null, RoadInner, AccessRoute);
            dc.DrawGeometry(null, RoadDivider, AccessRoute);

            // Ground-level security compound with angular hardscape; no
            // inaccurate perspective circles behind the main tower.
            dc.DrawGeometry(Yard, YardPen, YardShape);

            // Deterministic, low-contrast vegetation avoids per-frame random
            // allocation and leaves the action around the central site clear.
            for (int y = 0; y < 13; y++)
            {
                for (int x = 0; x < 17; x++)
                {
                    int seed = (x * 53 + y * 79 + x * y * 13) % 97;
                    if (seed > 24) continue;
                    double xx = 17 + x * 45 + (seed % 9);
                    double yy = 14 + y * 46 + (seed % 11);
                    double dx = xx - field.Width / 2;
                    double dy = yy - field.Height / 2;
                    if (dx * dx + dy * dy < 185 * 185 ||
                        (xx < 280 && yy > 430 && xx + yy < 635))
                        continue;

                    if (seed % 4 == 0)
                    {
                        dc.DrawEllipse(StoneShadow, null,
                            new Point(xx, yy + 2), 4.5, 2.7);
                        dc.DrawEllipse(Stone, null,
                            new Point(xx - 0.7, yy), 3.2, 1.9);
                    }
                    else
                    {
                        dc.DrawEllipse(Trees, null,
                            new Point(xx, yy), 9, 6);
                        dc.DrawEllipse(TreeHighlight, null,
                            new Point(xx - 2, yy - 2), 5, 3);
                        dc.DrawLine(TurfPen,
                            new Point(xx + 8, yy + 1),
                            new Point(xx + 11, yy - 4));
                    }
                }
            }

            // Four small corner registration marks lend a site-map feeling
            // without filling the board with a 32px tile grid.
            DrawCorner(dc, 11, 11, 1, 1);
            DrawCorner(dc, field.Width - 11, 11, -1, 1);
            DrawCorner(dc, 11, field.Height - 11, 1, -1);
            DrawCorner(dc, field.Width - 11, field.Height - 11, -1, -1);

            dc.Pop();
            dc.Pop();
            dc.DrawRectangle(null, BorderPen,
                new Rect(field.X - 2, field.Y - 2,
                    field.Width + 4, field.Height + 4));
        }

        internal static void DrawFence(DrawingContext dc,
            Point center, double radius, bool intact, int level)
        {
            // An actual 12-sided physical fence with support posts, rather
            // than a glowing defensive circle/radar overlay. The perimeter
            // uses the gameplay FenceRadius without changing hit detection.
            int sides = 12;
            Point[] points = new Point[sides];
            for (int i = 0; i < sides; i++)
            {
                double angle = (i + 0.5) * Math.PI * 2 / sides;
                points[i] = new Point(center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);
            }

            for (int i = 0; i < sides; i++)
            {
                Point a = points[i], b = points[(i + 1) % sides];
                dc.DrawLine(intact ? FencePen : FenceBrokenPen, a, b);
                if (intact)
                {
                    // Mesh diagonal and lower guide line between posts.
                    Point innerA = new(center.X + (a.X - center.X) * 0.965,
                        center.Y + (a.Y - center.Y) * 0.965);
                    Point innerB = new(center.X + (b.X - center.X) * 0.965,
                        center.Y + (b.Y - center.Y) * 0.965);
                    dc.DrawLine(FenceInnerPen, innerA, innerB);
                    dc.DrawLine(FenceInnerPen, a, innerB);
                }

                dc.DrawRoundedRectangle(intact ? YardEdge : Red,
                    FencePostPen,
                    new Rect(a.X - 3.2, a.Y - 3.2, 6.4, 6.4),
                    1.4, 1.4);
            }

            // Upgrade level changes fence durability in the simulation;
            // restrained physical rivets make that readable, not extra rings.
            if (intact && level > 1)
            {
                for (int i = 0; i < sides; i += 3)
                    dc.DrawEllipse(Cyan, null, points[i],
                        1.5 + Math.Min(level, 6) * 0.2, 1.5);
            }
        }

        internal static void DrawSweep(DrawingContext dc,
            Point center, double remaining, double duration, bool rf)
        {
            // Short outward-moving strokes, not another oversized circle.
            double progress = Math.Clamp(1 - remaining / duration, 0, 1);
            double radius = 50 + progress * (rf ? 176 : 192);
            var pen = rf ? RfPulsePen : SecurityPulsePen;
            int count = rf ? 10 : 12;
            for (int i = 0; i < count; i++)
            {
                double angle = i * Math.PI * 2 / count;
                double cs = Math.Cos(angle), sn = Math.Sin(angle);
                dc.DrawLine(pen,
                    new Point(center.X + cs * radius,
                        center.Y + sn * radius),
                    new Point(center.X + cs * (radius + 15),
                        center.Y + sn * (radius + 15)));
            }
        }

        internal static void DrawLightningWarning(DrawingContext dc,
            Point center, double timeRemaining)
        {
            // Corner brackets focus on the mast, avoiding a full warning ring.
            double radius = 46;
            for (int i = 0; i < 4; i++)
            {
                double angle = i * Math.PI / 2 + Math.PI / 4;
                double cx = center.X + Math.Cos(angle) * radius;
                double cy = center.Y + Math.Sin(angle) * radius;
                double ix = center.X - cx, iy = center.Y - cy;
                double n = Math.Sqrt(ix * ix + iy * iy);
                dc.DrawLine(LightningPen,
                    new Point(cx, cy),
                    new Point(cx + ix / n * 15, cy + iy / n * 15));
                dc.DrawEllipse(null, LightningWarning,
                    new Point(cx, cy), 3, 3);
            }
        }

        internal static void DrawStatusBadges(DrawingContext dc,
            Rect field, int lightningLevel, int surgeLevel,
            int rfLevel, double dpi)
        {
            double x = field.Right - 177;
            double y = field.Top + 17;
            if (lightningLevel > 0)
            {
                BadgeChip(dc, x, y, "LIGHTNING", lightningLevel,
                    Gold, BoltFill, LightningGlyph, dpi);
                y += 34;
            }
            if (surgeLevel > 0)
            {
                BadgeChip(dc, x, y, "SURGE", surgeLevel,
                    Cyan, ShieldFill, ShieldGlyph, dpi);
                y += 34;
            }
            if (rfLevel > 0)
                BadgeChip(dc, x, y, "RF FILTER", rfLevel,
                    Purple, RfFill, RfGlyph, dpi);
        }

        private static void BadgeChip(DrawingContext dc,
            double x, double y, string name, int level, Brush accent,
            Brush iconBrush, Geometry glyph, double dpi)
        {
            Pen chipPen = name == "LIGHTNING"
                ? GoldBadgePen : name == "SURGE" ? CyanBadgePen : PurpleBadgePen;
            Pen glyphPen = name == "LIGHTNING"
                ? BoltGlyphPen : name == "SURGE" ? ShieldGlyphPen : RfGlyphPen;
            dc.DrawRoundedRectangle(Badge, chipPen,
                new Rect(x, y, 160, 29), 5, 5);
            dc.PushTransform(new TranslateTransform(x + 9, y + 4));
            dc.PushTransform(new ScaleTransform(0.68, 0.68));
            dc.DrawGeometry(null, glyphPen, glyph);
            if (name != "RF FILTER")
                dc.DrawGeometry(iconBrush, null, glyph);
            dc.Pop();
            dc.Pop();

            var label = new FormattedText(
                name + "  LV " + level.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    FontWeights.Bold, FontStretches.Normal),
                12.6, White, dpi);
            dc.DrawText(label, new Point(x + 32, y + 6));
        }

        private static void DrawCorner(DrawingContext dc,
            double x, double y, double dx, double dy)
        {
            dc.DrawLine(AccentPen, new Point(x, y),
                new Point(x + dx * 12, y));
            dc.DrawLine(AccentPen, new Point(x, y),
                new Point(x, y + dy * 12));
        }

        private static Geometry Shape(string path)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }

        private static Brush Ink(string hex)
        {
            var color = (SolidColorBrush)new BrushConverter()
                .ConvertFromString(hex)!;
            color.Freeze();
            return color;
        }

        private static Brush Gradient(string from, string to, double angle)
        {
            var start = (Color)ColorConverter.ConvertFromString(from)!;
            var end = (Color)ColorConverter.ConvertFromString(to)!;
            var brush = new LinearGradientBrush(start, end, angle);
            brush.Freeze();
            return brush;
        }

        private static Pen Stroke(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen Dashed(Brush brush, double thickness,
            double dash, double gap)
        {
            var pen = new Pen(brush, thickness)
            {
                DashStyle = new DashStyle(new[] { dash, gap }, 0)
            };
            pen.Freeze();
            return pen;
        }
    }
}
