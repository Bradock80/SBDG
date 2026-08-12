@echo off
chcp 65001 >nul
title SGDB — Redefinir senha
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET nao encontrado. Rode INSTALAR_DOTNET.bat primeiro.
    pause
    exit /b 1
)

echo.
echo  Redefinir senha do SGDB
echo  -----------------------
echo  Padrao: usuario admin, nova senha admin
echo  Uso:    RESET_SENHA.bat [usuario] [nova_senha]
echo.

dotnet run --project "scripts\ResetSenha\ResetSenha.csproj" -c Release -- %*
if errorlevel 1 (
    echo.
    echo [ERRO] Nao foi possivel redefinir a senha.
    pause
    exit /b 1
)

echo.
pause
