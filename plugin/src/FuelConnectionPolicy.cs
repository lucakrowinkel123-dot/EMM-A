using System;
using System.Collections.Generic;

namespace EMMA3.Client
{
    internal static class FuelConnectionPolicy
    {
        internal static bool IsCraftEndpoint(long gridId, string terminalName, ISet<long> craftGrids, string otherCraftTag)
        {
            return craftGrids.Contains(gridId) && (terminalName == null
                || terminalName.IndexOf(otherCraftTag, StringComparison.OrdinalIgnoreCase) < 0);
        }

        internal sealed class Thruster { public long Id; public string Name, Fuel; public bool RequiresConveyor, Initialized; }
        internal sealed class Source { public long Id; public string[] Fuels; public bool Owned, Initialized; }
        internal sealed class Result
        {
            public readonly List<Thruster> Disconnected = new List<Thruster>();
            public readonly List<Thruster> Unknown = new List<Thruster>();
            public bool Passed { get { return Disconnected.Count == 0 && Unknown.Count == 0; } }
        }
        internal static Result Check(IList<Thruster> thrusters, IList<Source> sources, bool networkReady, Func<long, long, bool> reachable)
        {
            var result = new Result();
            foreach (var thruster in thrusters)
            {
                if (!thruster.Initialized) { result.Unknown.Add(thruster); continue; }
                if (!thruster.RequiresConveyor) continue;
                if (!networkReady) { result.Unknown.Add(thruster); continue; }
                bool connected = false, unknownSource = false;
                foreach (var source in sources)
                {
                    if (!source.Owned) continue;
                    if (!source.Initialized) { unknownSource = true; continue; }
                    if (source.Fuels == null || Array.IndexOf(source.Fuels, thruster.Fuel) < 0) continue;
                    if (reachable(thruster.Id, source.Id) || reachable(source.Id, thruster.Id))
                    { connected = true; break; }
                }
                if (!connected)
                {
                    if (unknownSource) result.Unknown.Add(thruster);
                    else result.Disconnected.Add(thruster);
                }
            }
            return result;
        }
    }
}
