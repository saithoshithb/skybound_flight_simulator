using System;
using UnityEngine;

namespace Skybound
{
    [Serializable]
    public class GearLeg
    {
        public string name;
        /// <summary>Contact point, body FRD (m from CG).</summary>
        public V3 pos;
        public double spring; // N/m
        public double damper; // N·s/m
        public bool steerable;
        public bool brakes;
        /// <summary>A skid, not a wheel: high sliding friction, flagged as a strike.</summary>
        public bool skid;
    }

    [Serializable]
    public class StructurePoint
    {
        public string name;
        public V3 pos;
    }

    [Serializable]
    public class AircraftLook
    {
        public bool highWing;
        public string body;
        public string stripe;
        public string accent;
    }

    /// <summary>Easy-flight speeds (kt): cruise, take-off target, and the adjustable range.</summary>
    [Serializable]
    public class EasySpeeds
    {
        public double cruiseKt, takeoffKt, minKt, maxKt, rotateKt;
    }

    /// <summary>
    /// Aircraft definition: geometry, mass properties, engine, and nondimensional
    /// stability/control derivatives (aerospace body axes). Units SI.
    ///
    /// Control sign convention: elevator + = nose up, aileron + = roll right,
    /// rudder + = yaw right.
    /// </summary>
    [CreateAssetMenu(fileName = "Aircraft", menuName = "Skybound/Aircraft")]
    public class AircraftDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public double mass; // kg
        public V3 inertia; // Ixx, Iyy, Izz (kg·m²)
        public double wingArea; // S, m²
        public double span; // b, m
        public double chord; // c-bar, m
        public double oswald;
        public double maxPowerW;
        public double propEfficiency;
        public double staticThrustN;
        public double engineTimeConstantS;

        [Header("Lift and drag")]
        public double CL0;
        public double CLalpha;
        public double CLq;
        public double CLde;
        public double CD0;
        /// <summary>Centre (rad) and sharpness of the lift-to-flat-plate blend. Peak CL comes a few degrees earlier.</summary>
        public double alphaStall;
        public double stallSharpness;

        [Header("Side force")]
        public double CYbeta;
        public double CYdr;

        [Header("Roll")]
        public double Clbeta;
        public double Clp;
        public double Clr;
        public double Clda;
        public double Cldr;

        [Header("Pitch")]
        public double Cm0;
        public double Cmalpha;
        public double Cmq;
        public double Cmde;

        [Header("Yaw")]
        public double Cnbeta;
        public double Cnp;
        public double Cnr;
        public double Cnda;
        public double Cndr;

        [Header("Control surface limits (rad) and actuator lag (s)")]
        public double maxElevator;
        public double maxAileron;
        public double maxRudder;
        public double surfaceLagS;
        public double maxNoseSteer;

        [Header("Gear and structure")]
        public GearLeg[] gear;
        /// <summary>Points that must never touch the ground (crash if they do).</summary>
        public StructurePoint[] structure;
        public double vneMs;

        [Header("Menu and model")]
        [TextArea] public string description;
        public AircraftLook look;
        public EasySpeeds easy;
    }
}
