param(
    [string]$Dotnet = 'dotnet',
    [string]$Wix = 'wix',
    [string]$Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $repo 'artifacts\stage'
$out = Join-Path $repo 'artifacts\release'
# Generated stage only: validate its resolved boundary before removing stale publish files.
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $expectedStage = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts\stage'))
    if ($resolvedStage -ne $expectedStage -or -not $resolvedStage.StartsWith([IO.Path]::GetFullPath($repo) + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected staging path' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Force $stage, $out | Out-Null
function Checked { param([string]$Command,[string[]]$Arguments) & $Command @Arguments; if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)" } }
Checked $Wix @('extension','add','--global','WixToolset.Util.wixext/6.0.2')
Checked $Dotnet @('publish',"$repo\src\MkRgb.Desktop\MkRgb.Desktop.csproj",'-c','Release','-r','win-x64','--self-contained','true','-o',$stage,'-p:DebugType=None')
Checked $Dotnet @('publish',"$repo\src\MkRgb.Service\MkRgb.Service.csproj",'-c','Release','-r','win-x64','--self-contained','true','-o',"$stage\service",'-p:DebugType=None')
$backend = Join-Path $repo 'vendor\OpenRGB-1.1.zip'
New-Item -ItemType Directory -Force (Split-Path $backend) | Out-Null
if (-not (Test-Path $backend)) { Invoke-WebRequest 'https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip' -OutFile $backend }
if ((Get-FileHash $backend -Algorithm SHA256).Hash -ne '63291726C6FC84EB479EF2EE752E97E1187F6CE9C49FD27EEE40DED588A8172A') { throw 'OpenRGB checksum mismatch' }
Expand-Archive -LiteralPath $backend -DestinationPath "$repo\vendor\OpenRGB" -Force
New-Item -ItemType Directory -Force "$stage\service\OpenRGB" | Out-Null
Get-ChildItem -LiteralPath "$repo\vendor\OpenRGB\OpenRGB Windows 64-bit" | Copy-Item -Destination "$stage\service\OpenRGB" -Recurse -Force


Copy-Item -LiteralPath "$repo\LICENSE","$repo\THIRD-PARTY-NOTICES.md" -Destination $stage -Force
Copy-Item -LiteralPath "$repo\licenses" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$repo\docs" -Destination $stage -Recurse -Force

# Deterministic component identities are based on relative paths; WiX generates stable GUIDs.
function Id([string]$value) { 'C' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($value))).Substring(0,24) }
function Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
$components = [Collections.Generic.List[string]]::new()
function DirectoryXml([string]$folder,[string]$relative) {
    $result = [Text.StringBuilder]::new()
    foreach ($file in Get-ChildItem -LiteralPath $folder -File | Sort-Object Name) {
        $rel = if($relative) { "$relative\$($file.Name)" } else { $file.Name }
        $id = Id $rel
        $components.Add($id)
        [void]$result.Append('<Component Id="'+$id+'" Guid="*"><File Id="F'+$id+'" Source="'+(Xml $file.FullName)+'" KeyPath="yes" />')
        if ($rel -eq 'service\MkRgb.Service.exe') {
            [void]$result.Append('<ServiceInstall Id="InstallService" Name="MkRgbHardware" DisplayName="MK RGB Hardware" Description="Local hardware monitoring and cooling control" Type="ownProcess" Start="auto" ErrorControl="normal" Account="LocalSystem" Arguments="--owner-sid [UserSID]"><util:ServiceConfig FirstFailureActionType="restart" SecondFailureActionType="restart" ThirdFailureActionType="restart" RestartServiceDelayInSeconds="5" ResetPeriodInDays="1" /></ServiceInstall><ServiceControl Id="ControlService" Name="MkRgbHardware" Start="install" Stop="both" Remove="uninstall" Wait="yes" />')
        }
        if ($rel -eq 'MK RGB.exe') { [void]$result.Append('<Shortcut Id="StartMenuShortcut" Directory="ProgramMenuFolder" Name="MK RGB" Advertise="yes" WorkingDirectory="INSTALLFOLDER" />') }
        [void]$result.Append('</Component>')
    }
    foreach ($directory in Get-ChildItem -LiteralPath $folder -Directory | Sort-Object Name) {
        $rel = if($relative) { "$relative\$($directory.Name)" } else { $directory.Name }
        [void]$result.Append('<Directory Id="D'+(Id $rel)+'" Name="'+(Xml $directory.Name)+'">')
        [void]$result.Append((DirectoryXml $directory.FullName $rel))
        [void]$result.Append('</Directory>')
    }
    $result.ToString()
}
$tree = DirectoryXml $stage ''
$refs = ($components | ForEach-Object { '<ComponentRef Id="'+$_+'" />' }) -join ''
$source = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
  <Package Name="MK RGB" Manufacturer="MKonline08" Version="$Version" UpgradeCode="8CCF329A-9A64-443D-BCC9-A12A8E9B9D21" Scope="perMachine" Compressed="yes">
    <MajorUpgrade DowngradeErrorMessage="A newer MK RGB version is already installed." Schedule="afterInstallInitialize" AllowSameVersionUpgrades="yes" />
    <MediaTemplate EmbedCab="yes" CompressionLevel="high" />
    <StandardDirectory Id="ProgramFiles64Folder"><Directory Id="INSTALLFOLDER" Name="MK RGB">$tree</Directory></StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder" />
    <Feature Id="Complete" Title="MK RGB" Level="1">$refs</Feature>
  </Package>
</Wix>
"@
$wxs = Join-Path $repo 'artifacts\MkRgb.wxs'
[IO.File]::WriteAllText($wxs,$source)
Checked $Wix @('build','-ext','WixToolset.Util.wixext','-arch','x64',$wxs,'-out',"$out\MK-RGB-$Version-win-x64.msi",'-pdbtype','none')
Compress-Archive -Path "$stage\*" -DestinationPath "$out\MK-RGB-$Version-win-x64-portable.zip" -Force
foreach ($source in (Get-Content (Join-Path $PSScriptRoot 'sources.json') -Raw | ConvertFrom-Json)) {
    $archive = Join-Path $repo ('vendor\' + $source.File)
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $source.Url -OutFile $archive }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLower() -ne $source.Sha256) { throw "Source checksum mismatch: $($source.File)" }
    Copy-Item -LiteralPath $archive -Destination $out -Force
}
Get-ChildItem $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" } | Set-Content "$out\SHA256SUMS.txt" -Encoding utf8NoBOM
Write-Output "Release artifacts: $out"
