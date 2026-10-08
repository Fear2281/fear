@echo off
cd /d "%~dp0"
set ASPNETCORE_ENVIRONMENT=Development
start "" /b powershell.exe -NoProfile -Command "$url='http://127.0.0.1:5088'; for($i=0;$i -lt 60;$i++){try{if((Invoke-RestMethod ($url+'/api/health') -TimeoutSec 2).application -eq 'Encore'){Start-Process $url;exit}}catch{};Start-Sleep -Seconds 1};Write-Host 'Open http://127.0.0.1:5088 in your browser.'"
echo Encore is starting. Keep this window open while using the site.
echo Close this window to stop Encore.
Encore.exe --urls http://127.0.0.1:5088
pause
