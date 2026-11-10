$ErrorActionPreference='Stop'
$files=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'tools') -File -Recurse | Where-Object {$_.Extension -in @('.cs','.csproj','.py') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'})
$files+=@(Get-Item -LiteralPath (Join-Path $PSScriptRoot '..\plugin\src\ShipNames.cs'))
$files+=@(Get-Item -LiteralPath (Join-Path $PSScriptRoot 'modules.json'),(Join-Path $PSScriptRoot 'roles.json'),(Join-Path $PSScriptRoot 'build-modules.ps1'),$PSCommandPath)
foreach($file in ($files|Sort-Object FullName)) {
 $relative=if($file.FullName.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){$file.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')}else{'../plugin/src/ShipNames.cs'}
 [pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
