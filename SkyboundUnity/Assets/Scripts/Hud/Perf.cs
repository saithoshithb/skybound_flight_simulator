using System;

namespace Skybound
{
    /// <summary>
    /// Frame-rate monitor. Fed once per rendered frame; the FPS counter reads it, and a one-time hint
    /// suggests a lower graphics tier when the frame rate stays low.
    /// </summary>
    public static class Perf
    {
        const double LOW_FPS = 32;
        const double LOW_FOR_S = 6;

        public static double fps;
        /// <summary>Seconds the frame rate has been continuously below the threshold.</summary>
        public static double lowFor;
        public static bool hinted;

        public static void Reset()
        {
            fps = 0;
            lowFor = 0;
            hinted = false;
        }

        public static void PerfFrame(double dt, Action onLow)
        {
            if (dt <= 0 || dt > 0.5) return;
            var now = 1 / dt;
            fps = fps == 0 ? now : fps + (now - fps) * Math.Min(1, dt * 2);
            lowFor = fps < LOW_FPS ? lowFor + dt : 0;
            if (!hinted && lowFor > LOW_FOR_S)
            {
                hinted = true;
                onLow();
            }
        }
    }
}
