using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skybound
{
    /// <summary>
    /// The browser game's scene code is written in three.js space: x right, y up, −z forward.
    /// Unity has z forward, so a scene is the same scene mirrored in z. These helpers take the
    /// numbers exactly as the TypeScript has them, which keeps the ported scene code comparable
    /// line by line.
    /// </summary>
    public static class Three
    {
        public static Vector3 Pos(float x, float y, float z) => new Vector3(x, y, -z);

        /// <summary>A three.js rotation (radians, XYZ order). Mirroring in z flips the x and y rotations.</summary>
        public static Quaternion Euler(float x, float y, float z)
        {
            return Quaternion.AngleAxis(-x * Mathf.Rad2Deg, Vector3.right)
                * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up)
                * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        }

        /// <summary>A colour as written: sRGB. Materials and lights expect this and convert it themselves.</summary>
        public static Color Hex(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var color))
                throw new System.ArgumentException("Not a colour: " + hex);
            return color;
        }

        /// <summary>A colour as three.js holds it: linear. For anything mixed by hand.</summary>
        public static Color Linear(string hex) => Hex(hex).linear;

        public static Transform Group(string name, Transform parent, Vector3 position)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = position;
            return group;
        }

        /// <summary>A mesh in the scene. Terrain and crashes come from the flight model, so no colliders.</summary>
        public static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position,
            Quaternion? rotation = null, Vector3? scale = null, bool castShadow = false)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation ?? Quaternion.identity;
            part.transform.localScale = scale ?? Vector3.one;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return part;
        }
    }

    /// <summary>Meshes, materials and textures made at run time. Unity doesn't free these with the scene objects.</summary>
    public sealed class Owned
    {
        readonly List<Object> items = new List<Object>();

        public T Add<T>(T item) where T : Object
        {
            items.Add(item);
            return item;
        }

        public void DestroyAll()
        {
            foreach (var item in items)
            {
                if (Application.isPlaying) Object.Destroy(item);
                else Object.DestroyImmediate(item);
            }
            items.Clear();
        }
    }
}
