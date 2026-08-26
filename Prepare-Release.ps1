param(
    [string]$Version,
    [string]$Title,
    [string[]]$Changes,
    [string]$MinimumSupportedVersion,
    [switch]$Preview,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "SmartGridSuite Release Preparation"
Write-Host "=================================="
Write-Host ""

# ------------------------------------------------------------
# Repository paths
# ------------------------------------------------------------

$RepoRoot =
    Split-Path -Parent $MyInvocation.MyCommand.Path

$ClientProject =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.Client\SmartGridSuite.Client.csproj"

$ApiSettings =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.Api\appsettings.json"

$ChangeLog =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.Client\Assets\Data\ChangeLog.json"

$PublishProfile =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.Client\Properties\PublishProfiles\ClickOnceProfile.pubxml"

$Solution =
    Join-Path `
        $RepoRoot `
        "SmartGridSuite.sln"

$RequiredFiles = @(
    $ClientProject,
    $ApiSettings,
    $ChangeLog,
    $PublishProfile,
    $Solution
)

foreach ($File in $RequiredFiles)
{
    if (-not (Test-Path $File))
    {
        throw "Required file was not found: $File"
    }
}

# ------------------------------------------------------------
# Helpers
# ------------------------------------------------------------

function Test-Utf8Bom
{
    param(
        [string]$Path
    )

    $Bytes =
        [System.IO.File]::ReadAllBytes($Path)

    return (
        $Bytes.Length -ge 3 -and
        $Bytes[0] -eq 0xEF -and
        $Bytes[1] -eq 0xBB -and
        $Bytes[2] -eq 0xBF
    )
}

function Write-Utf8Text
{
    param(
        [string]$Path,
        [string]$Text,
        [bool]$UseBom
    )

    $Encoding =
        New-Object `
            System.Text.UTF8Encoding($UseBom)

    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        $Encoding
    )
}

function ConvertTo-JsonString
{
    param(
        [string]$Value
    )

    return (
        $Value |
        ConvertTo-Json -Compress
    )
}

function Replace-XmlElementValue
{
    param(
        [string]$Text,
        [string]$ElementName,
        [string]$Value
    )

    $EscapedName =
        [regex]::Escape($ElementName)

    $Pattern =
        "(<$EscapedName>)[^<]*(</$EscapedName>)"

    $Regex =
        New-Object `
            System.Text.RegularExpressions.Regex($Pattern)

    $Matches =
        $Regex.Matches($Text)

    if ($Matches.Count -ne 1)
    {
        throw (
            "Expected exactly one <$ElementName> element, " +
            "but found $($Matches.Count)."
        )
    }

    $Replacement =
        '${1}' +
        $Value +
        '${2}'

    return $Regex.Replace(
        $Text,
        $Replacement,
        1
    )
}

function Replace-JsonStringProperty
{
    param(
        [string]$Text,
        [string]$PropertyName,
        [string]$Value
    )

    $EscapedName =
        [regex]::Escape($PropertyName)

    $Pattern =
        '("' +
        $EscapedName +
        '"\s*:\s*)"(?:\\.|[^"\\])*"'

    $Regex =
        New-Object `
            System.Text.RegularExpressions.Regex($Pattern)

    $Matches =
        $Regex.Matches($Text)

    if ($Matches.Count -ne 1)
    {
        throw (
            "Expected exactly one JSON property named " +
            "'$PropertyName', but found $($Matches.Count)."
        )
    }

    $Replacement =
        '${1}' +
        (ConvertTo-JsonString $Value)

    return $Regex.Replace(
        $Text,
        $Replacement,
        1
    )
}

# ------------------------------------------------------------
# Read current files
# ------------------------------------------------------------

$ClientOriginal =
    Get-Content $ClientProject -Raw

$ApiOriginal =
    Get-Content $ApiSettings -Raw

$ChangeLogOriginal =
    Get-Content $ChangeLog -Raw

$PublishProfileOriginal =
    Get-Content $PublishProfile -Raw

$ClientHadBom =
    Test-Utf8Bom $ClientProject

$ApiHadBom =
    Test-Utf8Bom $ApiSettings

$ChangeLogHadBom =
    Test-Utf8Bom $ChangeLog

$PublishProfileHadBom =
    Test-Utf8Bom $PublishProfile

# ------------------------------------------------------------
# Determine current versions
# ------------------------------------------------------------

[xml]$ClientXml =
    $ClientOriginal

$CurrentClientVersion =
    [string](
        $ClientXml.Project.PropertyGroup.Version |
        Select-Object -First 1
    )

if ([string]::IsNullOrWhiteSpace(
    $CurrentClientVersion))
{
    throw "Could not determine current client version."
}

$CurrentClientVersion =
    $CurrentClientVersion.Trim()

$ApiJson =
    $ApiOriginal |
    ConvertFrom-Json

$CurrentApiVersion =
    [string]$ApiJson.ClientVersion.LatestVersion

$ChangeLogJson =
    $ChangeLogOriginal |
    ConvertFrom-Json

$CurrentChangeLogVersion =
    [string]$ChangeLogJson[0].version

Write-Host "Current client version:    $CurrentClientVersion"
Write-Host "Current API version:       $CurrentApiVersion"
Write-Host "Current Change Log version: $CurrentChangeLogVersion"
Write-Host ""

# ------------------------------------------------------------
# Refuse to prepare a release if existing metadata is already
# inconsistent.
# ------------------------------------------------------------

if ($CurrentClientVersion -ne $CurrentApiVersion)
{
    throw @"
Existing version mismatch detected.

Client: $CurrentClientVersion
API:    $CurrentApiVersion

Fix the mismatch before preparing another release.
"@
}

if ($CurrentClientVersion -ne $CurrentChangeLogVersion)
{
    throw @"
Existing version mismatch detected.

Client:     $CurrentClientVersion
Change Log: $CurrentChangeLogVersion

Fix the mismatch before preparing another release.
"@
}

# ------------------------------------------------------------
# Propose the next revision automatically.
# ------------------------------------------------------------

$CurrentVersionObject =
    New-Object System.Version($CurrentClientVersion)

if ($CurrentVersionObject.Revision -lt 0)
{
    throw (
        "SmartGridSuite versions must contain four parts, " +
        "for example 3.1.0.6."
    )
}

$SuggestedVersion =
    "{0}.{1}.{2}.{3}" -f `
        $CurrentVersionObject.Major,
        $CurrentVersionObject.Minor,
        $CurrentVersionObject.Build,
        ($CurrentVersionObject.Revision + 1)

if ([string]::IsNullOrWhiteSpace($Version))
{
    $EnteredVersion =
        Read-Host "New version [$SuggestedVersion]"

    if ([string]::IsNullOrWhiteSpace(
        $EnteredVersion))
    {
        $Version =
            $SuggestedVersion
    }
    else
    {
        $Version =
            $EnteredVersion.Trim()
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$')
{
    throw (
        "Version must use four numeric parts, " +
        "for example 3.1.0.7."
    )
}

$NewVersionObject =
    New-Object System.Version($Version)

if (
    $NewVersionObject.CompareTo(
        $CurrentVersionObject
    ) -le 0
)
{
    throw (
        "New version $Version must be greater than " +
        "current version $CurrentClientVersion."
    )
}

# ------------------------------------------------------------
# Minimum supported version.
#
# By default, SmartGridSuite requires the newly released client.
# A different minimum may be supplied with:
#
# -MinimumSupportedVersion 3.1.0.6
# ------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace(
    $MinimumSupportedVersion))
{
    $MinimumSupportedVersion =
        $Version
}

if (
    $MinimumSupportedVersion -notmatch
    '^\d+\.\d+\.\d+\.\d+$'
)
{
    throw "MinimumSupportedVersion is not valid."
}

$MinimumVersionObject =
    New-Object System.Version(
        $MinimumSupportedVersion
    )

if (
    $MinimumVersionObject.CompareTo(
        $NewVersionObject
    ) -gt 0
)
{
    throw (
        "Minimum supported version cannot be newer " +
        "than the release version."
    )
}

# ------------------------------------------------------------
# Release title
# ------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Title))
{
    $Title =
        Read-Host "Release title"
}

if ([string]::IsNullOrWhiteSpace($Title))
{
    throw "A release title is required."
}

$Title =
    $Title.Trim()

# ------------------------------------------------------------
# Release notes
# ------------------------------------------------------------

if ($null -eq $Changes -or $Changes.Count -eq 0)
{
    Write-Host ""
    Write-Host "Enter release changes one at a time."
    Write-Host "Press Enter on a blank line when finished."
    Write-Host ""

    $ChangeList =
        New-Object `
            System.Collections.Generic.List[string]

    $ChangeNumber = 1

    while ($true)
    {
        $Change =
            Read-Host "Change $ChangeNumber"

        if ([string]::IsNullOrWhiteSpace($Change))
        {
            break
        }

        [void]$ChangeList.Add(
            $Change.Trim()
        )

        $ChangeNumber++
    }

    $Changes =
        @($ChangeList)
}

$Changes =
    @(
        $Changes |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        } |
        ForEach-Object {
            $_.Trim()
        }
    )

if ($Changes.Count -eq 0)
{
    throw "At least one release change is required."
}

# ------------------------------------------------------------
# Dates
# ------------------------------------------------------------

$Now =
    Get-Date

$ReleaseDate =
    $Now.ToString(
        "MMMM d, yyyy",
        [System.Globalization.CultureInfo]::InvariantCulture
    )

$PublishedAtUtc =
    $Now.ToUniversalTime().ToString(
        "yyyy-MM-ddTHH:mm:ssZ",
        [System.Globalization.CultureInfo]::InvariantCulture
    )

Write-Host ""
Write-Host "Release preparation:"
Write-Host "  Version:           $Version"
Write-Host "  Minimum supported: $MinimumSupportedVersion"
Write-Host "  Date:              $ReleaseDate"
Write-Host "  Title:             $Title"
Write-Host ""

foreach ($Change in $Changes)
{
    Write-Host "  - $Change"
}

Write-Host ""

# ------------------------------------------------------------
# Create updated client project.
# ------------------------------------------------------------

$ClientUpdated =
    $ClientOriginal

foreach ($ElementName in @(
    "Version",
    "AssemblyVersion",
    "FileVersion",
    "InformationalVersion"
))
{
    $ClientUpdated =
        Replace-XmlElementValue `
            -Text $ClientUpdated `
            -ElementName $ElementName `
            -Value $Version
}

# ------------------------------------------------------------
# Update ClickOnce profile too.
#
# Deploy-ClickOnce.ps1 already overrides this during automated
# publishing, but keeping the profile synchronized also makes a
# manual Visual Studio Publish produce the correct version.
# ------------------------------------------------------------

$Revision =
    $NewVersionObject.Revision.ToString()

$PublishProfileUpdated =
    Replace-XmlElementValue `
        -Text $PublishProfileOriginal `
        -ElementName "ApplicationVersion" `
        -Value $Version

$PublishProfileUpdated =
    Replace-XmlElementValue `
        -Text $PublishProfileUpdated `
        -ElementName "ApplicationRevision" `
        -Value $Revision

$PublishProfileUpdated =
    Replace-XmlElementValue `
        -Text $PublishProfileUpdated `
        -ElementName "IsRevisionIncremented" `
        -Value "False"

# ------------------------------------------------------------
# Update API ClientVersion metadata.
# ------------------------------------------------------------

$ApiUpdated =
    Replace-JsonStringProperty `
        -Text $ApiOriginal `
        -PropertyName "LatestVersion" `
        -Value $Version

$ApiUpdated =
    Replace-JsonStringProperty `
        -Text $ApiUpdated `
        -PropertyName "MinimumSupportedVersion" `
        -Value $MinimumSupportedVersion

$ApiUpdated =
    Replace-JsonStringProperty `
        -Text $ApiUpdated `
        -PropertyName "PublishedAtUtc" `
        -Value $PublishedAtUtc

$NewLine =
    if ($ApiOriginal.Contains("`r`n"))
    {
        "`r`n"
    }
    else
    {
        "`n"
    }

$ReleaseNoteLines =
    New-Object `
        System.Collections.Generic.List[string]

for (
    $Index = 0;
    $Index -lt $Changes.Count;
    $Index++
)
{
    $Suffix =
        if ($Index -lt ($Changes.Count - 1))
        {
            ","
        }
        else
        {
            ""
        }

    $JsonChange =
        ConvertTo-JsonString $Changes[$Index]

    [void]$ReleaseNoteLines.Add(
        "      $JsonChange$Suffix"
    )
}

$ReleaseNotesBody =
    $ReleaseNoteLines -join $NewLine

$ReleaseNotesPattern =
    '("ReleaseNotes"\s*:\s*)\[(?s:.*?)\]'

$ReleaseNotesRegex =
    New-Object `
        System.Text.RegularExpressions.Regex(
            $ReleaseNotesPattern
        )

if (
    $ReleaseNotesRegex.Matches(
        $ApiUpdated
    ).Count -ne 1
)
{
    throw (
        "Could not uniquely locate the API " +
        "ReleaseNotes array."
    )
}

$ReleaseNotesReplacement =
    '${1}[' +
    $NewLine +
    $ReleaseNotesBody +
    $NewLine +
    '    ]'

$ApiUpdated =
    $ReleaseNotesRegex.Replace(
        $ApiUpdated,
        $ReleaseNotesReplacement,
        1
    )

# ------------------------------------------------------------
# Insert new Change Log entry at the top.
# ------------------------------------------------------------

if (
    @($ChangeLogJson |
        Where-Object {
            $_.version -eq $Version
        }).Count -gt 0
)
{
    throw (
        "Change Log already contains version $Version."
    )
}

$ChangeLogNewLine =
    if ($ChangeLogOriginal.Contains("`r`n"))
    {
        "`r`n"
    }
    else
    {
        "`n"
    }

$EntryLines =
    New-Object `
        System.Collections.Generic.List[string]

[void]$EntryLines.Add("  {")
[void]$EntryLines.Add(
    '    "version": ' +
    (ConvertTo-JsonString $Version) +
    ","
)
[void]$EntryLines.Add(
    '    "date": ' +
    (ConvertTo-JsonString $ReleaseDate) +
    ","
)
[void]$EntryLines.Add(
    '    "title": ' +
    (ConvertTo-JsonString $Title) +
    ","
)
[void]$EntryLines.Add(
    '    "changes": ['
)

for (
    $Index = 0;
    $Index -lt $Changes.Count;
    $Index++
)
{
    $Suffix =
        if ($Index -lt ($Changes.Count - 1))
        {
            ","
        }
        else
        {
            ""
        }

    [void]$EntryLines.Add(
        "      " +
        (ConvertTo-JsonString $Changes[$Index]) +
        $Suffix
    )
}

[void]$EntryLines.Add("    ]")
[void]$EntryLines.Add("  }")

$NewEntry =
    $EntryLines -join $ChangeLogNewLine

$OpeningBracket =
    [regex]::Match(
        $ChangeLogOriginal,
        '^\s*\['
    )

if (-not $OpeningBracket.Success)
{
    throw "Change Log does not begin with a JSON array."
}

$InsertPosition =
    $OpeningBracket.Index +
    $OpeningBracket.Length

$Before =
    $ChangeLogOriginal.Substring(
        0,
        $InsertPosition
    )

$After =
    $ChangeLogOriginal.Substring(
        $InsertPosition
    )

$After =
    [regex]::Replace(
        $After,
        '^\r?\n',
        '',
        1
    )

$ChangeLogUpdated =
    $Before +
    $ChangeLogNewLine +
    $NewEntry +
    "," +
    $ChangeLogNewLine +
    $ChangeLogNewLine +
    $After

# ------------------------------------------------------------
# Preview mode
# ------------------------------------------------------------

if ($Preview)
{
    Write-Host "PREVIEW ONLY - no files were changed."
    Write-Host ""
    Write-Host "Files that would be updated:"
    Write-Host "  SmartGridSuite.Client\SmartGridSuite.Client.csproj"
    Write-Host "  SmartGridSuite.Api\appsettings.json"
    Write-Host "  SmartGridSuite.Client\Assets\Data\ChangeLog.json"
    Write-Host "  SmartGridSuite.Client\Properties\PublishProfiles\ClickOnceProfile.pubxml"
    Write-Host ""

    exit 0
}

# ------------------------------------------------------------
# Write everything as one logical operation.
#
# If validation or build fails, restore the original version
# metadata automatically.
# ------------------------------------------------------------

try
{
    Write-Host "Updating release files..."

    Write-Utf8Text `
        -Path $ClientProject `
        -Text $ClientUpdated `
        -UseBom $ClientHadBom

    Write-Utf8Text `
        -Path $ApiSettings `
        -Text $ApiUpdated `
        -UseBom $ApiHadBom

    Write-Utf8Text `
        -Path $ChangeLog `
        -Text $ChangeLogUpdated `
        -UseBom $ChangeLogHadBom

    Write-Utf8Text `
        -Path $PublishProfile `
        -Text $PublishProfileUpdated `
        -UseBom $PublishProfileHadBom

    # --------------------------------------------------------
    # Parse files again to ensure the generated XML/JSON is
    # valid before allowing the release to continue.
    # --------------------------------------------------------

    [xml]$ValidatedClient =
        Get-Content $ClientProject -Raw

    [xml]$ValidatedProfile =
        Get-Content $PublishProfile -Raw

    $ValidatedApi =
        Get-Content $ApiSettings -Raw |
        ConvertFrom-Json

    $ValidatedChangeLog =
        Get-Content $ChangeLog -Raw |
        ConvertFrom-Json

    if (
        [string]$ValidatedClient.Project.PropertyGroup.Version -ne
        $Version
    )
    {
        throw "Client version validation failed."
    }

    if (
        [string]$ValidatedApi.ClientVersion.LatestVersion -ne
        $Version
    )
    {
        throw "API version validation failed."
    }

    if (
        [string]$ValidatedChangeLog[0].version -ne
        $Version
    )
    {
        throw "Change Log validation failed."
    }

    if (
        [string]$ValidatedProfile.Project.PropertyGroup.ApplicationVersion -ne
        $Version
    )
    {
        throw "ClickOnce profile validation failed."
    }

    Write-Host "Release metadata validation passed."

    # --------------------------------------------------------
    # Build the solution unless explicitly skipped.
    # --------------------------------------------------------

    if (-not $SkipBuild)
    {
        Write-Host ""
        Write-Host "Building SmartGridSuite..."
        Write-Host ""

        & dotnet build `
            $Solution `
            -c Release

        if ($LASTEXITCODE -ne 0)
        {
            throw (
                "Solution build failed with exit code " +
                "$LASTEXITCODE."
            )
        }

        Write-Host ""
        Write-Host "Build succeeded."
    }
}
catch
{
    Write-Host ""
    Write-Host "Release preparation failed."
    Write-Host "Restoring original release metadata..."

    Write-Utf8Text `
        -Path $ClientProject `
        -Text $ClientOriginal `
        -UseBom $ClientHadBom

    Write-Utf8Text `
        -Path $ApiSettings `
        -Text $ApiOriginal `
        -UseBom $ApiHadBom

    Write-Utf8Text `
        -Path $ChangeLog `
        -Text $ChangeLogOriginal `
        -UseBom $ChangeLogHadBom

    Write-Utf8Text `
        -Path $PublishProfile `
        -Text $PublishProfileOriginal `
        -UseBom $PublishProfileHadBom

    Write-Host "Original release metadata restored."
    Write-Host ""

    throw
}

# ------------------------------------------------------------
# Finished
# ------------------------------------------------------------

Write-Host ""
Write-Host "=================================="
Write-Host "Release $Version is prepared."
Write-Host "=================================="
Write-Host ""

Write-Host "Next:"
Write-Host ""
Write-Host "  git diff"
Write-Host "  git status"
Write-Host ""
Write-Host "Then commit/push and deploy with:"
Write-Host ""
Write-Host "  .\Deploy-Api.ps1"
Write-Host "  .\Deploy-ClickOnce.ps1"
Write-Host ""