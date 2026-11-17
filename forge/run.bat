@echo off
rem Chosen Forge: build the editor and start it (a map file given: opened).
rem usage: forge\run.bat [map file]
rem        forge\run.bat --roundtrip <out dir> [maps...]   (headless: open each map, save it into out dir, compare)
rem        forge\run.bat --selftest                        (headless: the editing self-test)
rem        forge\run.bat --uitest                          (headless: the modes driven by mouse and key events)
rem        forge\run.bat --replay=<session folder or last> [--accept] [--until=N]   (headless: a recorded session replayed)
rem        forge\run.bat --regress                         (headless: every replay in forge\tests\replays)
rem        forge\run.bat --addtest=<session folder or last> --name=<name> [--note="..."]   (a session made a regression replay)
setlocal
set HERE=%~dp0
set GODOT=%HERE%..\tools\godot-4.7-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64
dotnet build "%HERE%ChosenForge.csproj" -v q || exit /b 1
"%GODOT%_console.exe" --headless --path "%HERE%." --import >nul 2>&1
rem (cmd splits arguments at "=": --replay=<folder> arrives as --replay; %* passes them on as typed)
if "%~1"=="--roundtrip" goto headless
if "%~1"=="--selftest" goto headless
if "%~1"=="--uitest" goto headless
if "%~1"=="--regress" goto headless
if "%~1"=="--replay" goto headless
if "%~1"=="--addtest" goto headless
start "" "%GODOT%.exe" --path "%HERE%." -- %*
exit /b 0
:headless
rem (its own log: test runs never push the user's logs out of user://logs, which keeps five)
"%GODOT%_console.exe" --headless --path "%HERE%." --log-file "%TEMP%\chosen-forge-test.log" -- %*
exit /b %ERRORLEVEL%
