using UnityEngine;

namespace Skybound
{
    /// <summary>
    /// Physics uses NED world / FRD body in double precision; Unity is left-handed
    /// with x right (east), y up, z forward (north). The mapping is a reflection,
    /// so a quaternion's vector part changes sign as well as axes.
    /// </summary>
    public static class Coords
    {
        public static Vector3 ToUnityPosition(V3 ned) => new Vector3((float)ned.y, (float)-ned.z, (float)ned.x);

        public static Quaternion ToUnityRotation(Quat q) => new Quaternion((float)-q.y, (float)q.z, (float)-q.x, (float)q.w);
    }
}
