@echo off
setlocal EnableDelayedExpansion
rem ========================================================================
rem  oc.cmd — ZERO-MANUAL-STEP OpenCode launcher for OET Prep Platform
rem ========================================================================
rem
rem  What this does (in order):
rem    1. Disables oh-my-opencode telemetry.
rem    2. Prepends project-local binaries (ast-grep, comment-checker) to PATH.
rem    3. cd's to the project root so relative paths in configs resolve.
rem    4. Launches `opencode`, forwarding every argument you passed.
rem
rem  Usage:
rem    oc                 # launch OpenCode TUI
rem    oc run "fix bug"   # any opencode subcommand just works
rem
rem  Why this exists:
rem    The old workflow required you to remember `.opencode\activate.cmd`
rem    BEFORE every `opencode` run. Forgetting it silently disabled
rem    ast-grep/comment-checker and re-enabled telemetry. This wrapper
rem    removes that footgun — type `oc` and everything is primed.
rem ========================================================================

rem --- Resolve project root (the directory that CONTAINS .opencode) ---
set "OC_SCRIPT_DIR=%~dp0"
rem Strip trailing backslash
if "%OC_SCRIPT_DIR:~-1%"=="\" set "OC_SCRIPT_DIR=%OC_SCRIPT_DIR:~0,-1%"
rem Project root = parent of .opencode
for %%I in ("%OC_SCRIPT_DIR%\..") do set "OC_PROJECT_ROOT=%%~fI"

rem --- 1. Telemetry off ---
set "OMO_SEND_ANONYMOUS_TELEMETRY=0"
set "OMO_DISABLE_POSTHOG=1"

rem --- 2. Prepend project-local tool binaries ---
set "OC_BIN_A=%OC_PROJECT_ROOT%\.opencode\node_modules\.bin"
set "OC_BIN_B=%OC_PROJECT_ROOT%\.opencode\node_modules\@code-yeongyu\comment-checker\bin"
set "PATH=%OC_BIN_A%;%OC_BIN_B%;%PATH%"

rem --- 3. Go home ---
cd /d "%OC_PROJECT_ROOT%"

rem --- 4. Launch, forwarding all args ---
echo [oc] PATH primed. telemetry=off. cwd=%CD%
opencode %*
exit /b %ERRORLEVEL%
