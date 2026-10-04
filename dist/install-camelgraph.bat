@echo off
setlocal

rem ============================================================
rem  CamelGraph - Navisworks 2024 bundle installer
rem
rem  Copies CamelGraph.bundle\ (next to this script) into the
rem  per-user Autodesk ApplicationPlugins folder. On the next
rem  start of Navisworks Manage/Simulate 2024 the "BIMCamel"
rem  ribbon tab appears with the CamelGraph button.
rem
rem  Usage:
rem    install-camelgraph.bat              install / update
rem    install-camelgraph.bat uninstall    remove the bundle
rem ============================================================

set "SRC=%~dp0CamelGraph.bundle"
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle"

rem The product was called Dyncamelo up to version 0.48. Its bundle folder and Add/Remove Programs entry are removed when
rem CamelGraph is installed or removed, so only one plug-in loads.
set "LEGACY=%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle"
set "LEGACYKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Dyncamelo"

if /i "%~1"=="uninstall" goto :uninstall

if not exist "%SRC%\PackageContents.xml" (
    echo [ERROR] Bundle not found next to this script:
    echo         %SRC%
    echo         Run this .bat from the dist\ folder of the CamelGraph repo.
    exit /b 1
)

if not exist "%SRC%\2024\CamelGraph.App.dll" (
    echo [ERROR] CamelGraph.App.dll is missing from "%SRC%\2024".
    echo         Build the solution first:  dotnet build CamelGraph.sln -c Release
    echo         then copy the DLLs listed in 2024\PLACE_CAMELGRAPH_DLLS_HERE.txt.
    exit /b 1
)

if exist "%LEGACY%" (
    echo Removing the earlier Dyncamelo install...
    rmdir /s /q "%LEGACY%"
    if exist "%LEGACY%" (
        echo [ERROR] Could not remove "%LEGACY%". Close Navisworks and run this script again.
        exit /b 1
    )
)
reg delete "%LEGACYKEY%" /f >nul 2>&1

echo Installing CamelGraph bundle...
echo   from: %SRC%
echo   to:   %DEST%
echo.

robocopy "%SRC%" "%DEST%" /E /NFL /NDL /NJH /NJS /XF PLACE_CAMELGRAPH_DLLS_HERE.txt >nul
if errorlevel 8 (
    echo [ERROR] Copy failed ^(robocopy exit code %ERRORLEVEL%^).
    echo         If Navisworks is running, close it and run this script again.
    exit /b 1
)

rem Files extracted from a downloaded zip carry the "from the internet" mark
rem (Zone.Identifier). .NET Framework refuses to load such DLLs -
rem FileLoadException 0x80131515, shown by Navisworks as PLUGIN_LOAD_02.
rem Strip the mark from everything just installed.
echo Unblocking installed files...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%DEST%' -Recurse -File | Unblock-File" >nul 2>&1

echo [OK] CamelGraph installed to:
echo      %DEST%
echo.
echo Start ^(or restart^) Navisworks Manage/Simulate 2024 - the
echo "BIMCamel" ribbon tab appears with the CamelGraph button.
exit /b 0

:uninstall
if exist "%LEGACY%" (
    rmdir /s /q "%LEGACY%"
    echo [OK] Removed %LEGACY%
)
reg delete "%LEGACYKEY%" /f >nul 2>&1
if exist "%DEST%" (
    rmdir /s /q "%DEST%"
    echo [OK] Removed %DEST%
) else (
    echo Nothing to remove - %DEST% does not exist.
)
exit /b 0
