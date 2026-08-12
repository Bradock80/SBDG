@echo off
chcp 65001 >nul
title SGDB Nativo — Compilar e abrir
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET nao encontrado. Rode INSTALAR_DOTNET.bat primeiro.
    pause
    exit /b 1
)

echo Fechando SGDB e processos dotnet que bloqueiam a DLL...
taskkill /IM SGDB.exe /F >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fechar-sgdb.ps1"

echo Compilando SGDB...
dotnet build "src\SGDB.App\SGDB.App.csproj" -c Release
if errorlevel 1 (
    echo.
    echo Tentando novamente apos liberar arquivos bloqueados...
    taskkill /IM SGDB.exe /F >nul 2>&1
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fechar-sgdb.ps1"
    dotnet build "src\SGDB.App\SGDB.App.csproj" -c Release
)
if errorlevel 1 (
    echo.
    echo [ERRO] Falha na compilacao.
    echo Feche o SGDB e qualquer "dotnet run" / depurador e tente de novo.
    pause
    exit /b 1
)

echo.
echo Abrindo SGDB...
start "" "src\SGDB.App\bin\Release\net8.0-windows\SGDB.exe"
echo.
echo Executavel gerado em:
echo   src\SGDB.App\bin\Release\net8.0-windows\SGDB.exe
echo.
pause
