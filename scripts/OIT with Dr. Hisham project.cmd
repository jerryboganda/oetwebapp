@echo off
set "SCRIPT=C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\scripts\one-click-local-deploy.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"%SCRIPT%\"'"
