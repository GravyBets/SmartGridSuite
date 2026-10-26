using System;
using System.Windows;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Hand-drawn animated vector characters for the fictional radio-shop
    /// arcade. Coordinates are local to each enemy. No external sprites,
    /// assets or runtime image downloads are required.
    /// </summary>
    internal static class TowerDefenseEnemyArt
    {
        private static readonly Brush Outline = Ink("#081321");
        private static readonly Brush Shadow = Ink("#57010A18");
        private static readonly Brush Hoodie = Ink("#293E56");
        private static readonly Brush HoodieLight = Ink("#48657C");
        private static readonly Brush Denim = Ink("#283C60");
        private static readonly Brush Skin = Ink("#DDA584");
        private static readonly Brush SkinShade = Ink("#A76D54");
        private static readonly Brush Copper = Ink("#F1A65D");
        private static readonly Brush CopperShade = Ink("#B96A3F");
        private static readonly Brush Steel = Ink("#D6E2EE");
        private static readonly Brush SteelDark = Ink("#71869D");
        private static readonly Brush NeonCyan = Ink("#66F1F9");
        private static readonly Brush Yellow = Ink("#FFDF6B");
        private static readonly Brush Orange = Ink("#FF9B48");
        private static readonly Brush WorkVest = Ink("#EEB64E");
        private static readonly Brush Harness = Ink("#E9546D");
        private static readonly Brush Lime = Ink("#86ED50");
        private static readonly Brush Leaf = Ink("#38B95D");
        private static readonly Brush DarkLeaf = Ink("#187944");
        private static readonly Brush Beak = Ink("#F4AD31");
        private static readonly Brush White = Ink("#F5FCFF");
        private static readonly Brush Wing = Ink("#D5EDF6");
        private static readonly Brush WingShade = Ink("#8DC0D4");
        private static readonly Brush BeeBody = Ink("#F4C74F");
        private static readonly Brush BeeHaze = Ink("#35FFD66C");
        private static readonly Brush RedArmor = Ink("#B23D45");
        private static readonly Brush RedHighlight = Ink("#E96958");
        private static readonly Brush BossDark = Ink("#491F33");
        private static readonly Brush BossAura = Ink("#2AFA7857");
        private static readonly Brush DisturbedDirt = Ink("#715544");
        private static readonly Brush DirtEdge = Ink("#362C32");
        private static readonly Brush DirtRidge = Ink("#A87953");
        private static readonly Brush Weed = Ink("#5B8B59");
        private static readonly Brush InterferenceFrame = Ink("#987C9C");
        private static readonly Brush InterferenceArmor = Ink("#584664");
        private static readonly Brush InterferenceBeacon = Ink("#F56ACD");
        private static readonly Brush InterferenceGlow = Ink("#5BE971D8");

        private static readonly Pen OutlinePen = Stroke(Outline, 1.5);
        private static readonly Pen ThinOutline = Stroke(Outline, 1.0);
        private static readonly Pen DenimPen = Stroke(Denim, 5.0);
        private static readonly Pen HoodiePen = Stroke(Hoodie, 5.5);
        private static readonly Pen HoodieLightPen = Stroke(HoodieLight, 2.8);
        private static readonly Pen GlovePen = Stroke(Skin, 2.8);
        private static readonly Pen SteelPen = Stroke(Steel, 2.0);
        private static readonly Pen CopperPen = Stroke(Copper, 2.6);
        private static readonly Pen RedPen = Stroke(Harness, 2.7);
        private static readonly Pen DarkPen = Stroke(Outline, 3.3);
        private static readonly Pen JammerFramePen = Stroke(InterferenceFrame, 2.6);
        private static readonly Pen JammerBracePen = Stroke(InterferenceArmor, 1.8);
        private static readonly Pen DirtPen = Stroke(DirtEdge, 1.3);
        private static readonly Pen JammerWavePen = Stroke(InterferenceBeacon, 1.4);
        private static readonly Pen WeedPen = Stroke(Weed, 2);

        private static readonly Geometry HoodieBody = Shape(
            "M -8,-11 Q -12,-8 -11,2 L -8,9 L 8,9 L 10,1 Q 12,-8 7,-11 Z");
        private static readonly Geometry BoltCutterJaws = Shape(
            "M 0,0 L 5,-7 L 7,-6 L 5,-1 L 11,-4 L 13,-1 L 4,2 Z");
        private static readonly Geometry BirdBody = Shape(
            "M -15,-1 Q -9,-14 5,-8 Q 14,-5 13,3 " +
            "Q 5,11 -9,8 Q -15,4 -15,-1 Z");
        private static readonly Geometry BirdWing = Shape(
            "M -5,-3 Q -20,-18 -27,-23 Q -19,-24 -9,-17 " +
            "Q 2,-16 4,-7 L 1,0 Z");
        private static readonly Geometry BirdTail = Shape(
            "M -11,3 L -30,9 L -19,8 L -34,16 " +
            "Q -17,16 -6,9 Z");
        private static readonly Geometry BossTorso = Shape(
            "M -17,-17 L -25,-9 L -22,9 L -15,20 " +
            "L 15,20 L 22,9 L 25,-9 L 17,-17 Z");

        internal static double HealthBarOffset(int kind) => kind switch
        {
            1 => -36,
            2 => -27,
            3 => -27,
            4 => -58,
            _ => -32
        };

        internal static double HealthBarWidth(int kind) =>
            kind == 4 ? 86 : kind == 3 ? 38 : 33;

        /// <param name="kind">
        /// 0 = copper thief, 1 = tower climber, 2 = parakeet,
        /// 3 = bee swarm, 4 = Scrap King boss.
        /// </param>
        internal static void Draw(DrawingContext dc, int kind,
            Point location, double time, double seed, double climbSeconds,
            bool facingRight, double vaultProgress = -1)
        {
            // Individual phase offsets keep neighboring enemies from marching
            // in lockstep. All animations freeze naturally when paused.
            double phase = time * (kind == 3 ? 12 : kind == 2 ? 8.3 : 10) +
                seed * 2.7;
            dc.PushTransform(new TranslateTransform(location.X, location.Y));
            // Parakeets have a beak on the right in local coordinates, so
            // reverse the entire bird when it flies in from screen-right.
            bool directional = kind is 0 or 1 or 2 or 4;
            if (directional)
                dc.PushTransform(new ScaleTransform(facingRight ? 1 : -1, 1));

            bool vaulting = vaultProgress >= 0 && kind is 0 or 1 or 4;
            if (vaulting)
            {
                double p = Math.Clamp(vaultProgress, 0, 1);
                double lift = Math.Sin(p * Math.PI) *
                    (kind == 4 ? 23 : 17);
                // Shadow stays on the ground while the character vaults.
                dc.DrawEllipse(Shadow, null, new Point(0, 17),
                    kind == 4 ? 22 : 11, 2.7);
                dc.DrawLine(CopperPen, new Point(-11, 13),
                    new Point(-16, 11 - Math.Sin(p * Math.PI) * 3));
                dc.PushTransform(new TranslateTransform(
                    Math.Sin(p * Math.PI) * 3, -lift));
                dc.PushTransform(new RotateTransform(
                    (kind == 4 ? -9 : -17) * Math.Sin(p * Math.PI)));
            }

            switch (kind)
            {
                case 1:
                    DrawClimber(dc, phase, climbSeconds);
                    break;
                case 2:
                    DrawParakeet(dc, phase);
                    break;
                case 3:
                    DrawBeeSwarm(dc, phase);
                    break;
                case 4:
                    DrawScrapKing(dc, phase);
                    break;
                default:
                    DrawCopperThief(dc, phase);
                    break;
            }

            if (vaulting)
            {
                dc.Pop();
                dc.Pop();
            }
            if (directional)
                dc.Pop();
            dc.Pop();
        }

        internal static void DrawInterferenceTower(DrawingContext dc,
            Point position, double age, double seconds)
        {
            double rise = Math.Clamp(age / 1.35, 0, 1);
            double wobble = Math.Sin(seconds * 9 + position.X) * 0.7;
            dc.PushTransform(new TranslateTransform(position.X, position.Y));

            // Soil is disturbed as a hostile miniature repeater pushes up
            // outside the compound. Dust and grass frame its foundation.
            dc.DrawEllipse(Shadow, null, new Point(1, 11), 31, 9);
            dc.DrawEllipse(DisturbedDirt, DirtPen,
                new Point(0, 5), 29, 10);
            dc.DrawEllipse(DirtEdge, null, new Point(0, 4), 19, 5);
            dc.DrawEllipse(DirtRidge, null,
                new Point(-19, 4), 7, 3.6);
            dc.DrawEllipse(DirtRidge, null,
                new Point(17, 5), 9, 3.8);
            Line(dc, WeedPen, -23, 1, -27, -6);
            Line(dc, WeedPen, -23, 1, -19, -7);
            Line(dc, WeedPen, 25, 2, 29, -5);
            Line(dc, WeedPen, 25, 2, 22, -6);

            // The rising portion is clipped behind the ground lip.
            dc.PushClip(new RectangleGeometry(new Rect(-34, -63, 68, 68)));
            dc.PushTransform(new TranslateTransform(wobble, 29 * (1 - rise)));
            dc.DrawRoundedRectangle(InterferenceArmor, OutlinePen,
                new Rect(-14, -6, 28, 11), 2, 2);
            dc.DrawRectangle(SteelDark, null, new Rect(-11, -3, 22, 4));

            // Purposeful hostile tripod, cross-braces and RF panel hardware.
            Line(dc, JammerFramePen, 0, -47, -16, 2);
            Line(dc, JammerFramePen, 0, -47, 16, 2);
            Line(dc, JammerFramePen, -16, 2, 16, 2);
            for (int rung = 0; rung < 4; rung++)
            {
                double y = -36 + rung * 10;
                double half = 3 + rung * 3.1;
                Line(dc, JammerBracePen, -half, y, half + 3.5, y + 9);
                Line(dc, JammerBracePen, half, y, -half - 3.5, y + 9);
                Line(dc, JammerFramePen, -half, y, half, y);
            }

            // Two visibly directional sector antennas with RF emitters.
            dc.DrawRoundedRectangle(Steel, OutlinePen,
                new Rect(-17, -38, 6, 18), 2, 2);
            dc.DrawRoundedRectangle(Steel, OutlinePen,
                new Rect(11, -35, 6, 18), 2, 2);
            dc.DrawRectangle(InterferenceBeacon, null,
                new Rect(-16, -35, 2, 11));
            dc.DrawRectangle(InterferenceBeacon, null,
                new Rect(14, -32, 2, 11));
            Line(dc, JammerFramePen, 0, -47, 0, -54);
            dc.DrawEllipse(InterferenceGlow, null,
                new Point(0, -54), 7, 7);
            dc.DrawEllipse(InterferenceBeacon, ThinOutline,
                new Point(0, -54), 3.7, 3.7);

            // A short, intermittent pair of emissions, no large circles.
            double blink = 0.4 + 0.6 *
                Math.Abs(Math.Sin(seconds * 3 + position.X * 0.02));
            if (rise > 0.55 && blink > 0.6)
            {
                Line(dc, JammerWavePen, -23, -33, -28, -35);
                Line(dc, JammerWavePen, 23, -33, 28, -35);
            }

            dc.Pop();
            dc.Pop();

            // Foreground ridge masks the lower lattice during emergence.
            dc.DrawEllipse(DisturbedDirt, null,
                new Point(0, 7.3), 20, 4.5);
            dc.DrawEllipse(DirtRidge, null,
                new Point(-10, 6), 8, 2.3);
            dc.Pop();
        }

        private static void DrawCopperThief(DrawingContext dc, double phase)
        {
            double run = Math.Sin(phase) * 5.2;
            double bounce = Math.Abs(Math.Sin(phase)) * 1.6;

            dc.DrawEllipse(Shadow, null, new Point(0, 17), 12, 3);
            dc.PushTransform(new TranslateTransform(0, -bounce));

            // Long jeans, alternating strides and chunky dark shoes.
            Line(dc, DenimPen, -4, 6, -6 + run, 14);
            Line(dc, DenimPen, 4, 6, 6 - run, 14);
            Line(dc, DarkPen, -6 + run, 14, -11 + run, 15);
            Line(dc, DarkPen, 6 - run, 14, 12 - run, 15);

            dc.DrawGeometry(Hoodie, OutlinePen, HoodieBody);
            dc.DrawGeometry(HoodieLight, null,
                ShapeCache.HoodieHighlight);
            Line(dc, ThinOutline, 1, -8, 1, 7);
            dc.DrawEllipse(Steel, null, new Point(1, -1), 1.1, 1.1);

            // Raised hood, dark face-mask, eyes and bandana.
            dc.DrawEllipse(Hoodie, OutlinePen, new Point(0, -15), 9.6, 10.7);
            dc.DrawEllipse(Skin, null, new Point(1.3, -14), 6.0, 6.5);
            dc.DrawRectangle(Outline, null, new Rect(-4.7, -13, 11.8, 5.9));
            dc.DrawRectangle(White, null, new Rect(1.8, -15.5, 2.5, 1.7));
            dc.DrawEllipse(HoodieLight, null,
                new Point(-4.0, -22), 4.2, 1.5);

            // Running arms and a bag of stolen copper on his left side.
            Line(dc, HoodiePen, -9, -7, -15, -2 - run * 0.38);
            Line(dc, GlovePen, -15, -2 - run * 0.38, -14, 3 - run * 0.38);
            dc.DrawEllipse(CopperShade, OutlinePen,
                new Point(-12.5, 8), 6.0, 5.4);
            Line(dc, CopperPen, -17, 7, -9, 7);
            Line(dc, CopperPen, -16, 10, -10, 10);

            Line(dc, HoodiePen, 9, -7, 14, -1 + run * 0.3);
            Line(dc, GlovePen, 14, -1 + run * 0.3, 15, 3 + run * 0.3);

            // Big bolt cutters—orange grips with crossed steel jaws.
            Line(dc, CopperPen, 15, 4, 20, -10);
            Line(dc, CopperPen, 17, 4, 23, -7);
            dc.PushTransform(new TranslateTransform(20, -10));
            dc.DrawGeometry(Steel, ThinOutline, BoltCutterJaws);
            dc.Pop();
            dc.Pop();
        }

        private static void DrawClimber(DrawingContext dc,
            double phase, double climbSeconds)
        {
            double reach = Math.Sin(phase * 0.66) * 2.6;
            double lift = climbSeconds > 0
                ? Math.Sin(phase * 0.32) * 2.5 : 0;

            dc.DrawEllipse(Shadow, null, new Point(0, 18), 12, 2.3);
            dc.PushTransform(new TranslateTransform(0, -lift));

            // Orange hardhat, goggles, reflective vest and climbing harness.
            Line(dc, DenimPen, -4, 7, -8 + reach, 17);
            Line(dc, DenimPen, 4, 7, 7 - reach, 17);
            Line(dc, DarkPen, -8 + reach, 17, -13 + reach, 18);
            Line(dc, DarkPen, 7 - reach, 17, 12 - reach, 18);

            dc.DrawGeometry(WorkVest, OutlinePen, HoodieBody);
            dc.DrawRectangle(White, null, new Rect(-6.5, -9, 2.1, 14));
            dc.DrawRectangle(White, null, new Rect(4.5, -9, 2.1, 14));
            Line(dc, RedPen, -7, -4, 7, 7);
            Line(dc, RedPen, 7, -4, -7, 7);
            dc.DrawEllipse(SteelDark, OutlinePen, new Point(0, 6), 3, 3);

            // Arms reaching up to grasp a virtual ladder; when attached,
            // the hanging rope makes the climber instantly recognizable.
            Line(dc, HoodiePen, -9, -8, -13 - reach, -19);
            Line(dc, HoodiePen, 9, -8, 13 + reach, -19);
            dc.DrawEllipse(Skin, null,
                new Point(-13 - reach, -19), 3, 3);
            dc.DrawEllipse(Skin, null,
                new Point(13 + reach, -19), 3, 3);
            dc.DrawEllipse(Skin, OutlinePen, new Point(0, -17), 7.8, 8.2);
            dc.DrawEllipse(Orange, OutlinePen, new Point(0, -22), 11, 5.5);
            dc.DrawRectangle(Orange, OutlinePen, new Rect(-12, -22, 24, 3.6));
            dc.DrawRectangle(SteelDark, null,
                new Rect(-5.5, -17, 11, 3.1));

            if (climbSeconds > 0)
            {
                Line(dc, SteelPen, -14, -27, -14, 21);
                for (int n = 0; n < 4; n++)
                    Line(dc, ThinOutline, -16, -23 + n * 11,
                        -11, -23 + n * 11);
                dc.DrawEllipse(Orange, null,
                    new Point(-14, 3), 2.7, 3.5);
            }
            dc.Pop();
        }

        private static void DrawParakeet(DrawingContext dc, double phase)
        {
            double flap = Math.Sin(phase) * 26;
            double floatY = Math.Sin(phase * 0.45) * 3;
            dc.DrawEllipse(Shadow, null, new Point(0, 18), 12, 2.3);
            dc.PushTransform(new TranslateTransform(0, floatY));

            // Long split tail with a bright lime/turquoise parrot silhouette.
            dc.DrawGeometry(DarkLeaf, ThinOutline, BirdTail);
            dc.DrawGeometry(Lime, OutlinePen, BirdBody);
            dc.DrawEllipse(Leaf, null, new Point(-6, 2), 8.3, 5.7);

            // Two independently animated wings built from tapered feathers.
            dc.PushTransform(new RotateTransform(flap, -2, -7));
            dc.DrawGeometry(Leaf, OutlinePen, BirdWing);
            Line(dc, StrokeCache.Feather, -1, -9, -18, -20);
            dc.Pop();

            dc.PushTransform(new RotateTransform(-flap * 0.67, 2, -2));
            dc.DrawGeometry(DarkLeaf, ThinOutline, ShapeCache.LowerWing);
            dc.Pop();

            dc.DrawEllipse(Lime, OutlinePen, new Point(10, -6), 7.5, 7.8);
            dc.DrawGeometry(Beak, ThinOutline, ShapeCache.ParakeetBeak);
            dc.DrawEllipse(Outline, null, new Point(12.9, -9), 2.0, 2.1);
            dc.DrawEllipse(White, null, new Point(13.4, -9.6), 0.75, 0.75);

            // Parakeet's crest and long turquoise flight feathers.
            Line(dc, StrokeCache.Crest, 5, -12, 2, -18);
            Line(dc, StrokeCache.Crest, 9, -13, 9, -20);
            Line(dc, StrokeCache.Tail, -15, 4, -29, 15);
            dc.Pop();
        }

        private static void DrawBeeSwarm(DrawingContext dc, double phase)
        {
            // Not one giant yellow blob: seven distinct insects, wing beats
            // at different phases, moving around a translucent swarm cloud.
            dc.DrawEllipse(BeeHaze, null, new Point(0, -1), 22, 16);
            dc.DrawEllipse(Shadow, null, new Point(0, 18), 13, 2.3);

            for (int n = 0; n < 7; n++)
            {
                double theta = n * (Math.PI * 2 / 7) + phase * 0.29;
                double orbit = n % 2 == 0 ? 14 : 9;
                double x = Math.Cos(theta) * orbit +
                    Math.Sin(phase * 1.37 + n) * 2.3;
                double y = Math.Sin(theta) * (orbit * 0.72) -
                    Math.Abs(Math.Cos(phase * 1.8 + n)) * 3;
                double beat = Math.Sin(phase * 2.7 + n * 1.4) * 3;

                // Double translucent wings and striped, segmented bodies.
                dc.DrawEllipse(WingShade, null,
                    new Point(x - 3, y - 3.8 - beat * 0.25), 3.5, 2.1);
                dc.DrawEllipse(Wing, null,
                    new Point(x + 3, y - 3.8 + beat * 0.25), 3.5, 2.1);
                dc.DrawEllipse(BeeBody, ThinOutline,
                    new Point(x, y), 5.1, 3.5);
                Line(dc, StrokeCache.BeeStripe,
                    x - 1.3, y - 2.4, x - 1.3, y + 2.4);
                Line(dc, StrokeCache.BeeStripe,
                    x + 1.9, y - 2.2, x + 1.9, y + 2.2);
                dc.DrawEllipse(Outline, null,
                    new Point(x + 4, y - 0.8), 1.4, 1.3);
            }
        }

        private static void DrawScrapKing(DrawingContext dc, double phase)
        {
            double stomp = Math.Sin(phase * 0.36) * 4;
            double shoulderBounce = Math.Abs(Math.Sin(phase * 0.36)) * 2.1;

            // Distinct red warning aura, huge shadow, giant coat and armor.
            dc.DrawEllipse(BossAura, StrokeCache.Aura,
                new Point(0, -3), 36, 36);
            dc.DrawEllipse(Shadow, null,
                new Point(0, 29), 28, 6);
            dc.PushTransform(new TranslateTransform(0, -shoulderBounce));

            // Heavy boots stomping out of phase, with steel toe caps.
            Line(dc, StrokeCache.BossLeg, -10, 14, -13 + stomp, 27);
            Line(dc, StrokeCache.BossLeg, 10, 14, 13 - stomp, 27);
            dc.DrawRectangle(Outline, null,
                new Rect(-24 + stomp, 23, 17, 8));
            dc.DrawRectangle(Outline, null,
                new Rect(8 - stomp, 23, 18, 8));
            dc.DrawRectangle(SteelDark, null,
                new Rect(-24 + stomp, 23, 6, 4));
            dc.DrawRectangle(SteelDark, null,
                new Rect(20 - stomp, 23, 6, 4));

            dc.DrawGeometry(BossDark, StrokeCache.BossOutline, BossTorso);
            dc.DrawGeometry(RedArmor, OutlinePen, BossTorso);
            dc.DrawRectangle(BossDark, null, new Rect(-7, -14, 14, 31));
            dc.DrawRectangle(RedHighlight, null, new Rect(-12, -12, 5, 23));
            dc.DrawRectangle(RedHighlight, null, new Rect(9, -12, 5, 23));
            dc.DrawRectangle(Yellow, OutlinePen, new Rect(-7, 5, 14, 7));
            dc.DrawEllipse(Steel, ThinOutline, new Point(0, 9), 3, 3);

            // Extra-wide shoulder plates with rivets and industrial spikes.
            dc.DrawEllipse(RedArmor, OutlinePen,
                new Point(-21, -14), 13, 11);
            dc.DrawEllipse(RedArmor, OutlinePen,
                new Point(21, -14), 13, 11);
            dc.DrawGeometry(SteelDark, ThinOutline, ShapeCache.LeftShoulderSpike);
            dc.DrawGeometry(SteelDark, ThinOutline, ShapeCache.RightShoulderSpike);
            dc.DrawEllipse(Steel, null, new Point(-25, -16), 2, 2);
            dc.DrawEllipse(Steel, null, new Point(25, -16), 2, 2);

            Line(dc, StrokeCache.BossArm, -27, -10, -32, 8);
            Line(dc, StrokeCache.BossArm, 27, -10, 32, 10);
            dc.DrawEllipse(BossDark, OutlinePen,
                new Point(-33, 10), 8, 8);
            dc.DrawEllipse(BossDark, OutlinePen,
                new Point(33, 12), 8, 8);

            // Welding-mask face, luminous eyes and riveted breathing grille.
            dc.DrawEllipse(Outline, OutlinePen,
                new Point(0, -25), 13, 14);
            dc.DrawGeometry(SteelDark, OutlinePen,
                ShapeCache.BossMask);
            dc.DrawRectangle(NeonCyan, null,
                new Rect(-8, -29, 6, 2.8));
            dc.DrawRectangle(NeonCyan, null,
                new Rect(3, -29, 6, 2.8));
            dc.DrawRectangle(Outline, null,
                new Rect(-7, -22, 14, 6));
            for (int grill = 0; grill < 3; grill++)
                dc.DrawRectangle(Steel, null,
                    new Rect(-4 + grill * 4, -21, 1.8, 4.5));
            dc.DrawGeometry(RedArmor, OutlinePen,
                ShapeCache.BossCrown);

            // Three-loop copper scrap coil slung over left shoulder.
            for (int coil = 0; coil < 3; coil++)
                dc.DrawEllipse(null, CopperPen,
                    new Point(-30 - coil * 3.0, 7 - coil * 4),
                    8, 10);

            // Massive articulated cutters with steel hooked jaw.
            Line(dc, StrokeCache.CutterGrip, 33, 12, 43, -23);
            Line(dc, StrokeCache.CutterGrip, 28, 9, 38, -21);
            dc.PushTransform(new TranslateTransform(41, -24));
            dc.PushTransform(new ScaleTransform(2.0, 2.0));
            dc.DrawGeometry(Steel, OutlinePen, BoltCutterJaws);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private static void Line(DrawingContext dc, Pen pen,
            double x1, double y1, double x2, double y2) =>
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

        private static Brush Ink(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter()
                .ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private static Pen Stroke(Brush brush, double size)
        {
            var pen = new Pen(brush, size);
            pen.Freeze();
            return pen;
        }

        private static Geometry Shape(string path)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }

        private static class ShapeCache
        {
            internal static readonly Geometry HoodieHighlight =
                Shape("M -9,-8 Q -6,-12 -4,-10 L -6,6 L -8,5 Z");
            internal static readonly Geometry LowerWing =
                Shape("M -5,0 Q -8,15 -22,17 Q -20,6 -8,-7 Z");
            internal static readonly Geometry ParakeetBeak =
                Shape("M 15,-6 L 25,-4 L 16,0 Z");
            internal static readonly Geometry LeftShoulderSpike =
                Shape("M -33,-21 L -40,-36 L -23,-26 Z");
            internal static readonly Geometry RightShoulderSpike =
                Shape("M 33,-21 L 40,-36 L 23,-26 Z");
            internal static readonly Geometry BossMask =
                Shape("M -12,-31 L -11,-39 L 11,-39 L 12,-31 " +
                      "L 10,-19 L -10,-19 Z");
            internal static readonly Geometry BossCrown =
                Shape("M -11,-35 L -16,-43 L -5,-39 L 0,-47 " +
                      "L 5,-39 L 16,-43 L 11,-35 Z");
        }

        private static class StrokeCache
        {
            internal static readonly Pen Feather = Stroke(Lime, 1.5);
            internal static readonly Pen Crest = Stroke(Leaf, 2.0);
            internal static readonly Pen Tail = Stroke(NeonCyan, 2.2);
            internal static readonly Pen BeeStripe = Stroke(Outline, 1.2);
            internal static readonly Pen Aura = Stroke(RedHighlight, 1.9);
            internal static readonly Pen BossLeg = Stroke(BossDark, 13);
            internal static readonly Pen BossOutline = Stroke(Outline, 3.3);
            internal static readonly Pen BossArm = Stroke(RedArmor, 12);
            internal static readonly Pen CutterGrip = Stroke(CopperShade, 5);
        }
    }
}
