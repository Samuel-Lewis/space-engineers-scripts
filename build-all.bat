@echo off
setlocal

pushd "%~dp0"

call :build "Scripts\Tagger\Tagger\Tagger.csproj" || goto :failed
call :build "Scripts\LaunchControl\LaunchControl\LaunchControl.csproj" || goto :failed
call :build "Scripts\FleetTelemetry\FleetTelemetry\FleetTelemetry.csproj" || goto :failed

echo.
echo All solutions built successfully.
popd
exit /b 0

:build
echo.
echo Building %~1...
dotnet build "%~1" -c Release
exit /b %errorlevel%

:failed
set "build_exit_code=%errorlevel%"
echo.
echo Build failed with exit code %build_exit_code%.
popd
exit /b %build_exit_code%
