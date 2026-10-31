using UnityEngine;

namespace Skybound
{
    /// <summary>
    /// The flight camera. Chase view: follows heading and some pitch, never roll.
    /// Cockpit view: rigidly attached to the airframe. Blends smoothly between them.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        const float CHASE_DISTANCE = 15;
        const float CHASE_HEIGHT = 3.6f;
        const float CHASE_FOV = 55;
        const float COCKPIT_FOV = 68;
        const float BLEND_RATE = 2.2f;
        /// <summary>Pilot's eye in aircraft coordinates (left seat). z forward, x right, y up.</summary>
        public static readonly Vector3 EYE = new Vector3(-0.28f, 0.82f, 0.25f);
        /// <summary>The pilot looks slightly down, toward the panel and the nose.</summary>
        public static readonly Quaternion EYE_TILT = Quaternion.AngleAxis(0.17f * Mathf.Rad2Deg, Vector3.right);

        FlightSession session;
        AircraftView aircraft;
        Camera cam;
        Vector3 chasePos;
        bool initialised;
        float blend;

        void Awake()
        {
            session = FindAnyObjectByType<FlightSession>();
            aircraft = FindAnyObjectByType<AircraftView>();
            cam = GetComponent<Camera>();
            cam.fieldOfView = CHASE_FOV;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 40000;
        }

        void LateUpdate()
        {
            if (UiStore.screen != AppScreen.Flight) return;
            var flight = session.flight;
            var rawDt = Time.deltaTime;
            var dt = Mathf.Min(rawDt, 0.1f);
            var pos = Coords.ToUnityPosition(flight.state.pos);
            var quat = Coords.ToUnityRotation(flight.state.q);

            // --- chase camera: follows heading and some pitch, never roll ---
            var fwd = quat * Vector3.forward;
            var flatFwd = new Vector3(fwd.x, 0, fwd.z);
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
            flatFwd = Vector3.Lerp(flatFwd.normalized, fwd, 0.35f).normalized;

            var desired = pos - flatFwd * CHASE_DISTANCE + Vector3.up * CHASE_HEIGHT;
            // Never below the ground (or a hillside) behind the aircraft.
            desired.y = Mathf.Max(desired.y, (float)flight.terrain(desired.z, desired.x) + 1.2f);
            // First frame, or the aircraft was teleported (restart, final approach): cut, don't glide 4 km.
            if (!initialised || (chasePos - desired).sqrMagnitude > 150 * 150)
            {
                chasePos = desired;
                initialised = true;
            }
            chasePos = Vector3.Lerp(chasePos, desired, 1 - Mathf.Exp(-dt * 5));
            var chaseLook = pos + flatFwd * 8 + Vector3.up;
            var chaseQuat = Quaternion.LookRotation(chaseLook - chasePos, Vector3.up);

            // --- cockpit camera: rigidly attached to the airframe ---
            var eyePos = pos + quat * EYE;
            var eyeQuat = quat * EYE_TILT;

            // --- smooth transition between views ---
            var target = session.view == CameraView.Cockpit ? 1f : 0f;
            blend += Mathf.Sign(target - blend) * Mathf.Min(Mathf.Abs(target - blend), rawDt * BLEND_RATE);
            var e = blend * blend * (3 - 2 * blend);
            transform.SetPositionAndRotation(Vector3.Lerp(chasePos, eyePos, e), Quaternion.Slerp(chaseQuat, eyeQuat, e));
            cam.fieldOfView = Mathf.Lerp(CHASE_FOV, COCKPIT_FOV, e);
            aircraft.ShowCockpit(e > 0.5f);
        }
    }
}
