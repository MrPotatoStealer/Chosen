@echo off
rem Chosen (Blood in Godot): build the C# game and play a level from its Blood .MAP (no map: the main menu).
rem usage: godot\play.bat [map] [options]      (no map: the title screen; options as in README.md, e.g. --play=20261018-212040)
setlocal
set HERE=%~dp0
set MAP=%~1
rem no map named: the title screen (New Game picks a level); a map named: straight into it
set MAPARG=--map=%MAP%
if "%MAP%"=="" set MAPARG=
set GODOT=%HERE%..\tools\godot-4.7-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64
dotnet build "%HERE%Chosen.csproj" -v q || exit /b 1
"%GODOT%_console.exe" --headless --path "%HERE%." --import >nul 2>&1
start "" "%GODOT%.exe" --path "%HERE%." -- %MAPARG% %2 %3 %4 %5 %6 %7 %8 %9
