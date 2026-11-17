@echo off
rem The older Python extraction (the C# one, maptool extract or ChosenLoader, replaces it): Blood's data into game\blood3d.
rem usage: build_assets.bat ["path\to\Blood - Refreshed Supply"]
rem Needs Python 3 with Pillow (pip install pillow).
setlocal
set ROOT=%~dp0
if not "%~1"=="" set "BLOOD=%~1"
set GAME=%ROOT%game\blood3d
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
if "%BLOOD%"=="" for /f "delims=" %%B in ('call "%PY%" -I "%ROOT%pipeline\blood_install.py"') do set "BLOOD=%%B"
if "%BLOOD%"=="" exit /b 1
set NBLOOD=%ROOT%third_party\nblood\source\blood\src

pushd "%ROOT%pipeline"
echo === textures, sounds, music, SEQ/QAV/tile data
"%PY%" export.py "%BLOOD%" "%GAME%" || goto fail
echo === palette (PLU) sprite variants
"%PY%" export_pals.py "%BLOOD%" "%GAME%" || goto fail
echo === voxel models (KVX to MD3)
"%PY%" -I voxels.py "%BLOOD%" "%GAME%" || goto fail
echo === runtime tables
rem (its QuakeC table goes to extracted\qc: the DarkPlaces version that read it is retired)
if not exist "%ROOT%extracted\qc" mkdir "%ROOT%extracted\qc"
"%PY%" gen_qcdata.py "%GAME%" "%ROOT%extracted\qc" || goto fail
echo === menu graphics (the console background)
"%PY%" make_menu_gfx.py "%GAME%" || goto fail
popd
echo Done. Now run godot\play.bat
exit /b 0
:fail
popd
echo FAILED
exit /b 1
