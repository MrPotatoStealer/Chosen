@echo off
rem A release for GitHub: the game (Chosen.exe, ChosenLoader.exe) and Chosen Forge built from the commit tagged
rem v<VERSION>, each program's pack checked for anything of Blood's or a picture of it (pipeline\pck_list.py), zipped
rem into release\: Chosen-<version>-windows-x64.zip, ChosenForge-<version>-windows-x64.zip and their SHA-256 sums.
rem Each README.txt says where the source is (the origin remote, at the tag), as the GPL asks of a binary.
rem usage: build_release.bat            (the tagged commit, nothing uncommitted)
rem        build_release.bat --test     (the working tree as it is; its zips say <version>-test)
rem Then: git push origin main v<VERSION>, and a GitHub release from the tag with the zips and sums (README.md).
setlocal
set ROOT=%~dp0
set /p VER=<"%ROOT%VERSION"
set TAG=v%VER%
set NAME=%VER%
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
pushd "%ROOT%"
set SRC=
for /f "delims=" %%U in ('git remote get-url origin 2^>nul') do set "SRC=%%U"
if /i "%~1"=="--test" (
  set NAME=%VER%-test
  goto build
)
rem a release is exactly the tagged commit: nothing uncommitted or untracked, HEAD tagged v<VERSION>, a public origin
set DIRTY=
for /f "delims=" %%L in ('git status --porcelain') do set DIRTY=1
if defined DIRTY (
  echo Uncommitted changes: commit them first, or build_release.bat --test
  goto fail
)
set TAGGED=
for /f "delims=" %%T in ('git tag --points-at HEAD') do if "%%T"=="%TAG%" set TAGGED=1
if not defined TAGGED (
  echo HEAD isn't tagged %TAG%: git tag %TAG%
  goto fail
)
if not defined SRC (
  echo No origin remote: the release's README says where its source is
  goto fail
)

:build
set OUT=%ROOT%release\%NAME%
if exist "%OUT%" rmdir /s /q "%OUT%"
call "%ROOT%build_dist.bat" "%OUT%\Chosen" || goto fail

echo === checking what the programs carry
for %%E in ("%OUT%\Chosen\Chosen.exe" "%OUT%\Chosen\ChosenLoader.exe" "%OUT%\ChosenForge\ChosenForge.exe") do (
  "%PY%" -I "%ROOT%pipeline\pck_list.py" %%E --check || goto fail
)
for %%D in (Chosen ChosenForge) do (
  if exist "%OUT%\%%D\data" (
    echo %OUT%\%%D\data: a data folder in the release
    goto fail
  )
)

rem where the source is: the repository's web address at this tag
if defined SRC set "SRC=%SRC:.git=%"
if not defined SRC set "SRC=none yet, no origin remote: a test build"
for %%D in (Chosen ChosenForge) do (
  >> "%OUT%\%%D\README.txt" echo.
  >> "%OUT%\%%D\README.txt" echo Version %VER%. Source code: %SRC% ^(tag %TAG%^)
)

echo === zipping
set ZIPS=%ROOT%release
del /q "%ZIPS%\Chosen-%NAME%-windows-x64.zip" "%ZIPS%\ChosenForge-%NAME%-windows-x64.zip" 2>nul
rem the programs at the zip's top (Windows' Extract All makes the folder, named after the zip)
for %%D in (Chosen ChosenForge) do (
  dir /b "%OUT%\%%D" > "%OUT%\%%D.lst"
  tar -a -c -f "%ZIPS%\%%D-%NAME%-windows-x64.zip" -C "%OUT%\%%D" -T "%OUT%\%%D.lst" || goto fail
)
> "%ZIPS%\SHA256SUMS-%NAME%.txt" (
  for %%Z in ("%ZIPS%\*-%NAME%-windows-x64.zip") do (
    for /f "delims=" %%H in ('certutil -hashfile "%%~fZ" SHA256 ^| findstr /v ":"') do echo %%H  %%~nxZ
  )
)
type "%ZIPS%\SHA256SUMS-%NAME%.txt"
popd
echo Done: release\Chosen-%NAME%-windows-x64.zip, release\ChosenForge-%NAME%-windows-x64.zip, release\SHA256SUMS-%NAME%.txt
exit /b 0
:fail
popd
echo FAILED
exit /b 1
