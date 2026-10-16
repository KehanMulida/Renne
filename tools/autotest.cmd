@echo off
REM ---------------------------------------------------------------
REM  autotest.cmd - wrapper for autotest.ps1
REM
REM  1) Windows defaults to ExecutionPolicy=Restricted, which blocks
REM     running .ps1 files directly. This wrapper bypasses it for
REM     this single invocation only - no permanent machine change.
REM  2) The console defaults to the OEM code page (936 on zh-CN),
REM     which garbles the script's UTF-8 Chinese output. Switch to
REM     65001 for the duration of the call, then restore it.
REM
REM  Usage:  Tools\autotest.cmd run -Turns 10 -Scene Test
REM          Tools\autotest.cmd errors
REM ---------------------------------------------------------------
setlocal

for /f "tokens=2 delims=:" %%a in ('chcp') do set "_OLDCP=%%a"
chcp 65001 >nul

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0autotest.ps1" %*
set "_RC=%ERRORLEVEL%"

if defined _OLDCP chcp %_OLDCP% >nul
exit /b %_RC%
