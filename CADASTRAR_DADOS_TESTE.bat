@echo off
chcp 65001 >nul
title SGDB — Cadastrar dados de teste
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET nao encontrado. Rode INSTALAR_DOTNET.bat primeiro.
    pause
    exit /b 1
)

echo.
echo  Cadastrar fornecedores e produtos de teste
echo  ------------------------------------------
echo  Fornecedores: Ambev, Femsa, Indaia, Petropolis
echo  Produtos: cervejas, refrigerantes, agua, energetico
echo  (nao duplica se ja existirem pelo CNPJ/codigo)
echo.

dotnet run --project "scripts\SeedDemo\SeedDemo.csproj" -c Release
if errorlevel 1 (
    echo.
    echo [ERRO] Nao foi possivel cadastrar os dados de teste.
    pause
    exit /b 1
)

echo.
pause
