@echo off
rem The distribution: Chosen.exe and ChosenLoader.exe into Chosen beside the repository's folder (or the folder given), Chosen Forge into
rem ChosenForge beside it. Players unpack Chosen, run ChosenLoader once (it finds their Blood install and extracts what
rem the game needs into data\ beside Chosen.exe), then play; no Python, no .bat files.
rem usage: build_dist.bat [out folder]      (each Godot export takes several minutes)
setlocal
set ROOT=%~dp0
set GODOT=%ROOT%tools\godot-4.7-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
set OUT=%~1
if "%OUT%"=="" set OUT=%ROOT%..\Chosen
for %%I in ("%OUT%") do set OUT=%%~fI
for %%I in ("%OUT%\..\ChosenForge") do set FORGE=%%~fI
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%FORGE%" mkdir "%FORGE%"

rem the version (VERSION): the programs say "0.1.0+<commit>", not "-dev" (Directory.Build.props); the exe files'
rem properties come from each export preset, which must say the same (a new version: VERSION, the three
rem export_presets.cfg's file_version and product_version, the three project.godot's config/version)
set /p VER=<"%ROOT%VERSION"
set CHOSEN_RELEASE=1
for %%P in (godot loader forge) do (
  findstr /c:"application/file_version=\"%VER%.0\"" "%ROOT%%%P\export_presets.cfg" >nul || (
    echo %%P\export_presets.cfg: its file_version isn't VERSION's %VER%.0
    goto fail
  )
  findstr /c:"config/version=\"%VER%\"" "%ROOT%%%P\project.godot" >nul || (
    echo %%P\project.godot: its config/version isn't VERSION's %VER%
    goto fail
  )
)
echo === Chosen %VER%

for %%P in (godot loader forge) do (
  echo === %%P: build
  dotnet build "%ROOT%%%P" -v q || goto fail
  "%GODOT%" --headless --path "%ROOT%%%P" --import >nul 2>&1
)
echo === Chosen.exe
"%GODOT%" --headless --path "%ROOT%godot" --export-release "Windows Desktop" "%OUT%\Chosen.exe" || goto fail
echo === ChosenLoader.exe
"%GODOT%" --headless --path "%ROOT%loader" --export-release "Windows Desktop" "%OUT%\ChosenLoader.exe" || goto fail
echo === ChosenForge.exe
"%GODOT%" --headless --path "%ROOT%forge" --export-release "Windows Desktop" "%FORGE%\ChosenForge.exe" || goto fail
copy /y "%ROOT%dist\README.txt" "%OUT%\README.txt" >nul
copy /y "%ROOT%dist\README-Forge.txt" "%FORGE%\README.txt" >nul
rem the licences go with the programs (GPL v2, and Build's for the game's engine ports: LICENSE.md)
for %%D in ("%OUT%" "%FORGE%") do (
  copy /y "%ROOT%LICENSE.md" "%%~D\LICENSE.md" >nul
  xcopy /y /i /q "%ROOT%licenses" "%%~D\licenses" >nul
)
echo Done: %OUT% and %FORGE%
exit /b 0
:fail
echo FAILED
exit /b 1
