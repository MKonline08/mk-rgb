param([Parameter(Mandatory)][string]$Msi, [string]$Wix = 'wix', [string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$msiFile = (Resolve-Path -LiteralPath $Msi).Path
$out = Join-Path $repo "artifacts/release/MK-RGB-$Version-Setup.exe"
& $Wix extension add --global WixToolset.BootstrapperApplications.wixext/6.0.2
if ($LASTEXITCODE -ne 0) { throw 'Could not load the WiX bootstrapper extension.' }
$license = [IO.File]::ReadAllText((Join-Path $repo 'LICENSE')).Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace("`r",'').Replace("`n",'\par ')
$rtf = Join-Path $repo 'artifacts/license.rtf'
[IO.File]::WriteAllText($rtf, '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 '+$license+'}')
function Xml([string]$s) { [Security.SecurityElement]::Escape($s) }
$source = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:bal="http://wixtoolset.org/schemas/v4/wxs/bal">
  <Bundle Name="MK RGB" Version="$Version" Manufacturer="MKonline08" UpgradeCode="6BD2DE69-5A93-46B7-A207-D4728E5278F0" IconSourceFile="$(Xml (Join-Path $repo 'src/MkRgb.Desktop/Assets/mk-rgb.ico'))">
    <BootstrapperApplication>
      <bal:WixStandardBootstrapperApplication LicenseFile="$(Xml $rtf)" Theme="rtfLargeLicense" ShowVersion="yes" SuppressOptionsUI="yes" />
    </BootstrapperApplication>
    <Chain><MsiPackage Id="MkRgbApplication" SourceFile="$(Xml $msiFile)" Compressed="yes" Visible="no" /></Chain>
  </Bundle>
</Wix>
"@
$wxs = Join-Path $repo 'artifacts/Bundle.wxs'
[IO.File]::WriteAllText($wxs, $source)
& $Wix build -arch x64 -ext WixToolset.BootstrapperApplications.wixext $wxs -out $out -pdbtype none
if ($LASTEXITCODE -ne 0) { throw 'EXE installer packaging failed.' }
Write-Output "EXE installer: $out"
