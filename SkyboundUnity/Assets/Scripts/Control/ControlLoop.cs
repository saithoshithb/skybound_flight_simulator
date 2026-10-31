namespace Skybound
{
    /// <summary>
    /// The single per-frame control update. Reads the hub and publishes into the
    /// mutable ControlCommand that FlightSession mixes with the keyboard.
    /// </summary>
    public static class ControlLoop
    {
        public static void Frame(SensorHub hub, ControlPipeline pipeline, Neutral neutral, ControlSettings settings, double dt, ControlCommand controls)
        {
            var latest = hub.IsUsable() ? hub.Latest() : null;
            var c = pipeline.Update(latest?.sample, neutral, settings, dt);
            controls.elevator = c.elevator;
            controls.aileron = c.aileron;
            controls.rudder = c.rudder;
            controls.engagement = c.engagement;
        }

        /// <summary>Any reconnect starts from zero authority so the first samples can't jerk the aircraft.</summary>
        public static void ReengageOnReconnect(HubStatus st, ControlPipeline pipeline)
        {
            if (st.link == LinkState.Connecting || st.link == LinkState.Awaiting) pipeline.Reengage();
        }
    }
}
