@echo off
chcp 65001 >nul
title SGDB — Gerar ZIP para GitHub Release
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET nao encontrado. Rode INSTALAR_DOTNET.bat primeiro.
  pause
  exit /b 1
)

echo Fechando SGDB...
taskkill /IM SGDB.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

set "VER=0.2.0"
for /f "tokens=3 delims=<>" %%V in ('findstr /C:"<Version>" "src\SGDB.App\SGDB.App.csproj"') do set "VER=%%V"

set "OUT=%~dp0SGDB_Para_Outro_PC\SGDB"
set "ZIP=%USERPROFILE%\Desktop\SGDB-%VER%-win-x64.zip"

echo.
echo Versao: %VER%
echo Publicando...
dotnet publish "src\SGDB.App\SGDB.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%" --nologo
if errorlevel 1 (
  echo [ERRO] Falha no publish.
  pause
  exit /b 1
)

echo.
echo Criando ZIP na Area de Trabalho...
if exist "%ZIP%" del /f /q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo [ERRO] Falha ao criar ZIP.
  pause
  exit /b 1
)

echo.
echo ========================================
echo  PRONTO
echo ========================================
echo Arquivo:
echo   %ZIP%
echo.
echo Agora no navegador:
echo 1) Abra https://github.com/Bradock80/SBDG/releases/new
echo 2) Em "Choose a tag" / etiqueta digite: v%VER%
echo 3) Title / titulo: SGDB %VER%
echo 4) Arraste o ZIP da Area de Trabalho para anexar
echo 5) Clique em Publicar lancamento
echo.
explorer /select,"%ZIP%"
pause
