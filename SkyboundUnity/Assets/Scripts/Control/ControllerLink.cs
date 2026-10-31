using System.Collections;
using UnityEngine;

namespace Skybound
{
    /// <summary>
    /// The motion controller's connection to the flight: owns the sensor hub, the serial
    /// session and the control pipeline, and fills FlightSession.controls every frame.
    /// Runs before FlightSession so a frame flies on that frame's controls.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ControllerLink : MonoBehaviour
    {
        public SensorHub hub;
        public SerialSession serial;
        public readonly ControlPipeline pipeline = new ControlPipeline();
        public Neutral neutral => SettingsStore.Get().neutral;

        FlightSession session;
        int autoRecenterRun;

        public ControlSettings Control => SettingsStore.Get().control;

        void Awake()
        {
            session = FindAnyObjectByType<FlightSession>();
            hub = new SensorHub();
            serial = new SerialSession(hub);
            hub.Subscribe(st => ControlLoop.ReengageOnReconnect(st, pipeline));
        }

        void Update()
        {
            hub.Update();
            serial.Update();
            ControlLoop.Frame(hub, pipeline, neutral, Control, Time.unscaledDeltaTime, session.controls);
        }

        /// <summary>A serial port stays locked until it's closed, so it must not outlive Play mode.</summary>
        void OnDisable()
        {
            hub?.Disconnect();
        }

        public void Recenter()
        {
            StartCoroutine(RecenterRoutine());
        }

        IEnumerator RecenterRoutine()
        {
            if (!hub.IsUsable())
            {
                session.Flash("No live motion controller to recenter");
                yield break;
            }
            session.Flash("Recentering · hold still", 1200);
            NeutralResult r = null;
            yield return NeutralCalibration.CaptureNeutral(hub, x => r = x);
            if (!r.ok)
            {
                session.Flash(r.detail, 3500);
                yield break;
            }
            SetNeutral(r.neutral);
            session.Flash("Neutral set");
        }

        public void StartAutoRecenter()
        {
            StartCoroutine(AutoRecenter());
        }

        /// <summary>
        /// Whatever pose the pilot holds when a flight starts is "straight and level".
        /// A neutral saved earlier (different grip, controller on the desk) otherwise
        /// reads as a constant small turn. Skipped when no controller is live; if the
        /// hand is moving, the capture is rejected and the old neutral stays.
        /// </summary>
        IEnumerator AutoRecenter()
        {
            var run = ++autoRecenterRun;
            yield return null; // after the restart's own message, which this one replaces
            if (!hub.IsUsable()) yield break;
            session.Flash("Hold the controller in your normal position · setting neutral", 2500);
            var until = hub.Now() + 1200;
            while (hub.Now() < until) yield return null;
            if (run != autoRecenterRun || !hub.IsUsable()) yield break;
            NeutralResult r = null;
            yield return NeutralCalibration.CaptureNeutral(hub, x => r = x);
            if (run != autoRecenterRun) yield break;
            if (!r.ok)
            {
                session.Flash("Could not set neutral (controller moving). Hold it still and press R.", 4500);
                yield break;
            }
            SetNeutral(r.neutral);
            session.Flash("Neutral set · this position now flies straight", 3000);
        }

        public void SetNeutral(Neutral captured)
        {
            SettingsStore.SetNeutral(captured);
            pipeline.Reengage();
        }
    }
}
