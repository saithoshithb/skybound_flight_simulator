using UnityEngine;
using static Skybound.FlightMath;

namespace Skybound
{
    public enum ControlSource
    {
        Motion,
        Keyboard,
    }

    public enum CameraView
    {
        Chase,
        Cockpit,
    }

    public class PilotInfo
    {
        public ControlSource source = ControlSource.Keyboard;
        public string message = "";
        public double messageUntil;
        /// <summary>Easy flight targets, for the HUD.</summary>
        public double targetVsFpm;
        public double targetBankDeg;
    }

    /// <summary>
    /// The live flight: one FlightModel plus the pilot input it flies with. One frame,
    /// in order: pilot input, then physics (fixed 240 Hz sub-steps). No Unity physics
    /// is involved; views read the state afterwards in LateUpdate.
    /// </summary>
    public class FlightSession : MonoBehaviour
    {
        const double THROTTLE_RATE = 0.5; // full range in 2 s
        const double TRIM_RATE = 0.25;

        [Tooltip("The aircraft assets in Assets/Data. Edit their numbers there to retune them.")]
        [SerializeField] AircraftDef[] fleet;

        public FlightModel flight;
        public readonly PilotInput pilot = new PilotInput();
        public readonly EasyFlight easy = new EasyFlight();
        public readonly PilotInfo pilotInfo = new PilotInfo();
        public readonly ControlCommand controls = new ControlCommand();
        public readonly KeyboardAxes keyboard = new KeyboardAxes();

        /// <summary>The camera view lives in the UI store, as it does in the browser game.</summary>
        public CameraView view => UiStore.view;

        readonly EasyCommand easyCommand = new EasyCommand();
        AircraftDef activeAircraft;
        bool activeEasyFlight;
        ControllerLink link;

        void Awake()
        {
            AircraftCatalog.Use(fleet);
            link = FindAnyObjectByType<ControllerLink>();
            var s = SettingsStore.Get();
            activeAircraft = AircraftCatalog.ById(s.aircraftId);
            activeEasyFlight = s.easyFlight;
            flight = new FlightModel(activeAircraft, TerrainHeight.At);
            easy.Configure(activeAircraft);
            ResetOnRunway();
        }

        void Update()
        {
            ApplySettingsChanges();
            if (UiStore.screen != AppScreen.Flight || UiStore.paused) return;

            var dt = System.Math.Min(Time.deltaTime, 0.1);
            UpdatePilot(dt);
            flight.Advance(pilot, dt);
            if (link != null) Recorder.RecordFrame(dt, this, link.hub);
        }

        /// <summary>
        /// Mix motion-controller and keyboard commands. They add, so the keyboard can
        /// supply rudder (or correct) while the controller flies pitch and roll; the
        /// controller's own engagement ramp already fades it in and out smoothly.
        ///
        /// With Easy flight on, those same commands mean "bank this much" and "climb or
        /// descend this fast", and the throttle keys set a target speed.
        /// </summary>
        public void UpdatePilot(double dt)
        {
            keyboard.Update(dt);
            var k = keyboard;
            var settings = SettingsStore.Get();
            var pitchCmd = Clamp(controls.elevator + k.elevator, -1, 1);
            var rollCmd = Clamp(controls.aileron + k.aileron, -1, 1);
            pilot.brake = k.brake;
            pilotInfo.source = controls.engagement > 0.5 ? ControlSource.Motion : ControlSource.Keyboard;

            if (settings.easyFlight)
            {
                var onGround = flight.telemetry.phase != FlightPhase.Airborne;
                var before = easy.speedTargetKt;
                easy.AdjustSpeed(k.throttleDir, dt, onGround);
                if (before == 0 && easy.speedTargetKt > 0) Flash("Taking off · it lifts off and climbs to 400 ft by itself · tilt sideways to steer", 4500);
                easyCommand.pitch = pitchCmd;
                easyCommand.roll = rollCmd;
                var o = easy.Update(flight.telemetry, easyCommand, dt, flight.alphaWarn / DEG);
                pilot.elevator = o.elevator;
                pilot.aileron = o.aileron;
                // Q/E still steer the nose wheel on the ground and add to the auto-rudder in the air.
                pilot.rudder = Clamp(controls.rudder + k.rudder + o.rudder, -1, 1);
                pilot.throttle = o.throttle;
                pilot.trim = 0;
                pilotInfo.targetVsFpm = o.targetVsFpm;
                pilotInfo.targetBankDeg = o.targetBankDeg;
                return;
            }

            pilot.elevator = pitchCmd;
            pilot.aileron = settings.bankAssist ? Assists.BankAssist(rollCmd, flight.telemetry) : rollCmd;
            var rudderAssist = settings.autoRudder ? Assists.AutoRudder(flight.telemetry, pilot.aileron) : 0;
            pilot.rudder = Clamp(controls.rudder + k.rudder + rudderAssist, -1, 1);
            pilot.throttle = Clamp(pilot.throttle + k.throttleDir * THROTTLE_RATE * dt, 0, 1);
            pilot.trim = Clamp(pilot.trim + k.trimDir * TRIM_RATE * dt, -1, 1);
        }

        public void Flash(string message, double ms = 2500)
        {
            pilotInfo.message = message;
            pilotInfo.messageUntil = Time.unscaledTimeAsDouble + ms / 1000;
        }

        public void ResetOnRunway()
        {
            flight.ResetOnRunway();
            pilot.Clear();
            easy.Reset(false);
            Flash(SettingsStore.Get().easyFlight ? "On runway 36 · press + (or PgUp) once to take off" : "On runway 36 · add power with + or PgUp");
        }

        public void ResetAirborne()
        {
            var (trim, throttle) = flight.ResetAirborne();
            pilot.Clear();
            pilot.trim = trim;
            pilot.throttle = throttle;
            easy.Reset(true, trim, throttle);
            Flash("Long final, runway 36 · 1,000 ft · trimmed");
        }

        /// <summary>
        /// Switching modes mid-flight hands over without a jump: each side starts from
        /// what the aircraft is doing now.
        /// </summary>
        void ApplySettingsChanges()
        {
            var settings = SettingsStore.Get();
            var wanted = AircraftCatalog.ById(settings.aircraftId);
            if (wanted != activeAircraft)
            {
                activeAircraft = wanted;
                flight.SetAircraft(wanted);
                easy.Configure(wanted);
                easy.Reset(false);
                pilot.Clear();
                return;
            }
            if (settings.easyFlight == activeEasyFlight) return;
            activeEasyFlight = settings.easyFlight;
            var airborne = flight.telemetry.phase == FlightPhase.Airborne;
            if (settings.easyFlight)
            {
                easy.Reset(airborne, pilot.elevator + pilot.trim, pilot.throttle);
                if (airborne) easy.speedTargetKt = Clamp(System.Math.Floor(flight.telemetry.airspeedKt / 5 + 0.5) * 5, easy.speeds.minKt, easy.speeds.maxKt);
            }
            else pilot.trim = Clamp(pilot.elevator, -1, 1);
        }
    }
}
