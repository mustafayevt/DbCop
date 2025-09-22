# 🚀 DbCop Deployment Guide

## Quick Start

### Option 1: Single Executable (Recommended) ⭐
**Best for: End users, simple distribution**

1. Run the deployment script:
   ```bash
   deploy.bat
   ```
   OR
   ```powershell
   .\deploy.ps1
   ```

2. Find your executable at: `bin\Publish\SingleExecutable\DbCop.exe`

3. **That's it!** Copy `DbCop.exe` to any Windows x64 machine and run it.
   - ✅ No installation required
   - ✅ No dependencies needed  
   - ✅ Fully portable
   - ⚠️  File size: ~159 MB

### Option 2: Framework-Dependent (Smallest)
**Best for: Environments with .NET 6 already installed**

1. Use the framework-dependent build: `bin\Publish\FrameworkDependent\DbCop.exe`
2. Target machine must have **.NET 6 Desktop Runtime** installed
3. Download runtime: https://dotnet.microsoft.com/download/dotnet/6.0
   - ✅ Small file size: ~6 MB
   - ⚠️  Requires .NET 6 Desktop Runtime

## Manual Building

If you prefer to build manually:

### Single Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

### Framework-Dependent  
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

### Portable (Multiple Files)
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## 📊 Deployment Comparison

| Type | Size | Requirements | Distribution | Use Case |
|------|------|-------------|-------------|----------|
| **Single Executable** | ~159 MB | None | Copy 1 file | 🎯 **Recommended** - End users |  
| **Framework-Dependent** | ~6 MB | .NET 6 Runtime | Copy 1 file | Controlled environments |
| **Portable** | ~159 MB | None | Copy folder | Development/Testing |

## 🛠️ Advanced Options

### Custom Runtime Targets
You can build for different Windows architectures:

```bash
# Windows x64 (default)
dotnet publish -r win-x64

# Windows x86 (32-bit)  
dotnet publish -r win-x86

# Windows ARM64
dotnet publish -r win-arm64
```

### Trimming (Experimental)
To reduce file size further, you can enable trimming (may cause issues):

```bash
dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

⚠️ **Warning:** Trimming may break WPF applications. Test thoroughly!

## 📂 Output Locations

After running `deploy.bat` or `deploy.ps1`:

```
bin/Publish/
├── SingleExecutable/
│   └── DbCop.exe                 # ⭐ Main distribution file
├── FrameworkDependent/  
│   └── DbCop.exe                 # Requires .NET 6 Runtime
└── Portable/
    ├── DbCop.exe                 # Main executable
    ├── *.dll                     # Dependencies
    └── runtimes/                 # Runtime components
```

## 🔧 Troubleshooting

### "Application failed to start"
- **Single Executable:** Should work on any Windows x64 machine
- **Framework-Dependent:** Install .NET 6 Desktop Runtime

### Large File Size
- Use **Framework-Dependent** version if .NET 6 is available
- Consider **Portable** version for development scenarios

### Antivirus Issues
- Some antivirus software may flag self-contained executables
- Add exception for DbCop.exe if needed
- This is common with .NET single-file deployments

## 📋 System Requirements

- **Operating System:** Windows 10 (1607+) / Windows 11 / Windows Server 2016+
- **Architecture:** x64 (64-bit)
- **Memory:** 512 MB RAM minimum, 1 GB recommended
- **Disk Space:** 200 MB free space
- **.NET Runtime:** Not required for Single Executable, .NET 6 Desktop Runtime for Framework-Dependent

## 🚀 Distribution Tips

1. **For internal company use:** Single Executable is ideal
2. **For software distribution:** Consider code signing the executable
3. **For enterprise deployment:** Use Group Policy or SCCM with Single Executable
4. **For developers:** Use Portable version to see all dependencies

---

💡 **Recommended:** Use the **Single Executable** version for the best user experience!