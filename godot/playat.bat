@echo off
rem Chosen: play a level from a test spot, in Chosen (like play.bat), NBlood, or both to compare.
rem usage: godot\playat.bat [nblood|both] [nomonsters] [nosectors] <spot>           a named spot in godot\spots.txt
rem        godot\playat.bat [nblood|both] [nomonsters] [nosectors] <map> <x> <y> <z> [yaw] [pitch] [sector]   anywhere
rem        godot\playat.bat                                                     lists the spots
rem nomonsters: no enemies (Chosen's nomonsters command, your b3d_monsters setting untouched; NBlood -nodudes 1).
rem nosectors: Chosen draws the converter's faces instead of the sectors from Build's walls (--sectors=0, r_sectors 0).
rem Quake units, z the feet; sector: the Build sector (where rooms share space; -1: found from the position).
rem NBlood (third_party\nblood, build_nblood.bat) runs from tools\nblood_play with its own nblood.cfg, at the skill
rem Chosen's b3d_skill is set to. BLOOD: the Blood install (else found as ChosenLoader finds it, pipeline\blood_install.py).
setlocal
set HERE=%~dp0
set ROOT=%HERE%..\
set ENGINE=godot
set NOMON=0
set SECTORS=
if /i "%~1"=="nomonsters" set NOMON=1& shift
if /i "%~1"=="nosectors" set SECTORS=--sectors=0& shift
if /i "%~1"=="nblood" set ENGINE=nblood
if /i "%~1"=="both" set ENGINE=both
if not "%ENGINE%"=="godot" shift
if /i "%~1"=="nomonsters" set NOMON=1& shift
if /i "%~1"=="nosectors" set SECTORS=--sectors=0& shift
if /i "%~1"=="nomonsters" set NOMON=1& shift
if "%~1"=="" goto list
if not "%~4"=="" (
    set YAW=%~5
    set PITCH=%~6
    set SECTOR=%~7
    if "%~5"=="" set YAW=0
    if "%~6"=="" set PITCH=0
    if "%~7"=="" set SECTOR=-1
    call :run %1 %2 %3 %4 ""
    exit /b
)
for /f "usebackq eol=# tokens=1-8*" %%a in ("%HERE%spots.txt") do (
    if /i "%%a"=="%~1" (
        set YAW=%%f
        set PITCH=%%g
        set SECTOR=%%h
        call :run %%b %%c %%d %%e "%%i"
        exit /b
    )
)
echo No spot named "%~1" in godot\spots.txt.
:list
echo Spots (godot\spots.txt; playat [nblood^|both] [nomonsters] ^<spot^>):
for /f "usebackq eol=# tokens=1,2" %%a in ("%HERE%spots.txt") do echo   %%a  (%%b)
exit /b 1

rem :run map x y z "console commands"   (YAW, PITCH, SECTOR set)
:run
set CMDS=%~5
if not "%ENGINE%"=="godot" call :nblood %1 %2 %3 %4
if "%ENGINE%"=="nblood" exit /b
set EXEC=warp %2 %3 %4 %YAW% %PITCH% %SECTOR%
if "%NOMON%"=="1" set EXEC=nomonsters; %EXEC%
if not "%CMDS%"=="" set EXEC=%EXEC%; %CMDS%
call "%HERE%play.bat" %1 "--exec=%EXEC%" %SECTORS%
exit /b

:nblood
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
if "%BLOOD%"=="" for /f "delims=" %%B in ('call "%PY%" -I "%ROOT%pipeline\blood_install.py"') do set "BLOOD=%%B"
if "%BLOOD%"=="" exit /b 1
set NB=%ROOT%third_party\nblood\nblood.exe
if not exist "%NB%" (
    echo NBlood is not built: run build_nblood.bat
    exit /b 1
)
set NBRUN=%ROOT%tools\nblood_play
if not exist "%NBRUN%" mkdir "%NBRUN%"
if not exist "%NBRUN%\nblood.cfg" (
    > "%NBRUN%\nblood.cfg" (
        echo [Setup]
        echo ForceSetup = 0
        echo NoAutoLoad = 1
        echo.
        echo [Screen Setup]
        echo ScreenBPP = 32
        echo ScreenWidth = 1920
        echo ScreenHeight = 1080
        echo ScreenMode = 0
        echo Polymer = 0
    )
)
set SKILL=2
set NODUDES=
if "%NOMON%"=="1" set NODUDES=-nodudes 1
set CFG=%APPDATA%\Godot\app_userdata\Chosen\config.cfg
if exist "%CFG%" for /f "tokens=2 delims== " %%s in ('findstr /b /c:"b3d_skill" "%CFG%"') do set SKILL=%%~s
pushd "%NBRUN%"
rem the install, and for an add-on's map (Death Wish's dwe1m1) its folder and episode file (pipeline\addons.py)
set NBMAP=-j "%BLOOD%" -map %1
set PY=python
if exist "%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe" set PY=%LOCALAPPDATA%\Python\pythoncore-3.14-64\python.exe
for /f "delims=" %%a in ('""%PY%" "%ROOT%pipeline\addons.py" nbargs %1"') do set NBMAP=%%a
start "" "%NB%" -usecwd -nosetup -quick %NBMAP% -skill %SKILL% -b3dwarp v%2,%3,%4,%YAW%,%PITCH%,%SECTOR% %NODUDES%
popd
exit /b
