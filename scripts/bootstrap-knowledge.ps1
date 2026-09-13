[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$organizerRoot = Join-Path $repoRoot "data\organizer"
$expectedSnapshot = "snap_0e979dfef376faff41f7c76416fda457"
$expectedPages = 830
$expectedFragments = 3899
$expectedCards = 20
$maxPlainGitBytes = 50MB

function Decode-Utf8 {
    param([Parameter(Mandatory = $true)][string]$Base64)
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Base64))
}

$expected = @(
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRjdC70LXQutGC0YDQvtC90L3QvtC80YMg0LDQutGC0LjRgNC+0LLQsNC90LjRji5wZGY="); Size = 4843247; Sha256 = "d14f883b14f8f117842541900300901404b37d43cdddf13c3e3266cfbd9155b3"; Pages = 93; Fragments = 567; Version = "v11"; Date = "2026-08-11" },
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRhNC+0YDQvNC40YDQvtCy0LDQvdC40Y4gWU1MLnBkZg=="); Size = 1197287; Sha256 = "753fc58d5c7ae3b15932ef658d88f4eb2886273af2e3067389c5aa0934bd9a9c"; Pages = 39; Fragments = 298; Version = $null; Date = $null },
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRgNCw0LHQvtGC0LUg0YEg0J/QvtGA0YLQsNC70L7QvCDQtNC70Y8g0L/QvtGB0YLQsNCy0YnQuNC60LAucGRm"); Size = 25943732; Sha256 = "3c52c3633b6bc84e99ca1a23336536e9408ba83b28f160ee5c02738a2f754c6b"; Pages = 370; Fragments = 1439; Version = "v87"; Date = "2026-08-11" },
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRgdC+0LfQtNCw0L3QuNGOINC+0YTQtdGA0YLRiyDQuCDQodCi0JUucGRm"); Size = 3891038; Sha256 = "ce50227cb1bb29b9145dd0fb52181c353c03bb11e00a0ab467cf544914b20159"; Pages = 65; Fragments = 360; Version = $null; Date = "2026-06-04" },
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRgNCw0LHQvtGC0LUg0YEg0LzQsNGI0LjQvdC+0YfQuNGC0LDQtdC80YvQvNC4INC00L7QstC10YDQtdC90L3QvtGB0YLRj9C80LgucGRm"); Size = 1122812; Sha256 = "755870a7454fd166146bfd64a7c50009fc834340798d28ff9cee3132265272b9"; Pages = 7; Fragments = 15; Version = $null; Date = $null },
    @{ Name = (Decode-Utf8 "0JjQvdGB0YLRgNGD0LrRhtC40Y8g0L/QviDRgNCw0LHQvtGC0LUg0YEg0J/QvtGA0YLQsNC70L7QvCDQtNC70Y8g0LfQsNC60LDQt9GH0LjQutCwLnBkZg=="); Size = 16045058; Sha256 = "7355b2c39b4aa4fbac758f64aeab0bdfd4a8219cc483dd5ca52e28eacb59fcde"; Pages = 256; Fragments = 1220; Version = "v65"; Date = "2026-08-11" }
)

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & docker compose --project-directory $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Invoke-ComposeOutput {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & docker compose --project-directory $repoRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $exitCode"
    }
    return $output
}

if (-not (Test-Path -LiteralPath $organizerRoot -PathType Container)) {
    throw "organizer source directory is missing: $organizerRoot"
}

$actualPdfs = @(Get-ChildItem -LiteralPath $organizerRoot -File -Filter "*.pdf")
if ($actualPdfs.Count -ne $expected.Count) {
    throw "expected exactly $($expected.Count) organizer PDFs, found $($actualPdfs.Count)"
}

$manifestEntries = @()
foreach ($entry in $expected) {
    $path = Join-Path $organizerRoot $entry.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "missing organizer PDF: $($entry.Name)"
    }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $entry.Size) {
        throw "size mismatch for $($entry.Name): expected $($entry.Size), got $($file.Length)"
    }
    if ($file.Length -gt $maxPlainGitBytes) {
        throw "$($entry.Name) exceeds 50 MB; add a reviewed Git LFS policy before bootstrap"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $entry.Sha256) {
        throw "SHA-256 mismatch for $($entry.Name)"
    }
    $manifestEntries += [ordered]@{
        path = "/bootstrap/organizer/$($entry.Name)"
        original_filename = $entry.Name
        source_reference = "organizer/$($entry.Name)"
        declared_version = $entry.Version
        declared_date = $entry.Date
        expected_pages = $entry.Pages
        expected_fragments = $entry.Fragments
        corpus = "NORMATIVE"
        review_status = "PENDING_REVIEW"
    }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker command was not found; install Docker Desktop and rerun"
}

$manifestPath = Join-Path ([IO.Path]::GetTempPath()) "tenderhack-bootstrap-$PID-manifest.json"
$runnerPath = Join-Path ([IO.Path]::GetTempPath()) "tenderhack-bootstrap-$PID-run.py"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$runner = @'
import asyncio
import json

from tenderhack_knowledge.ingestion.bootstrap import bootstrap_postgres
from tenderhack_knowledge.persistence.db import create_engine
from tenderhack_knowledge.persistence.repository import PostgresKnowledgeRepository


async def main() -> None:
    engine = create_engine()
    if engine is None:
        raise RuntimeError("DATABASE_URL is not configured")
    try:
        result = await bootstrap_postgres(
            "/bootstrap/manifest.json",
            PostgresKnowledgeRepository(engine),
        )
        print("BOOTSTRAP_RESULT=" + json.dumps(result.model_dump(mode="json"), ensure_ascii=False, separators=(",", ":")))
    finally:
        await engine.dispose()


asyncio.run(main())
'@

try {
    [IO.File]::WriteAllText($manifestPath, ($manifestEntries | ConvertTo-Json -Depth 5), $utf8NoBom)
    [IO.File]::WriteAllText($runnerPath, $runner, $utf8NoBom)

    Invoke-Compose -Arguments @("config", "--quiet")
    Invoke-Compose -Arguments @("up", "-d", "postgres", "knowledge")

    $postgresReady = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & docker compose --project-directory $repoRoot exec -T postgres pg_isready *> $null
        if ($LASTEXITCODE -eq 0) {
            $postgresReady = $true
            break
        }
        Start-Sleep -Seconds 2
    }
    if (-not $postgresReady) {
        throw "PostgreSQL did not become ready within 60 seconds"
    }

    Invoke-Compose -Arguments @("exec", "-T", "knowledge", "alembic", "upgrade", "head")

    $organizerMount = "$(Resolve-Path $organizerRoot):/bootstrap/organizer:ro"
    $conditionsMount = "$(Resolve-Path (Join-Path $repoRoot 'src\knowledge\conditions')):/app/conditions:ro"
    $manifestMount = "${manifestPath}:/bootstrap/manifest.json:ro"
    $runnerMount = "${runnerPath}:/bootstrap/run.py:ro"
    $runOutput = Invoke-ComposeOutput -Arguments @(
        "run", "--rm", "--no-deps", "-T",
        "-e", "KNOWLEDGE_SKIP_MIGRATIONS=true",
        "-v", $organizerMount,
        "-v", $conditionsMount,
        "-v", $manifestMount,
        "-v", $runnerMount,
        "knowledge", "python", "/bootstrap/run.py"
    )

    $resultLine = @($runOutput | Where-Object { $_ -is [string] -and $_.StartsWith("BOOTSTRAP_RESULT=") } | Select-Object -Last 1)
    if ($resultLine.Count -ne 1) {
        throw "bootstrap did not return an auditable result"
    }
    $result = $resultLine[0].Substring("BOOTSTRAP_RESULT=".Length) | ConvertFrom-Json
    if ($result.status -eq "FAILED") {
        throw "bootstrap returned FAILED"
    }
    if ($result.snapshot_id -ne $expectedSnapshot) {
        throw "snapshot mismatch: expected $expectedSnapshot, got $($result.snapshot_id)"
    }
    if ([int]$result.pages -ne $expectedPages -or [int]$result.fragments -ne $expectedFragments) {
        throw "corpus count mismatch: expected $expectedPages pages/$expectedFragments fragments, got $($result.pages)/$($result.fragments)"
    }
    if ([int]$result.condition_cards -ne $expectedCards -or [int]$result.structurally_verified_cards -ne $expectedCards) {
        throw "condition-card verification mismatch: expected $expectedCards verified cards"
    }
    if ($result.historical_corpus_isolated -ne $true) {
        throw "bootstrap did not confirm historical corpus isolation"
    }

    Write-Output "SNAPSHOT_VERIFIED=$($result.snapshot_id)"
    Write-Output "FRAGMENTS_VERIFIED=$($result.fragments)"
    Write-Output "CARDS_VERIFIED=$($result.structurally_verified_cards)"
    Write-Output "REPEATED_BOOTSTRAP_IDEMPOTENT=$($result.repeated_bootstrap.all_versions_idempotent)"
    Write-Output "COMPOSE_CONFIG=PASS"
}
finally {
    Remove-Item -LiteralPath $manifestPath, $runnerPath -Force -ErrorAction SilentlyContinue
}
