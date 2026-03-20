@echo off
setlocal

echo === Building RhinoIfc ===
dotnet build RhinoIfc\RhinoIfc.csproj -c Release
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo.
echo === Staging for Yak packaging ===
set STAGE=yak_stage
if exist %STAGE% rd /s /q %STAGE%
mkdir %STAGE%

:: Copy plugin output
copy RhinoIfc\bin\Release\RhinoIfc.dll %STAGE%\RhinoIfc.rhp

:: Copy xBIM managed dependencies
copy RhinoIfc\bin\Release\Xbim.*.dll %STAGE%\
copy RhinoIfc\bin\Release\Microsoft.Extensions.*.dll %STAGE%\ 2>nul
copy RhinoIfc\bin\Release\Microsoft.Bcl.*.dll %STAGE%\ 2>nul
copy RhinoIfc\bin\Release\System.*.dll %STAGE%\ 2>nul

:: Copy native geometry engine DLLs
copy RhinoIfc\bin\Release\Xbim.Geometry.Engine32.dll %STAGE%\
copy RhinoIfc\bin\Release\Xbim.Geometry.Engine64.dll %STAGE%\

:: Copy manifest
copy manifest.yml %STAGE%\

echo.
echo === Building Yak package ===
cd %STAGE%
yak build
if errorlevel 1 (
    echo YAK BUILD FAILED - is yak.exe on PATH?
    echo You can install it: https://developer.rhino3d.com/guides/yak/
    cd ..
    exit /b 1
)

cd ..
echo.
echo === Done ===
echo Yak package created in %STAGE%\
dir %STAGE%\*.yak 2>nul
