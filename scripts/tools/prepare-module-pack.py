from pathlib import Path
import re, sys, subprocess
root=Path(__file__).resolve().parents[1]
roles=sys.argv[1:] or ['CarrierCommander','CarrierPilot','MinerCommander','MinerPilot']
header='''using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using VRage;
namespace IngameScript { public class Program : MyGridProgram {
'''
for role in roles:
    source=(root/f'generated/{role}.cs').read_text(encoding='utf-8-sig')
    protected,count=re.subn(r'(?m)^(\s*(?:(?:public|private|protected|internal)\s+)?enum\s+\w+\s*(?::\s*\w+\s*)?\{[^{}]*\})', lambda m:'\n#region mdk preserve\n'+m[1]+'\n#endregion\n',source)
    assert count==len(re.findall(r'\benum\s+\w+\s*(?::\s*\w+\s*)?\{',source))
    project=root/'pack'/role
    project.mkdir(parents=True,exist_ok=True)
    (project/'Program.cs').write_text(header+protected+'\n}}\n',encoding='utf-8')
    transform=root/'tools/PackTransforms/bin/Release/net9.0/PackTransforms.dll'
    wrapped=project/'Program.cs'
    if 'void UpdateRuntimeMetrics(' in protected:
        subprocess.run(['dotnet',str(transform),'simplify-runtime',str(wrapped),str(wrapped)],check=True)
    subprocess.run(['dotnet',str(transform),'enum-rewrite',str(wrapped),str(wrapped),'--game-bin','C:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64'],check=True)
    subprocess.run(['dotnet',str(transform),'pack-text',str(wrapped),str(wrapped)],check=True)
    subprocess.run(['dotnet',str(transform),'api-aliases',str(wrapped),str(wrapped)],check=True)
    (project/f'{role}.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion><PlatformTarget>x64</PlatformTarget><GenerateAssemblyInfo>false</GenerateAssemblyInfo><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><PackageReference Include="Mal.Mdk2.PbPackager" Version="2.2.14" PrivateAssets="all" /><PackageReference Include="Mal.Mdk2.References" Version="2.2.8" PrivateAssets="all" /></ItemGroup>
</Project>''',encoding='utf-8')
    (project/'mdk.ini').write_text('[mdk]\ntype=programmableblock\nnamespaces=IngameScript\nminify=full\nminifyextraoptions=none\nignores=obj/**/*,bin/**/*\n',encoding='utf-8')
    (project/'mdk.local.ini').write_text(f'[mdk]\nbinarypath=C:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64\noutput={root.as_posix()}/packed\ninteractive=DoNothing\nlog={role}-pack.log\n',encoding='utf-8')
