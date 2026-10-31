using UnityEngine;

namespace Skybound
{
    /// <summary>Shows the aircraft where the flight model says it is, with its surfaces and propeller moving.</summary>
    public class AircraftView : MonoBehaviour
    {
        /// <summary>Instruments redraw ~30 Hz from the same telemetry the HUD shows.</summary>
        const float PANEL_INTERVAL_S = 1f / 30;

        FlightSession session;
        readonly Owned owned = new Owned();
        AircraftDef built;
        bool cockpitShown;
        float panelClock = PANEL_INTERVAL_S;

        public AircraftParts Parts { get; private set; }
        /// <summary>Fuselage and cabin shell, hidden in the cockpit view (the eye is inside it).</summary>
        public GameObject Exterior => Parts.exterior;
        /// <summary>Cockpit interior, shown only in the cockpit view.</summary>
        public GameObject Cockpit => Parts.cockpit;

        void Awake()
        {
            session = FindAnyObjectByType<FlightSession>();
        }

        // After every Awake, so the flight model exists, and before any LateUpdate.
        void Start()
        {
            Rebuild();
        }

        void OnDestroy()
        {
            owned.DestroyAll();
        }

        /// <summary>Rebuild on aircraft change so geometry and livery follow the new definition.</summary>
        void Rebuild()
        {
            built = session.flight.ac;
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                // Hidden at once: Destroy only takes effect at the end of the frame.
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            owned.DestroyAll();
            Parts = AircraftBuilder.Build(built, transform, owned);
            var panelMaterial = Parts.panelSurface.sharedMaterial;
            panelMaterial.SetColor("_BaseColor", Color.white);
            panelMaterial.SetTexture("_BaseMap", InstrumentPanel.Get().Texture);
            ShowCockpit(cockpitShown);
        }

        void LateUpdate()
        {
            var flight = session.flight;
            if (flight.ac != built) Rebuild();
            if (UiStore.screen != AppScreen.Flight) return;
            var s = flight.state;
            transform.SetPositionAndRotation(Coords.ToUnityPosition(s.pos), Coords.ToUnityRotation(s.q));
            var t = flight.telemetry;
            var rpm = (float)Panel.EngineRpm(s.engine, t.airspeedKt);
            AircraftBuilder.SyncAircraft(Parts, new AircraftVisualState
            {
                elevator = (float)s.surfaces.elevator,
                aileron = (float)s.surfaces.aileron,
                rudder = (float)s.surfaces.rudder,
                engine = (float)s.engine,
                throttle = (float)t.throttle,
                rpm = rpm,
                maxElevator = (float)flight.ac.maxElevator,
                maxAileron = (float)flight.ac.maxAileron,
                timeS = Time.timeAsDouble,
            }, UiStore.paused ? 0 : Mathf.Min(Time.deltaTime, 0.1f));

            panelClock += Time.unscaledDeltaTime;
            if (panelClock >= PANEL_INTERVAL_S && (UiStore.view == CameraView.Cockpit || UiStore.showInstruments))
            {
                panelClock = 0;
                InstrumentPanel.Get().Draw(t, new PanelState
                {
                    rpm = rpm,
                    stall = t.stalled,
                    brakes = t.brake > 0 && t.phase == FlightPhase.Ground,
                    easyFlight = SettingsStore.Get().easyFlight,
                    lowAltitude = t.phase == FlightPhase.Airborne && t.aglFt < 200 && t.verticalSpeedFpm < -300,
                });
            }
        }

        public void ShowCockpit(bool show)
        {
            cockpitShown = show;
            if (Cockpit.activeSelf != show) Cockpit.SetActive(show);
            if (Exterior.activeSelf == show) Exterior.SetActive(!show);
        }
    }
}
