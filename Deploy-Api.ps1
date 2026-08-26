param(
    [string]$Server = "admin@10.130.206.135",
    [string]$RemoteApiRoot = "/opt/smartgridsuite"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "SmartGridSuite API Deployment"
Write-Host "============================="
Write-Host ""

# ------------------------------------------------------------
# Resolve repository paths.
# ------------------------------------------------------------

$RepoRoot =
    Split-Path -Parent $MyInvocation.MyCommand.Path

$ApiProject =
    Join-Path $RepoRoot "SmartGridSuite.Api\SmartGridSuite.Api.csproj"

$ApiSettings =
    Join-Path $RepoRoot "SmartGridSuite.Api\appsettings.json"

if (-not (Test-Path $ApiProject))
{
    throw "API project was not found: $ApiProject"
}

if (-not (Test-Path $ApiSettings))
{
    throw "API appsettings.json was not found: $ApiSettings"
}

# ------------------------------------------------------------
# Read the release version from appsettings.json.
#
# This means we never manually type the version into this script.
# ------------------------------------------------------------

$Settings =
    Get-Content $ApiSettings -Raw |
    ConvertFrom-Json

$Version =
    $Settings.ClientVersion.LatestVersion

if ([string]::IsNullOrWhiteSpace($Version))
{
    throw "Could not determine ClientVersion.LatestVersion from appsettings.json."
}

Write-Host "Detected release version: $Version"

# ------------------------------------------------------------
# Local release paths.
# ------------------------------------------------------------

$ReleaseRoot =
    Join-Path $RepoRoot "artifacts\release-$Version"

$ApiPublishFolder =
    Join-Path $ReleaseRoot "api-linux-x64"

$ArchiveName =
    "SmartGridSuite.Api-$Version-linux-x64.tar.gz"

$ApiArchive =
    Join-Path $ReleaseRoot $ArchiveName

# ------------------------------------------------------------
# Publish a self-contained Linux x64 API.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Publishing API..."

Remove-Item $ApiPublishFolder `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item $ApiArchive `
    -Force `
    -ErrorAction SilentlyContinue

New-Item `
    -ItemType Directory `
    -Force `
    -Path $ApiPublishFolder |
    Out-Null

& dotnet publish `
    $ApiProject `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    "-p:Version=$Version" `
    "-p:AssemblyVersion=$Version" `
    "-p:FileVersion=$Version" `
    -o $ApiPublishFolder

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$PublishedExecutable =
    Join-Path $ApiPublishFolder "SmartGridSuite.Api"

if (-not (Test-Path $PublishedExecutable))
{
    throw "Published SmartGridSuite.Api executable was not found."
}

# ------------------------------------------------------------
# Package only the new API publish.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Creating API archive..."

& tar `
    -czf $ApiArchive `
    -C $ApiPublishFolder `
    .

if ($LASTEXITCODE -ne 0)
{
    throw "tar failed with exit code $LASTEXITCODE."
}

$ArchiveInfo =
    Get-Item $ApiArchive

Write-Host (
    "Archive created: {0:N1} MB" -f
    ($ArchiveInfo.Length / 1MB)
)

# ------------------------------------------------------------
# Upload API archive.
# ------------------------------------------------------------

$RemoteArchive =
    "/tmp/$ArchiveName"

Write-Host ""
Write-Host "Uploading $Version to $Server..."

& scp `
    $ApiArchive `
    "${Server}:$RemoteArchive"

if ($LASTEXITCODE -ne 0)
{
    throw "SCP failed with exit code $LASTEXITCODE."
}

# ------------------------------------------------------------
# Build a temporary Linux deployment script.
#
# We explicitly write it with LF line endings so Bash never sees
# Windows CRLF characters.
# ------------------------------------------------------------

$RemoteScriptName =
    "deploy-smartgridsuite-api-$Version.sh"

$LocalRemoteScript =
    Join-Path $env:TEMP $RemoteScriptName

$RemoteScriptPath =
    "/tmp/$RemoteScriptName"

$RemoteScript = @'
#!/bin/bash
set -euo pipefail

VERSION='__VERSION__'
ARCHIVE='__REMOTE_ARCHIVE__'

ROOT='__REMOTE_API_ROOT__'
LIVE="$ROOT/api"
NEXT="$ROOT/api-next"

SERVICE='smartgridsuite-api'

echo
echo "Preparing SmartGridSuite API $VERSION..."
echo

# ------------------------------------------------------------
# Determine currently deployed version for backup naming.
# ------------------------------------------------------------

CURRENT_VERSION=$(python3 -c "
import json
with open('$LIVE/appsettings.json', encoding='utf-8-sig') as f:
    data=json.load(f)
print(data.get('ClientVersion',{}).get('LatestVersion','previous'))
")

if [ -z "$CURRENT_VERSION" ]; then
    CURRENT_VERSION='previous'
fi

BACKUP="$ROOT/api-backup-$CURRENT_VERSION"

echo "Current API: $CURRENT_VERSION"
echo "New API:     $VERSION"
echo

# ------------------------------------------------------------
# Stage the new API while the current API remains online.
# ------------------------------------------------------------

sudo rm -rf "$NEXT"
sudo mkdir -p "$NEXT"

sudo tar -xzf "$ARCHIVE" \
    -C "$NEXT"

# ------------------------------------------------------------
# Preserve production configuration.
#
# Keep the current production appsettings.json and replace only
# its ClientVersion block with the newly published one.
# ------------------------------------------------------------

sudo python3 - "$LIVE/appsettings.json" "$NEXT/appsettings.json" <<'PY'
import json
import sys

live_path = sys.argv[1]
new_path = sys.argv[2]

with open(live_path, "r", encoding="utf-8-sig") as f:
    live = json.load(f)

with open(new_path, "r", encoding="utf-8-sig") as f:
    new = json.load(f)

live["ClientVersion"] = new["ClientVersion"]

with open(new_path, "w", encoding="utf-8") as f:
    json.dump(live, f, indent=2)
PY

if [ -f "$LIVE/appsettings.Development.json" ]; then
    sudo cp \
        "$LIVE/appsettings.Development.json" \
        "$NEXT/appsettings.Development.json"
fi

sudo chown -R sgsapi:sgsapi "$NEXT"
sudo chmod +x "$NEXT/SmartGridSuite.Api"

# ------------------------------------------------------------
# Verify staged files BEFORE downtime.
# ------------------------------------------------------------

test -x "$NEXT/SmartGridSuite.Api"

grep -Fq \
    "\"LatestVersion\": \"$VERSION\"" \
    "$NEXT/appsettings.json"

echo
echo "Staging complete."
echo "Performing live swap..."
echo

# ------------------------------------------------------------
# ACTUAL DOWNTIME STARTS HERE.
# ------------------------------------------------------------

sudo rm -rf "$BACKUP"

sudo service "$SERVICE" stop

sudo mv "$LIVE" "$BACKUP"
sudo mv "$NEXT" "$LIVE"

# Do not allow a failed start command to bypass rollback.
if sudo service "$SERVICE" start
then
    echo "New API process started. Verifying endpoint..."
else
    echo "New API failed to start."
fi

# ------------------------------------------------------------
# Verify new API.
# ------------------------------------------------------------

API_OK=0

for ATTEMPT in 1 2 3 4 5 6 7 8 9 10
do
    if RESPONSE=$(curl -fsS \
        http://127.0.0.1:7140/api/system/client-version \
        2>/dev/null)
    then
        if echo "$RESPONSE" | grep -Fq "$VERSION"
        then
            API_OK=1
            break
        fi
    fi

    sleep 1
done

# ------------------------------------------------------------
# Automatic rollback if verification fails.
# ------------------------------------------------------------

if [ "$API_OK" -ne 1 ]; then
    echo
    echo "ERROR: API $VERSION failed verification."
    echo "Rolling back to $CURRENT_VERSION..."

    sudo service "$SERVICE" stop || true

    sudo rm -rf "$ROOT/api-failed-$VERSION"
    sudo mv "$LIVE" "$ROOT/api-failed-$VERSION"

    sudo mv "$BACKUP" "$LIVE"

    sudo service "$SERVICE" start

    echo
    echo "Rollback complete."
    exit 1
fi

rm -f "$ARCHIVE"

echo
echo "SmartGridSuite API $VERSION deployed successfully."
echo "Previous API preserved at:"
echo "$BACKUP"
echo
'@

$RemoteScript =
    $RemoteScript.Replace(
        "__VERSION__",
        $Version
    )

$RemoteScript =
    $RemoteScript.Replace(
        "__REMOTE_ARCHIVE__",
        $RemoteArchive
    )

$RemoteScript =
    $RemoteScript.Replace(
        "__REMOTE_API_ROOT__",
        $RemoteApiRoot
    )

# Force Unix LF line endings.
$RemoteScript =
    $RemoteScript -replace "`r`n", "`n"

[System.IO.File]::WriteAllText(
    $LocalRemoteScript,
    $RemoteScript,
    [System.Text.UTF8Encoding]::new($false)
)

# ------------------------------------------------------------
# Upload deployment script.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Uploading deployment helper..."

& scp `
    $LocalRemoteScript `
    "${Server}:$RemoteScriptPath"

if ($LASTEXITCODE -ne 0)
{
    throw "Could not upload remote deployment script."
}

# ------------------------------------------------------------
# Execute with an interactive TTY so sudo can request a password.
# ------------------------------------------------------------

Write-Host ""
Write-Host "Deploying API..."
Write-Host ""
Write-Host "The existing API remains online until the final swap."
Write-Host ""

& ssh -tt `
    $Server `
    "bash '$RemoteScriptPath'; RESULT=`$?; rm -f '$RemoteScriptPath'; exit `$RESULT"

$RemoteExitCode =
    $LASTEXITCODE

Remove-Item $LocalRemoteScript `
    -Force `
    -ErrorAction SilentlyContinue

if ($RemoteExitCode -ne 0)
{
    throw "API deployment failed with exit code $RemoteExitCode."
}

Write-Host ""
Write-Host "==================================="
Write-Host "SmartGridSuite API $Version is LIVE."
Write-Host "==================================="
Write-Host ""