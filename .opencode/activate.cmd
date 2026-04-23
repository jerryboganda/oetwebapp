@echo off
rem ========================================================================
rem  OET Prep Platform — OpenCode / oh-my-opencode activation script
rem ========================================================================
rem
rem  Run this ONCE per shell before `opencode` to put the project-local
rem  oh-my-opencode CLI, ast-grep, and comment-checker binaries on PATH.
rem
rem  Usage (cmd.exe):
rem      .opencode\activate.cmd
rem      opencode
rem
rem  Usage (PowerShell):
rem      & ".opencode\activate.cmd"    # or use the .ps1 variant below
rem
rem  Telemetry is disabled here too so nothing phones home.
rem ========================================================================

set "OMO_SEND_ANONYMOUS_TELEMETRY=0"
set "OMO_DISABLE_POSTHOG=1"

rem Put project-local binaries ahead of everything else.
set "PATH=%~dp0node_modules\.bin;%~dp0node_modules\@code-yeongyu\comment-checker\bin;%PATH%"

echo [omo] activate.cmd: PATH primed.
echo [omo]   + .opencode\node_modules\.bin               (ast-grep, comment-checker)
echo [omo]   + .opencode\node_modules\@code-yeongyu\comment-checker\bin
echo [omo] OMO_SEND_ANONYMOUS_TELEMETRY=0  OMO_DISABLE_POSTHOG=1
echo [omo] You can now run: opencode
