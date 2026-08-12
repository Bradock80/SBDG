@echo off
chcp 65001 >nul
title SGDB — Atualizar neste PC
cd /d "%~dp0"

set "EXE=%~dp0SGDB\SGDB.exe"
set "SRC=%~dp0Banco\deposito.db"
set "DEST=%LOCALAPPDATA%\SGDB\deposito.db"

echo ========================================
echo   SGDB — atualizar programa + dados
echo ========================================
echo.
echo Pasta desta atualizacao:
echo   %~dp0
echo.
echo 1) Fecha o SGDB se estiver aberto
echo 2) Copia o banco para AppData
echo 3) Recria o atalho apontando para ESTA pasta
echo 4) Abre o programa novo
echo.
pause

echo.
echo Fechando SGDB...
taskkill /IM SGDB.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%EXE%" (
  echo [ERRO] Nao achei SGDB\SGDB.exe
  echo Copie a pasta SGDB_Para_Outro_PC INTEIRA para Documentos.
  pause
  exit /b 1
)

if not exist "%SRC%" (
  echo [ERRO] Nao achei Banco\deposito.db
  pause
  exit /b 1
)

if not exist "%LOCALAPPDATA%\SGDB" mkdir "%LOCALAPPDATA%\SGDB"

if exist "%DEST%" (
  echo Backup do banco antigo...
  copy /Y "%DEST%" "%LOCALAPPDATA%\SGDB\deposito.db.bak_antes_atualizacao" >nul
)

echo Copiando banco...
copy /Y "%SRC%" "%DEST%" >nul
if errorlevel 1 (
  echo [ERRO] Nao consegui copiar o banco. Feche o SGDB e tente de novo.
  pause
  exit /b 1
)

echo Recriando atalho na area de trabalho...
call "%~dp0CRIAR_ATALHO.bat"

echo.
echo Pronto!
echo Abrindo o SGDB desta pasta:
echo   %EXE%
echo.
start "" "%EXE%"
echo.
echo Se ainda parecer antigo: delete o atalho velho da area de trabalho
echo e use so o que este script criou ^(ou abra SGDB\SGDB.exe desta pasta^).
echo.
pause
