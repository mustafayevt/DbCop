# 🛡️ DbCop - Database Synchronization Tool

**DbCop** is a powerful Windows desktop application for synchronizing SQL Server databases with enhanced safety features and intelligent connection detection. Built with WPF and .NET 6, it provides a user-friendly interface for database operations while prioritizing data protection through comprehensive safety mechanisms.

## 🌟 Key Features

### 🔄 **Advanced Database Synchronization**
- **Three Sync Modes** for different use cases:
  - **📦 BACPAC Full Copy**: Complete database replacement (schema + data)
  - **📋 DACPAC Safe Schema**: Preserve data while updating schema safely  
  - **⚡ DACPAC Force Schema**: Force schema matching (allows data loss)
- **Real-Time Progress Tracking** with detailed logging
- **Cancellable Operations** with proper cleanup
- **Pre-Flight Connection Testing** before sync operations

### 🛡️ **Enhanced Safety Features**
- **🧠 Smart Connection Detection**: Multi-layer local vs remote analysis
- **🔍 IP Address Analysis**: Recognizes private network ranges (192.168.x.x, 10.x.x.x, etc.)
- **🌐 Network Interface Scanning**: Validates against actual local network adapters
- **📡 DNS Resolution**: Hostname to IP resolution for comprehensive detection
- **⚠️ Multi-Level Warnings**: Prevents accidental remote database modifications
- **🔍 Connection Analysis Tool**: Detailed reports on why connections are classified as local/remote

### 🔐 **Secure Connection Management**
- **Encrypted Credential Storage**: Secure storage of database connections
- **Multiple Authentication Methods**: Windows Authentication or SQL Server credentials
- **Connection Testing**: Verify connections before operations
- **Saved Connection Profiles**: Reusable connection configurations

### 📊 **Advanced Features**
- **🔧 SqlPackage.exe Auto-Detection**: Automatically finds installed SqlPackage tools
- **📋 Comprehensive Logging**: All operations logged with detailed information
- **📁 Database Auto-Discovery**: Load available databases from servers
- **🎯 Smart Database Matching**: Automatic source-to-target name matching
- **🔄 Background Operations**: Non-blocking UI during long operations

## 🚀 Quick Start

### Option 1: Download Pre-Built Release (Recommended)
1. **Download** the latest release from [GitHub Releases](https://github.com/mustafayevt/DbCop/releases)
2. **Run** `DbCop-v1.0.0-win-x64.exe` - No installation required!
3. **Start syncing** databases with enhanced safety

### Option 2: Build from Source
1. **Clone the repository**:
   ```bash
   git clone https://github.com/mustafayevt/DbCop.git
   cd DbCop
   ```

2. **Build and run**:
   ```bash
   # Quick run for development
   dotnet run
   
   # Or build a release
   .\deploy.bat
   ```

## 💻 System Requirements

- **Operating System**: Windows 10 (1607+), Windows 11, or Windows Server 2016+
- **Architecture**: x64 (64-bit)
- **RAM**: 512 MB minimum, 1 GB recommended
- **Disk Space**: 200 MB free space
- **.NET Runtime**: .NET 6 Desktop Runtime (or use single-file executable)
- **SqlPackage.exe**: Auto-detected from Visual Studio, SSDT, or Azure Data Studio installations

## 🔧 Usage Guide

### 1. **Setting Up Connections**
- Click **"Manage Connections"** to add database servers
- Configure authentication (Windows Auth or SQL Server credentials)
- Test connections before saving

### 2. **Selecting Sync Mode**
- **📦 Full Copy (BACPAC)**: Complete database replacement - use for dev/test environments
- **📋 Safe Schema (DACPAC)**: Schema updates while preserving data - use for production
- **⚡ Force Schema (DACPAC)**: Force exact schema match - use with caution

### 3. **Safety Verification**
- Use **🔍 "Analyze Target"** button to understand connection classification
- Review warnings for remote connections
- Verify target database name before proceeding

### 4. **Running Synchronization**
- Select source and target connections
- Choose databases (or let auto-matching work)
- Click **"Start Sync"** and monitor progress
- Review detailed logs for any issues

## 🛡️ Safety Features Explained

DbCop includes comprehensive safety mechanisms to prevent accidental data loss:

### **Connection Analysis**
```
✅ LOCAL EXAMPLES:
• localhost\SQLEXPRESS
• .\SQL2019  
• 127.0.0.1
• 192.168.1.100 (if on local network)
• DESKTOP-ABC123 (if matches local machine)

⚠️ REMOTE EXAMPLES:  
• prod-server.company.com
• 10.5.1.100 (if not local)
• Public IP addresses
• Cloud database services
```

### **Multi-Layer Detection**
1. **Hostname Analysis**: Checks for localhost, machine names, IP patterns
2. **Network Interface Scanning**: Validates IPs against actual network adapters  
3. **DNS Resolution**: Resolves hostnames to IP addresses for verification
4. **Private Range Detection**: Identifies RFC 1918 private network ranges

## 📁 Project Structure

```
DbCop/
├── 📄 MainWindow.xaml          # Main UI layout
├── 📄 MainWindow.xaml.cs       # Core application logic & safety features
├── 📄 ConnectionManagementWindow.xaml(.cs)  # Connection management UI
├── 📄 DbCop.csproj            # Project configuration with deployment settings
├── 📜 deploy.bat/.ps1         # Deployment scripts (multiple formats)
├── 📜 create-release.ps1      # GitHub release automation
├── 📜 DbCop-Launcher.bat      # User-friendly installer/launcher
├── 📖 DEPLOYMENT.md           # Comprehensive deployment guide
└── 📖 README.md               # This file
```

## 🔨 Development & Deployment

### **Available Build Scripts**
```bash
# Windows Batch - Multiple deployment formats
.\deploy.bat

# PowerShell - Enhanced output and error handling  
.\deploy.ps1

# GitHub Release - Complete release package
.\create-release.ps1 -Version "v1.0.1"
```

### **Build Outputs**
- **Single Executable**: `bin\Publish\SingleExecutable\DbCop.exe` (~71MB, no dependencies)
- **Framework-Dependent**: `bin\Publish\FrameworkDependent\DbCop.exe` (~6MB, requires .NET 6)
- **Portable**: `bin\Publish\Portable\` (folder with all files)

## ⚠️ Important Safety Notes

- **🔐 Always verify target connections** before syncing
- **🔍 Use the "Analyze Target" button** to understand connection classification  
- **💾 Create backups** before performing any sync operations
- **🧪 Test on non-production databases** first
- **📋 Review sync mode carefully** - Full Copy replaces everything!

### **Sync Mode Safety Comparison**

| Mode | Data Safety | Use Case | Risk Level |
|------|-------------|----------|------------|
| **📦 Full Copy** | ❌ Target data replaced | Development, Testing | 🔴 High |
| **📋 Safe Schema** | ✅ Data preserved | Production Updates | 🟡 Low |  
| **⚡ Force Schema** | ⚠️ Data may be lost | Schema Fixes | 🔴 High |

## 🤝 Contributing

Contributions are welcome! Here's how you can help:

1. **🐛 Report Issues**: Use GitHub Issues for bug reports
2. **💡 Suggest Features**: Share enhancement ideas
3. **🔧 Submit PRs**: Fork, develop, and submit pull requests
4. **📖 Improve Docs**: Help enhance documentation

### **Development Setup**
```bash
# Clone and setup
git clone https://github.com/mustafayevt/DbCop.git
cd DbCop

# Install dependencies (if needed)
dotnet restore

# Run in development mode
dotnet run

# Run tests (if available)
dotnet test
```

## 📝 License

This project is open source. Please check the repository for license details.

## 🆘 Support & Troubleshooting

### **Getting Help**
1. **📋 Check the built-in log console** for detailed error information
2. **🔍 Use "Analyze Target" button** for connection troubleshooting  
3. **📖 Review the comprehensive logs** generated by the application
4. **🐛 Create a GitHub issue** with log details for complex problems

### **Common Issues**
- **SqlPackage.exe not found**: Install Visual Studio, SSDT, or Azure Data Studio
- **Connection failures**: Check server names, credentials, and network connectivity
- **Large file operations**: Ensure adequate disk space for temporary BACPAC/DACPAC files
- **Permission errors**: Verify database permissions for target operations

## 🔗 Links

- **📂 Repository**: [https://github.com/mustafayevt/DbCop](https://github.com/mustafayevt/DbCop)
- **📦 Releases**: [GitHub Releases](https://github.com/mustafayevt/DbCop/releases)
- **🐛 Issues**: [GitHub Issues](https://github.com/mustafayevt/DbCop/issues)
- **📖 Documentation**: See `DEPLOYMENT.md` for detailed deployment guide

---

**🎉 Happy Database Syncing!**

*Remember: When in doubt about a connection, DbCop errs on the side of safety! 🛡️*

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For questions or support, please open an issue on GitHub.