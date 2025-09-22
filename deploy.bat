@echo off
REM ====================================================================
REM DbCop - Database Synchronization Tool - Deployment Script
REM ====================================================================
REM
REM This script builds and publishes DbCop in multiple deployment formats
REM 
REM Created: September 22, 2025
REM ====================================================================

echo.
echo ====================================================================
echo   DbCop - Database Synchronization Tool - Deployment Script
echo ====================================================================
echo.

REM Clean previous builds
echo [1/4] Cleaning previous builds...
if exist "bin\Publish" rmdir /S /Q "bin\Publish"
dotnet clean -c Release

echo.
echo [2/4] Building Single Executable (Self-Contained)...
echo       - Target: Windows x64
echo       - Runtime: Included (.NET 6)  
echo       - File: Single EXE (~159 MB)
echo       - Pros: No dependencies, fully portable
echo       - Cons: Large file size
dotnet publish -p:PublishProfile=SingleExecutable-Win64 -c Release

echo.
echo [3/4] Building Framework-Dependent Version...
echo       - Target: Windows x64
echo       - Runtime: Requires .NET 6 Desktop Runtime
echo       - File: Small EXE (~6 MB)
echo       - Pros: Small file size
echo       - Cons: Requires .NET 6 Desktop Runtime on target
dotnet publish -p:PublishProfile=FrameworkDependent-Win64 -c Release

echo.
echo [4/4] Building Portable Version...
echo       - Target: Windows x64
echo       - Runtime: Included (.NET 6)
echo       - Files: Multiple files in folder
echo       - Pros: Can see all dependencies
echo       - Cons: Multiple files to distribute
dotnet publish -p:PublishProfile=Portable-Win64 -c Release

echo.
echo ====================================================================
echo   Deployment Complete!
echo ====================================================================
echo.
echo Available deployments:
echo.
echo 1. SINGLE EXECUTABLE (Recommended for distribution):
echo    Location: bin\Publish\SingleExecutable\DbCop.exe
echo    Size: ~159 MB
echo    Requirements: None (fully self-contained)
echo    Usage: Copy DbCop.exe to any Windows x64 machine and run
echo.
echo 2. FRAMEWORK-DEPENDENT (Smallest size):
echo    Location: bin\Publish\FrameworkDependent\DbCop.exe  
echo    Size: ~6 MB
echo    Requirements: .NET 6 Desktop Runtime
echo    Download: https://dotnet.microsoft.com/download/dotnet/6.0
echo.
echo 3. PORTABLE (Developer-friendly):
echo    Location: bin\Publish\Portable\
echo    Requirements: None (runtime included in folder)
echo    Usage: Copy entire folder and run DbCop.exe
echo.
echo TIP: For most users, use option 1 (Single Executable)
echo.
pause