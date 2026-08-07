$ErrorActionPreference = 'Stop'
$dll = 'C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development\FlaxEngine.CSharp.dll'
$bytes = [System.IO.File]::ReadAllBytes($dll)
$asm = [System.Reflection.Assembly]::Load($bytes)
$types = $asm.GetExportedTypes()
$editorTypes = $types | Where-Object { $_.Namespace -like 'FlaxEditor*' }
Write-Host "=== FlaxEditor namespaces ==="
$editorTypes | ForEach-Object { $_.Namespace } | Sort-Object -Unique
Write-Host "=== Top-level FlaxEditor types (first 200) ==="
$editorTypes | Where-Object { $_.Namespace -eq 'FlaxEditor' } | Select-Object -First 200 | ForEach-Object { $_.FullName }
