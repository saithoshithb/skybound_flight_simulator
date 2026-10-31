using System;
using static Skybound.FlightMath;

namespace Skybound
{
    /// <summary>Pilot commands, all in the aircraft's sign convention.</summary>
    public class PilotInput
    {
        /// <summary>-1…1, + = nose up</summary>
        public double elevator;
        /// <summary>-1…1, + = roll right</summary>
        public double aileron;
        /// <summary>-1…1, + = yaw right (also steers the nose wheel)</summary>
        public double rudder;
        /// <summary>0…1</summary>
        public double throttle;
        /// <summary>0…1</summary>
        public double brake;
        /// <summary>-1…1 elevator trim, added to elevator before limits</summary>
        public double trim;

        public void Clear()
        {
            elevator = aileron = rudder = throttle = brake = trim = 0;
        }
    }

    /// <summary>Actual surface deflections (rad, aerodynamic sign).</summary>
    public struct Surfaces
    {
        public double elevator, aileron, rudder;
    }

    public class FlightState
    {
        public V3 pos; // NED, m
        public V3 vel; // NED, m/s
        public Quat q; // body -> world
        public V3 w; // body rates p, q, r (rad/s)
        public double engine; // 0…1, lags throttle
        public Surfaces surfaces;
    }

    public enum FlightPhase
    {
        Ground,
        Airborne,
        Crashed,
    }

    public class Telemetry
    {
        public double airspeedKt; // indicated
        public double groundSpeedKt;
        public double altitudeFt;
        /// <summary>Height of the CG above the terrain directly below.</summary>
        public double aglFt;
        /// <summary>Rate of heading change (deg/s, + = right).</summary>
        public double turnRateDps;
        /// <summary>Body lateral specific force in g (+ = pushed right), drives the slip ball.</summary>
        public double lateralG;
        public double headingDeg; // 0–360
        public double verticalSpeedFpm;
        public double pitchDeg;
        public double rollDeg;
        /// <summary>Body roll rate p.</summary>
        public double rollRateDps;
        /// <summary>Body pitch rate q.</summary>
        public double pitchRateDps;
        public double aoaDeg;
        public double sideslipDeg;
        public double loadFactor;
        public double throttle;
        public double engine;
        public double trim;
        public double brake;
        public FlightPhase phase;
        public bool stalled;
        public bool overspeed;
        public string crashReason;
        /// <summary>Wheels in contact with the ground, for HUD/sounds.</summary>
        public int wheelsOnGround;
        /// <summary>Tail skid touched the ground in the last 1.5 s.</summary>
        public bool tailStrike;
        public double timeS;
    }

    /// <summary>
    /// Six-degree-of-freedom rigid-body flight model.
    ///
    /// World frame NED with the ground plane at z = 0 (field elevation 0 ft MSL).
    /// Forces: aerodynamics from stability derivatives with a smooth post-stall
    /// lift break, propeller thrust from engine power, gravity, and spring-damper
    /// landing gear with rolling/braking/side friction and nose-wheel steering.
    ///
    /// Pilot commands go through first-order surface actuators, so a step input
    /// never produces an instantaneous rotation: the aircraft responds through
    /// moments, inertia and damping.
    /// </summary>
    public class FlightModel
    {
        public const double STEP_S = 1.0 / 240;
        const double G = 9.80665;
        const double RHO0 = 1.225;
        const double MS_TO_KT = 1.943844;
        const double M_TO_FT = 3.28084;

        public AircraftDef ac;
        public FlightState state;
        public FlightPhase phase = FlightPhase.Ground;
        public string crashReason;
        /// <summary>The same object every frame, updated in place.</summary>
        public readonly Telemetry telemetry = new Telemetry();
        double accumulator;
        double time;
        double lastLoad = 1;
        double lastLateralG;
        int wheelsOnGround;
        double tailStrikeUntil = -1;
        PilotInput lastInput = new PilotInput();

        /// <summary>Angle of attack of maximum lift, and the stall-warning angle 2° before it.</summary>
        public double alphaCLmax;
        public double alphaWarn;

        /// <summary>Terrain height (m above field) at a north/east position. Flat by default.</summary>
        public readonly Func<double, double, double> terrain;

        public FlightModel(AircraftDef ac = null, Func<double, double, double> terrain = null)
        {
            this.ac = ac != null ? ac : AircraftCatalog.Trainer;
            this.terrain = terrain ?? ((north, east) => 0);
            SetAircraft(this.ac);
        }

        public static double AirDensity(double altitudeM) =>
            RHO0 * Math.Pow(Math.Max(0.2, 1 - 2.25577e-5 * altitudeM), 4.2559);

        /// <summary>Lift coefficient vs angle of attack with a smooth stall (blended to flat-plate lift).</summary>
        public static double LiftCoefficient(AircraftDef ac, double alpha)
        {
            var sigma = StallBlend(ac, alpha);
            var linear = ac.CL0 + ac.CLalpha * alpha;
            var sinA = Math.Sin(alpha);
            var flatPlate = 2 * Sign(alpha) * (sinA * sinA) * Math.Cos(alpha);
            return (1 - sigma) * linear + sigma * flatPlate;
        }

        static double StallBlend(AircraftDef ac, double alpha)
        {
            var M = ac.stallSharpness;
            var a0 = ac.alphaStall;
            var e1 = Math.Exp(-M * (alpha - a0));
            var e2 = Math.Exp(M * (alpha + a0));
            return (1 + e1 + e2) / ((1 + e1) * (1 + e2));
        }

        /// <summary>Steady level-flight trim at a true airspeed: angle of attack, elevator (rad), throttle.</summary>
        public static (double alpha, double elevatorRad, double throttle) TrimLevel(AircraftDef ac, double speedMs, double altitudeM)
        {
            var qbar = 0.5 * AirDensity(altitudeM) * speedMs * speedMs;
            var CLreq = (ac.mass * G) / (qbar * ac.wingArea);
            // Solve CL(a) + CLde·de = CLreq and Cm = 0 together (linear region).
            // de = -(Cm0 + Cma·a)/Cmde  =>  CL0 + CLa·a - CLde(Cm0 + Cma·a)/Cmde = CLreq
            var alpha = (CLreq - ac.CL0 + (ac.CLde * ac.Cm0) / ac.Cmde) / (ac.CLalpha - (ac.CLde * ac.Cmalpha) / ac.Cmde);
            var de = -(ac.Cm0 + ac.Cmalpha * alpha) / ac.Cmde;
            var CL = ac.CL0 + ac.CLalpha * alpha + ac.CLde * de;
            var CD = ac.CD0 + CL * CL / (Math.PI * ac.oswald * (ac.span * ac.span / ac.wingArea));
            var drag = qbar * ac.wingArea * CD;
            var thrustFull = Math.Min(ac.staticThrustN, (ac.propEfficiency * ac.maxPowerW) / speedMs);
            return (alpha, de, Clamp(drag / thrustFull, 0, 1));
        }

        /// <summary>1-g stall speed (indicated, kt) at sea level, from the lift curve's maximum.</summary>
        public static double StallSpeedKt(AircraftDef ac)
        {
            double clMax = 0;
            for (double a = 0; a < 35 * DEG; a += 0.1 * DEG) clMax = Math.Max(clMax, LiftCoefficient(ac, a));
            return Math.Sqrt((2 * ac.mass * G) / (RHO0 * ac.wingArea * clMax)) * MS_TO_KT;
        }

        /// <summary>Switch aircraft type; the new aircraft starts parked on the runway.</summary>
        public void SetAircraft(AircraftDef ac)
        {
            this.ac = ac;
            var best = double.NegativeInfinity;
            double at = 0;
            for (double a = 0; a < 35 * DEG; a += 0.1 * DEG)
            {
                var cl = LiftCoefficient(ac, a);
                if (cl > best)
                {
                    best = cl;
                    at = a;
                }
            }
            alphaCLmax = at;
            alphaWarn = at - 2 * DEG;
            ResetOnRunway();
        }

        /// <summary>Parked at the runway threshold, engine idle, heading north (runway 36).</summary>
        public void ResetOnRunway(double northM = 60)
        {
            var gearDepth = ac.gear[0].pos.z;
            state = new FlightState
            {
                pos = new V3(northM, 0, -gearDepth + 0.06),
                vel = new V3(0, 0, 0),
                q = FromEuler(0, 0, 0),
                w = new V3(0, 0, 0),
                engine = 0,
            };
            phase = FlightPhase.Ground;
            FinishReset();
        }

        /// <summary>
        /// Trimmed level flight on a long final for runway 36. Returns the trim
        /// command the pilot must hold so the aircraft doesn't immediately pitch.
        /// </summary>
        public (double trim, double throttle) ResetAirborne(double distanceM = 4000, double altitudeM = 300, double speedMs = 50)
        {
            var t = TrimLevel(ac, speedMs, altitudeM);
            state = new FlightState
            {
                pos = new V3(-distanceM, 0, -altitudeM),
                // Level flight path, nose raised by the trim angle of attack.
                vel = new V3(speedMs, 0, 0),
                q = FromEuler(0, t.alpha, 0),
                w = new V3(0, 0, 0),
                engine = t.throttle,
                surfaces = new Surfaces { elevator = t.elevatorRad, aileron = 0, rudder = 0 },
            };
            phase = FlightPhase.Airborne;
            FinishReset();
            return (-t.elevatorRad / ac.maxElevator, t.throttle);
        }

        void FinishReset()
        {
            crashReason = null;
            accumulator = 0;
            time = 0;
            tailStrikeUntil = -1;
            lastLoad = 1;
            ComputeTelemetry(lastInput);
        }

        /// <summary>Advance by a variable frame time using fixed sub-steps.</summary>
        public void Advance(PilotInput input, double frameDt)
        {
            lastInput = input;
            if (phase == FlightPhase.Crashed) return;
            accumulator += Math.Min(frameDt, 0.1);
            while (accumulator >= STEP_S)
            {
                Step(input, STEP_S);
                accumulator -= STEP_S;
                if (phase == FlightPhase.Crashed) break;
            }
            ComputeTelemetry(input);
        }

        public void Step(PilotInput input, double dt)
        {
            var s = state;
            time += dt;

            // --- actuators ---
            var k = 1 - Math.Exp(-dt / ac.surfaceLagS);
            // Positive elevator command = trailing edge up = negative aerodynamic de.
            var targetElevator = -Clamp(input.elevator + input.trim, -1, 1) * ac.maxElevator;
            var targetAileron = Clamp(input.aileron, -1, 1) * ac.maxAileron;
            var targetRudder = Clamp(input.rudder, -1, 1) * ac.maxRudder;
            s.surfaces.elevator += (targetElevator - s.surfaces.elevator) * k;
            s.surfaces.aileron += (targetAileron - s.surfaces.aileron) * k;
            s.surfaces.rudder += (targetRudder - s.surfaces.rudder) * k;
            s.engine += (Clamp(input.throttle, 0, 1) - s.engine) * (1 - Math.Exp(-dt / ac.engineTimeConstantS));

            // --- aerodynamics (body frame) ---
            var vb = RotateInv(s.q, s.vel);
            var V = Length(vb);
            var altitude = -s.pos.z;
            var rho = AirDensity(altitude);
            var forceB = new V3(0, 0, 0);
            var momentB = new V3(0, 0, 0);
            if (V > 0.5)
            {
                var alpha = Math.Atan2(vb.z, vb.x);
                var beta = Math.Asin(Clamp(vb.y / V, -1, 1));
                var qbar = 0.5 * rho * V * V;
                var ph = (s.w.x * ac.span) / (2 * V);
                var qh = (s.w.y * ac.chord) / (2 * V);
                var rh = (s.w.z * ac.span) / (2 * V);
                var de = s.surfaces.elevator;
                var da = s.surfaces.aileron;
                var dr = s.surfaces.rudder;
                var AR = (ac.span * ac.span) / ac.wingArea;
                var sinA = Math.Sin(alpha);

                var CL = LiftCoefficient(ac, alpha) + ac.CLq * qh + ac.CLde * de;
                var CLlin = ac.CL0 + ac.CLalpha * alpha;
                var sigma = StallBlend(ac, alpha);
                var CD = ac.CD0 + ((1 - sigma) * CLlin * CLlin) / (Math.PI * ac.oswald * AR) + sigma * 1.2 * (sinA * sinA);
                var CY = ac.CYbeta * beta + ac.CYdr * dr;
                var Cl = ac.Clbeta * beta + ac.Clp * ph + ac.Clr * rh + ac.Clda * da + ac.Cldr * dr;
                // Past the stall the tail loses some authority and the wing pitches the nose down.
                var Cm = ac.Cm0 + ac.Cmalpha * alpha + ac.Cmq * qh + ac.Cmde * de * (1 - 0.3 * sigma) - 0.25 * sigma * Sign(alpha);
                var Cn = ac.Cnbeta * beta + ac.Cnp * ph + ac.Cnr * rh + ac.Cnda * da + ac.Cndr * dr;

                var L = qbar * ac.wingArea * CL;
                var D = qbar * ac.wingArea * CD;
                var Y = qbar * ac.wingArea * CY;
                double ca = Math.Cos(alpha), sa = sinA;
                forceB = new V3(-D * ca + L * sa, Y, -D * sa - L * ca);
                momentB = new V3(qbar * ac.wingArea * ac.span * Cl, qbar * ac.wingArea * ac.chord * Cm, qbar * ac.wingArea * ac.span * Cn);
            }

            // --- thrust along body x ---
            var thrust = s.engine * Math.Min(ac.staticThrustN, (ac.propEfficiency * ac.maxPowerW) / Math.Max(V, 1));
            forceB = Add(forceB, new V3(thrust, 0, 0));

            // Load factor from non-gravity forces (what the pilot feels).
            var ground = GroundForces(input, V);
            var totalNonGravBody = Add(forceB, RotateInv(s.q, ground.force));
            lastLoad = -totalNonGravBody.z / (ac.mass * G);
            lastLateralG = totalNonGravBody.y / (ac.mass * G);

            // --- integrate translation ---
            var forceW = Add(Add(Rotate(s.q, forceB), ground.force), new V3(0, 0, ac.mass * G));
            s.vel = Add(s.vel, Scale(forceW, dt / ac.mass));
            s.pos = Add(s.pos, Scale(s.vel, dt));

            // --- integrate rotation (diagonal inertia) ---
            var I = ac.inertia;
            var M = Add(momentB, ground.moment);
            var Iw = new V3(I.x * s.w.x, I.y * s.w.y, I.z * s.w.z);
            var gyro = Cross(s.w, Iw);
            var wdot = new V3((M.x - gyro.x) / I.x, (M.y - gyro.y) / I.y, (M.z - gyro.z) / I.z);
            s.w = Add(s.w, Scale(wdot, dt));
            var dq = QMul(s.q, new Quat(0, s.w.x, s.w.y, s.w.z));
            s.q = QNormalize(new Quat(s.q.w + 0.5 * dq.w * dt, s.q.x + 0.5 * dq.x * dt, s.q.y + 0.5 * dq.y * dt, s.q.z + 0.5 * dq.z * dt));

            if (!CheckStructure(ground.maxSinkMs)) phase = ground.anyContact ? FlightPhase.Ground : FlightPhase.Airborne;
        }

        struct GroundResult
        {
            public V3 force, moment;
            public double maxSinkMs;
            public bool anyContact;
        }

        GroundResult GroundForces(PilotInput input, double airspeed)
        {
            var s = state;
            var force = new V3(0, 0, 0);
            var moment = new V3(0, 0, 0);
            var contacts = 0;
            double maxSinkMs = 0;
            var skidContact = false;
            // Nose-wheel steering fades out with speed, as rudder authority takes over.
            // Full authority while taxiing, ~10% by 40 kt: big nose-wheel angles at speed swerve the aircraft.
            var steer = Clamp(input.rudder, -1, 1) * ac.maxNoseSteer * Clamp(1 - (airspeed - 5) / 15, 0.1, 1);

            foreach (var leg in ac.gear)
            {
                var pw = Add(s.pos, Rotate(s.q, leg.pos));
                var penetration = pw.z + terrain(pw.x, pw.y); // ground at z = -h, z points down
                if (penetration <= 0) continue;
                if (!leg.skid) contacts++;
                var vp = Add(s.vel, Rotate(s.q, Cross(s.w, leg.pos)));
                maxSinkMs = Math.Max(maxSinkMs, vp.z);
                var normal = Math.Max(0, leg.spring * penetration + leg.damper * vp.z);
                if (normal == 0) continue;

                var angle = leg.steerable ? steer : 0;
                var wheelFwd = Rotate(s.q, new V3(Math.Cos(angle), Math.Sin(angle), 0));
                var h = Math.Sqrt(wheelFwd.x * wheelFwd.x + wheelFwd.y * wheelFwd.y);
                if (h == 0) h = 1;
                var fwd = new V3(wheelFwd.x / h, wheelFwd.y / h, 0);
                var lat = new V3(-fwd.y, fwd.x, 0);
                var vLong = Dot(vp, fwd);
                var vLat = Dot(vp, lat);
                if (leg.skid) skidContact = true;
                var mu = leg.skid ? 0.45 : 0.025 + (leg.brakes ? Clamp(input.brake, 0, 1) * 0.65 : 0);
                var fLong = -mu * normal * Math.Tanh(vLong / 0.4);
                var fLat = -(leg.skid ? 0.45 : 0.85) * normal * Math.Tanh(vLat / 0.25);
                var f = new V3(fwd.x * fLong + lat.x * fLat, fwd.y * fLong + lat.y * fLat, -normal);
                force = Add(force, f);
                moment = Add(moment, Cross(leg.pos, RotateInv(s.q, f)));
            }
            wheelsOnGround = contacts;
            if (skidContact) tailStrikeUntil = time + 1.5;
            return new GroundResult { force = force, moment = moment, maxSinkMs = maxSinkMs, anyContact = contacts > 0 || skidContact };
        }

        /// <summary>Returns true if the aircraft crashed this step.</summary>
        bool CheckStructure(double sinkMs)
        {
            if (sinkMs > 4.5) return Crash($"Hard landing ({Math.Floor(sinkMs * 196.85 + 0.5):0} fpm at touchdown)");
            foreach (var p in ac.structure)
            {
                var pw = Add(state.pos, Rotate(state.q, p.pos));
                if (pw.z + terrain(pw.x, pw.y) > 0.02) return Crash($"{p.name} struck the ground");
            }
            return false;
        }

        bool Crash(string reason)
        {
            phase = FlightPhase.Crashed;
            crashReason = reason;
            state.vel = new V3(0, 0, 0);
            state.w = new V3(0, 0, 0);
            return true;
        }

        void ComputeTelemetry(PilotInput input)
        {
            var s = state;
            var t = telemetry;
            var e = ToEuler(s.q);
            var vb = RotateInv(s.q, s.vel);
            var V = Length(vb);
            var alpha = V > 1 ? Math.Atan2(vb.z, vb.x) : 0;
            var beta = V > 1 ? Math.Asin(Clamp(vb.y / V, -1, 1)) : 0;
            var altitudeM = -s.pos.z;
            var ias = V * Math.Sqrt(AirDensity(altitudeM) / RHO0);
            t.airspeedKt = ias * MS_TO_KT;
            t.groundSpeedKt = Math.Sqrt(s.vel.x * s.vel.x + s.vel.y * s.vel.y) * MS_TO_KT;
            t.altitudeFt = altitudeM * M_TO_FT;
            t.aglFt = (altitudeM - terrain(s.pos.x, s.pos.y)) * M_TO_FT;
            t.turnRateDps = ((s.w.y * Math.Sin(e.roll) + s.w.z * Math.Cos(e.roll)) / Math.Max(0.1, Math.Cos(e.pitch))) / DEG;
            t.lateralG = lastLateralG;
            t.headingDeg = ((e.yaw / DEG) % 360 + 360) % 360;
            t.verticalSpeedFpm = -s.vel.z * M_TO_FT * 60;
            t.pitchDeg = e.pitch / DEG;
            t.rollDeg = e.roll / DEG;
            t.rollRateDps = s.w.x / DEG;
            t.pitchRateDps = s.w.y / DEG;
            t.aoaDeg = alpha / DEG;
            t.sideslipDeg = beta / DEG;
            t.loadFactor = lastLoad;
            t.throttle = Clamp(input.throttle, 0, 1);
            t.engine = s.engine;
            t.trim = input.trim;
            t.brake = input.brake;
            t.phase = phase;
            t.stalled = phase == FlightPhase.Airborne && V > 5 && alpha > alphaWarn;
            t.overspeed = V > ac.vneMs;
            t.crashReason = crashReason;
            t.wheelsOnGround = wheelsOnGround;
            t.tailStrike = time < tailStrikeUntil;
            t.timeS = time;
        }
    }
}
