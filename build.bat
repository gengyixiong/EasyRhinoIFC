@echo off
setlocal
pushd "%~dp0"

echo === Building EasyRhinoIFC ===
dotnet build RhinoIfc.sln -c Release
if errorlevel 1 goto :build_failed

echo.
echo === Staging release files ===
set "STAGE=yak_stage"
if exist "%STAGE%" rd /s /q "%STAGE%"
mkdir "%STAGE%"
if errorlevel 1 goto :stage_failed

copy /y "RhinoIfc\bin\Release\EasyRhinoIFC.rhp" "%STAGE%\" >nul || goto :stage_failed
copy /y "GH_RhinoIfc\bin\Release\GH_EasyRhinoIFC.dll" "%STAGE%\" >nul || goto :stage_failed
copy /y "RhinoIfc\bin\Release\*.dll" "%STAGE%\" >nul || goto :stage_failed
copy /y "manifest.yml" "%STAGE%\" >nul || goto :stage_failed

echo.
echo === Building Yak package ===
where yak >nul 2>nul
if errorlevel 1 (
    echo Yak CLI not found; release files are staged in %STAGE%\.
    goto :done
)

pushd "%STAGE%"
yak build
if errorlevel 1 (
    popd
    goto :yak_failed
)
popd

:done
echo.
echo === Done ===
echo Release files created in %STAGE%\
if exist "%STAGE%\*.yak" dir /b "%STAGE%\*.yak"
popd
exit /b 0

:build_failed
echo BUILD FAILED
popd
exit /b 1

:stage_failed
echo STAGING FAILED
popd
exit /b 1

:yak_failed
echo YAK BUILD FAILED
popd
exit /b 1
