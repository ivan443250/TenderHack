$ErrorActionPreference = "Stop"

$required = @(
  "src/support-core/TenderHack.sln",
  "src/knowledge/pyproject.toml",
  "src/web/package.json",
  "compose.yaml",
  "docs/contracts/knowledge-v0.openapi.yaml"
)

foreach ($path in $required) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "Missing required scaffold artifact: $path"
  }
}

$trackedUppercaseDocs = git ls-files | Select-String -CaseSensitive '^Docs/'
if ($trackedUppercaseDocs) {
  throw "Uppercase Docs/ paths are not allowed: $trackedUppercaseDocs"
}

$forbidden = rg -n --glob '!docs/**' '(Kafka|Redis|Qdrant|Kubernetes|GraphRAG)' src compose.yaml infra 2>$null
if ($LASTEXITCODE -eq 0 -and $forbidden) {
  throw "Forbidden infrastructure term found in active scaffold:`n$forbidden"
}

Write-Output "TenderHack scaffold structure is valid."
