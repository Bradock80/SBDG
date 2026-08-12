@echo off
chcp 65001 >nul
title SGDB — Atualizar pacote para outro PC
cd /d "%~dp0"

echo Fechando SGDB...
taskkill /IM SGDB.exe /F >nul 2>&1

echo Publicando (Release win-x64, self-contained)...
dotnet publish "src\SGDB.App\SGDB.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "SGDB_Para_Outro_PC\SGDB"
if errorlevel 1 (
  echo [ERRO] Publish falhou.
  pause
  exit /b 1
)

if not exist "SGDB_Para_Outro_PC\SGDB\Assets" mkdir "SGDB_Para_Outro_PC\SGDB\Assets"
copy /Y "src\SGDB.App\Assets\app.ico" "SGDB_Para_Outro_PC\SGDB\Assets\app.ico" >nul

echo.
echo Pacote atualizado em SGDB_Para_Outro_PC\SGDB
echo Rode CRIAR_ATALHO.bat la, ou abra o SGDB (cria o atalho sozinho).
echo.
pause
