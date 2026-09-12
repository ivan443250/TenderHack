[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path -Path $PSScriptRoot -ChildPath "..\models"
}

$requiredFreeBytes = 20GB
$fullOutputPath = [IO.Path]::GetFullPath($OutputDirectory)
$driveName = ([IO.Path]::GetPathRoot($fullOutputPath)).Substring(0, 1)
$drive = Get-PSDrive -Name $driveName
if ($drive.Free -lt $requiredFreeBytes) {
    throw "At least 20 GB free space is required on $($drive.Name):; available $([math]::Round($drive.Free / 1GB, 2)) GB"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$models = @(
    [pscustomobject]@{
        Name = "giga"
        FileName = "giga-embeddings-0826-3b-q8_0.gguf"
        Repository = "ai-babai/giga-embeddings-0826-3b-gguf"
        Revision = "04c5a2d751ce20908200de2324444a14a81b1d80"
        ExpectedSha256 = "429f2d04a968ffe73137fe65c2e458a08236056168b905d208b4d81ecab08c22"
        Url = "https://huggingface.co/ai-babai/giga-embeddings-0826-3b-gguf/resolve/04c5a2d751ce20908200de2324444a14a81b1d80/giga-embeddings-0826-3b-q8_0.gguf?download=true"
    }
    [pscustomobject]@{
        Name = "qwen"
        FileName = "Qwen3.8-4B-Q6_K.gguf"
        Repository = "empero-ai/Qwen3.8-4B-Distill-GGUF"
        Revision = "391fc7d103e3942a408def3e4f51c2f85d464417"
        ExpectedSha256 = $null
        Url = "https://huggingface.co/empero-ai/Qwen3.8-4B-Distill-GGUF/resolve/391fc7d103e3942a408def3e4f51c2f85d464417/Qwen3.8-4B-Q6_K.gguf?download=true"
    }
)

function Download-Model([pscustomobject]$Model) {
    $destination = Join-Path $OutputDirectory $Model.FileName
    $partial = "$destination.partial"
    if (Test-Path -LiteralPath $destination) {
        $length = (Get-Item -LiteralPath $destination).Length
        if ($length -gt 0) {
            Write-Host "Reusing existing $($Model.FileName) ($length bytes)"
            return $destination
        }
        Remove-Item -LiteralPath $destination -Force
    }

    Write-Host "Downloading $($Model.Repository)/$($Model.FileName) at immutable revision $($Model.Revision)"
    & curl.exe --fail --location --retry 5 --retry-delay 5 --retry-all-errors --continue-at - --output $partial $Model.Url
    if ($LASTEXITCODE -ne 0) {
        throw "Download failed for $($Model.FileName); partial file retained at $partial"
    }
    if (-not (Test-Path -LiteralPath $partial) -or (Get-Item -LiteralPath $partial).Length -le 0) {
        throw "Downloaded file is empty: $partial"
    }
    Move-Item -LiteralPath $partial -Destination $destination -Force
    return $destination
}

$paths = @{}
foreach ($model in $models) {
    $paths[$model.Name] = Download-Model $model
}

$gigaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $paths.giga).Hash.ToLowerInvariant()
if ($gigaHash -ne $models[0].ExpectedSha256) {
    throw "Giga SHA256 mismatch: expected $($models[0].ExpectedSha256), measured $gigaHash"
}
$qwenHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $paths.qwen).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    schema = 1
    models = [ordered]@{
        giga = [ordered]@{
            file = $models[0].FileName
            repository = $models[0].Repository
            revision = $models[0].Revision
            sha256 = $gigaHash
            expected_sha256 = $models[0].ExpectedSha256
            size_bytes = (Get-Item -LiteralPath $paths.giga).Length
        }
        qwen = [ordered]@{
            file = $models[1].FileName
            repository = $models[1].Repository
            revision = $models[1].Revision
            sha256 = $qwenHash
            expected_sha256 = $null
            size_bytes = (Get-Item -LiteralPath $paths.qwen).Length
        }
        querit = [ordered]@{
            status = "UNVERIFIED"
            file = $null
        }
    }
}
$manifestPath = Join-Path (Split-Path -Parent $OutputDirectory) "model-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Giga SHA256 verified: $gigaHash"
Write-Host "Qwen SHA256 measured (no frozen expected hash): $qwenHash"
Write-Host "Manifest written: $manifestPath"
foreach ($model in $models) {
    $path = $paths[$model.Name]
    Write-Host "$($model.FileName): $((Get-Item -LiteralPath $path).Length) bytes"
}
