using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRageMath;

namespace EMMA3.Client
{
    // Stored on the Carrier's world-persistent bridge. All geometry is relative
    // to the reference miner, so it is independent of the Carrier's position.
    internal sealed class MinerDimensions
    {
        public string Identity;
        public double Radius;
        public Vector3D RemotePosition, RemoteForward, RemoteUp, ToolOffsetLocal;
        public readonly List<DrillFootprint> Drills = new List<DrillFootprint>();
        private const int MaxDrills = 256;
        public static MinerDimensions Capture(string identity, double radius, MatrixD remote, MatrixD connector, IList<DrillFootprint> drills, Vector3D toolOffset)
        {
            MatrixD inverse = MatrixD.Invert(connector);
            var data = new MinerDimensions {
                Identity = identity, Radius = radius, ToolOffsetLocal = toolOffset,
                RemotePosition = Vector3D.Transform(remote.Translation, inverse),
                RemoteForward = Vector3D.TransformNormal(remote.Forward, inverse),
                RemoteUp = Vector3D.TransformNormal(remote.Up, inverse)
            };
            if (drills != null) foreach (DrillFootprint drill in drills) data.Drills.Add(drill);
            if (!data.Valid()) throw new ArgumentException("Invalid miner dimensions");
            return data;
        }
        public MatrixD Project(MatrixD carrierConnector)
        {
            var remote = MatrixD.CreateWorld(RemotePosition, RemoteForward, RemoteUp);
            return GeometrySafety.ProjectRemoteAtCarrierConnector(remote, MatrixD.Identity, carrierConnector);
        }
        public string Serialize()
        {
            if (!Valid()) throw new InvalidOperationException("Invalid miner dimensions");
            var parts = new List<string> { "1", Convert.ToBase64String(Encoding.UTF8.GetBytes(Identity)),
                Number(Radius), Vector(RemotePosition), Vector(RemoteForward), Vector(RemoteUp), Vector(ToolOffsetLocal), Drills.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (DrillFootprint drill in Drills) parts.Add(Vector(drill.LocalOffset) + "," + Number(drill.Radius));
            return string.Join("|", parts);
        }
        public static bool TryParse(string text, out MinerDimensions data)
        {
            data = null;
            if (string.IsNullOrEmpty(text) || text.Length > 32000) return false;
            try
            {
                string[] p = text.Split('|'); int count; double radius; Vector3D pos, forward, up, tool;
                if (p.Length < 8 || p[0] != "1" || !ReadNumber(p[2], out radius) || !ReadVector(p[3], out pos)
                    || !ReadVector(p[4], out forward) || !ReadVector(p[5], out up) || !ReadVector(p[6], out tool)
                    || !int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                    || count < 1 || count > MaxDrills || p.Length != 8 + count) return false;
                var result = new MinerDimensions { Identity = Encoding.UTF8.GetString(Convert.FromBase64String(p[1])), Radius = radius,
                    RemotePosition = pos, RemoteForward = forward, RemoteUp = up, ToolOffsetLocal = tool };
                for (int i = 0; i < count; i++)
                {
                    string[] d = p[8 + i].Split(','); double x, y, z, r;
                    if (d.Length != 4 || !ReadNumber(d[0], out x) || !ReadNumber(d[1], out y) || !ReadNumber(d[2], out z) || !ReadNumber(d[3], out r)) return false;
                    result.Drills.Add(new DrillFootprint { LocalOffset = new Vector3D(x, y, z), Radius = r });
                }
                if (!result.Valid()) return false;
                data = result; return true;
            }
            catch (FormatException) { return false; }
        }
        private bool Valid()
        {
            if (string.IsNullOrWhiteSpace(Identity) || Identity.Length > 256 || !Finite(Radius) || Radius <= 0 || Radius > 10000
                || !RemotePosition.IsValid() || !RemoteForward.IsValid() || !RemoteUp.IsValid() || !ToolOffsetLocal.IsValid()
                || Math.Abs(RemoteForward.LengthSquared() - 1) > .0001 || Math.Abs(RemoteUp.LengthSquared() - 1) > .0001
                || Math.Abs(Vector3D.Dot(RemoteForward, RemoteUp)) > .0001 || Drills.Count < 1 || Drills.Count > MaxDrills) return false;
            foreach (DrillFootprint drill in Drills)
                if (!drill.LocalOffset.IsValid() || !Finite(drill.Radius) || drill.Radius <= 0 || drill.Radius > 10000) return false;
            return true;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Vector(Vector3D v) { return Number(v.X) + "," + Number(v.Y) + "," + Number(v.Z); }
        private static bool ReadNumber(string s, out double n)
        { return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && Finite(n); }
        private static bool ReadVector(string s, out Vector3D v)
        {
            v = Vector3D.Zero; string[] p = s.Split(','); double x, y, z;
            if (p.Length != 3 || !ReadNumber(p[0], out x) || !ReadNumber(p[1], out y) || !ReadNumber(p[2], out z)) return false;
            v = new Vector3D(x, y, z); return true;
        }
    }
}
