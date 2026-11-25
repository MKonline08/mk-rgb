param([Parameter(Mandatory)][string]$Msi, [Parameter(Mandatory)][string]$PawnIO, [string]$Wix = 'wix', [string]$Version = '0.1.3', [string]$PawnIOVersion = '2.2.0.0')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$msiFile = (Resolve-Path -LiteralPath $Msi).Path
$pawnIoFile = (Resolve-Path -LiteralPath $PawnIO).Path
$out = Join-Path $repo "artifacts/release/MK-RGB-$Version-Setup.exe"
foreach ($extension in 'WixToolset.BootstrapperApplications.wixext/6.0.2', 'WixToolset.Util.wixext/6.0.2') {
    & $Wix extension add --global $extension
    if ($LASTEXITCODE -ne 0) { throw "Could not load the WiX extension $extension." }
}
$license = [IO.File]::ReadAllText((Join-Path $repo 'LICENSE')).Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace("`r",'').Replace("`n",'\par ')
$rtf = Join-Path $repo 'artifacts/license.rtf'
[IO.File]::WriteAllText($rtf, '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 '+$license+'}')
function Xml([string]$s) { [Security.SecurityElement]::Escape($s) }
# The chain installs everything MK RGB needs, in order:
#  1. PawnIO, the signed driver OpenRGB and LibreHardwareMonitor use for RAM/motherboard RGB and CPU sensors.
#     Skipped when the same or a newer version is already installed; kept on uninstall because other apps share it.
#     A PawnIO failure does not block MK RGB (USB lighting still works) and the Devices page explains what is missing.
#  2. The MK RGB MSI: app, hardware service, and the bundled OpenRGB backend (self-contained, no .NET install needed).
$source = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:bal="http://wixtoolset.org/schemas/v4/wxs/bal" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
  <Bundle Name="MK RGB" Version="$Version" Manufacturer="MKonline08" UpgradeCode="6BD2DE69-5A93-46B7-A207-D4728E5278F0" IconSourceFile="$(Xml (Join-Path $repo 'src/MkRgb.Desktop/Assets/mk-rgb.ico'))">
    <BootstrapperApplication>
      <bal:WixStandardBootstrapperApplication LicenseFile="$(Xml $rtf)" Theme="rtfLargeLicense" ShowVersion="yes" SuppressOptionsUI="yes" />
    </BootstrapperApplication>
    <util:RegistrySearch Id="FindPawnIO" Root="HKLM" Key="SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" Value="DisplayVersion" Variable="InstalledPawnIOVersion" Result="value" Bitness="always64" />
    <Chain>
      <ExePackage Id="PawnIO" DisplayName="PawnIO hardware access driver" SourceFile="$(Xml $pawnIoFile)" Compressed="yes" PerMachine="yes" Permanent="yes" Vital="no"
                  DetectCondition="InstalledPawnIOVersion AND InstalledPawnIOVersion &gt;= v$PawnIOVersion" InstallArguments="-install -silent">
        <ExitCode Value="0" Behavior="success" />
        <ExitCode Value="183" Behavior="success" />
        <ExitCode Value="3010" Behavior="scheduleReboot" />
      </ExePackage>
      <MsiPackage Id="MkRgbApplication" SourceFile="$(Xml $msiFile)" Compressed="yes" Visible="no" />
    </Chain>
  </Bundle>
</Wix>
"@
$wxs = Join-Path $repo 'artifacts/Bundle.wxs'
[IO.File]::WriteAllText($wxs, $source)
& $Wix build -arch x64 -ext WixToolset.BootstrapperApplications.wixext -ext WixToolset.Util.wixext $wxs -out $out -pdbtype none
if ($LASTEXITCODE -ne 0) { throw 'EXE installer packaging failed.' }
Write-Output "EXE installer: $out"
