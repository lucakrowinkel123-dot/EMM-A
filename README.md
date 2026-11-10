# EMM-A 0.8.56

Automates asteroid mining for one carrier and one miner in Space Engineers 1. The client plugin plans routes and the four programmable-block scripts fly the ships.

The four ready-to-paste scripts and their matching readable sources are in [scripts](scripts). Each ship has a Commander and a Pilot. EMM-A 0.9.00 introduced the multi-miner fleet.

## Source and build

The [plugin source](plugin/src) targets .NET Framework 4.8 and Pulsar Legacy. On Windows, use the installed Space Engineers 1 game assemblies and the Pulsar Legacy libraries. The project reads their locations from SpaceEngineersBin64 and PulsarLibraries or uses the defaults in Directory.Build.props.

~~~powershell
dotnet build plugin/EMMA3.Client.csproj -c Release
~~~

[Companion project](https://github.com/lucakrowinkel123-dot/HangarManager)
