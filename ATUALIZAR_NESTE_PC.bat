@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
title SGDB — Atualizar programa (seguro)

rem ============================================================
rem  ATUALIZAR_NESTE_PC.bat — SOMENTE PROGRAMA
rem  NÃO altera: deposito.db, WAL/SHM, tokens, AppData, backups
rem ============================================================

cd /d "%~dp0"

rem Destino da instalação do PROGRAMA (nunca AppData).
rem Pode sobrescrever com: ATUALIZAR_NESTE_PC.bat "D:\caminho\SGDB"
rem ou variável de ambiente SGDB_INSTALL_DIR (testes).
if not "%~1"=="" (
  set "DEST=%~1"
) else if defined SGDB_INSTALL_DIR (
  set "DEST=%SGDB_INSTALL_DIR%"
) else (
  set "DEST=%USERPROFILE%\Documents\SGDB"
)

rem Origem = pasta deste script, ou subpasta SGDB\ (layout legado).
set "SRC=%~dp0"
if exist "%~dp0SGDB\SGDB.exe" set "SRC=%~dp0SGDB\"

rem Remove barra final inconsistente
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
if "%DEST:~-1%"=="\" set "DEST=%DEST:~0,-1%"

echo ========================================
echo   SGDB — atualizar SOMENTE o programa
echo ========================================
echo.
echo Origem (pacote):
echo   %SRC%
echo.
echo Destino (instalacao):
echo   %DEST%
echo.
echo IMPORTANTE:
echo   Os dados reais do SGDB ficam em:
echo   %LOCALAPPDATA%\SGDB
echo   e NAO serao alterados por este script.
echo.
echo   Este update NAO copia banco, tokens nem AppData.
echo.
if /I "%SGDB_UPDATE_NOPAUSE%"=="1" goto :SkipPause1
pause
:SkipPause1

echo.
echo [1/7] Fechando SGDB...
taskkill /IM SGDB.exe /F >nul 2>&1
rem Evita "timeout" ^(quebra com stdin redirecionado^)
ping -n 2 127.0.0.1 >nul

if not exist "%SRC%\SGDB.exe" (
  echo [ERRO] Nao achei SGDB.exe em:
  echo   %SRC%
  echo Extraia o ZIP do release e rode este .bat de dentro da pasta do programa.
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

rem Bloqueia destino = pasta de DADOS
for %%I in ("%LOCALAPPDATA%\SGDB") do set "APPDATA_SGDB=%%~fI"
for %%I in ("%DEST%") do set "DEST_FULL=%%~fI"
for %%I in ("%SRC%") do set "SRC_FULL=%%~fI"
if /I "%DEST_FULL%"=="%APPDATA_SGDB%" (
  echo [ERRO] Destino nao pode ser %%LOCALAPPDATA%%\SGDB ^(pasta de DADOS^).
  echo Use a pasta do PROGRAMA, ex.: %%USERPROFILE%%\Documents\SGDB
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

echo [2/7] Validando pacote ^(sem banco / sem tokens^)...
if exist "%SRC%\Banco\" (
  echo [ERRO] Pacote contem pasta Banco\ — update abortado.
  echo Remova Banco\ do pacote. O banco da loja fica so em AppData.
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

set "BAD=0"
for %%E in (db db-wal db-shm bin) do (
  for /f "delims=" %%F in ('dir /s /b "%SRC%\*.%%E" 2^>nul') do (
    echo [ERRO] Arquivo proibido no pacote: %%F
    set "BAD=1"
  )
)
if "!BAD!"=="1" (
  echo.
  echo Update abortado: pacote nao pode conter *.db / *.db-wal / *.db-shm / *.bin
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

if /I "%SRC_FULL%"=="%DEST_FULL%" (
  echo [ERRO] Origem e destino sao a mesma pasta.
  echo Extraia o ZIP novo em outra pasta ^(ex.: Desktop^) e rode o .bat de la,
  echo apontando para a instalacao em Documents\SGDB.
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

rem Timestamp para backup
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "TS=%%I"
set "BACKUP=%DEST%_INSTALACAO_ANTIGA_%TS%"

echo [3/7] Backup da instalacao antiga ^(somente programa^)...
if exist "%DEST%\SGDB.exe" (
  if exist "%BACKUP%" (
    echo [ERRO] Pasta de backup ja existe: %BACKUP%
    if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
    exit 1
  )
  move "%DEST%" "%BACKUP%" >nul
  if errorlevel 1 (
    echo [ERRO] Falha ao mover instalacao antiga para backup.
    echo Feche o SGDB / antivirus e tente de novo.
    if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
    exit 1
  )
  echo   Backup: %BACKUP%
) else (
  echo   Nenhuma instalacao anterior em %DEST%
)

echo [4/7] Criando pasta de instalacao limpa...
mkdir "%DEST%" 2>nul
if not exist "%DEST%" (
  echo [ERRO] Nao consegui criar: %DEST%
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

echo [5/7] Copiando programa novo...
robocopy "%SRC%" "%DEST%" /E /NFL /NDL /NJH /NJS /nc /ns /np /XD Banco Backups .git src tests /XF ATUALIZAR_NESTE_PC.bat >nul
set "RC=!ERRORLEVEL!"
rem robocopy: 0-7 = sucesso parcial/ok; >=8 = falha
if !RC! GEQ 8 (
  echo [ERRO] Falha ao copiar arquivos ^(robocopy !RC!^).
  echo Instalacao pode estar incompleta. Restaure o backup:
  echo   %BACKUP%
  call :WriteLog "ERRO robocopy RC=!RC!"
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

if not exist "%DEST%\SGDB.exe" (
  echo [ERRO] Apos a copia, SGDB.exe nao esta no destino.
  call :WriteLog "ERRO SGDB.exe ausente apos copia"
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

rem Copia o script seguro para a instalacao ^(referencia^)
copy /Y "%~f0" "%DEST%\ATUALIZAR_NESTE_PC.bat" >nul 2>&1

rem Revalidar destino nao ganhou banco por engano
set "BADDEST=0"
for %%E in (db db-wal db-shm bin) do (
  for /f "delims=" %%F in ('dir /s /b "%DEST%\*.%%E" 2^>nul') do (
    echo [ERRO] Arquivo proibido apareceu no destino: %%F
    set "BADDEST=1"
  )
)
if "!BADDEST!"=="1" (
  echo Abortando — remova arquivos de dados do pacote e tente de novo.
  call :WriteLog "ERRO destino contem db/bin"
  if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
  exit 1
)

echo [6/7] Recriando atalho na area de trabalho...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; $p = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SGDB.lnk'; $s = $ws.CreateShortcut($p); $s.TargetPath = '%DEST%\SGDB.exe'; $s.WorkingDirectory = '%DEST%'; $s.Description = 'SGDB'; $s.Save()"
if errorlevel 1 (
  echo [AVISO] Nao foi possivel recriar o atalho. Abra manualmente:
  echo   %DEST%\SGDB.exe
)

call :WriteLog "OK origem=%SRC% destino=%DEST% backup=%BACKUP%"

echo [7/7] Concluido.
echo.
echo Programa atualizado em:
echo   %DEST%\SGDB.exe
echo.
echo Dados da loja ^(intocados^):
echo   %LOCALAPPDATA%\SGDB
echo.
echo Rollback ^(se precisar^):
echo   1^) Feche o SGDB
echo   2^) Apague/renomeie %DEST%
echo   3^) Renomeie o backup de volta para %DEST%
if exist "%BACKUP%" echo   Backup atual: %BACKUP%
echo.
echo Abrindo SGDB...
if /I not "%SGDB_UPDATE_NOSTART%"=="1" start "" "%DEST%\SGDB.exe"
echo.
if /I not "%SGDB_UPDATE_NOPAUSE%"=="1" pause
exit 0

:WriteLog
if not exist "%DEST%" mkdir "%DEST%" 2>nul
>>"%DEST%\update.log" echo [%DATE% %TIME%] %~1
exit /b 0
