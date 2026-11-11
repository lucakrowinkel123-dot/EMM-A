# EMM-A client plugin

EMM-A 0.9.40 Single Miner automates asteroid mining for one carrier and one registered miner in Space Engineers 1. Install the EMM-A client plugin through Pulsar Legacy and paste the five matching scripts into their programmable blocks.

## 0.9.40 Single Miner scripts

Open a script, choose **Raw**, and paste the entire file into the matching block’s editor.

| Carrier | Miner |
| --- | --- |
| [Carrier Commander](scripts/0.9.40-single-miner/EMM-A%20Carrier.txt) | [Miner Commander](scripts/0.9.40-single-miner/EMM-A%20Miner.txt) |
| [Carrier Pilot](scripts/0.9.40-single-miner/EMM-A%20Carrier%20Pilot.txt) | [Miner Pilot](scripts/0.9.40-single-miner/EMM-A%20Miner%20Pilot.txt) |
| [Carrier LCD](scripts/0.9.40-single-miner/EMM-A%20Carrier%20LCD.txt) | |

A stored miner keeps its registration. A carrier with an existing multi-miner save must finish its fleet work and reduce its roster to the miner it wants to keep using its previous Commander before switching. Back up that save before changing registrations.

The [0.8.56 release](https://github.com/lucakrowinkel123-dot/EMM-A/tree/v0.8.56) has its own plugin and four scripts.

## Source and build

The [plugin source](plugin/src) targets .NET Framework 4.8 and Pulsar Legacy. On Windows, use the installed Space Engineers 1 game assemblies and the Pulsar Legacy libraries. The project reads their locations from SpaceEngineersBin64 and PulsarLibraries or uses the defaults in Directory.Build.props.

~~~powershell
dotnet build plugin/EMMA3.Client.csproj -c Release
~~~

Run the scan-target and per-instance logging regressions with `dotnet run --project tests/PluginIsolation/PluginIsolation.Tests.csproj -c Release`. Plugin and script logs, including startup diagnostics, are written under `Storage/EMMA3` in the current game user data directory.

[Companion project](https://github.com/lucakrowinkel123-dot/HangarManager)
