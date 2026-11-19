param([string]$ReportPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Library\Bee\artifacts\Android\ManagedStripped\UnityLinkerToEditorData.json'))
$ErrorActionPreference = 'Stop'
$pulseReport = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$pulseRetained = @($pulseReport.report.modules | ForEach-Object { $_.dependencies } | ForEach-Object { $_.name })
$pulseRequired = @('MeshFilter','MeshRenderer','BoxCollider','CapsuleCollider','SphereCollider')
$pulseMissing = @($pulseRequired | Where-Object { $_ -notin $pulseRetained })
if ($pulseMissing.Count -gt 0) { throw ('Android startup dependencies were stripped: ' + ($pulseMissing -join ', ')) }
Write-Output ('PASS: Android retains all runtime primitive components: ' + ($pulseRequired -join ', '))
