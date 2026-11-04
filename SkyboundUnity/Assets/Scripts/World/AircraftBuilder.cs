using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybound
{
    /// <summary>The moving and switchable parts of a built aircraft.</summary>
    public sealed class AircraftParts
    {
        public Transform elevator;
        public Transform aileronLeft;
        public Transform aileronRight;
        public Transform rudder;
        public Transform prop;
        public Material discMaterial;
        /// <summary>Cockpit interior, shown only in the cockpit view.</summary>
        public GameObject cockpit;
        /// <summary>Fuselage and cabin shell, hidden in the cockpit view (the eye is inside it).</summary>
        public GameObject exterior;
        public Transform yokeLeft;
        public Transform yokeRight;
        public Transform throttleKnob;
        public GameObject[] strobes;
        /// <summary>Where the live six-pack goes. A plain dark surface until stage 5 draws the instruments.</summary>
        public MeshRenderer panelSurface;
        public float propAngle;
    }

    public struct AircraftVisualState
    {
        public float elevator;
        public float aileron;
        public float rudder;
        public float engine;
        public float throttle;
        public float rpm;
        public float maxElevator;
        public float maxAileron;
        public double timeS;
    }

    /// <summary>
    /// Procedural aircraft, built to the flight model's geometry (metres, origin at the centre
    /// of gravity). Gear contact points and wingtips line up with the physics contact points, so
    /// the wheels sit on the runway and a wing strike looks like one. Numbers are the browser
    /// game's, in three.js space: −z forward.
    /// </summary>
    public sealed class AircraftBuilder
    {
        const float YOKE_Z = -0.62f;
        const float THROTTLE_Z = -0.9f;

        readonly Owned owned;

        readonly Material body, bodyShadowed, stripe, accent, glass, dark, tire, metal, panel, disc;

        AircraftBuilder(AircraftDef def, Owned owned)
        {
            this.owned = owned;
            var look = def.look;
            body = owned.Add(MaterialKit.Lit(look.body, 0.45f, 0.05f));
            bodyShadowed = owned.Add(MaterialKit.Lit(look.body, 0.45f, 0.05f, true));
            stripe = owned.Add(MaterialKit.Lit(look.stripe, 0.5f));
            accent = owned.Add(MaterialKit.Lit(look.accent, 0.5f));
            glass = owned.Add(MaterialKit.Lit("#1b2630", 0.08f, 0.6f));
            dark = owned.Add(MaterialKit.Lit("#25282b", 0.8f));
            tire = owned.Add(MaterialKit.Lit("#151617", 0.95f));
            metal = owned.Add(MaterialKit.Lit("#9aa0a5", 0.35f, 0.8f));
            panel = owned.Add(MaterialKit.Lit("#2b2e31", 0.9f));
            disc = owned.Add(MaterialKit.BasicTransparent("#2a2a2a", 0.18f));
        }

        /// <summary>Fuselage as a lathe along z: nose at −2.2, tail at +5.9. Scaled to a boxy cross-section where it's used.</summary>
        public static Geo FuselageGeometry()
        {
            var profile = new[]
            {
                new Vector2(0.0f, -2.2f),
                new Vector2(0.42f, -2.1f),
                new Vector2(0.56f, -1.7f),
                new Vector2(0.62f, -1.0f),
                new Vector2(0.66f, -0.2f),
                new Vector2(0.64f, 0.8f),
                new Vector2(0.52f, 1.8f),
                new Vector2(0.36f, 3.0f),
                new Vector2(0.22f, 4.4f),
                new Vector2(0.14f, 5.9f),
                new Vector2(0.0f, 5.95f),
            };
            // Lathe axis is y; rotating +90° about x maps profile y → z (nose at −z).
            return Geo.Lathe(profile, 24).RotateX(Math.PI / 2).ComputeVertexNormals();
        }

        /// <summary>Builds the model under <paramref name="root"/>. Everything made is added to <paramref name="owned"/>.</summary>
        public static AircraftParts Build(AircraftDef def, Transform root, Owned owned)
        {
            var builder = new AircraftBuilder(def, owned);
            // Aircraft modelled in Blender (Tools/blender) are in Resources/Aircraft; the others are built here.
            var model = Resources.Load<GameObject>("Aircraft/" + def.name);
            return model != null ? builder.FromModel(model, root) : builder.Aircraft(def, root);
        }

        /// <summary>
        /// Copies a Blender model under <paramref name="root"/> and swaps its materials for the game's, picked by
        /// material name. The model uses <see cref="Aircraft"/>'s part names, but is at most one level deep (see Tools/blender/aircraft.py).
        /// </summary>
        AircraftParts FromModel(GameObject model, Transform root)
        {
            // Only the copies made here: on a rebuild the old model is still under root until the end of the frame.
            var copies = new Dictionary<string, Transform>();
            var renderers = new List<MeshRenderer>();
            foreach (Transform child in model.transform)
            {
                var copy = UnityEngine.Object.Instantiate(child, root, false);
                copy.name = child.name;
                copies[child.name] = copy;
                renderers.AddRange(copy.GetComponentsInChildren<MeshRenderer>(true));
            }
            Transform Find(string path)
            {
                var cut = path.IndexOf('/');
                copies.TryGetValue(cut < 0 ? path : path.Substring(0, cut), out var found);
                if (found != null && cut >= 0) found = found.Find(path.Substring(cut + 1));
                return found != null ? found : throw new InvalidOperationException("The aircraft model has no " + path);
            }

            var materials = new Dictionary<string, Material>
            {
                { "Body", bodyShadowed }, { "Stripe", stripe }, { "Accent", accent }, { "Glass", glass }, { "Dark", dark },
                { "Tire", tire }, { "Metal", metal }, { "Panel", panel }, { "Disc", disc },
                { "Seat", owned.Add(MaterialKit.Lit("#4a4038", 0.9f)) },
                { "Prop tip", owned.Add(MaterialKit.Lit("#e3b21b", 0.5f)) },
                { "Light red", owned.Add(MaterialKit.Basic("#ff3b2f")) },
                { "Light green", owned.Add(MaterialKit.Basic("#3dff6e")) },
                { "Light white", owned.Add(MaterialKit.Basic("#ffffff")) },
                { "Landing light", owned.Add(MaterialKit.Basic("#fffbe8")) },
                { "Live instruments", owned.Add(MaterialKit.BasicNotToneMapped("#101214")) },
                { "Radio green", owned.Add(MaterialKit.BasicNotToneMapped("#5fd38a")) },
                { "Radio amber", owned.Add(MaterialKit.BasicNotToneMapped("#d89a3a")) },
            };
            var cockpit = Find("Cockpit");
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials;
                for (var i = 0; i < slots.Length; i++)
                {
                    // The importer names them "<model>-<Blender material>".
                    var name = slots[i].name.Substring(slots[i].name.LastIndexOf('-') + 1);
                    slots[i] = materials.TryGetValue(name, out var m) ? m
                        : throw new InvalidOperationException($"{renderer.name}: no game material for '{slots[i].name}'");
                }
                renderer.sharedMaterials = slots;
                // Unlit parts (lights, the propeller disc) and the cockpit cast none, as in Aircraft.
                var cast = !renderer.transform.IsChildOf(cockpit) && slots[0].shader != disc.shader;
                renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            var parts = new AircraftParts
            {
                elevator = Find("Elevator"),
                aileronLeft = Find("Aileron left"),
                aileronRight = Find("Aileron right"),
                rudder = Find("Rudder"),
                prop = Find("Propeller"),
                discMaterial = disc,
                cockpit = cockpit.gameObject,
                exterior = Find("Exterior").gameObject,
                yokeLeft = Find("Cockpit/Yoke left"),
                yokeRight = Find("Cockpit/Yoke right"),
                throttleKnob = Find("Cockpit/Throttle"),
                strobes = new[] { Find("Strobe left").gameObject, Find("Strobe right").gameObject },
                panelSurface = Find("Cockpit/Live instruments").GetComponent<MeshRenderer>(),
            };
            parts.cockpit.SetActive(false);
            return parts;
        }

        GameObject Part(string name, Transform parent, Geo geo, Material material, Vector3 position, Quaternion? rotation = null, Vector3? scale = null, bool castShadow = false)
        {
            return Three.Part(name, parent, owned.Add(geo.ToMesh(name)), material, position, rotation, scale, castShadow);
        }

        void Wheel(Transform parent, Vector3 position)
        {
            Part("Wheel", parent, Geo.Cylinder(0.22, 0.22, 0.14, 18), tire, position, Three.Euler(0, 0, Mathf.PI / 2), null, true);
        }

        AircraftParts Aircraft(AircraftDef def, Transform root)
        {
            var parts = new AircraftParts();
            // Geometry follows the flight model's definition so contact points line up.
            var high = def.look.highWing;
            var wingY = high ? 1.0f : -0.32f;
            var half = (float)def.span / 2;
            var chord = (float)def.chord;
            var trailZ = 0.1f + chord / 2;
            var mainX = Mathf.Abs((float)def.gear[1].pos.y);
            var mainZ = -(float)def.gear[1].pos.x;
            var noseZ = -(float)def.gear[0].pos.x;

            var exterior = Three.Group("Exterior", root, Vector3.zero);
            parts.exterior = exterior.gameObject;
            // Fuselage, flattened sides, slightly taller than wide
            Part("Fuselage", exterior, FuselageGeometry(), bodyShadowed, Three.Pos(0, 0.05f, 0), null, new Vector3(0.85f, 1.05f, 1), true);
            // Cheat line along each side
            for (var s = -1; s <= 1; s += 2)
                Part("Cheat line", exterior, Geo.Box(0.02, 0.12, 5.6), stripe, Three.Pos(s * 0.53f, -0.05f, 1.2f), null, null, true);
            if (high)
            {
                // Cabin: fills the gap between the fuselage and the wing root
                Part("Cabin", exterior, Geo.Box(1.08, 0.46, 1.9), body, Three.Pos(0, 0.72f, 0.15f), null, null, true);
                // Cabin windows
                Part("Windshield", exterior, Geo.Box(1.0, 0.04, 0.55), glass, Three.Pos(0, 0.72f, -0.86f), Three.Euler(-0.95f, 0, 0));
                for (var s = -1; s <= 1; s += 2)
                    Part("Side window", exterior, Geo.Box(0.02, 0.3, 1.45), glass, Three.Pos(s * 0.55f, 0.74f, 0.1f));
            }
            else
            {
                // Bubble canopy over a low cockpit, with a turtle-deck behind it
                Part("Canopy", exterior, Geo.Sphere(1, 24, 14, 0, Math.PI * 2, 0, Math.PI / 2), glass, Three.Pos(0, 0.62f, -0.1f), null, new Vector3(0.52f, 0.42f, 1.05f), true);
                Part("Turtle deck", exterior, Geo.Box(0.7, 0.26, 1.6), body, Three.Pos(0, 0.6f, 1.4f), Three.Euler(-0.12f, 0, 0), null, true);
            }

            // Wing on top of the cabin, with ailerons on the outer trailing edge
            Part("Wing", root, Geo.Box(def.span, 0.13, chord), bodyShadowed, Three.Pos(0, wingY, 0.1f), null, null, true);
            for (var s = -1; s <= 1; s += 2)
                Part("Wingtip", root, Geo.Box(0.1, 0.14, chord + 0.01), accent, Three.Pos(s * (half - 0.05f), wingY, 0.1f), null, null, true);
            parts.aileronLeft = Three.Group("Aileron left", root, Three.Pos(-(half - 1.55f), wingY, trailZ));
            Part("Surface", parts.aileronLeft, Geo.Box(2.6, 0.06, 0.36), body, Three.Pos(0, 0, 0.18f), null, null, true);
            parts.aileronRight = Three.Group("Aileron right", root, Three.Pos(half - 1.55f, wingY, trailZ));
            Part("Surface", parts.aileronRight, Geo.Box(2.6, 0.06, 0.36), body, Three.Pos(0, 0, 0.18f), null, null, true);
            // Wing struts (high wing only)
            if (high)
            {
                for (var s = -1; s <= 1; s += 2)
                {
                    var a = Three.Pos(s * 0.5f, -0.35f, 0.1f);
                    var b = Three.Pos(s * 2.7f, 0.94f, 0.15f);
                    var q = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                    Part("Strut", root, Geo.Cylinder(0.035, 0.035, Vector3.Distance(a, b), 8), body, (a + b) * 0.5f, q, null, true);
                }
            }

            // Horizontal tail with elevator
            Part("Tailplane", root, Geo.Box(3.4, 0.08, 0.8), body, Three.Pos(0, 0.12f, 5.05f), null, null, true);
            parts.elevator = Three.Group("Elevator", root, Three.Pos(0, 0.12f, 5.45f));
            Part("Surface", parts.elevator, Geo.Box(3.3, 0.05, 0.44), body, Three.Pos(0, 0, 0.22f), null, null, true);
            // Fin and rudder
            Part("Fin", root, Geo.Box(0.07, 1.35, 0.95), body, Three.Pos(0, 0.85f, 5.15f), Three.Euler(0.35f, 0, 0), null, true);
            parts.rudder = Three.Group("Rudder", root, Three.Pos(0, 0.85f, 5.72f));
            Part("Surface", parts.rudder, Geo.Box(0.06, 1.3, 0.4), stripe, Three.Pos(0, 0, 0.2f), null, null, true);

            // Engine: spinner and two-blade propeller
            parts.prop = Three.Group("Propeller", root, Three.Pos(0, -0.02f, -2.25f));
            Part("Spinner", parts.prop, Geo.Cone(0.2, 0.42, 18), accent, Vector3.zero, Three.Euler(-Mathf.PI / 2, 0, 0), null, true);
            Part("Blade", parts.prop, Geo.Box(0.12, 1.9, 0.05), dark, Vector3.zero, null, null, true);
            Part("Propeller disc", root, Geo.Circle(0.95, 32), disc, Three.Pos(0, -0.02f, -2.28f));
            parts.discMaterial = disc;

            // Fixed tricycle gear; contact points match the physics gear legs
            Part("Nose leg", root, Geo.Cylinder(0.04, 0.04, 0.85, 8), metal, Three.Pos(0, -0.55f, noseZ));
            Wheel(root, Three.Pos(0, -0.83f, noseZ));
            for (var s = -1; s <= 1; s += 2)
            {
                Part("Main leg", root, Geo.Box(0.05, 0.95, 0.12), metal, Three.Pos(s * (mainX - 0.35f), -0.55f, mainZ), Three.Euler(0, 0, s * 0.55f));
                Wheel(root, Three.Pos(s * mainX, -0.83f, mainZ));
            }

            // Wheel fairings on the mains
            for (var s = -1; s <= 1; s += 2)
                Part("Fairing", root, Geo.Sphere(1, 16, 10), body, Three.Pos(s * mainX, -0.8f, mainZ - 0.03f), null, new Vector3(0.16f, 0.26f, 0.52f), true);
            // Pitot tube under the left wing, exhaust under the cowl, landing light
            Part("Pitot", root, Geo.Cylinder(0.012, 0.012, 0.42, 6), metal, Three.Pos(-(half * 0.48f), wingY - 0.1f, 0.1f - chord / 2 - 0.1f), Three.Euler(Mathf.PI / 2, 0, 0));
            Part("Exhaust", root, Geo.Cylinder(0.03, 0.035, 0.3, 8), metal, Three.Pos(0.18f, -0.58f, -1.55f), Three.Euler(Mathf.PI / 2 + 0.3f, 0, 0));
            Part("Landing light", root, Geo.Box(0.18, 0.06, 0.02), owned.Add(MaterialKit.Basic("#fffbe8")), Three.Pos(-(half * 0.57f), wingY, 0.1f - chord / 2 + 0.08f));

            // Position lights (red left, green right, white tail) and wingtip strobes
            Part("Light red", root, Geo.Sphere(0.045, 8, 6), owned.Add(MaterialKit.Basic("#ff3b2f")), Three.Pos(-(half + 0.03f), wingY, -0.2f));
            Part("Light green", root, Geo.Sphere(0.045, 8, 6), owned.Add(MaterialKit.Basic("#3dff6e")), Three.Pos(half + 0.03f, wingY, -0.2f));
            var white = owned.Add(MaterialKit.Basic("#ffffff"));
            Part("Light tail", root, Geo.Sphere(0.04, 8, 6), white, Three.Pos(0, 0.2f, 5.98f));
            parts.strobes = new GameObject[2];
            for (var i = 0; i < 2; i++)
            {
                var s = i == 0 ? -1 : 1;
                parts.strobes[i] = Part("Strobe", root, Geo.Sphere(0.06, 8, 6), white, Three.Pos(s * (half + 0.03f), wingY, 0.35f));
            }

            Cockpit(parts, root, high);
            parts.cockpit.SetActive(false);
            return parts;
        }

        void Cockpit(AircraftParts parts, Transform root, bool high)
        {
            // Cockpit interior, shown only in the cockpit view
            var cockpit = Three.Group("Cockpit", root, Vector3.zero);
            parts.cockpit = cockpit.gameObject;
            // Cowling ahead of the windshield, so the nose attitude is visible
            Part("Cowling", cockpit, Geo.Box(1.0, 0.12, 1.1), body, Three.Pos(0, 0.44f, -1.75f), Three.Euler(0.12f, 0, 0));
            // Instrument panel: live six-pack on the pilot's side, radio stack on the right
            var board = Three.Group("Instrument panel", cockpit, Three.Pos(0, 0.42f, -1.02f));
            board.localRotation = Three.Euler(-0.2f, 0, 0);
            Part("Panel", board, Geo.Box(1.12, 0.4, 0.05), panel, Vector3.zero);
            var live = Part("Live instruments", board, Geo.Plane(0.68, 0.34), owned.Add(MaterialKit.BasicNotToneMapped("#101214")), Three.Pos(-0.2f, 0, 0.0265f));
            parts.panelSurface = live.GetComponent<MeshRenderer>();
            float[] radios = { 0.08f, 0.02f, -0.04f, -0.1f };
            foreach (var y in radios)
                Part("Radio", board, Geo.Box(0.3, 0.05, 0.01), dark, Three.Pos(0.36f, y, 0.03f));
            for (var i = 0; i < radios.Length; i++)
                Part("Radio display", board, Geo.Plane(0.1, 0.022), owned.Add(MaterialKit.BasicNotToneMapped(i == 0 ? "#5fd38a" : "#d89a3a")), Three.Pos(0.33f, radios[i], 0.036f));
            // Glareshield
            Part("Glareshield", cockpit, Geo.Box(1.14, 0.04, 0.3), dark, Three.Pos(0, 0.635f, -1.1f), Three.Euler(-0.15f, 0, 0));
            // Control yokes (move with the actual aileron / elevator deflection)
            parts.yokeLeft = Yoke("Yoke left", cockpit, -0.28f);
            parts.yokeRight = Yoke("Yoke right", cockpit, 0.28f);
            // Throttle plunger, centre of the panel: pushed in = full power
            parts.throttleKnob = Three.Group("Throttle", cockpit, Three.Pos(0.02f, 0.27f, THROTTLE_Z));
            Part("Shaft", parts.throttleKnob, Geo.Cylinder(0.007, 0.007, 0.12, 6), metal, Three.Pos(0, 0, -0.06f), Three.Euler(Mathf.PI / 2, 0, 0));
            Part("Knob", parts.throttleKnob, Geo.Cylinder(0.022, 0.022, 0.03, 12), dark, Vector3.zero, Three.Euler(Mathf.PI / 2, 0, 0));
            // Seats' top edges and the floor tunnel, for depth at the bottom of the view
            Part("Tunnel", cockpit, Geo.Box(0.18, 0.12, 1.0), panel, Three.Pos(0, 0.02f, -0.5f));
            // Windshield posts, door frames and cabin roof (a bubble canopy has none)
            for (var s = -1; s <= 1; s += 2)
            {
                if (high)
                {
                    Part("Windshield post", cockpit, Geo.Box(0.04, 0.5, 0.05), panel, Three.Pos(s * 0.52f, 0.8f, -0.95f), Three.Euler(0.75f, 0, 0));
                    Part("Door post", cockpit, Geo.Box(0.03, 0.5, 0.05), panel, Three.Pos(s * 0.54f, 0.75f, 0.15f));
                }
                Part("Door sill", cockpit, Geo.Box(0.03, 0.3, 2.0), panel, Three.Pos(s * 0.56f, 0.3f, -0.2f));
            }
            if (high)
                Part("Roof", cockpit, Geo.Box(1.1, 0.03, 1.7), panel, Three.Pos(0, 0.96f, -0.1f));
        }

        Transform Yoke(string name, Transform cockpit, float x)
        {
            var yoke = Three.Group(name, cockpit, Three.Pos(x, 0.3f, YOKE_Z));
            Part("Shaft", yoke, Geo.Cylinder(0.014, 0.014, 0.36, 8), metal, Three.Pos(0, 0, -0.18f), Three.Euler(Mathf.PI / 2, 0, 0));
            Part("Bar", yoke, Geo.Box(0.3, 0.035, 0.035), dark, Vector3.zero);
            for (var s = -1; s <= 1; s += 2)
                Part("Grip", yoke, Geo.Box(0.035, 0.12, 0.04), dark, Three.Pos(s * 0.15f, 0.05f, 0));
            Part("Hub", yoke, Geo.Cylinder(0.03, 0.03, 0.01, 12), accent, Three.Pos(0, 0, 0.02f));
            return yoke;
        }

        /// <summary>Wingtip strobes: double flash every 1.2 s.</summary>
        public static bool StrobeFlash(double timeS)
        {
            var phase = timeS % 1.2;
            return phase < 0.05 || (phase > 0.15 && phase < 0.2);
        }

        /// <summary>Push the latest physics state into the model's moving parts.</summary>
        public static void SyncAircraft(AircraftParts p, in AircraftVisualState v, float dt)
        {
            p.elevator.localRotation = Three.Euler(v.elevator, 0, 0); // + trailing edge down
            p.aileronRight.localRotation = Three.Euler(-v.aileron, 0, 0); // + roll right: right TE up
            p.aileronLeft.localRotation = Three.Euler(v.aileron, 0, 0);
            p.rudder.localRotation = Three.Euler(0, v.rudder, 0); // + trailing edge right
            p.propAngle = (p.propAngle + v.rpm / 60 * Mathf.PI * 2 * dt) % (Mathf.PI * 2);
            p.prop.localRotation = Three.Euler(0, 0, p.propAngle);
            // Blades blur into a faint disc once the engine is running.
            MaterialKit.SetOpacity(p.discMaterial, 0.08f + v.engine * 0.14f);

            // Yokes mirror the real surface positions: turn for aileron, push/pull for elevator.
            var roll = v.aileron / v.maxAileron;
            var pull = -v.elevator / v.maxElevator;
            SyncYoke(p.yokeLeft, roll, pull);
            SyncYoke(p.yokeRight, roll, pull);
            var knob = p.throttleKnob.localPosition;
            knob.z = -(THROTTLE_Z - v.throttle * 0.07f);
            p.throttleKnob.localPosition = knob;

            var flash = StrobeFlash(v.timeS);
            foreach (var strobe in p.strobes)
                if (strobe.activeSelf != flash) strobe.SetActive(flash);
        }

        static void SyncYoke(Transform yoke, float roll, float pull)
        {
            yoke.localRotation = Three.Euler(0, 0, -roll * 0.6f);
            var at = yoke.localPosition;
            at.z = -(YOKE_Z + pull * 0.07f);
            yoke.localPosition = at;
        }
    }
}
