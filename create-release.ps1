# ====================================================================
# DbCop - GitHub Release Builder
# ====================================================================
# This script creates a complete release package for GitHub
# ====================================================================

param(
    [string]$Version = "v1.0.0",
    [string]$OutputDir = "release"
)

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  DbCop - GitHub Release Builder" -ForegroundColor Cyan  
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

# Clean and create release directory
Write-Host "[1/6] Preparing release directory..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Path "$OutputDir/assets" | Out-Null

# Clean previous builds
Write-Host "[2/6] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "bin\Publish") {
    Remove-Item -Recurse -Force "bin\Publish"
}
dotnet clean -c Release | Out-Null

# Build all versions
Write-Host "[3/6] Building release versions..." -ForegroundColor Yellow

Write-Host "      Building Single Executable (Primary Release)..." -ForegroundColor Gray
$result = dotnet publish -p:PublishProfile=SingleExecutable-Win64 -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "      ❌ Failed to build single executable" -ForegroundColor Red
    exit 1
}

Write-Host "      Building Framework-Dependent version..." -ForegroundColor Gray  
$result = dotnet publish -p:PublishProfile=FrameworkDependent-Win64 -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "      ❌ Failed to build framework-dependent version" -ForegroundColor Red
    exit 1
}

Write-Host "      Building Portable version..." -ForegroundColor Gray
$result = dotnet publish -p:PublishProfile=Portable-Win64 -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "      ❌ Failed to build portable version" -ForegroundColor Red
    exit 1
}

# Copy primary release (single executable)
Write-Host "[4/6] Packaging primary release..." -ForegroundColor Yellow
$primaryFile = "bin\Publish\SingleExecutable\DbCop.exe"
if (Test-Path $primaryFile) {
    Copy-Item $primaryFile "$OutputDir/DbCop-$Version-win-x64.exe"
    $size = (Get-Item $primaryFile).Length
    Write-Host "      ✅ Primary release: DbCop-$Version-win-x64.exe ($([math]::Round($size/1MB,1)) MB)" -ForegroundColor Green
} else {
    Write-Host "      ❌ Primary release file not found" -ForegroundColor Red
    exit 1
}

# Copy alternative versions  
Write-Host "[5/6] Packaging alternative versions..." -ForegroundColor Yellow

# Framework-dependent version
$frameworkFile = "bin\Publish\FrameworkDependent\DbCop.exe"
if (Test-Path $frameworkFile) {
    Copy-Item $frameworkFile "$OutputDir/assets/DbCop-$Version-framework-dependent-win-x64.exe"
    $size = (Get-Item $frameworkFile).Length
    Write-Host "      ✅ Framework-dependent: DbCop-$Version-framework-dependent-win-x64.exe ($([math]::Round($size/1MB,1)) MB)" -ForegroundColor Green
}

# Portable version (zip it)
if (Test-Path "bin\Publish\Portable") {
    $portableZip = "$OutputDir/assets/DbCop-$Version-portable-win-x64.zip"
    Compress-Archive -Path "bin\Publish\Portable\*" -DestinationPath $portableZip -Force
    $size = (Get-Item $portableZip).Length
    Write-Host "      ✅ Portable: DbCop-$Version-portable-win-x64.zip ($([math]::Round($size/1MB,1)) MB)" -ForegroundColor Green
}

# Create documentation files for release
Write-Host "[6/6] Creating release documentation..." -ForegroundColor Yellow

# Release notes
$releaseNotes = @"
# 🚀 DbCop $Version - Database Synchronization Tool

## 🎯 **Quick Start**

**For most users:** Download ``DbCop-$Version-win-x64.exe`` - No installation required, just run it!

## 📦 **What's Included**

### Primary Release (Recommended)
- **DbCop-$Version-win-x64.exe** - Self-contained executable (~71 MB)
  - ✅ No dependencies required
  - ✅ Works on any Windows x64 machine  
  - ✅ Enhanced local/remote connection detection
  - ✅ Comprehensive safety warnings

### Alternative Downloads (Assets section)
- **Framework-Dependent** (~6 MB) - Requires .NET 6 Desktop Runtime
- **Portable ZIP** (~71 MB) - Extract and run, includes all files

## 🔧 **Key Features**

### 🛡️ Enhanced Safety Features
- **Smart Connection Detection**: Automatically detects local vs remote servers
- **IP Address Analysis**: Recognizes private network ranges (192.168.x.x, 10.x.x.x, etc.)
- **Network Interface Scanning**: Validates against actual local network adapters
- **DNS Resolution**: Resolves hostnames to detect local addresses
- **Multi-Level Warnings**: Prevents accidental remote database modifications

### 🔄 Database Synchronization Modes
- **BACPAC Full Copy**: Complete database replacement with schema + data
- **DACPAC Safe Schema**: Preserve data while updating schema safely
- **DACPAC Force Schema**: Allow data loss to force exact schema matching

### 🔐 Connection Management
- **Encrypted Credentials**: Secure storage of database connections
- **Connection Testing**: Verify connections before use
- **Connection Analysis**: Detailed analysis of why connections are classified as local/remote

### 📊 Advanced Features
- **SqlPackage.exe Auto-Detection**: Automatically finds installed SqlPackage tools
- **Comprehensive Logging**: All operations logged with detailed information
- **Real-Time Progress**: Live progress tracking for sync operations
- **Database Auto-Discovery**: Load databases from servers automatically

## 💻 **System Requirements**

- **OS**: Windows 10 (1607+), Windows 11, or Windows Server 2016+
- **Architecture**: x64 (64-bit)
- **RAM**: 512 MB minimum, 1 GB recommended
- **Disk Space**: 200 MB free space
- **Dependencies**: None (for primary release)

## 🚀 **Installation**

### Single Executable (Recommended)
1. Download ``DbCop-$Version-win-x64.exe``
2. Save to any folder (Desktop, C:\Tools\, etc.)
3. Double-click to run - That's it!

### Framework-Dependent (Smaller)
1. Install [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)
2. Download ``DbCop-$Version-framework-dependent-win-x64.exe``
3. Run the executable

## ⚠️ **Important Safety Notes**

- **Always verify target connections** before syncing
- **Use the "🔍 Analyze Target" button** to understand connection classification
- **Create backups** before performing any sync operations
- **Test on non-production databases** first
- **Review sync mode carefully** - Full Copy replaces everything!

## 🔍 **Connection Analysis Example**

DbCop will automatically detect and warn about remote connections:

- ✅ **Local**: ``localhost\SQLEXPRESS``, ``.\SQL2019``, ``192.168.1.100``
- ⚠️ **Remote**: ``prod-server.company.com``, ``10.5.1.100`` (if not local), public IPs

## 📖 **Documentation**

- Built-in help and tooltips throughout the application
- Connection analysis reports with detailed explanations
- Comprehensive logging for troubleshooting

## 🐛 **Support**

If you encounter issues:
1. Check the built-in log console for detailed error information  
2. Use the "🔍 Analyze Target" button for connection troubleshooting
3. Review the comprehensive logs generated by the application
4. Create an issue on GitHub with log details

## 🏷️ **Version $Version Changes**

- ✨ Enhanced local/remote connection detection with multi-layer validation
- 🛡️ Advanced safety warnings for remote database operations  
- 🔍 New connection analysis tool with detailed reporting
- 📦 Optimized single executable with embedded dependencies
- 🚀 Improved deployment options and documentation
- 🔧 Better SqlPackage.exe auto-detection across multiple installation types
- 💾 Secure connection credential storage with encryption

---

**Happy Database Syncing! 🎉**

*Remember: When in doubt about a connection, DbCop errs on the side of safety!*
"@

Set-Content -Path "$OutputDir/RELEASE-NOTES.md" -Value $releaseNotes -Encoding UTF8

# Create checksums for verification
Write-Host "      Creating checksums..." -ForegroundColor Gray
$checksums = @()
Get-ChildItem $OutputDir -Recurse -File | ForEach-Object {
    $hash = Get-FileHash $_.FullName -Algorithm SHA256
    $relativePath = $_.FullName.Replace((Get-Location).Path + "\$OutputDir\", "")
    $checksums += "$($hash.Hash)  $relativePath"
}
Set-Content -Path "$OutputDir/SHA256SUMS.txt" -Value $checksums -Encoding UTF8

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  Release Package Created Successfully!" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "📦 Release Contents:" -ForegroundColor White
Write-Host ""
Get-ChildItem $OutputDir -Recurse | ForEach-Object {
    if ($_.PSIsContainer) {
        Write-Host "📁 $($_.Name)/" -ForegroundColor Yellow
    } else {
        $size = if ($_.Length -gt 1MB) { "$([math]::Round($_.Length/1MB,1)) MB" } else { "$([math]::Round($_.Length/1KB,1)) KB" }
        if ($_.Name -like "*.exe") {
            Write-Host "🚀 $($_.Name) ($size)" -ForegroundColor Green
        } elseif ($_.Name -like "*.zip") {
            Write-Host "📦 $($_.Name) ($size)" -ForegroundColor Blue  
        } else {
            Write-Host "📄 $($_.Name) ($size)" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "🎯 **Main Release File:** DbCop-$Version-win-x64.exe" -ForegroundColor Green
Write-Host "📂 **Location:** $(Get-Location)\$OutputDir\" -ForegroundColor Gray
Write-Host ""
Write-Host "🔥 **Ready for GitHub Release Upload!** 🔥" -ForegroundColor Cyan

# Offer to open the release directory
Write-Host ""
$choice = Read-Host "Open release directory? (Y/N)"
if ($choice -eq "Y" -or $choice -eq "y") {
    Start-Process "explorer" -ArgumentList "$OutputDir"
}

Write-Host ""
Write-Host "📋 **GitHub Release Instructions:**" -ForegroundColor Yellow
Write-Host "1. Go to your GitHub repository" -ForegroundColor White
Write-Host "2. Click 'Releases' → 'Create a new release'" -ForegroundColor White  
Write-Host "3. Tag: $Version" -ForegroundColor White
Write-Host "4. Title: DbCop $Version - Database Synchronization Tool" -ForegroundColor White
Write-Host "5. Copy/paste content from RELEASE-NOTES.md" -ForegroundColor White
Write-Host "6. Upload DbCop-$Version-win-x64.exe as the main asset" -ForegroundColor White
Write-Host "7. Upload files from assets/ folder as additional assets" -ForegroundColor White
Write-Host "8. Check 'Set as latest release' and publish!" -ForegroundColor White
Write-Host ""