namespace Skybound
{
    /// <summary>What the motion controller is asking for. All zero with no controller connected.</summary>
    public class ControlCommand
    {
        public double elevator;
        public double aileron;
        public double rudder;
        /// <summary>0–1 authority given to the sensor; ramps in/out on connect, recenter and dropouts.</summary>
        public double engagement;
    }
}
