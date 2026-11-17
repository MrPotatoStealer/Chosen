@echo off
rem Install one of Refreshed Supply's add-ons (the install's addons\<folder>: Death Wish, Cryptic Passage, MARROW) for
rem the game: its art, tile table, episode file, music and cutscenes (game\blood3d\addons\<id>, pipeline\export_addon.py),
rem then every level's map (pipeline\export_maps.py: the base game's and each installed add-on's). Its levels then load
rem by name like any other (--map=dwe1m1) and show in the menu's ADD-ONS.
rem usage: install_addon.bat "Death Wish"       (or its id: dw, cryptic, marrow)
setlocal
set ROOT=%~dp0
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
if "%~1"=="" ( echo usage: install_addon.bat "Death Wish" & exit /b 1 )
"%PY%" -I "%ROOT%pipeline\export_addon.py" "%~1" || exit /b 1
"%PY%" -I "%ROOT%pipeline\export_maps.py" || exit /b 1
