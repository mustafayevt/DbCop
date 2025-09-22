@echo off
REM ====================================================================
REM DbCop Installer/Launcher
REM ====================================================================
REM This script helps users get started with DbCop quickly
REM ====================================================================

title DbCop - Database Synchronization Tool

:MENU
cls
echo.
echo ====================================================================
echo   DbCop - Database Synchronization Tool
echo   Version 1.0.0 - September 2025
echo ====================================================================
echo.
echo   A powerful tool for synchronizing SQL Server databases with
echo   enhanced safety features for local vs remote connection detection.
echo.
echo ====================================================================
echo   MENU
echo ====================================================================
echo.
echo   1. Run DbCop (if installed locally)
echo   2. Check System Requirements  
echo   3. Installation Help
echo   4. View Documentation
echo   5. Exit
echo.
set /p choice="Select an option (1-5): "

if "%choice%"=="1" goto RUN
if "%choice%"=="2" goto REQUIREMENTS
if "%choice%"=="3" goto INSTALL_HELP  
if "%choice%"=="4" goto DOCS
if "%choice%"=="5" goto EXIT
goto MENU

:RUN
cls
echo.
echo ====================================================================
echo   Running DbCop...
echo ====================================================================
echo.

REM Try to find and run DbCop.exe
if exist "DbCop.exe" (
    echo Starting DbCop from current directory...
    start "" "DbCop.exe"
    goto SUCCESS
) 

if exist "bin\Publish\SingleExecutable\DbCop.exe" (
    echo Starting DbCop from build directory...
    start "" "bin\Publish\SingleExecutable\DbCop.exe"
    goto SUCCESS
)

echo ❌ DbCop.exe not found in current directory.
echo.
echo Please ensure you have:
echo   1. Downloaded DbCop.exe to this folder, OR
echo   2. Built the application using deploy.bat
echo.
pause
goto MENU

:SUCCESS
echo ✅ DbCop started successfully!
echo.
echo You can now:
echo   • Add database connections
echo   • Analyze connection types (local vs remote)
echo   • Sync databases safely with enhanced warnings
echo.
pause
goto MENU

:REQUIREMENTS
cls
echo.
echo ====================================================================
echo   System Requirements
echo ====================================================================
echo.
echo ✅ SINGLE EXECUTABLE VERSION (Recommended):
echo    • Windows 10 (1607+) / Windows 11 / Windows Server 2016+
echo    • x64 (64-bit) architecture
echo    • 512 MB RAM minimum, 1 GB recommended  
echo    • 200 MB free disk space
echo    • NO additional software required
echo.
echo ✅ FRAMEWORK-DEPENDENT VERSION:
echo    • Same as above, PLUS:
echo    • .NET 6 Desktop Runtime
echo    • Download: https://dotnet.microsoft.com/download/dotnet/6.0
echo.
echo 🔍 CHECKING YOUR SYSTEM:
systeminfo | findstr /i "OS Name"
systeminfo | findstr /i "System Type"  
echo.
echo 🔍 Checking for .NET 6:
dotnet --version 2>nul && echo ✅ .NET is installed || echo ❌ .NET not found (only needed for framework-dependent version)
echo.
pause
goto MENU

:INSTALL_HELP
cls  
echo.
echo ====================================================================
echo   Installation Help
echo ====================================================================
echo.
echo 🚀 EASY INSTALLATION (Recommended):
echo.
echo   1. Download the Single Executable version (DbCop.exe)
echo   2. Save it to any folder (e.g., Desktop, C:\Tools\)
echo   3. Double-click DbCop.exe to run
echo   4. That's it! No installation required.
echo.
echo 📦 ALTERNATIVE INSTALLATIONS:
echo.
echo   Framework-Dependent (smaller file):
echo   1. Install .NET 6 Desktop Runtime first:
echo      https://dotnet.microsoft.com/download/dotnet/6.0
echo   2. Download DbCop.exe (framework-dependent version)
echo   3. Run it
echo.
echo   From Source Code:
echo   1. Install .NET 6 SDK
echo   2. Run: git clone [repository]
echo   3. Run: deploy.bat
echo   4. Use: bin\Publish\SingleExecutable\DbCop.exe
echo.
echo 🛡️  SECURITY NOTE:
echo   DbCop includes enhanced safety features to prevent accidental
echo   modifications to remote databases. Always verify your target!
echo.
pause
goto MENU

:DOCS
cls
echo.
echo ====================================================================
echo   Documentation
echo ====================================================================
echo.
echo 📖 AVAILABLE DOCUMENTATION:
echo.
if exist "README.md" echo   • README.md - General information
if exist "DEPLOYMENT.md" echo   • DEPLOYMENT.md - Deployment guide  
echo   • Built-in help in the application
echo   • Connection analysis tool (🔍 Analyze Target button)
echo.
echo 🔧 KEY FEATURES:
echo.
echo   • Multi-mode database synchronization:
echo     - BACPAC Full Copy (complete replacement)
echo     - DACPAC Safe Schema (preserve data)  
echo     - DACPAC Force Schema (allow data loss)
echo.
echo   • Enhanced Safety Features:
echo     - Local vs Remote connection detection
echo     - IP address analysis (private ranges)
echo     - Network interface scanning
echo     - DNS resolution validation
echo     - Comprehensive warning system
echo.
echo   • Connection Management:
echo     - Save encrypted database connections
echo     - Test connections before use
echo     - Analyze connection types
echo.
echo   • Detailed Logging:
echo     - All operations logged
echo     - Connection analysis reports
echo     - SqlPackage.exe auto-detection
echo.
echo 💡 TIP: Use the "🔍 Analyze Target" button in the app to see
echo          detailed connection analysis and safety information.
echo.
pause
goto MENU

:EXIT
cls
echo.
echo ====================================================================
echo   Thanks for using DbCop!
echo ====================================================================
echo.
echo 🛡️  Remember: Always verify your target connections and backup
echo     your databases before performing sync operations.
echo.
echo 📧 For support, check the project documentation or repository.
echo.
echo Have a great day! 🚀
echo.
timeout /t 3 >nul
exit

:ERROR
echo ❌ An error occurred. Please check the logs and try again.
pause
goto MENU