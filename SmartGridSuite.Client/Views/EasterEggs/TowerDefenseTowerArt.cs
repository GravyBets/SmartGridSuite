using System;
using System.Windows;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Cached WPF vector rendering for the fictional central radio site and
    /// its animated, individually aiming gun mounts. Art only: no network or
    /// real radio/tower access.
    /// </summary>
    internal static class TowerDefenseTowerArt
    {
        private static readonly Brush Outline = Ink("#071424");
        private static readonly Brush Shadow = Ink("#60050D1F");
        private static readonly Brush Concrete = Ink("#536B7F");
        private static readonly Brush ConcreteTop = Ink("#91A5B4");
        private static readonly Brush MetalDark = Ink("#354D62");
        private static readonly Brush MetalMid = Ink("#6E8DA5");
        private static readonly Brush MetalLight = Ink("#BFD3E2");
        private static readonly Brush MetalWhite = Ink("#E8F6FE");
        private static readonly Brush Turquoise = Ink("#64EAF7");
        private static readonly Brush Blue = Ink("#3383BD");
        private static readonly Brush CyanGlow = Ink("#245DECF6");
        private static readonly Brush Red = Ink("#F15D68");
        private static readonly Brush RedGlow = Ink("#54FF566F");
        private static readonly Brush Gold = Ink("#F8C85B");
        private static readonly Brush Orange = Ink("#E78F4B");
        private static readonly Brush Purple = Ink("#C79AFF");
        private static readonly Brush PurpleGlow = Ink("#4BC79AFF");
        private static readonly Brush Cabinet = Ink("#AABDC9");
        private static readonly Brush CabinetDark = Ink("#496579");
        private static readonly Brush Grass = Ink("#164354");

        private static readonly Pen SteelEdge = Line(MetalLight, 3.2);
        private static readonly Pen SteelCross = Line(MetalMid, 1.8);
        private static readonly Pen DarkEdge = Line(Outline, 1.5);
        private static readonly Pen CablePen = Line(Outline, 2.3);
        private static readonly Pen YellowPen = Line(Gold, 2.3);
        private static readonly Pen WhitePen = Line(MetalWhite, 1.3);
        private static readonly Pen RedPen = Line(Red, 2.4);
        private static readonly Pen RingPen = Line(Turquoise, 1.2);
        private static readonly Pen TurretPlatformPen = Line(MetalWhite, 1.6);
        private static readonly Pen TurretBarrelPen = Line(Outline, 1.2);

        private static readonly Geometry BasePlatform = Shape(
            "M -51,40 L -35,31 L 39,31 L 54,40 L 40,53 L -42,53 Z");
        private static readonly Geometry EquipmentHut = Shape(
            "M 26,13 L 61,13 L 61,38 L 26,38 Z");
        private static readonly Geometry HutRoof = Shape(
            "M 22,14 L 29,7 L 57,7 L 65,14 Z");
        private static readonly Geometry MainTowerFront = Shape(
            "M -29,36 L -8,-78 L 8,-78 L 30,36 Z");
        private static readonly Geometry LeftPanel = Shape(
            "M -25,-58 L -19,-68 L -13,-63 L -17,-47 Z");
        private static readonly Geometry RightPanel = Shape(
            "M 15,-64 L 21,-60 L 23,-43 L 17,-48 Z");
        private static readonly Geometry TurretShell = Shape(
            "M -11,-8 L -4,-13 L 12,-10 L 16,-4 L 16,5 " +
            "L 9,10 L -9,7 Z");
        private static readonly Geometry EnergyShell = Shape(
            "M -11,-7 L -3,-11 L 11,-10 L 17,-5 L 18,4 " +
            "L 9,9 L -9,8 Z");
        private static readonly Geometry DishFace = Shape(
            "M -10,-8 Q -17,0 -10,8 L -1,7 Q -6,0 -1,-7 Z");

        internal static Point TurretOrigin(Point center, bool right) =>
            new(center.X + (right ? 34 : -34), center.Y - 36);

        internal static double MastTipY(Point center) => center.Y - 109;

        internal static void DrawSite(DrawingContext dc, Point center,
            double seconds, int lightning, int surge, int filtering)
        {
            dc.PushTransform(new TranslateTransform(center.X, center.Y));

            // A small rectangular equipment-pad glow, not more concentric
            // circles competing with the main site's fence and UI overlays.
            dc.DrawEllipse(Shadow, null, new Point(5, 46), 78, 23);
            dc.DrawRoundedRectangle(Grass, null,
                new Rect(-73, -2, 146, 76), 11, 11);
            dc.DrawRoundedRectangle(CyanGlow, null,
                new Rect(-57, 12, 114, 47), 7, 7);

            // Heavy concrete slab with depth, tie-down bolts, warning stripes.
            dc.DrawGeometry(Concrete, DarkEdge, BasePlatform);
            dc.DrawRoundedRectangle(ConcreteTop, DarkEdge,
                new Rect(-44, 29, 88, 17), 3, 3);
            dc.DrawRectangle(Gold, null, new Rect(-37, 44, 74, 2));
            for (int bolt = 0; bolt < 4; bolt++)
                dc.DrawEllipse(MetalWhite, null,
                    new Point(-34 + bolt * 22, 38), 1.7, 1.7);

            // Equipment cabinet and weatherproof radio shelter at the base.
            dc.DrawGeometry(CabinetDark, DarkEdge, EquipmentHut);
            dc.DrawGeometry(MetalMid, DarkEdge, HutRoof);
            dc.DrawRoundedRectangle(Cabinet, DarkEdge,
                new Rect(31, 17, 24, 19), 2, 2);
            dc.DrawRectangle(MetalDark, null, new Rect(34, 20, 16, 7));
            dc.DrawRectangle(Turquoise, null, new Rect(36, 22, 12, 2));
            dc.DrawEllipse(Gold, null, new Point(51, 32), 1.4, 1.4);
            dc.DrawLine(CablePen, new Point(27, 24), new Point(16, 18));
            dc.DrawLine(CablePen, new Point(16, 18), new Point(10, -4));

            // Rear bracing makes the site read as a dimensional lattice
            // rather than the single skinny triangle in the old version.
            dc.DrawLine(Line(MetalDark, 5), new Point(-20, 32),
                new Point(-4, -79));
            dc.DrawLine(Line(MetalDark, 5), new Point(20, 32),
                new Point(4, -79));
            dc.DrawGeometry(MetalDark, DarkEdge, MainTowerFront);

            // Tapered four-legged mast: side rails, central spine, diagonal
            // cross-braces and horizontal service platforms.
            dc.DrawLine(SteelEdge, new Point(-29, 36), new Point(-8, -78));
            dc.DrawLine(SteelEdge, new Point(30, 36), new Point(8, -78));
            dc.DrawLine(WhitePen, new Point(-15, 32), new Point(-4, -78));
            dc.DrawLine(WhitePen, new Point(16, 32), new Point(4, -78));
            dc.DrawLine(Line(MetalMid, 2.0),
                new Point(0, -80), new Point(0, 37));
            for (int i = 0; i < 7; i++)
            {
                double y = -65 + i * 15.2;
                double half = 9.0 + (y + 78) / 114.0 * 20.0;
                double bottomHalf = half + 2.8;
                dc.DrawLine(SteelCross,
                    new Point(-half, y), new Point(half, y));
                dc.DrawLine(SteelCross,
                    new Point(-half, y), new Point(bottomHalf, y + 14));
                dc.DrawLine(SteelCross,
                    new Point(half, y), new Point(-bottomHalf, y + 14));
            }

            // Ladder rails and rungs, subtly set to the tower's right face.
            dc.DrawLine(CablePen, new Point(5, -72), new Point(12, 28));
            dc.DrawLine(CablePen, new Point(10, -72), new Point(17, 28));
            for (int rung = 0; rung < 11; rung++)
            {
                double y = -67 + rung * 8.8;
                double x = 7.3 + rung * 0.55;
                dc.DrawLine(WhitePen, new Point(x, y), new Point(x + 5, y));
            }

            // Two alternating warning bands and a real-looking maintenance
            // platform, complete with safety railings.
            dc.DrawRectangle(Red, null, new Rect(-13, -44, 27, 5));
            dc.DrawRectangle(MetalWhite, null, new Rect(-12, -40, 25, 3));
            dc.DrawRoundedRectangle(MetalLight, DarkEdge,
                new Rect(-32, -52, 64, 6), 2, 2);
            dc.DrawLine(WhitePen, new Point(-30, -52), new Point(-30, -62));
            dc.DrawLine(WhitePen, new Point(30, -52), new Point(30, -62));
            dc.DrawLine(WhitePen, new Point(-30, -62), new Point(30, -62));

            // Sector antennas: narrow white radomes with brackets and cable.
            dc.DrawGeometry(Cabinet, DarkEdge, LeftPanel);
            dc.DrawGeometry(MetalWhite, DarkEdge, RightPanel);
            dc.DrawRoundedRectangle(MetalWhite, DarkEdge,
                new Rect(-21, -89, 8, 35), 2, 2);
            dc.DrawRoundedRectangle(MetalWhite, DarkEdge,
                new Rect(13, -84, 8, 33), 2, 2);
            dc.DrawLine(CablePen, new Point(-15, -54), new Point(-11, -23));
            dc.DrawLine(CablePen, new Point(17, -51), new Point(16, -25));

            // Offset microwave dish with feed horn, for a recognizable
            // communications tower silhouette even at laptop resolution.
            dc.DrawLine(SteelEdge, new Point(13, -48), new Point(37, -72));
            dc.PushTransform(new TranslateTransform(38, -74));
            dc.DrawGeometry(MetalLight, DarkEdge, DishFace);
            dc.DrawEllipse(MetalWhite, null, new Point(-10, 0), 3.8, 7);
            dc.DrawLine(YellowPen, new Point(-4, 0), new Point(9, 0));
            dc.DrawEllipse(Gold, null, new Point(9, 0), 2.7, 2.7);
            dc.Pop();

            // Mast cap, lightning rod and blinking aviation beacon.
            dc.DrawRoundedRectangle(MetalDark, WhitePen,
                new Rect(-10, -87, 20, 9), 2, 2);
            dc.DrawLine(SteelEdge, new Point(0, -87), new Point(0, -107));
            dc.DrawLine(RingPen, new Point(0, -107), new Point(0, -116));
            bool beaconOn = Math.Sin(seconds * 5.0) > 0;
            dc.DrawEllipse(beaconOn ? RedGlow : Red, null,
                new Point(0, -101), beaconOn ? 11 : 6, beaconOn ? 11 : 6);
            dc.DrawEllipse(beaconOn ? Red : MetalDark, WhitePen,
                new Point(0, -101), 4, 4);

            if (lightning > 0)
            {
                dc.DrawLine(YellowPen, new Point(0, -116),
                    new Point(0, -122));
                dc.DrawEllipse(Gold, null,
                    new Point(0, -122), 2.7 + Math.Min(3, lightning), 2.7);
            }
            if (surge > 0)
            {
                dc.DrawRoundedRectangle(MetalDark, RingPen,
                    new Rect(-50, 25, 13, 19), 2, 2);
                dc.DrawEllipse(Turquoise, null,
                    new Point(-43.5, 32), 2.7, 2.7);
            }
            if (filtering > 0)
            {
                // RF filtering is indicated on the cabinet and sector feed
                // rather than a large inaccurate radio-wave ellipse.
                for (int i = 0; i < Math.Min(3, filtering); i++)
                {
                    double x = 33 + i * 6;
                    dc.DrawLine(RingPen,
                        new Point(x, 17), new Point(x, 10));
                    dc.DrawEllipse(Turquoise, null,
                        new Point(x, 8), 1.7, 1.7);
                }
            }

            dc.Pop();
        }

        internal static void DrawTurret(DrawingContext dc, Point basePoint,
            Point? aim, bool right, bool firing)
        {
            dc.PushTransform(new TranslateTransform(basePoint.X, basePoint.Y));

            // A reinforced bolted platform fixed to the tower's horizontal
            // cross-beam. This remains stationary as the gun pivots.
            dc.DrawRoundedRectangle(MetalDark, DarkEdge,
                new Rect(-19, -3, 38, 12), 3, 3);
            dc.DrawLine(WhitePen, new Point(-23, 10), new Point(20, 10));
            dc.DrawLine(SteelCross, new Point(-17, 8), new Point(-6, 19));
            dc.DrawLine(SteelCross, new Point(17, 8), new Point(7, 19));
            dc.DrawEllipse(MetalLight, TurretPlatformPen, new Point(0, 0), 11, 11);
            dc.DrawEllipse(Outline, null, new Point(0, 0), 4, 4);
            for (int i = 0; i < 4; i++)
                dc.DrawEllipse(MetalWhite, null,
                    new Point(Math.Cos(i * Math.PI / 2) * 7,
                        Math.Sin(i * Math.PI / 2) * 7), 1.3, 1.3);

            double dx = aim?.X - basePoint.X ?? (right ? 1 : -1);
            double dy = aim?.Y - basePoint.Y ?? -0.4;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            dc.PushTransform(new RotateTransform(angle));

            if (!right)
            {
                // Ballistic / kinetic turret with twin independently
                // recognizable dark barrels, armored housing and heat vents.
                dc.DrawGeometry(MetalMid, DarkEdge, TurretShell);
                dc.DrawRoundedRectangle(MetalDark, DarkEdge,
                    new Rect(9, -8, 14, 6), 1, 1);
                dc.DrawRoundedRectangle(MetalDark, DarkEdge,
                    new Rect(9, 3, 14, 6), 1, 1);
                dc.DrawRectangle(MetalWhite, null, new Rect(20, -7, 6, 4));
                dc.DrawRectangle(MetalWhite, null, new Rect(20, 4, 6, 4));
                dc.DrawRectangle(Gold, null, new Rect(-6, -7, 12, 2));
                for (int vent = 0; vent < 3; vent++)
                    dc.DrawRectangle(Outline, null,
                        new Rect(-7 + vent * 5, 3, 2.5, 4));
                dc.DrawEllipse(Gold, DarkEdge, new Point(-6, 0), 2.8, 2.8);
                if (firing)
                {
                    dc.DrawEllipse(Gold, null, new Point(32, 0), 10, 7);
                    dc.DrawEllipse(Orange, null, new Point(32, 0), 5.5, 4.5);
                }
            }
            else
            {
                // Distinct blue/purple RF pulse cannon: shielded gimbal,
                // induction coils, glowing barrel and energy muzzle.
                dc.DrawGeometry(MetalDark, DarkEdge, EnergyShell);
                dc.DrawEllipse(PurpleGlow, null, new Point(5, 0), 17, 14);
                dc.DrawRoundedRectangle(Blue, TurretBarrelPen,
                    new Rect(8, -5, 24, 10), 2, 2);
                for (int coil = 0; coil < 4; coil++)
                    dc.DrawRectangle(coil % 2 == 0 ? Purple : Turquoise,
                        null, new Rect(11 + coil * 6, -6, 2.5, 12));
                dc.DrawEllipse(MetalWhite, DarkEdge, new Point(30, 0), 6, 7);
                dc.DrawEllipse(Turquoise, null, new Point(31, 0), 3, 4);
                dc.DrawEllipse(Purple, DarkEdge, new Point(-5, 0), 3, 3);
                if (firing)
                {
                    dc.DrawEllipse(PurpleGlow, null,
                        new Point(39, 0), 15, 15);
                    dc.DrawEllipse(Turquoise, RingPen,
                        new Point(39, 0), 7, 7);
                }
            }

            dc.Pop();
            dc.Pop();
        }

        private static Geometry Shape(string path)
        {
            var g = Geometry.Parse(path);
            g.Freeze();
            return g;
        }

        private static Brush Ink(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
            brush.Freeze();
            return brush;
        }

        private static Pen Line(Brush brush, double width)
        {
            var pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }
    }
}
