@echo off
rem Build NBlood (third_party\nblood) with Chosen's comparison patch (pipeline\nblood_b3dview.patch) for
rem "python pipeline\bugs.py nblood <id>": -b3dview x,y,z,yaw,pitch draws the view from a Chosen camera,
rem -b3dshot seconds saves a screenshot and quits; -b3dwarp x,y,z,yaw[,pitch[,sector]] starts an ordinary game with
rem the player there (godot\playat.bat nblood). Needs Visual Studio 2022 (C++ desktop workload).
setlocal
set ROOT=%~dp0
pushd "%ROOT%third_party\nblood"
git apply --check "%ROOT%pipeline\nblood_b3dview.patch" 2>nul && git apply "%ROOT%pipeline\nblood_b3dview.patch"
popd
set MSB=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe
"%MSB%" "%ROOT%third_party\nblood\platform\Windows\nblood.vcxproj" -p:Configuration=Release -p:Platform=x64 -p:SolutionDir="%ROOT%third_party\nblood\platform\Windows\\" -m -v:minimal -nologo || exit /b 1
echo Built third_party\nblood\nblood.exe
