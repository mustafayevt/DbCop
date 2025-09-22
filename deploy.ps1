# ====================================================================
# DbCop - Database Synchronization Tool - Deployment Script (PowerShell)
# ====================================================================
#
# This script builds and publishes DbCop in multiple deployment formats
# 
# Created: September 22, 2025
# ====================================================================

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  DbCop - Database Synchronization Tool - Deployment Script" -ForegroundColor Cyan  
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "bin\Publish") {
    Remove-Item -Recurse -Force "bin\Publish"
}
dotnet clean -c Release | Out-Null

Write-Host ""
Write-Host "[2/4] Building Single Executable (Self-Contained)..." -ForegroundColor Yellow
Write-Host "      - Target: Windows x64" -ForegroundColor Gray
Write-Host "      - Runtime: Included (.NET 6)" -ForegroundColor Gray  
Write-Host "      - File: Single EXE (~159 MB)" -ForegroundColor Gray
Write-Host "      - Pros: No dependencies, fully portable" -ForegroundColor Green
Write-Host "      - Cons: Large file size" -ForegroundColor Red

$result1 = dotnet publish -p:PublishProfile=SingleExecutable-Win64 -c Release 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "      ✅ Single executable created successfully" -ForegroundColor Green
    $size1 = (Get-Item "bin\Publish\SingleExecutable\DbCop.exe").Length
    Write-Host "      📁 Size: $([math]::Round($size1/1MB,2)) MB" -ForegroundColor Gray
} else {
    Write-Host "      ❌ Failed to create single executable" -ForegroundColor Red
}

Write-Host ""
Write-Host "[3/4] Building Framework-Dependent Version..." -ForegroundColor Yellow
Write-Host "      - Target: Windows x64" -ForegroundColor Gray
Write-Host "      - Runtime: Requires .NET 6 Desktop Runtime" -ForegroundColor Gray
Write-Host "      - File: Small EXE (~6 MB)" -ForegroundColor Gray
Write-Host "      - Pros: Small file size" -ForegroundColor Green
Write-Host "      - Cons: Requires .NET 6 Desktop Runtime on target" -ForegroundColor Red

$result2 = dotnet publish -p:PublishProfile=FrameworkDependent-Win64 -c Release 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "      ✅ Framework-dependent version created successfully" -ForegroundColor Green
    $size2 = (Get-Item "bin\Publish\FrameworkDependent\DbCop.exe").Length
    Write-Host "      📁 Size: $([math]::Round($size2/1MB,2)) MB" -ForegroundColor Gray
} else {
    Write-Host "      ❌ Failed to create framework-dependent version" -ForegroundColor Red
}

Write-Host ""
Write-Host "[4/4] Building Portable Version..." -ForegroundColor Yellow
Write-Host "      - Target: Windows x64" -ForegroundColor Gray
Write-Host "      - Runtime: Included (.NET 6)" -ForegroundColor Gray
Write-Host "      - Files: Multiple files in folder" -ForegroundColor Gray
Write-Host "      - Pros: Can see all dependencies" -ForegroundColor Green
Write-Host "      - Cons: Multiple files to distribute" -ForegroundColor Red

$result3 = dotnet publish -p:PublishProfile=Portable-Win64 -c Release 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "      ✅ Portable version created successfully" -ForegroundColor Green
    $folderSize = (Get-ChildItem "bin\Publish\Portable\" -Recurse | Measure-Object -Property Length -Sum).Sum
    Write-Host "      📁 Total size: $([math]::Round($folderSize/1MB,2)) MB" -ForegroundColor Gray
} else {
    Write-Host "      ❌ Failed to create portable version" -ForegroundColor Red
}

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  Deployment Complete!" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "📦 Available deployments:" -ForegroundColor White
Write-Host ""

Write-Host "1. 🚀 SINGLE EXECUTABLE (Recommended for distribution):" -ForegroundColor Green
Write-Host "   📍 Location: bin\Publish\SingleExecutable\DbCop.exe" -ForegroundColor Gray
if (Test-Path "bin\Publish\SingleExecutable\DbCop.exe") {
    $size = (Get-Item "bin\Publish\SingleExecutable\DbCop.exe").Length
    Write-Host "   📊 Size: $([math]::Round($size/1MB,2)) MB" -ForegroundColor Gray
}
Write-Host "   ✅ Requirements: None (fully self-contained)" -ForegroundColor Green
Write-Host "   🎯 Usage: Copy DbCop.exe to any Windows x64 machine and run" -ForegroundColor Gray
Write-Host ""

Write-Host "2. 📦 FRAMEWORK-DEPENDENT (Smallest size):" -ForegroundColor Blue  
Write-Host "   📍 Location: bin\Publish\FrameworkDependent\DbCop.exe" -ForegroundColor Gray
if (Test-Path "bin\Publish\FrameworkDependent\DbCop.exe") {
    $size = (Get-Item "bin\Publish\FrameworkDependent\DbCop.exe").Length
    Write-Host "   📊 Size: $([math]::Round($size/1MB,2)) MB" -ForegroundColor Gray
}
Write-Host "   ⚠️  Requirements: .NET 6 Desktop Runtime" -ForegroundColor Yellow
Write-Host "   🔗 Download: https://dotnet.microsoft.com/download/dotnet/6.0" -ForegroundColor Gray
Write-Host ""

Write-Host "3. 📂 PORTABLE (Developer-friendly):" -ForegroundColor Magenta
Write-Host "   📍 Location: bin\Publish\Portable\" -ForegroundColor Gray
if (Test-Path "bin\Publish\Portable\") {
    $folderSize = (Get-ChildItem "bin\Publish\Portable\" -Recurse | Measure-Object -Property Length -Sum).Sum
    Write-Host "   📊 Total size: $([math]::Round($folderSize/1MB,2)) MB" -ForegroundColor Gray
}
Write-Host "   ✅ Requirements: None (runtime included in folder)" -ForegroundColor Green
Write-Host "   🎯 Usage: Copy entire folder and run DbCop.exe" -ForegroundColor Gray
Write-Host ""

Write-Host "💡 TIP: For most users, use option 1 (Single Executable)" -ForegroundColor Cyan
Write-Host ""

# Offer to open the publish directory
$choice = Read-Host "Would you like to open the publish directory? (Y/N)"
if ($choice -eq "Y" -or $choice -eq "y") {
    if (Test-Path "bin\Publish") {
        Start-Process "explorer" -ArgumentList "bin\Publish"
    }
}

Write-Host ""
Write-Host "🎉 Deployment script completed!" -ForegroundColor Green