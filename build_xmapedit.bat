@echo off
rem Build xmapedit (third_party\xmapedit: NoOne's Blood map editor, https://github.com/NoOneBlood/xmapedit, a reference
rem for Chosen Forge's editing rules) with MSVC's nmake. Needs Visual Studio 2022 (C++ desktop workload). The editor ends up in
rem third_party\xmapedit\mapedit-data\xmapedit.exe. It always runs in its own folder and loads Blood's files from there
rem (BLOOD.RFF, SOUNDS.RFF, TILES0xx.ART, SURFACE.DAT, VOXEL.DAT, BLOOD.INI), so they are copied in from the Blood
rem folder (BLOOD_DIR, else BLOOD, else found as ChosenLoader finds it: pipeline\blood_install.py); files there that are newer (edited)
rem are kept.
setlocal
set ROOT=%~dp0
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
if not defined BLOOD_DIR for /f "delims=" %%B in ('call "%PY%" -I "%ROOT%pipeline\blood_install.py"') do set "BLOOD_DIR=%%B"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
pushd "%ROOT%third_party\xmapedit"
nmake /nologo /f Makefile.msvc RELEASE=1 %*
set ERR=%ERRORLEVEL%
popd
if not "%ERR%"=="0" exit /b %ERR%
if not exist "%BLOOD_DIR%\BLOOD.RFF" (
    echo No BLOOD.RFF in "%BLOOD_DIR%": set BLOOD_DIR to the Blood folder 1>&2
    exit /b 1
)
robocopy "%BLOOD_DIR%" "%ROOT%third_party\xmapedit\mapedit-data" BLOOD.RFF SOUNDS.RFF TILES*.ART SURFACE.DAT VOXEL.DAT SHADE.DAT BLOOD.INI /XO /NJH /NJS /NFL /NDL /NP >nul
if %ERRORLEVEL% GEQ 8 (
    echo Copying the Blood files failed 1>&2
    exit /b 1
)
echo Built third_party\xmapedit\mapedit-data\xmapedit.exe
