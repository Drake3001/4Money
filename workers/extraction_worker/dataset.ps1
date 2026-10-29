param(
    [switch]$CheckOnly,
    [string]$DriveUrl
)

$workerPython = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $workerPython -PathType Leaf)) {
    throw 'Brak .venv workera. Przygotuj środowisko według README.md.'
}
$datasetScript = Join-Path $PSScriptRoot 'dataset.py'
$datasetArguments = @()
if ($CheckOnly) { $datasetArguments += '--check-only' }
if ($DriveUrl) { $datasetArguments += @('--drive-url', $DriveUrl) }
& $workerPython $datasetScript @datasetArguments
exit $LASTEXITCODE
