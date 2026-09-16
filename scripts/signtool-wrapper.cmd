@echo off
REM Wrapper to find signtool.exe from Windows SDK and forward all arguments.
REM Needed because Tauri's signCommand splits by spaces and can't handle
REM paths with spaces like "Program Files (x86)".

setlocal
for /f "delims=" %%i in ('dir /b /s "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" 2^>nul ^| sort /r') do (
    set "SIGNTOOL=%%i"
    goto :found
)
echo ERROR: signtool.exe not found. Install the Windows SDK. >&2
exit /b 1

:found
"%SIGNTOOL%" %*
