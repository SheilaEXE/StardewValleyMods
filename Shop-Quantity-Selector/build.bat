@echo off
setlocal
cd /d "%~dp0"

echo.
echo Compilando Shop Quantity Selector...
echo.
dotnet build -c Release
if errorlevel 1 (
  echo.
  echo A compilacao falhou. Copie os erros da janela e envie para a Sage.
  pause
  exit /b 1
)

echo.
echo Criando o ZIP pronto para instalar...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$source = Join-Path $PWD 'bin\Release\net6.0';" ^
  "$staging = Join-Path $PWD 'dist\Shop Quantity Selector';" ^
  "$zip = Join-Path $PWD 'Shop Quantity Selector - INSTALAR.zip';" ^
  "New-Item -ItemType Directory -Force -Path $staging | Out-Null;" ^
  "Copy-Item -Path (Join-Path $source 'ShopQuantitySelector.dll'), 'manifest.json' -Destination $staging -Force;" ^
  "Copy-Item -Path 'i18n' -Destination $staging -Recurse -Force;" ^
  "Compress-Archive -Path (Join-Path $PWD 'dist\Shop Quantity Selector') -DestinationPath $zip -Force"

if errorlevel 1 (
  echo.
  echo A DLL foi compilada, mas o ZIP nao pode ser criado.
  echo A pasta pronta esta em: dist\Shop Quantity Selector
  pause
  exit /b 1
)

echo.
echo Pronto! O arquivo foi criado aqui:
echo Shop Quantity Selector - INSTALAR.zip
pause
