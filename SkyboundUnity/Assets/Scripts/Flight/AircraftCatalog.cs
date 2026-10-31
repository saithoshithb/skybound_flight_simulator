using UnityEngine;
using static Skybound.FlightMath;

namespace Skybound
{
    /// <summary>
    /// The two aircraft, as data. Values are in the range of light aircraft, taken
    /// from textbook data, not any manufacturer's. The assets in Assets/Data are
    /// generated from these.
    /// </summary>
    public static class AircraftCatalog
    {
        static AircraftDef trainer, sport;

        public static AircraftDef Trainer => trainer != null ? trainer : trainer = CreateTrainer();
        public static AircraftDef Sport => sport != null ? sport : sport = CreateSport();
        public static AircraftDef[] All => new[] { Trainer, Sport };

        public static AircraftDef ById(string id) => id == Sport.id ? Sport : Trainer;

        /// <summary>Prefer the project's saved assets, so numbers edited there take effect.</summary>
        public static void Use(AircraftDef[] assets)
        {
            if (assets == null) return;
            foreach (var a in assets)
            {
                if (a == null) continue;
                if (a.id == "trainer") trainer = a;
                if (a.id == "sport") sport = a;
            }
        }

        public static AircraftDef CreateTrainer()
        {
            var a = ScriptableObject.CreateInstance<AircraftDef>();
            a.name = "Trainer";
            a.id = "trainer";
            a.displayName = "Highwing 160 trainer";
            a.mass = 1043;
            a.inertia = new V3(1285, 1825, 2667);
            a.wingArea = 16.2;
            a.span = 10.9;
            a.chord = 1.49;
            a.oswald = 0.75;
            a.maxPowerW = 119000;
            a.propEfficiency = 0.8;
            a.staticThrustN = 2600;
            a.engineTimeConstantS = 0.6;
            a.CL0 = 0.31;
            a.CLalpha = 5.143;
            a.CLq = 3.9;
            a.CLde = 0.43;
            a.CD0 = 0.031;
            // Blend centre and sharpness: gives CLmax of about 1.55 at about 16° with a gradual break.
            a.alphaStall = 20 * DEG;
            a.stallSharpness = 28;
            a.CYbeta = -0.31;
            a.CYdr = -0.187;
            a.Clbeta = -0.089;
            a.Clp = -0.47;
            a.Clr = 0.096;
            a.Clda = 0.178;
            a.Cldr = -0.0147;
            a.Cm0 = -0.015;
            a.Cmalpha = -0.89;
            a.Cmq = -12.4;
            a.Cmde = -1.28;
            a.Cnbeta = 0.065;
            a.Cnp = -0.03;
            a.Cnr = -0.099;
            a.Cnda = -0.053;
            a.Cndr = 0.0657;
            a.maxElevator = 25 * DEG;
            a.maxAileron = 20 * DEG;
            a.maxRudder = 20 * DEG;
            a.surfaceLagS = 0.06;
            a.maxNoseSteer = 25 * DEG;
            a.gear = new[]
            {
                Leg("nose", 1.25, 0, 1.05, 35000, 4500, steerable: true),
                Leg("left main", -0.35, -1.25, 1.05, 55000, 6500, brakes: true),
                Leg("right main", -0.35, 1.25, 1.05, 55000, 6500, brakes: true),
                // Over-rotation scrapes the tail (about 13° nose-up on the mains) instead of ending the flight.
                Leg("tail", -5.2, 0, -0.1, 90000, 9000, skid: true),
            };
            a.structure = new[]
            {
                Point("Left wingtip", -0.2, -5.45, -1.0),
                Point("Right wingtip", -0.2, 5.45, -1.0),
                Point("Propeller", 2.3, 0, 0.75),
                Point("Fuselage", 0, 0, 0.55),
            };
            a.vneMs = 84;
            a.description = "Forgiving four-seat trainer. Stable, slow to stall, easy to land.";
            a.look = new AircraftLook { highWing = true, body = "#efede6", stripe = "#24405a", accent = "#a8412f" };
            a.easy = new EasySpeeds { cruiseKt = 95, takeoffKt = 90, minKt = 60, maxKt = 115, rotateKt = 55 };
            return a;
        }

        /// <summary>
        /// A livelier two-seat low-wing sport aircraft: more power, a smaller wing
        /// (faster, higher stall speed), quicker roll and lighter pitch damping.
        /// </summary>
        public static AircraftDef CreateSport()
        {
            var a = CreateTrainer();
            a.name = "Sport";
            a.id = "sport";
            a.displayName = "Lowwing 200 sport";
            a.mass = 1100;
            a.inertia = new V3(1150, 1750, 2600);
            a.wingArea = 14.2;
            a.span = 9.4;
            a.chord = 1.52;
            a.oswald = 0.78;
            a.maxPowerW = 150000;
            a.staticThrustN = 3400;
            a.CL0 = 0.26;
            a.CLalpha = 5.0;
            a.CD0 = 0.027;
            a.Clp = -0.42;
            a.Clda = 0.23;
            a.Cmq = -10.5;
            a.Cm0 = -0.02;
            a.maxAileron = 22 * DEG;
            a.gear = new[]
            {
                Leg("nose", 1.3, 0, 1.05, 36000, 4600, steerable: true),
                Leg("left main", -0.3, -1.4, 1.05, 58000, 6800, brakes: true),
                Leg("right main", -0.3, 1.4, 1.05, 58000, 6800, brakes: true),
                Leg("tail", -5.0, 0, -0.1, 90000, 9000, skid: true),
            };
            a.structure = new[]
            {
                // Low wing: tips sit below the CG, so bank on the runway is limited to about 9°.
                Point("Left wingtip", -0.1, -4.7, 0.3),
                Point("Right wingtip", -0.1, 4.7, 0.3),
                Point("Propeller", 2.3, 0, 0.75),
                Point("Fuselage", 0, 0, 0.55),
            };
            a.vneMs = 100;
            a.description = "Faster two-seat sport plane. More power and a crisper roll; lands faster.";
            a.look = new AircraftLook { highWing = false, body = "#e9e6df", stripe = "#7b2f2a", accent = "#2f3e4f" };
            a.easy = new EasySpeeds { cruiseKt = 120, takeoffKt = 110, minKt = 70, maxKt = 140, rotateKt = 66 };
            return a;
        }

        static GearLeg Leg(string name, double x, double y, double z, double spring, double damper, bool steerable = false, bool brakes = false, bool skid = false) =>
            new GearLeg { name = name, pos = new V3(x, y, z), spring = spring, damper = damper, steerable = steerable, brakes = brakes, skid = skid };

        static StructurePoint Point(string name, double x, double y, double z) =>
            new StructurePoint { name = name, pos = new V3(x, y, z) };
    }
}
