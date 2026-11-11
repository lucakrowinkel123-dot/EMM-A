# EMM-A client plugin

This branch contains the Pulsar Legacy client plugin bundled with EMM-A 0.9.40 for Space Engineers 1. It plans asteroid mining routes for a carrier and its fleet of miners. In-game use requires the matching 0.9.40 programmable-block scripts, installed separately.

The [0.8.56 release](https://github.com/lucakrowinkel123-dot/EMM-A/tree/v0.8.56) contains the earlier single-miner plugin and its four companion scripts. The scripts directory on this branch is from that release.

## Source and build

The [plugin source](plugin/src) targets .NET Framework 4.8 and Pulsar Legacy. On Windows, use the installed Space Engineers 1 game assemblies and the Pulsar Legacy libraries. The project reads their locations from SpaceEngineersBin64 and PulsarLibraries or uses the defaults in Directory.Build.props.

~~~powershell
dotnet build plugin/EMMA3.Client.csproj -c Release
~~~

Run the scan-target and per-instance logging regressions with `dotnet run --project tests/PluginIsolation/PluginIsolation.Tests.csproj -c Release`. Plugin and script logs, including startup diagnostics, are written under `Storage/EMMA3` in the current game user data directory.

[Companion project](https://github.com/lucakrowinkel123-dot/HangarManager)
