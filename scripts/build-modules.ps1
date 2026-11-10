[CmdletBinding()]
param([string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe")
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $roles=Get-Content -LiteralPath 'roles.json' -Raw | ConvertFrom-Json
    $logs=Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\generated\module-build'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    & dotnet build tools/ModuleBuilder -c Release -v quiet
    if($LASTEXITCODE -ne 0) {throw 'Module generator build failed.'}
    & dotnet build tools/PackTransforms -c Release -v quiet
    if($LASTEXITCODE -ne 0) {throw 'Script pack transforms build failed.'}
    foreach($entry in $roles) {
        & dotnet tools/ModuleBuilder/bin/Release/net9.0/ModuleBuilder.dll $PSScriptRoot $entry.role
        if($LASTEXITCODE -ne 0) {throw "Generation failed: $($entry.role)"}
        & $Python tools/prepare-module-pack.py $entry.role
        if($LASTEXITCODE -ne 0) {throw "Packing preparation failed: $($entry.role)"}
        $project=Join-Path $PSScriptRoot "pack\$($entry.role)\$($entry.role).csproj"
        & dotnet build $project -c Release -v quiet *> (Join-Path $logs "$($entry.role).log")
        if($LASTEXITCODE -ne 0) {throw "Script build failed: $($entry.role). See $logs"}
    }
    $utf8=New-Object Text.UTF8Encoding($false,$true)
    $counts=@()
    foreach($entry in $roles) {
        $packed=Join-Path $PSScriptRoot "packed\$($entry.role)\script.cs"
        $text=[IO.File]::ReadAllText($packed,$utf8)
        if($text.Length -ge 100000) {throw "$($entry.role) has $($text.Length) characters; every module must be below 100000."}
        $counts += [pscustomobject]@{role=$entry.role;file=$entry.file;block=$entry.block;characters=$text.Length;sha256=(Get-FileHash -LiteralPath $packed -Algorithm SHA256).Hash}
    }
    # Publish only after every module compiled and passed the same strict cap.
    foreach($entry in $roles) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "packed\$($entry.role)\script.cs") -Destination (Join-Path $PSScriptRoot $entry.file) -Force
    }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'module-build.json'),($counts|ConvertTo-Json -Depth 5),$utf8)
    $inputs=@(& (Join-Path $PSScriptRoot 'get-module-inputs.ps1'))
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build-inputs.json'),($inputs|ConvertTo-Json -Depth 5),$utf8)
    $counts | Format-Table role,characters,file -AutoSize
} finally { Pop-Location }
