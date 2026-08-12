# Encerra SGDB.exe e processos dotnet que estejam usando os binarios do projeto.
$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent

$dllRelease = Join-Path $root 'src\SGDB.App\bin\Release\net8.0-windows\SGDB.dll'
$dllDebug = Join-Path $root 'src\SGDB.App\bin\Debug\net8.0-windows\SGDB.dll'
$targets = @($dllRelease, $dllDebug) | Where-Object { Test-Path $_ }

Get-Process -Name 'SGDB' -ErrorAction SilentlyContinue | Stop-Process -Force

Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @('dotnet.exe', 'SGDB.exe') -and
        $_.CommandLine -match 'SGDB'
    } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

foreach ($dll in $targets) {
    $full = (Resolve-Path $dll).Path
    Get-Process -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $hit = $_.Modules | Where-Object { $_.FileName -eq $full }
            if ($hit) { Stop-Process -Id $_.Id -Force }
        }
        catch { }
    }
}

Start-Sleep -Seconds 2
