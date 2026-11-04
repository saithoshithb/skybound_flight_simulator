using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skybound
{
    /// <summary>
    /// The buildings made in Blender (Tools/blender/buildings.py): where each one stands, and its collision boxes,
    /// which make it solid ground for the flight model, so flying into one crashes and a flat roof holds the wheels.
    /// Each building stands on the lowest ground under its boxes; its plinth hides the slope.
    ///
    /// Coordinates: north (m) and east (m) from the runway-36 threshold; heading in degrees anticlockwise seen
    /// from above, 0 when the building's front faces south.
    /// </summary>
    public static class Buildings
    {
        [Serializable] class Solid { public double x, y, w, d, t; }
        [Serializable] class Kind { public string name; public Solid[] solids; }
        [Serializable] class Spot { public string type; public double north, east, heading; }
        [Serializable] class Layout { public Kind[] types; public Spot[] placed; }

        public struct Building
        {
            public string type;
            public double north, east, heading, ground;
        }

        /// <summary>A collision box: centre and half sizes in the building's frame, its top above the field, and how far it reaches from the building's origin.</summary>
        struct Box
        {
            public double north, east, cos, sin, x, y, halfW, halfD, top, reach;
        }

        static Building[] all;
        static Box[] boxes;
        static Dictionary<string, Solid[]> kinds;

        public static Building[] All
        {
            get
            {
                Load();
                return all;
            }
        }

        static void Load()
        {
            if (all != null) return;
            var asset = Resources.Load<TextAsset>("Buildings/layout");
            if (asset == null) throw new InvalidOperationException("Resources/Buildings/layout.json is missing: export it with Tools/blender/buildings.py");
            var layout = JsonUtility.FromJson<Layout>(asset.text);
            kinds = new Dictionary<string, Solid[]>();
            foreach (var kind in layout.types) kinds[kind.name] = kind.solids;

            var placed = new List<Building>();
            var solid = new List<Box>();
            foreach (var spot in layout.placed)
            {
                if (!kinds.TryGetValue(spot.type, out var solids)) throw new InvalidOperationException("No building type " + spot.type);
                var cos = Math.Cos(spot.heading * Math.PI / 180);
                var sin = Math.Sin(spot.heading * Math.PI / 180);
                var ground = TerrainHeight.At(spot.north, spot.east);
                foreach (var s in solids)
                {
                    for (var cx = -1; cx <= 1; cx += 2)
                    {
                        for (var cy = -1; cy <= 1; cy += 2)
                        {
                            var lx = s.x + cx * s.w / 2;
                            var ly = s.y + cy * s.d / 2;
                            ground = Math.Min(ground, TerrainHeight.At(spot.north + sin * lx + cos * ly, spot.east + cos * lx - sin * ly));
                        }
                    }
                }
                placed.Add(new Building { type = spot.type, north = spot.north, east = spot.east, heading = spot.heading, ground = ground });
                foreach (var s in solids)
                {
                    solid.Add(new Box
                    {
                        north = spot.north, east = spot.east, cos = cos, sin = sin,
                        x = s.x, y = s.y, halfW = s.w / 2, halfD = s.d / 2, top = ground + s.t,
                        reach = Math.Sqrt(Math.Pow(Math.Abs(s.x) + s.w / 2, 2) + Math.Pow(Math.Abs(s.y) + s.d / 2, 2)),
                    });
                }
            }
            boxes = solid.ToArray();
            all = placed.ToArray();
        }

        /// <summary>Height above the field of the highest building top at a point; 0 where there is none.</summary>
        public static double TopAt(double north, double east)
        {
            Load();
            double top = 0;
            foreach (var b in boxes)
            {
                var de = east - b.east;
                var dn = north - b.north;
                if (de * de + dn * dn > b.reach * b.reach) continue;
                // Into the building's frame
                var lx = b.cos * de + b.sin * dn;
                var ly = -b.sin * de + b.cos * dn;
                if (Math.Abs(lx - b.x) <= b.halfW && Math.Abs(ly - b.y) <= b.halfD && b.top > top) top = b.top;
            }
            return top;
        }

        /// <summary>Like <see cref="TopAt"/>, for one building only; 0 off it.</summary>
        public static double TopOf(Building b, double north, double east)
        {
            Load();
            var cos = Math.Cos(b.heading * Math.PI / 180);
            var sin = Math.Sin(b.heading * Math.PI / 180);
            var lx = cos * (east - b.east) + sin * (north - b.north);
            var ly = -sin * (east - b.east) + cos * (north - b.north);
            double top = 0;
            foreach (var s in kinds[b.type])
                if (Math.Abs(lx - s.x) <= s.w / 2 && Math.Abs(ly - s.y) <= s.d / 2) top = Math.Max(top, b.ground + s.t);
            return top;
        }

        /// <summary>The ground the flight model flies over: the terrain, or a building's top where one stands.</summary>
        public static double GroundAt(double north, double east) => Math.Max(TerrainHeight.At(north, east), TopAt(north, east));
    }
}
