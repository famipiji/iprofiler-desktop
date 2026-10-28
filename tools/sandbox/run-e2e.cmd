@echo off
rem Task 1.6: offline end-to-end check of the installed app, run INSIDE Windows Sandbox (networking disabled) from an
rem elevated prompt, after Smart App Control has been turned off in the sandbox. The kit folder (this script, the
rem -spike installer, samples\) is mapped read-only at C:\e2e; results go to the writable mapped folder C:\e2e-results.
rem Writes: env.txt (VC++ runtime + network before install), install.log, console.txt, e2e\report.json + outputs.
setlocal
set KIT=%~dp0
set OUT=C:\e2e-results
set WORK=%LOCALAPPDATA%\iProfiler-e2e
set APPDIR=%ProgramFiles%\iProfiler Desktop

> "%OUT%\env.txt" (
  echo === %DATE% %TIME% on %COMPUTERNAME% as %USERNAME%
  echo --- VC++ runtime in System32 before install
  for %%f in (msvcp140 vcruntime140 vcruntime140_1 vcomp140) do (
    if exist "%SystemRoot%\System32\%%f.dll" (echo %%f.dll present) else (echo %%f.dll MISSING)
  )
  echo --- network
  ipconfig
  ping -n 1 -w 1000 8.8.8.8
  nslookup www.microsoft.com
)

echo Installing...
"%KIT%installer\iProfilerDesktop-Setup-0.1.0.0-spike.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="%OUT%\install.log"
echo installer exit code %ERRORLEVEL%>> "%OUT%\env.txt"
if not exist "%APPDIR%\IProfiler.Spike.exe" (
  echo Install failed: see %OUT%\install.log
  exit /b 1
)

rem SQLite on the sandbox's own disk (not the mapped folder); copied to the results afterwards.
echo Running the end-to-end flow from %APPDIR% ...
"%APPDIR%\IProfiler.Spike.exe" e2e "%KIT%samples" "%WORK%" > "%OUT%\console.txt" 2>&1
set E2E=%ERRORLEVEL%
echo e2e exit code %E2E%>> "%OUT%\env.txt"
netstat -ano >> "%OUT%\env.txt"
robocopy "%WORK%" "%OUT%\e2e" /E /NFL /NDL /NJH /NJS > nul

if "%E2E%"=="0" (echo PASSED: %OUT%\e2e\report.json) else (echo FAILED with exit code %E2E%: see %OUT%\console.txt)
exit /b %E2E%
