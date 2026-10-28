param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$HarmonyDll
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$spec = Get-Content -LiteralPath (Join-Path $root 'Build.json') -Raw | ConvertFrom-Json
$game = (Resolve-Path -LiteralPath $GameDir).Path
$harmony = (Resolve-Path -LiteralPath $HarmonyDll).Path
if ((Get-Content -LiteralPath (Join-Path $game 'Version.txt') -Raw) -notmatch '^1\.7\.') { throw 'Supply a licensed RimWorld 1.7 installation.' }
$volume = [IO.Path]::GetPathRoot($root)
$disk = [IO.DriveInfo]::new($volume)
if ($disk.AvailableFreeSpace -lt 1500000000) { throw 'Less than 1.5 GB free; build stopped.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdks = @(& $dotnet --list-sdks | ForEach-Object {
    if ($_ -match '^([^ ]+) \[(.+)\]$') {
        $version = $matches[1]
        $candidate = Join-Path (Join-Path $matches[2] $version) 'Roslyn/bincore/csc.dll'
        if (Test-Path -LiteralPath $candidate) { [pscustomobject]@{Version=[version]($version.Split('-')[0]);Path=$candidate} }
    }
} | Sort-Object Version -Descending)
if (-not $sdks.Count) { throw 'An installed .NET SDK with Roslyn is required. No dependencies are downloaded.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root $spec.sourceDirectory) -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
if (-not $sources.Count) { throw 'Production sources are missing.' }
$references = @(Get-ChildItem -LiteralPath $game -File -Filter '*.dll' | Where-Object {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object FullName)
$references = @($references + $harmony | Sort-Object -Unique)
$output = Join-Path $root '.build/1.7'
[void][IO.Directory]::CreateDirectory($output)
$product = Join-Path $output ($spec.assemblyName + '.dll')
$arguments = @('/nologo','/noconfig','/nostdlib+','/langversion:latest','/target:library','/optimize+','/deterministic+','/debug-','/warn:4',('/pathmap:'+$root+'=.'),('/out:'+$product))
$arguments += @($references | ForEach-Object { '/reference:'+$_ })
$arguments += @($sources | ForEach-Object FullName)
$messages = @(& $dotnet $sdks[0].Path @arguments 2>&1 | ForEach-Object { "$_" })
$exitCode = $LASTEXITCODE
$receipt = [ordered]@{
    status=$(if($exitCode -eq 0){'COMPILED'}else{'FAILED'});
    checkedUtc=[DateTime]::UtcNow.ToString('o');
    gameVersion=(Get-Content -LiteralPath (Join-Path $game 'Version.txt') -Raw).Trim();
    gameApiSHA256=(Get-FileHash -LiteralPath (Join-Path $game 'Assembly-CSharp.dll')).Hash;
    harmonySHA256=(Get-FileHash -LiteralPath $harmony).Hash;
    sources=@($sources | ForEach-Object { @{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} });
    diagnostics=@($messages | ForEach-Object { $_.Replace($root,'REPOSITORY').Replace($game,'GAME').Replace($harmony,'HARMONY') });
    nativePlaytest='NOT_RUN_THIS_PASS';installed=$false;exitCode=$exitCode
}
if ($exitCode -eq 0) { $receipt['productSHA256']=(Get-FileHash -LiteralPath $product).Hash }
[IO.File]::WriteAllText((Join-Path $output 'Build-Receipt.json'),($receipt | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$messages | ForEach-Object { $_.Replace($root,'REPOSITORY').Replace($game,'GAME') }
if ($exitCode -ne 0) { throw "Compilation failed with exit code $exitCode" }
Write-Output ('Compiled '+$spec.assemblyName+' into .build/1.7. No game launched or installed files changed.')
