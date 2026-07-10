@echo off
setlocal

rem ============================================================
rem  Dyncamelo - Navisworks 2024 bundle installer
rem
rem  Copies Dyncamelo.bundle\ (next to this script) into the
rem  per-user Autodesk ApplicationPlugins folder. On the next
rem  start of Navisworks Manage/Simulate 2024 the "BIMCamel"
rem  ribbon tab appears with the Dyncamelo button.
rem
rem  Usage:
rem    install-dyncamelo.bat              install / update
rem    install-dyncamelo.bat uninstall    remove the bundle
rem ============================================================

set "SRC=%~dp0Dyncamelo.bundle"
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle"

if /i "%~1"=="uninstall" goto :uninstall

if not exist "%SRC%\PackageContents.xml" (
    echo [ERROR] Bundle not found next to this script:
    echo         %SRC%
    echo         Run this .bat from the dist\ folder of the Dyncamelo repo.
    exit /b 1
)

if not exist "%SRC%\2024\Dyncamelo.App.dll" (
    echo [ERROR] Dyncamelo.App.dll is missing from "%SRC%\2024".
    echo         Build the solution first:  dotnet build Dyncamelo.sln -c Release
    echo         then copy the DLLs listed in 2024\PLACE_DYNCAMELO_DLLS_HERE.txt.
    exit /b 1
)

echo Installing Dyncamelo bundle...
echo   from: %SRC%
echo   to:   %DEST%
echo.

robocopy "%SRC%" "%DEST%" /E /NFL /NDL /NJH /NJS /XF PLACE_DYNCAMELO_DLLS_HERE.txt >nul
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

echo [OK] Dyncamelo installed to:
echo      %DEST%
echo.
echo Start ^(or restart^) Navisworks Manage/Simulate 2024 - the
echo "BIMCamel" ribbon tab appears with the Dyncamelo button.
exit /b 0

:uninstall
if exist "%DEST%" (
    rmdir /s /q "%DEST%"
    echo [OK] Removed %DEST%
) else (
    echo Nothing to remove - %DEST% does not exist.
)
exit /b 0
