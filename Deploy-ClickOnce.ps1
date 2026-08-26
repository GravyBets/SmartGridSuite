param(
    [string]$PublishRoot = "C:\SmartGridSuitePublish\Test",
    [string]$Server = "admin@10.130.206.135",
    [string]$RemoteInstallRoot = "/var/www/smartgridsuite/install"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "SmartGridSuite ClickOnce Deployment"
Write-Host "==================================="
Write-Host ""

# ------------------------------------------------------------
# Resolve repository / project paths.
# ------------------------------------------------------------

$RepoRoot =
    Split-Path -Parent $MyInvocation.MyCommand.Path

$ClientProject =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.Client\SmartGridSuite.Client.csproj"

$PublishProfile =
    "ClickOnceProfile"

if (-not (Test-Path $ClientProject))
{
    throw "Client project was not found: $ClientProject"
}

# ------------------------------------------------------------
# Read the desired application version directly from the client
# project.
#
# This keeps:
#
#   Assembly version
#   File version
#   ClickOnce version
#
# synchronized automatically.
# ------------------------------------------------------------

[xml]$ClientProjectXml =
    Get-Content `
        $ClientProject `
        -Raw

$ExpectedVersion =
    $ClientProjectXml.Project.PropertyGroup.Version |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($ExpectedVersion))
{
    throw "Could not determine Version from SmartGridSuite.Client.csproj."
}

$ExpectedVersion =
    $ExpectedVersion.Trim()

Write-Host "Client version: $ExpectedVersion"

# ------------------------------------------------------------
# Publish ClickOnce automatically using the SAME profile Visual
# Studio uses.
#
# ApplicationVersion is explicitly overridden so the profile's
# wildcard/revision counter cannot create a different version.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Locating Visual Studio MSBuild..."

$VsWhere =
    Join-Path `
        ${env:ProgramFiles(x86)} `
        "Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $VsWhere))
{
    throw "Visual Studio vswhere.exe was not found: $VsWhere"
}

$MSBuildPath =
    & $VsWhere `
        -latest `
        -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($MSBuildPath))
{
    throw "Could not locate Visual Studio MSBuild.exe."
}

Write-Host "Using MSBuild:"
Write-Host $MSBuildPath

Write-Host ""
Write-Host "Preparing ClickOnce publish destination..."

$PublishOutput =
    $PublishRoot.TrimEnd("\") + "\"

$ExpectedVersionFolderName =
    "SmartGridSuite.Client_" +
    ($ExpectedVersion -replace "\.", "_")

$ExpectedVersionFolder =
    Join-Path `
        (Join-Path $PublishRoot "Application Files") `
        $ExpectedVersionFolderName

# Remove only files/folder that this release is about to regenerate.
# Historical ClickOnce versions remain untouched.
Remove-Item `
    (Join-Path $PublishRoot "SmartGridSuite.Client.application") `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item `
    (Join-Path $PublishRoot "setup.exe") `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item `
    (Join-Path $PublishRoot "Publish.html") `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item `
    $ExpectedVersionFolder `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

Write-Host "Publish destination:"
Write-Host $PublishOutput

Write-Host ""
Write-Host "Publishing ClickOnce..."

& $MSBuildPath `
    $ClientProject `
    /restore `
    /t:Publish `
    /p:Configuration=Release `
    /p:Platform="Any CPU" `
    /p:RuntimeIdentifier=win-x64 `
    /p:SelfContained=true `
    "/p:PublishProfile=$PublishProfile" `
    "/p:ApplicationVersion=$ExpectedVersion" `
    /p:IsRevisionIncremented=false `
    "/p:PublishDir=$PublishOutput" `
    "/p:PublishUrl=$PublishOutput"

if ($LASTEXITCODE -ne 0)
{
    throw "ClickOnce publish failed with exit code $LASTEXITCODE."
}

Write-Host "ClickOnce publish completed."

# ------------------------------------------------------------
# Locate the ClickOnce deployment manifest generated above.
# ------------------------------------------------------------

$ManifestPath =
    Join-Path $PublishRoot "SmartGridSuite.Client.application"

$SetupPath =
    Join-Path $PublishRoot "setup.exe"

$PublishHtmlPath =
    Join-Path $PublishRoot "Publish.html"

foreach ($RequiredFile in @(
    $ManifestPath,
    $SetupPath
))
{
    if (-not (Test-Path $RequiredFile))
    {
        throw "Required ClickOnce file was not found: $RequiredFile"
    }
}

if (-not (Test-Path $PublishHtmlPath))
{
    Write-Host "Publish.html was not generated; skipping it."
}

# ------------------------------------------------------------
# Read the version directly from the ACTUAL ClickOnce manifest.
#
# This prevents the deployment script from guessing which
# version Visual Studio just published.
# ------------------------------------------------------------

[xml]$ManifestXml =
    Get-Content $ManifestPath -Raw

$AssemblyIdentity =
    $ManifestXml.SelectSingleNode(
        "/*[local-name()='assembly']/*[local-name()='assemblyIdentity']"
    )

if ($null -eq $AssemblyIdentity)
{
    throw "Could not locate assemblyIdentity in the ClickOnce manifest."
}

$Version =
    $AssemblyIdentity.GetAttribute("version")

if ([string]::IsNullOrWhiteSpace($Version))
{
    throw "Could not determine ClickOnce version."
}

if ($Version -ne $ExpectedVersion)
{
    throw @"
ClickOnce version mismatch.

Client project: $ExpectedVersion
ClickOnce output: $Version

Deployment stopped before anything was uploaded.
"@
}

Write-Host "Detected ClickOnce version: $Version"

# ------------------------------------------------------------
# Determine the version-specific Application Files folder.
# Example:
# 3.1.0.6 -> SmartGridSuite.Client_3_1_0_6
# ------------------------------------------------------------

$VersionFolderName =
    "SmartGridSuite.Client_" +
    ($Version -replace "\.", "_")

$ApplicationFilesRoot =
    Join-Path $PublishRoot "Application Files"

$VersionFolder =
    Join-Path $ApplicationFilesRoot $VersionFolderName

if (-not (Test-Path $VersionFolder))
{
    throw @"
The expected ClickOnce application folder does not exist:

$VersionFolder

Make sure Visual Studio Publish completed successfully.
"@
}

# ------------------------------------------------------------
# Build a SMALL staging folder containing:
#
#   SmartGridSuite.Client.application
#   setup.exe
#   Application Files\<CURRENT VERSION>
#
# Publish.html is included only when MSBuild generates it.
# ------------------------------------------------------------

$PublishParent =
    Split-Path $PublishRoot -Parent

$StageRoot =
    Join-Path $PublishParent "Deploy-$Version"

$StageApplicationFiles =
    Join-Path $StageRoot "Application Files"

$ArchiveName =
    "SmartGridSuite-$Version-clickonce.tar.gz"

$ArchivePath =
    Join-Path $PublishParent $ArchiveName

Write-Host "Preparing deployment package..."

Remove-Item $StageRoot `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item $ArchivePath `
    -Force `
    -ErrorAction SilentlyContinue

New-Item `
    -ItemType Directory `
    -Force `
    -Path $StageApplicationFiles |
    Out-Null

Copy-Item `
    $ManifestPath `
    $StageRoot

Copy-Item `
    $SetupPath `
    $StageRoot

if (Test-Path $PublishHtmlPath)
{
    Copy-Item `
        $PublishHtmlPath `
        $StageRoot
}

Copy-Item `
    $VersionFolder `
    $StageApplicationFiles `
    -Recurse

# ------------------------------------------------------------
# Package only the current release.
# ------------------------------------------------------------

Write-Host "Creating archive..."

& tar `
    -czf $ArchivePath `
    -C $StageRoot `
    .

if ($LASTEXITCODE -ne 0)
{
    throw "tar failed with exit code $LASTEXITCODE."
}

$ArchiveInfo =
    Get-Item $ArchivePath

Write-Host (
    "Archive created: {0:N1} MB" -f
    ($ArchiveInfo.Length / 1MB)
)

# ------------------------------------------------------------
# Upload to the SmartGridSuite VM.
# ------------------------------------------------------------

$RemoteArchive =
    "/tmp/$ArchiveName"

Write-Host ""
Write-Host "Uploading $Version to $Server..."

& scp `
    $ArchivePath `
    "${Server}:$RemoteArchive"

if ($LASTEXITCODE -ne 0)
{
    throw "SCP failed with exit code $LASTEXITCODE."
}

# ------------------------------------------------------------
# Merge into the existing ClickOnce install directory.
#
# IMPORTANT:
# We do NOT replace the whole directory.
#
# This preserves:
#   - index.html
#   - historical ClickOnce versions
#   - the dynamic install-page version script
#
# Publish.html and the top-level ClickOnce manifest are replaced
# by the versions Visual Studio just generated.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Deploying on VM..."

$RemoteCommand =
    "sudo tar -xzf '$RemoteArchive' -C '$RemoteInstallRoot' && " +
    "sudo chmod -R a+rX '$RemoteInstallRoot' && " +
    "test -d '$RemoteInstallRoot/Application Files/$VersionFolderName' && " +
    "grep -Fq '$Version' '$RemoteInstallRoot/SmartGridSuite.Client.application' && " +
    "rm -f '$RemoteArchive' && " +
    "echo 'SmartGridSuite ClickOnce $Version deployed successfully.'"

& ssh -tt $Server $RemoteCommand

$RemoteExitCode = $LASTEXITCODE

if ($RemoteExitCode -ne 0)
{
    throw "Remote deployment failed with exit code $RemoteExitCode."
}

if ($LASTEXITCODE -ne 0)
{
    throw "Remote deployment failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "==================================="
Write-Host "ClickOnce $Version is LIVE."
Write-Host "==================================="
Write-Host ""
Write-Host "Install page:"
Write-Host "http://10.130.206.135/install/"
Write-Host ""