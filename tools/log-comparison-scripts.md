# Log Comparison Scripts

PowerShell scripts for verifying that refactored code produces identical output to legacy code — essential for safe performance refactoring.

## Use Case

When you refactor a service for performance (e.g., rewriting SQL queries or splitting monolithic operations), you need to verify the **output is identical**. These scripts automate that comparison.

## 1. Compare Filtered Logs

Compares structured log output between two process runs.

```powershell
# compare_filtered_logs.ps1
param(
    [Parameter(Mandatory)] [int]$BasePid,
    [Parameter(Mandatory)] [int]$TargetPid,
    [string]$LogDir = ".\Logs",
    [string]$DiffDir = ".\Logs\Diff"
)

# Find log files for each PID
$baseFiles = Get-ChildItem $LogDir -Filter "*$BasePid*.log"
$targetFiles = Get-ChildItem $LogDir -Filter "*$TargetPid*.log"

if (-not (Test-Path $DiffDir)) { New-Item -ItemType Directory -Path $DiffDir | Out-Null }

$matchCount = 0; $diffCount = 0

foreach ($baseFile in $baseFiles) {
    # Extract JSON payloads from structured log lines
    $baseData = Get-Content $baseFile.FullName |
        Where-Object { $_ -match 'DetailedData:|Result_End' } |
        ForEach-Object {
            if ($_ -match '\{.*\}') {
                $Matches[0] | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress
            }
        }

    $targetFile = $targetFiles | Where-Object { $_.Name -replace $BasePid, '' -eq ($baseFile.Name -replace $BasePid, '') }
    if (-not $targetFile) { Write-Warning "No matching target file for $($baseFile.Name)"; continue }

    $targetData = Get-Content $targetFile.FullName |
        Where-Object { $_ -match 'DetailedData:|Result_End' } |
        ForEach-Object {
            if ($_ -match '\{.*\}') {
                $Matches[0] | ConvertFrom-Json | ConvertTo-Json -Depth 20 -Compress
            }
        }

    # Compare normalized JSON
    $diff = Compare-Object $baseData $targetData
    if ($diff) {
        $diffCount++
        $diff | Out-File (Join-Path $DiffDir "$($baseFile.BaseName)_diff.txt")
        Write-Host "MISMATCH: $($baseFile.Name)" -ForegroundColor Red
    } else {
        $matchCount++
        Write-Host "MATCH: $($baseFile.Name)" -ForegroundColor Green
    }
}

Write-Host "`nResults: $matchCount matches, $diffCount mismatches"
```

**Usage:**
```powershell
.\compare_filtered_logs.ps1 -BasePid 12345 -TargetPid 67890
```

## 2. Character-Level Diff

For pinpointing exact differences in large JSON blobs that look identical at a glance.

```powershell
# analyze_diff.ps1
param(
    [Parameter(Mandatory)] [string]$File1,
    [Parameter(Mandatory)] [string]$File2
)

$content1 = Get-Content $File1 -Raw
$content2 = Get-Content $File2 -Raw

$minLen = [Math]::Min($content1.Length, $content2.Length)

for ($i = 0; $i -lt $minLen; $i++) {
    if ($content1[$i] -ne $content2[$i]) {
        $context = 50
        $start = [Math]::Max(0, $i - $context)
        Write-Host "First difference at position $i:"
        Write-Host "File1: ...$($content1.Substring($start, [Math]::Min($context * 2, $content1.Length - $start)))..."
        Write-Host "File2: ...$($content2.Substring($start, [Math]::Min($context * 2, $content2.Length - $start)))..."
        break
    }
}

if ($content1.Length -ne $content2.Length) {
    Write-Host "Length difference: File1=$($content1.Length), File2=$($content2.Length)"
}
```

## 3. Split Log by Context

Splits a monolithic log file into smaller files by request/context ID.

```powershell
# split_log_by_context.ps1
param(
    [Parameter(Mandatory)] [string]$LogFile,
    [Parameter(Mandatory)] [string]$OutputDir,
    [string]$ContextPattern = 'ContextId:\s*(\S+)'
)

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

$writers = @{}
Get-Content $LogFile | ForEach-Object {
    if ($_ -match $ContextPattern) {
        $contextId = $Matches[1]
        if (-not $writers[$contextId]) {
            $path = Join-Path $OutputDir "$contextId.log"
            $writers[$contextId] = [System.IO.StreamWriter]::new($path)
        }
        $writers[$contextId].WriteLine($_)
    }
}
$writers.Values | ForEach-Object { $_.Close() }
Write-Host "Split into $($writers.Count) context files in $OutputDir"
```

## Verification Workflow

1. **Run legacy code** → capture PID and logs
2. **Run refactored code** → capture PID and logs
3. **Compare:** `.\compare_filtered_logs.ps1 -BasePid <legacy> -TargetPid <refactored>`
4. **Analyze mismatches:** Use `analyze_diff.ps1` on diff files
5. **Investigate per-request:** Use `split_log_by_context.ps1` to isolate specific requests

This workflow catches subtle regressions like:
- Ordering changes in collections
- Floating point precision differences
- Missing or extra fields after refactoring
- Timestamp/GUID differences (expected — filter these out)
