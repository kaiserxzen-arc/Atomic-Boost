@echo off
:: Builds a single Atomic.exe into this folder (needs the .NET 8 SDK)
cd /d "%~dp0"
dotnet publish Atomic.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist -nologo
if %errorlevel% neq 0 ( pause & exit /b 1 )
move /y dist\Atomic.exe Atomic.exe >nul
rmdir dist
