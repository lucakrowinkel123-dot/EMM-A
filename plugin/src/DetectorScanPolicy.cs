using System;
using VRageMath;

namespace EMMA3.Client
{
    internal static class DetectorScanPolicy
    {
        internal static bool IsSdxLidar(string subtype)
        {
            return subtype == "sdx_detectorTargeted_lidar" || subtype == "sdx_detectorTargeted_lidarMes";
        }

        internal static double Range(double setting, double maximum, bool lidar)
        {
            if (!(maximum > 0) || double.IsInfinity(maximum) || double.IsNaN(setting) || double.IsInfinity(setting))
                return 0;
            // WeaponCore reports the lesser of the active hardware and ammo limits as the slider maximum.
            if (lidar && setting == -1) return maximum;
            return Math.Max(0, Math.Min(setting, maximum));
        }

        internal static bool Overlaps(Vector3D position, double radius, BoundingBoxD box)
        {
            if (!(radius > 0) || double.IsInfinity(radius)) return false;
            var nearest = Vector3D.Clamp(position, box.Min, box.Max);
            return Vector3D.DistanceSquared(position, nearest) <= radius * radius;
        }
    }
}
