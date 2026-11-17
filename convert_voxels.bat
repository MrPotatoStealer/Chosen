@echo off
rem Convert Blood's voxels (KVX) into DarkPlaces models: game\blood3d\models\voxels, data\voxels.txt.
rem build_assets.bat runs this too; use it on its own after changing pipeline\voxels.py.
rem usage: convert_voxels.bat ["path\to\Blood - Refreshed Supply"]
setlocal
set ROOT=%~dp0
if not "%~1"=="" set "BLOOD=%~1"
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
if "%BLOOD%"=="" for /f "delims=" %%B in ('call "%PY%" -I "%ROOT%pipeline\blood_install.py"') do set "BLOOD=%%B"
if "%BLOOD%"=="" exit /b 1
"%PY%" -I "%ROOT%pipeline\voxels.py" "%BLOOD%" "%ROOT%game\blood3d" || exit /b 1
