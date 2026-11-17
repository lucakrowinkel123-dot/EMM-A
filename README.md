# EMM-A client plugin

EMM-A 0.9.47B automates asteroid mining for one carrier and one registered miner in Space Engineers 1. This plugin update works with the five 0.9.46B programmable-block scripts.

Install an **SDX2 targeted lidar** on the carrier's own grid and switch it on. EMM-A requires a powered, working lidar that the player can access. Turning off the last qualifying lidar suspends plugin services and cancels scans in progress. A vanilla ore detector or a lidar on a docked ship cannot activate the plugin.

Each qualifying lidar uses its current WeaponCore range setting. When its range sphere touches an asteroid's bounding box, EMM-A scans the whole asteroid, including ore beyond the sphere. Even 5 cm of overlap qualifies. The SDX2 block description documents ship-identification pings. The ore map described here comes from EMM-A's own voxel scan.

## 0.9.46B scripts

Open a script, choose **Raw**, and paste the entire file into the matching block's editor.

| Carrier | Miner |
| --- | --- |
| [Carrier Commander](scripts/0.9.46B/EMM-A%20Carrier.txt) | [Miner Commander](scripts/0.9.46B/EMM-A%20Miner.txt) |
| [Carrier Pilot](scripts/0.9.46B/EMM-A%20Carrier%20Pilot.txt) | [Miner Pilot](scripts/0.9.46B/EMM-A%20Miner%20Pilot.txt) |
| [Carrier LCD](scripts/0.9.46B/EMM-A%20Carrier%20LCD.txt) | |

A stored miner keeps its registration. A carrier with an existing multi-miner save must finish its fleet work and reduce its roster to the miner it wants to keep using its previous Commander before switching. Back up that save before changing registrations.

The [0.8.56 release](https://github.com/lucakrowinkel123-dot/EMM-A/tree/v0.8.56) has its own plugin and four scripts.

## Source and build

The [plugin source](plugin/src) targets .NET Framework 4.8 and Pulsar Legacy. On Windows, use the installed Space Engineers 1 game assemblies and the Pulsar Legacy libraries. The project reads their locations from SpaceEngineersBin64 and PulsarLibraries or uses the defaults in Directory.Build.props.

~~~powershell
dotnet build plugin/EMMA3.Client.csproj -c Release
~~~

Run the detector boundary checks with `dotnet run --project tests/DetectorCoverage -c Release`. Run the scan-target and per-instance logging regressions with `dotnet run --project tests/PluginIsolation/PluginIsolation.Tests.csproj -c Release`. Plugin and script logs, including startup diagnostics, are written under `Storage/EMMA3` in the current game user data directory.

[Companion project](https://github.com/lucakrowinkel123-dot/HangarManager)
