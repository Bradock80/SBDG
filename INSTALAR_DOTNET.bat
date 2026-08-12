@echo off
chcp 65001 >nul
title SGDB Nativo — Instalar .NET 8
cd /d "%~dp0"

echo.
echo ============================================================
echo   Instalar .NET 8 SDK (necessario para compilar o SGDB)
echo ============================================================
echo.
echo O SGDB nativo usa C# + WPF. Precisa do .NET 8 SDK uma vez.
echo.

where dotnet >nul 2>&1
if not errorlevel 1 (
    echo .NET ja instalado:
    dotnet --version
    echo.
    pause
    exit /b 0
)

echo Baixando e instalando via winget...
echo (pode demorar alguns minutos)
echo.

winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements

echo.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo [AVISO] Feche e abra o terminal, ou reinicie o PC.
    echo Depois rode COMPILAR_E_ABRIR.bat
    echo.
    echo Ou baixe manualmente:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

dotnet --version
echo.
echo Pronto! Agora rode COMPILAR_E_ABRIR.bat
pause
