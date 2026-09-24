@echo off
rem Prepare la Sandbox pour PC Sante : lance preparer-sandbox.ps1 malgre le blocage des scripts .ps1 dans la Sandbox.
rem Le script demande ensuite les droits administrateur (cliquer Oui).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0preparer-sandbox.ps1"
