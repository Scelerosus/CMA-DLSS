@echo off
rem Builds native\out\CmaBridge.dll (x64, static runtime). Needs Visual Studio's C++ tools.
setlocal
cd /d "%~dp0"
set "VCVARS=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" set "VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
call "%VCVARS%" >nul 2>nul
if not exist out mkdir out
cl /nologo /LD /O2 /MT /EHa /W3 /std:c++17 /I ngx_sdk\include src\bridge.cpp /Fo:out\ /Fe:out\CmaBridge.dll ^
  /link /NOLOGO /IMPLIB:out\CmaBridge.lib ngx_sdk\lib\nvsdk_ngx_s.lib d3d11.lib dxgi.lib user32.lib advapi32.lib
exit /b %ERRORLEVEL%
