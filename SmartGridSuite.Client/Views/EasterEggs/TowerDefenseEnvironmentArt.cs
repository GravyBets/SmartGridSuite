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
        private static readonly Brush Yard = Ink("#294752");
        private static readonly Brush YardEdge = Ink("#415B68");
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
        private static readonly Brush OrbShell = Ink("#415E7D");
        private static readonly Brush OrbMetal = Ink("#A6C6D9");
        private static readonly Brush OrbCore = Ink("#77ECFF");
        private static readonly Brush OrbPulse = Ink("#54B2F6FF");
        private static readonly Brush OrbShadow = Ink("#38000E18");

        private static readonly Pen BorderPen = Stroke(FieldEdge, 1.5);
        private static readonly Pen AccentPen = Stroke(Cyan, 1.6);
        private static readonly Pen TurfPen = Stroke(TurfLight, 1.3);
        private static readonly Pen RoadOuter = Stroke(RoadEdge, 34);
        private static readonly Pen RoadInner = Stroke(Road, 29);
        private static readonly Pen RoadDivider = Dashed(RoadCenter, 1.2, 3, 10);
        private static readonly Pen YardPen = Stroke(YardEdge, 1.0);
        private static readonly Brush FenceBackColor = Ink("#6E939C");
        private static readonly Brush FenceFrontColor = Ink("#CEE3E6");
        private static readonly Pen FenceBackPen = Stroke(FenceBackColor, 1.35);
        private static readonly Pen FenceFrontPen = Stroke(FenceFrontColor, 2.1);
        private static readonly Pen FenceMeshPen = Stroke(YardEdge, 0.85);
        private static readonly Pen FenceBrokenPen = Dashed(Red, 1.6, 3, 5);
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
        private static readonly Pen OrbEdgePen = Stroke(OrbMetal, 1.45);
        private static readonly Pen OrbFinPen = Stroke(OrbShell, 2.8);
        private static readonly Pen OrbAccentPen = Stroke(OrbCore, 1.65);

        // The angled overhead site has a shallow, soft elliptical gravel yard
        // rather than the oversized octagonal slab in the previous artwork.
        private static readonly Geometry OrbHex = Shape(
            "M -9,0 L -5,-7 L 5,-7 L 9,0 L 5,7 L -5,7 Z");
        private static readonly Geometry YardShape = FrozenEllipse(
            new Point(385, 292), 165, 110);
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

        // All compound effects use the same shallow visual perspective.
        // Share the fence geometry with the game hit test so enemies do not
        // get intercepted at an invisible circular boundary.
        internal const double PerspectiveY = 0.68;
        private const double CompoundOffsetY = 9;

        internal static Point FenceCenter(Point tower) =>
            new(tower.X, tower.Y + CompoundOffsetY);

        internal static bool WithinFence(Point position, Point tower, double radius)
        {
            if (radius <= 0) return false;
            Point center = FenceCenter(tower);
            double dx = (position.X - center.X) / radius;
            double dy = (position.Y - center.Y) / (radius * PerspectiveY);
            return dx * dx + dy * dy <= 1.0;
        }

        internal static Point OrbitPoint(Point tower, double horizontalRadius,
            double angle) => new(
                tower.X + Math.Cos(angle) * horizontalRadius,
                tower.Y + CompoundOffsetY + Math.Sin(angle) *
                    horizontalRadius * 0.60);

        internal static void DrawFence(DrawingContext dc,
            Point tower, double radius, bool intact, int level,
            bool foreground)
        {
            Point center = FenceCenter(tower);
            // Only draw one depth layer per call. Rear rails pass *behind*
            // the tower; the front rails pass in front of the concrete base.
            // This avoids the previous flat, bright octagon overlay.
            const int halfSegments = 24;
            for (int i = 0; i <= halfSegments; i++)
            {
                double angle = (foreground ? 0 : Math.PI) +
                    i * Math.PI / halfSegments;
                double nextAngle = angle + Math.PI / halfSegments;
                if (i == halfSegments) break;

                Point a = new(center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius * PerspectiveY);
                Point b = new(center.X + Math.Cos(nextAngle) * radius,
                    center.Y + Math.Sin(nextAngle) * radius * PerspectiveY);

                Pen edge = intact
                    ? (foreground ? FenceFrontPen : FenceBackPen)
                    : FenceBrokenPen;
                dc.DrawLine(edge, a, b);

                if (intact)
                {
                    double railLift = foreground ? 8 : 6;
                    Point topA = new(a.X, a.Y - railLift);
                    Point topB = new(b.X, b.Y - railLift);
                    dc.DrawLine(FenceMeshPen, topA, topB);
                    // Fine fence mesh only, without luminous square corners.
                    if (i % 4 == 0)
                        dc.DrawLine(FenceMeshPen, a, topB);
                }

                if (i % 6 == 0)
                {
                    double postHeight = foreground ? 10 : 7;
                    dc.DrawLine(edge, a,
                        new Point(a.X, a.Y - postHeight));
                    dc.DrawEllipse(foreground ? FenceFrontColor : FenceBackColor,
                        null, new Point(a.X, a.Y - postHeight), 2.1, 1.35);
                    if (intact && level >= 4 && i % 12 == 0)
                        dc.DrawEllipse(Cyan, null,
                            new Point(a.X, a.Y - postHeight - 3), 1.3, 1.3);
                }
            }
        }

        internal static void DrawSweep(DrawingContext dc,
            Point tower, double remaining, double duration, bool rf)
        {
            // Short wavefront strokes follow the projected ellipse instead of
            // forming an unintentional round ring around the tower.
            double progress = Math.Clamp(1 - remaining / duration, 0, 1);
            double radius = 54 + progress * (rf ? 173 : 190);
            Point center = FenceCenter(tower);
            Pen pen = rf ? RfPulsePen : SecurityPulsePen;
            int segments = rf ? 10 : 12;
            for (int i = 0; i < segments; i++)
            {
                double angle = i * Math.PI * 2 / segments;
                double cs = Math.Cos(angle), sn = Math.Sin(angle);
                dc.DrawLine(pen,
                    new Point(center.X + cs * radius,
                        center.Y + sn * radius * PerspectiveY),
                    new Point(center.X + cs * (radius + 12),
                        center.Y + sn * (radius + 12) * PerspectiveY));
            }
        }

        internal static void DrawLightningWarning(DrawingContext dc,
            Point tower, double timeRemaining)
        {
            // Projected warning brackets, with no surrounding warning circle.
            Point center = FenceCenter(tower);
            const double radius = 52;
            for (int i = 0; i < 4; i++)
            {
                double angle = i * Math.PI / 2 + Math.PI / 4;
                double cs = Math.Cos(angle), sn = Math.Sin(angle);
                Point outer = new(center.X + cs * radius,
                    center.Y + sn * radius * PerspectiveY);
                Point inner = new(center.X + cs * (radius - 13),
                    center.Y + sn * (radius - 13) * PerspectiveY);
                dc.DrawLine(LightningPen, outer, inner);
                dc.DrawEllipse(null, LightningWarning, outer, 2.5, 2.5);
            }
        }

        internal static void DrawDroneOrb(DrawingContext dc,
            Point orbitPosition, double orbitAngle, bool front, double seconds)
        {
            // Small articulated flying interceptor, not the previous large
            // neon bullseye. Front/back size and tone sell an angled orbit.
            double scale = front ? 1.12 : 0.82;
            double hover = Math.Sin(seconds * 6 + orbitAngle) * 1.5;
            dc.PushTransform(new TranslateTransform(
                orbitPosition.X, orbitPosition.Y));

            // Flattened ground shadow sits below the actual hovering unit.
            dc.DrawEllipse(OrbShadow, null, new Point(2.0, 10), 10, 3.4);
            dc.PushTransform(new TranslateTransform(0, -6 + hover));
            dc.PushTransform(new ScaleTransform(scale, scale));

            // A restrained orbit-direction motion streak.
            double dx = -Math.Sin(orbitAngle);
            double dy = Math.Cos(orbitAngle) * 0.6;
            dc.DrawLine(OrbAccentPen,
                new Point(-dx * 10, -dy * 10),
                new Point(-dx * 16, -dy * 16));

            // Armored six-sided hovering drone, two short stabilizer fins,
            // turquoise engine/optical core, compact blue power nodes.
            dc.DrawLine(OrbFinPen, new Point(-8, 2), new Point(-15, 5));
            dc.DrawLine(OrbFinPen, new Point(8, 2), new Point(15, 5));
            dc.DrawEllipse(OrbMetal, null, new Point(-15, 5), 2.5, 2);
            dc.DrawEllipse(OrbMetal, null, new Point(15, 5), 2.5, 2);
            dc.DrawGeometry(OrbShell, OrbEdgePen, OrbHex);
            dc.DrawRectangle(OrbMetal, null, new Rect(-5, -5, 10, 2));
            dc.DrawEllipse(OrbPulse, null, new Point(0, 1), 5.6, 5);
            dc.DrawEllipse(front ? OrbCore : OrbMetal, OrbAccentPen,
                new Point(0, 1), 3.2, 3.2);
            dc.DrawEllipse(OrbCore, null, new Point(2, -1), 1, 1);

            dc.Pop();
            dc.Pop();
            dc.Pop();
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

        private static Geometry FrozenEllipse(Point center,
            double radiusX, double radiusY)
        {
            var geometry = new EllipseGeometry(center, radiusX, radiusY);
            geometry.Freeze();
            return geometry;
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
