@echo off
chcp 65001 >nul
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
set PY=
if exist "C:\ProgramData\anaconda3\python.exe" set PY="C:\ProgramData\anaconda3\python.exe"
if not defined PY if exist "%USERPROFILE%\anaconda3\python.exe" set PY="%USERPROFILE%\anaconda3\python.exe"
if not defined PY if exist "%LOCALAPPDATA%\anaconda3\python.exe" set PY="%LOCALAPPDATA%\anaconda3\python.exe"
if not defined PY for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do if exist "%%D\python.exe" set PY="%%D\python.exe"
if not defined PY where py >nul 2>nul && set PY=py
echo START %DATE% %TIME% > run_log.txt
if not defined PY (echo NO_PYTHON >> run_log.txt & goto end)
echo PYTHON %PY% >> run_log.txt
%PY% --version >> run_log.txt 2>&1
%PY% run_jobs.py jobs_e2.json responses_e2 --auto >> run_log.txt 2>&1
%PY% run_jobs.py jobs_e3_02.json responses_e3_02 --auto >> run_log.txt 2>&1
%PY% run_jobs.py jobs_e3_03.json responses_e3_03 --auto >> run_log.txt 2>&1
:end
echo ALL_DONE %DATE% %TIME% >> run_log.txt
