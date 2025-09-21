using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Data;
using System.Text.Json;

namespace DbCop
{
    /// <summary>
    /// Represents a saved database connection
    /// </summary>
    public class DatabaseConnection
    {
        public string Name { get; set; } = "";
        public string Server { get; set; } = "";
        public string UserId { get; set; } = "";
        public string PasswordEncrypted { get; set; } = "";
        public bool UseWindowsAuth { get; set; } = false;

        public override string ToString() => Name;

        public string GetDecryptedPassword()
        {
            if (string.IsNullOrEmpty(PasswordEncrypted))
                return "";

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(PasswordEncrypted);
                byte[] passwordBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(passwordBytes);
            }
            catch
            {
                return "";
            }
        }

        public void SetEncryptedPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                PasswordEncrypted = "";
                return;
            }

            try
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] encryptedBytes = ProtectedData.Protect(passwordBytes, null, DataProtectionScope.CurrentUser);
                PasswordEncrypted = Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                PasswordEncrypted = "";
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool _isSyncing = false;
        private readonly string _tempDacpacPath;
        private readonly string _logFilePath;
        private readonly string _connectionsFilePath;
        private List<DatabaseConnection> _savedConnections = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            _tempDacpacPath = Path.Combine(Path.GetTempPath(), $"DatabaseSync_{Guid.NewGuid():N}.dacpac");
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"DatabaseSync_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            _connectionsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SavedConnections.json");
            
            // Set default values
            UseWindowsAuthCheckBox.IsChecked = true;
            
            // Initialize common server names
            InitializeServerLists();
            
            // Auto-detect SqlPackage on startup (async to prevent UI blocking)
            _ = FindAndSetSqlPackagePathAsync();
            
            // Load saved settings
            LoadSettings();

            // Note: Authentication UI is now managed through saved connections

            // Setup automatic database name matching
            SetupDatabaseNameMatching();

            // Initialize log file
            InitializeLogFile();

            // Load saved connections
            LoadSavedConnections();
        }

        private void SetupDatabaseNameMatching()
        {
            // When source database changes, automatically update target database
            SourceDatabaseComboBox.SelectionChanged += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(SourceDatabaseComboBox.Text))
                {
                    TargetDatabaseComboBox.Text = SourceDatabaseComboBox.Text;
                    LogMessage($"Auto-matched target database to: {SourceDatabaseComboBox.Text}", false);
                }
            };

            // Also handle text changes for when user types directly
            SourceDatabaseComboBox.LostFocus += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(SourceDatabaseComboBox.Text) && 
                    string.IsNullOrWhiteSpace(TargetDatabaseComboBox.Text))
                {
                    TargetDatabaseComboBox.Text = SourceDatabaseComboBox.Text;
                    LogMessage($"Auto-matched target database to: {SourceDatabaseComboBox.Text}", false);
                }
            };
        }

        private void InitializeServerLists()
        {
            // Note: Server initialization now handled through saved connections
        }

        #region Settings Persistence

        private void LoadSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;
                
                // Note: Individual server/credential settings replaced with saved connections
                TargetDatabaseComboBox.Text = settings.TargetDatabase ?? "";
                UseWindowsAuthCheckBox.IsChecked = settings.UseWindowsAuth;
                
                SqlPackagePathTextBox.Text = settings.SqlPackagePath ?? "";
                
                // Note: Credential saving functionality removed - using connection-based approach
                
                // Note: Password handling now managed through saved connections
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading settings: {ex.Message}", true);
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;
                
                // Note: Individual server/credential settings replaced with saved connections
                settings.TargetDatabase = TargetDatabaseComboBox.Text;
                settings.UseWindowsAuth = UseWindowsAuthCheckBox.IsChecked ?? true;
                
                settings.SqlPackagePath = SqlPackagePathTextBox.Text;
                
                // Note: Credential saving functionality removed - using connection-based approach
                settings.SaveSourceCredentials = false;
                settings.SaveTargetCredentials = false;
                
                // Note: Password encryption now handled through saved connections
                settings.SourcePasswordEncrypted = "";
                settings.TargetPasswordEncrypted = "";
                
                settings.Save();
                LogMessage("Settings saved successfully", false);
            }
            catch (Exception ex)
            {
                LogMessage($"Error saving settings: {ex.Message}", true);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveSettings();
            CleanupTempFiles();
            base.OnClosing(e);
        }

        #endregion

        #region SqlPackage Auto-Detection

        private static readonly Dictionary<string, (string Path, DateTime CacheTime)> _sqlPackageCache = new();
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(24);

        private async Task FindAndSetSqlPackagePathAsync()
        {
            try
            {
                // Check cache first
                string cacheKey = Environment.MachineName;
                if (_sqlPackageCache.TryGetValue(cacheKey, out var cached) &&
                    DateTime.Now - cached.CacheTime < _cacheExpiry &&
                    File.Exists(cached.Path))
                {
                    Dispatcher.Invoke(() =>
                    {
                        SqlPackagePathTextBox.Text = cached.Path;
                        LogMessage($"✅ Using cached SqlPackage.exe path: {cached.Path}", false);
                    });
                    return;
                }

                await Task.Run(async () =>
                {
                    Dispatcher.Invoke(() => LogMessage("🔍 Searching for SqlPackage.exe...", false));

                    // Common SqlPackage.exe locations (ordered by preference - newer versions first)
                    var commonPaths = new List<string>
                    {
                        // SQL Server 2022
                        @"C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",

                        // SQL Server 2019
                        @"C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",

                        // SQL Server 2017
                        @"C:\Program Files\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe",

                        // SQL Server 2016
                        @"C:\Program Files\Microsoft SQL Server\130\DAC\bin\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server\130\DAC\bin\SqlPackage.exe",

                        // Visual Studio 2022 installations
                        @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
                        @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
                        @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",

                        // Visual Studio 2019 installations
                        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",

                        // Azure Data Studio
                        @"C:\Program Files\Azure Data Studio\resources\app\extensions\mssql\sqltoolsservice\Windows\SqlPackage.exe",

                        // .NET global tools
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".dotnet\tools\sqlpackage.exe"),

                        // SSMS installations
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 19\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe",
                        @"C:\Program Files (x86)\Microsoft SQL Server Management Studio 18\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe"
                    };

                    Dispatcher.Invoke(() => LogMessage($"Checking {commonPaths.Count} common locations...", false));

                    // First, check common paths in parallel for better performance
                    var validPaths = await Task.Run(() =>
                        commonPaths.AsParallel()
                            .Where(File.Exists)
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .ToList()
                    );

                    if (validPaths.Any())
                    {
                        string selectedPath = validPaths.First();
                        Dispatcher.Invoke(() =>
                        {
                            SqlPackagePathTextBox.Text = selectedPath;
                            LogMessage($"✅ Auto-detected SqlPackage.exe at: {selectedPath}", false);

                            // Test the executable
                            try
                            {
                                var versionInfo = FileVersionInfo.GetVersionInfo(selectedPath);
                                LogMessage($"   Version: {versionInfo.FileVersion}", false);
                            }
                            catch { }
                        });

                        // Cache the result
                        _sqlPackageCache[cacheKey] = (selectedPath, DateTime.Now);
                        return;
                    }

                    // If not found in common paths, do a more thorough search
                    Dispatcher.Invoke(() => LogMessage("⚠️ SqlPackage.exe not found in common locations. Performing optimized search...", false));

                    var searchPaths = new List<string>
                    {
                        @"C:\Program Files\Microsoft SQL Server",
                        @"C:\Program Files (x86)\Microsoft SQL Server",
                        @"C:\Program Files\Microsoft Visual Studio",
                        @"C:\Program Files (x86)\Microsoft Visual Studio",
                        @"C:\Program Files\Azure Data Studio"
                    };

                    // Search directories in parallel with cancellation for early termination
                    using var cts = new CancellationTokenSource();
                    var searchTasks = searchPaths.Where(Directory.Exists).Select(async searchPath =>
                    {
                        try
                        {
                            return await Task.Run(() =>
                            {
                                var foundFiles = Directory.GetFiles(searchPath, "SqlPackage.exe", SearchOption.AllDirectories)
                                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                                    .ToList();

                                Dispatcher.Invoke(() => LogMessage($"  Found {foundFiles.Count} instances in {searchPath}", false));
                                return foundFiles;
                            }, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return new List<string>();
                        }
                        catch (Exception searchEx)
                        {
                            Dispatcher.Invoke(() => LogMessage($"  Error searching {searchPath}: {searchEx.Message}", false));
                            return new List<string>();
                        }
                    });

                    var allResults = await Task.WhenAll(searchTasks);
                    var allFoundFiles = allResults.SelectMany(x => x).OrderByDescending(f => new FileInfo(f).LastWriteTime).ToList();

                    if (allFoundFiles.Any())
                    {
                        string selectedPath = allFoundFiles.First();
                        Dispatcher.Invoke(() =>
                        {
                            SqlPackagePathTextBox.Text = selectedPath;
                            LogMessage($"✅ Found SqlPackage.exe at: {selectedPath}", false);

                            // Test the executable
                            try
                            {
                                var versionInfo = FileVersionInfo.GetVersionInfo(selectedPath);
                                LogMessage($"   Version: {versionInfo.FileVersion}", false);
                            }
                            catch { }
                        });

                        // Cache the result
                        _sqlPackageCache[cacheKey] = (selectedPath, DateTime.Now);
                        return;
                    }

                    // Check PATH environment variable
                    Dispatcher.Invoke(() => LogMessage("Checking PATH environment variable...", false));
                    string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    var pathDirectories = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);

                    var pathResult = await Task.Run(() =>
                        pathDirectories.AsParallel()
                            .Select(pathDir => Path.Combine(pathDir.Trim(), "SqlPackage.exe"))
                            .FirstOrDefault(File.Exists)
                    );

                    if (!string.IsNullOrEmpty(pathResult))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            SqlPackagePathTextBox.Text = pathResult;
                            LogMessage($"✅ Found SqlPackage.exe in PATH: {pathResult}", false);
                        });

                        // Cache the result
                        _sqlPackageCache[cacheKey] = (pathResult, DateTime.Now);
                        return;
                    }

                    // If still not found, provide detailed guidance
                    Dispatcher.Invoke(() =>
                    {
                        LogMessage("❌ SqlPackage.exe not found anywhere on this system.", true);
                        LogMessage("", false);
                        LogMessage("💡 Installation options:", false);
                        LogMessage("   1. Install SQL Server Management Studio (SSMS) - includes SqlPackage.exe", false);
                        LogMessage("   2. Install Visual Studio with SQL Server Data Tools", false);
                        LogMessage("   3. Install via .NET CLI: dotnet tool install -g Microsoft.SqlPackage", false);
                        LogMessage("   4. Download standalone DacFramework from Microsoft", false);
                        LogMessage("", false);
                        LogMessage("🔗 Download links:", false);
                        LogMessage("   SSMS: https://docs.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms", false);
                        LogMessage("   DacFramework: https://www.microsoft.com/en-us/download/details.aspx?id=58207", false);
                    });
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LogMessage($"❌ Error during SqlPackage.exe auto-detection: {ex.Message}", true);
                    LogMessage($"Stack trace: {ex.StackTrace}", false);
                });
            }
        }

        private void FindAndSetSqlPackagePath()
        {
            // Synchronous wrapper for backward compatibility
            _ = FindAndSetSqlPackagePathAsync();
        }

        #endregion

        #region UI Event Handlers

        private void UseWindowsAuthCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Authentication UI is now managed through saved connections
        }

        private void UseWindowsAuthCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Authentication UI is now managed through saved connections
        }


        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select SqlPackage.exe",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = "SqlPackage.exe",
                InitialDirectory = @"C:\Program Files\Microsoft SQL Server\"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SqlPackagePathTextBox.Text = openFileDialog.FileName;
                LogMessage($"SqlPackage.exe path set to: {openFileDialog.FileName}", false);
            }
        }

        private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
        {
            FindAndSetSqlPackagePath();
        }


        private async void LoadSourceDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            if (SourceConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                await LoadDatabases(connection.Server, SourceDatabaseComboBox,
                    connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth);
            }
            else
            {
                ShowError("Please select a source connection first");
            }
        }

        private async void LoadTargetDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                await LoadDatabases(connection.Server, TargetDatabaseComboBox,
                    connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth);
            }
            else
            {
                ShowError("Please select a target connection first");
            }
        }

        private async void TestSourceConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (SourceConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                await TestConnection(connection.Server, SourceDatabaseComboBox.Text,
                    connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth, "source", true);
            }
            else
            {
                ShowError("Please select a source connection first");
            }
        }

        private async void TestTargetConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                await TestConnection(connection.Server, TargetDatabaseComboBox.Text,
                    connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth, "target", true);
            }
            else
            {
                ShowError("Please select a target connection first");
            }
        }


        private async void StartSyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSyncing)
            {
                LogMessage("Sync operation is already in progress", true);
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            _isSyncing = true;
            StartSyncButton.IsEnabled = false;
            SyncProgressBar.Value = 0;
            StatusBarText.Text = "Starting sync...";

            try
            {
                await PerformDatabaseSync();
            }
            finally
            {
                _isSyncing = false;
                StartSyncButton.IsEnabled = true;
                SyncProgressBar.Value = 0;
                StatusBarText.Text = "Ready";
            }
        }

        private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenLogFileLocation();
        }

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            // Validate source connection
            if (SourceConnectionComboBox.SelectedItem is not DatabaseConnection)
            {
                ShowError("Please select a source connection");
                return false;
            }

            if (string.IsNullOrWhiteSpace(SourceDatabaseComboBox.Text))
            {
                ShowError("Source database is required");
                return false;
            }

            // Validate target connection
            if (TargetConnectionComboBox.SelectedItem is not DatabaseConnection)
            {
                ShowError("Please select a target connection");
                return false;
            }

            if (string.IsNullOrWhiteSpace(TargetDatabaseComboBox.Text))
            {
                ShowError("Target database is required");
                return false;
            }

            // Validate SqlPackage.exe path
            if (string.IsNullOrWhiteSpace(SqlPackagePathTextBox.Text))
            {
                ShowError("SqlPackage.exe path is required");
                return false;
            }

            if (!File.Exists(SqlPackagePathTextBox.Text))
            {
                ShowError("SqlPackage.exe file not found at specified path");
                return false;
            }

            return true;
        }

        #endregion

        #region Connection Testing

        private async Task<bool> TestSourceConnection()
        {
            try
            {
                if (SourceConnectionComboBox.SelectedItem is DatabaseConnection connection)
                {
                    return await TestConnection(connection.Server, SourceDatabaseComboBox.Text,
                        connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth, "source", false);
                }
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"Source connection test failed: {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> TestTargetConnection()
        {
            try
            {
                if (TargetConnectionComboBox.SelectedItem is DatabaseConnection connection)
                {
                    return await TestConnection(connection.Server, TargetDatabaseComboBox.Text,
                        connection.UserId, connection.GetDecryptedPassword(), connection.UseWindowsAuth, "target", false);
                }
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"Target connection test failed: {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> TestTargetServerConnection()
        {
            try
            {
                string connectionString = BuildTargetServerConnectionString();
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Target server connection test failed: {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> EnsureTargetDatabaseExists()
        {
            try
            {
                string targetDatabaseName = TargetDatabaseComboBox.Text.Trim();
                if (string.IsNullOrEmpty(targetDatabaseName))
                {
                    // Use source database name if target is empty
                    targetDatabaseName = SourceDatabaseComboBox.Text.Trim();
                    if (string.IsNullOrEmpty(targetDatabaseName))
                    {
                        LogMessage("❌ No target database name specified", true);
                        return false;
                    }

                    // Update the target database combo box
                    Dispatcher.Invoke(() =>
                    {
                        TargetDatabaseComboBox.Text = targetDatabaseName;
                    });
                }

                string masterConnectionString = BuildTargetServerConnectionString();
                bool shouldDropDatabase = false;

                Dispatcher.Invoke(() =>
                {
                    shouldDropDatabase = DropTargetDatabaseCheckBox.IsChecked ?? false;
                });

                using (var connection = new SqlConnection(masterConnectionString))
                {
                    await connection.OpenAsync();

                    // Check if database exists
                    string checkDbQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName";
                    using (var checkCmd = new SqlCommand(checkDbQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@dbName", targetDatabaseName);
                        int dbCount = (int)await checkCmd.ExecuteScalarAsync();

                        if (dbCount > 0 && shouldDropDatabase)
                        {
                            LogMessage($"🗑️ Dropping existing target database '{targetDatabaseName}'...", false);

                            // Drop the database
                            string dropDbQuery = $"DROP DATABASE [{targetDatabaseName}]";
                            using (var dropCmd = new SqlCommand(dropDbQuery, connection))
                            {
                                await dropCmd.ExecuteNonQueryAsync();
                            }

                            LogMessage($"✅ Target database '{targetDatabaseName}' dropped successfully", false);
                            dbCount = 0; // Mark as non-existent so it gets created below
                        }

                        if (dbCount == 0)
                        {
                            LogMessage($"Creating target database '{targetDatabaseName}'...", false);

                            // Create the database
                            string createDbQuery = $"CREATE DATABASE [{targetDatabaseName}]";
                            using (var createCmd = new SqlCommand(createDbQuery, connection))
                            {
                                await createCmd.ExecuteNonQueryAsync();
                            }

                            LogMessage($"✅ Target database '{targetDatabaseName}' created successfully", false);
                        }
                        else
                        {
                            LogMessage($"✅ Target database '{targetDatabaseName}' already exists", false);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Failed to ensure target database exists: {ex.Message}", true);
                return false;
            }
        }

        private string BuildTargetServerConnectionString()
        {
            if (TargetConnectionComboBox.SelectedItem is not DatabaseConnection targetConnection)
            {
                throw new Exception("No target connection selected");
            }

            var builder = new StringBuilder();
            builder.Append($"Server={targetConnection.Server};");
            builder.Append("Database=master;"); // Connect to master database

            if (targetConnection.UseWindowsAuth)
            {
                builder.Append("Integrated Security=true;");
            }
            else
            {
                builder.Append($"User ID={targetConnection.UserId};");
                builder.Append($"Password={targetConnection.GetDecryptedPassword()};");
            }

            builder.Append("Connection Timeout=30;");
            builder.Append("TrustServerCertificate=true;");

            return builder.ToString();
        }

        #endregion

        #region Database Sync Logic

        private async Task PerformDatabaseSync()
        {
            try
            {
                // Clean up any existing temp file
                CleanupTempFiles();

                LogMessage("=== Starting Database Synchronization ===", false);
                LogMessage($"Temporary DACPAC file: {_tempDacpacPath}", false);

                // Pre-flight checks
                LogMessage("🔍 Performing pre-flight checks...", false);
                
                // Test source connection first
                LogMessage("Testing source database connection...", false);
                bool sourceConnected = await TestSourceConnection();
                if (!sourceConnected)
                {
                    throw new Exception("Cannot connect to source database. Please check your source connection settings.");
                }
                
                // Test target server connection (to master database)
                LogMessage("Testing target server connection...", false);
                bool targetServerConnected = await TestTargetServerConnection();
                if (!targetServerConnected)
                {
                    throw new Exception("Cannot connect to target server. Please check your target server connection settings.");
                }

                // Create target database if it doesn't exist
                LogMessage("Ensuring target database exists...", false);
                bool databaseReady = await EnsureTargetDatabaseExists();
                if (!databaseReady)
                {
                    throw new Exception("Cannot create or access target database.");
                }

                LogMessage("✅ All pre-flight checks passed", false);

                // Step 1: Extract source database to DACPAC
                SyncProgressBar.Value = 10;
                StatusBarText.Text = "Extracting source database...";
                LogMessage("Step 1: Extracting source database to DACPAC file...", false);

                bool extractSuccess = await ExecuteSqlPackageCommand(BuildExtractCommand());
                if (!extractSuccess)
                {
                    throw new Exception("Database extraction failed. Check the error messages above for details.");
                }

                // Step 2: Publish DACPAC to target database
                SyncProgressBar.Value = 60;
                StatusBarText.Text = "Publishing to target database...";
                LogMessage("Step 2: Publishing DACPAC to target database...", false);

                bool publishSuccess = await ExecuteSqlPackageCommand(BuildPublishCommand());
                if (!publishSuccess)
                {
                    throw new Exception("Database publish failed");
                }

                SyncProgressBar.Value = 100;
                StatusBarText.Text = "Sync completed successfully";
                LogMessage("=== Database Synchronization Completed Successfully ===", false);
                
                MessageBox.Show("Database synchronization completed successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR: {ex.Message}", true);
                ShowError($"Database synchronization failed: {ex.Message}");
            }
            finally
            {
                CleanupTempFiles();
            }
        }

        private string BuildExtractCommand()
        {
            if (SourceConnectionComboBox.SelectedItem is not DatabaseConnection sourceConnection)
            {
                throw new Exception("No source connection selected");
            }

            string connectionString;
            if (sourceConnection.UseWindowsAuth)
            {
                connectionString = $"Server={sourceConnection.Server};Database={SourceDatabaseComboBox.Text};" +
                                 $"Integrated Security=true;TrustServerCertificate=true;Encrypt=false;Connection Timeout=30;";
            }
            else
            {
                connectionString = $"Server={sourceConnection.Server};Database={SourceDatabaseComboBox.Text};" +
                                 $"User Id={sourceConnection.UserId};Password={sourceConnection.GetDecryptedPassword()};" +
                                 $"TrustServerCertificate=true;Encrypt=false;Connection Timeout=30;";
            }

            // Use only valid parameters for Extract action
            var command = $"/Action:Extract " +
                   $"/SourceConnectionString:\"{connectionString}\" " +
                   $"/TargetFile:\"{_tempDacpacPath}\" " +
                   $"/p:ExtractAllTableData=true " +
                   $"/p:CommandTimeout=300";

            // Log the command for debugging (without password)
            string safeConnectionString;
            if (sourceConnection.UseWindowsAuth)
            {
                safeConnectionString = connectionString; // No password to hide
            }
            else
            {
                safeConnectionString = $"Server={sourceConnection.Server};Database={SourceDatabaseComboBox.Text};" +
                                     $"User Id={sourceConnection.UserId};Password=***;TrustServerCertificate=true;Encrypt=false;";
            }
            var safeCommand = command.Replace(connectionString, safeConnectionString);
            LogMessage($"Extract Command: {safeCommand}", false);

            return command;
        }

        private string BuildPublishCommand()
        {
            if (TargetConnectionComboBox.SelectedItem is not DatabaseConnection targetConnection)
            {
                throw new Exception("No target connection selected");
            }

            // Build connection string that includes the target database directly
            string targetDatabaseName = TargetDatabaseComboBox.Text;
            string targetConnectionString;

            if (targetConnection.UseWindowsAuth)
            {
                targetConnectionString = $"Server={targetConnection.Server};Database={targetDatabaseName};" +
                                $"Integrated Security=true;TrustServerCertificate=true;Encrypt=false;Connection Timeout=30;";
            }
            else
            {
                targetConnectionString = $"Server={targetConnection.Server};Database={targetDatabaseName};" +
                                 $"User Id={targetConnection.UserId};Password={targetConnection.GetDecryptedPassword()};" +
                                 $"TrustServerCertificate=true;Encrypt=false;Connection Timeout=30;";
            }

            // Use only valid parameters for Publish action - removed /TargetDatabaseName since it's in connection string
            var command = $"/Action:Publish " +
                   $"/SourceFile:\"{_tempDacpacPath}\" " +
                   $"/TargetConnectionString:\"{targetConnectionString}\" " +
                   $"/p:BlockOnPossibleDataLoss=false " +
                   $"/p:CreateNewDatabase=true " +
                   $"/p:AllowIncompatiblePlatform=true " +
                   $"/p:CommandTimeout=300";

            // Log the command for debugging (without password)
            string safeConnectionString;
            if (targetConnection.UseWindowsAuth)
            {
                safeConnectionString = targetConnectionString; // No password to hide
            }
            else
            {
                safeConnectionString = System.Text.RegularExpressions.Regex.Replace(targetConnectionString, @"Password=[^;]*", "Password=***");
            }
            var safeCommand = command.Replace(targetConnectionString, safeConnectionString);
            LogMessage($"Publish Command: {safeCommand}", false);

            return command;
        }

        private async Task<bool> ExecuteSqlPackageCommand(string arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string sqlPackagePath = "";
                    Dispatcher.Invoke(() => sqlPackagePath = SqlPackagePathTextBox.Text);

                    // Validate SqlPackage.exe path
                    if (string.IsNullOrEmpty(sqlPackagePath) || !File.Exists(sqlPackagePath))
                    {
                        Dispatcher.Invoke(() => LogMessage($"ERROR: SqlPackage.exe not found at: {sqlPackagePath}", true));
                        return false;
                    }

                    Dispatcher.Invoke(() => LogMessage($"Executing: {Path.GetFileName(sqlPackagePath)}", false));
                    Dispatcher.Invoke(() => LogMessage($"Working Directory: {Path.GetDirectoryName(_tempDacpacPath)}", false));
                    Dispatcher.Invoke(() => LogMessage($"🔧 Command Arguments: {arguments}", false));

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = sqlPackagePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_tempDacpacPath)
                    };

                    var outputLines = new List<string>();
                    var errorLines = new List<string>();

                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null)
                        {
                            Dispatcher.Invoke(() => LogMessage("❌ Failed to start SqlPackage.exe process", true));
                            return false;
                        }

                        Dispatcher.Invoke(() => LogMessage($"⚡ SqlPackage.exe process started (PID: {process.Id})", false));

                        // Read output in real-time
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                outputLines.Add(e.Data);
                                Dispatcher.Invoke(() => LogMessage($"OUT: {e.Data}", false));
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                errorLines.Add(e.Data);
                                Dispatcher.Invoke(() => LogMessage($"ERR: {e.Data}", true));
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        bool success = process.ExitCode == 0;
                        
                        Dispatcher.Invoke(() => 
                        {
                            LogMessage($"⏱️ Process completed in {DateTime.Now:HH:mm:ss}", false);
                            
                            if (success)
                            {
                                LogMessage($"✅ SqlPackage.exe completed successfully (Exit Code: {process.ExitCode})", false);
                                if (File.Exists(_tempDacpacPath))
                                {
                                    var fileInfo = new FileInfo(_tempDacpacPath);
                                    LogMessage($"📁 DACPAC file created: {fileInfo.Length:N0} bytes", false);
                                }
                            }
                            else
                            {
                                LogMessage($"❌ SqlPackage.exe failed (Exit Code: {process.ExitCode})", true);
                                LogMessage("Common issues:", false);
                                LogMessage("• Check server name and database name", false);
                                LogMessage("• Verify username and password", false);
                                LogMessage("• Ensure database exists and is accessible", false);
                                LogMessage("• Check SQL Server is running and accepting connections", false);
                                
                                if (errorLines.Any())
                                {
                                    LogMessage("Error details:", true);
                                    foreach (var error in errorLines.Take(5))
                                    {
                                        LogMessage($"  {error}", true);
                                    }
                                }
                            }
                        });

                        return success;
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => 
                    {
                        LogMessage($"❌ Exception executing SqlPackage.exe: {ex.Message}", true);
                        LogMessage($"Stack trace: {ex.StackTrace}", false);
                    });
                    return false;
                }
            });
        }

        #endregion

        #region Database Discovery and Browsing

        private async Task DiscoverServers(ComboBox serverComboBox, string serverType)
        {
            try
            {
                LogMessage($"Discovering {serverType} servers...", false);
                
                await Task.Run(() =>
                {
                    try
                    {
                        // Add common server instances if not already present
                        var commonInstances = new List<string>
                        {
                            ".\\SQLEXPRESS",
                            "(local)\\SQLEXPRESS", 
                            "localhost\\SQLEXPRESS",
                            ".\\SQL2019",
                            ".\\SQL2022",
                            "localhost",
                            "(local)",
                            Environment.MachineName + "\\SQLEXPRESS",
                            Environment.MachineName
                        };

                        Dispatcher.Invoke(() =>
                        {
                            foreach (string instance in commonInstances)
                            {
                                if (!serverComboBox.Items.Contains(instance))
                                {
                                    serverComboBox.Items.Add(instance);
                                }
                            }
                            LogMessage($"Added common {serverType} server instances", false);
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => LogMessage($"Error adding {serverType} servers: {ex.Message}", true));
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error discovering {serverType} servers: {ex.Message}", true);
            }
        }

        private async Task LoadDatabases(string serverName, ComboBox databaseComboBox, string userId, string password, bool useWindowsAuth)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                ShowError("Please specify a server name first");
                return;
            }

            try
            {
                LogMessage($"Loading databases from server: {serverName}", false);

                await Task.Run(() =>
                {
                    try
                    {
                        string connectionString;
                        if (useWindowsAuth)
                        {
                            connectionString = $"Server={serverName};Integrated Security=true;TrustServerCertificate=true;Connection Timeout=5;";
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                            {
                                Dispatcher.Invoke(() => ShowError("User ID and Password are required for SQL Server authentication"));
                                return;
                            }
                            connectionString = $"Server={serverName};User Id={userId};Password={password};TrustServerCertificate=true;Connection Timeout=5;";
                        }

                        using (var connection = new SqlConnection(connectionString))
                        {
                            connection.Open();
                            using (var command = new SqlCommand("SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name", connection))
                            {
                                using (var reader = command.ExecuteReader())
                                {
                                    var databases = new List<string>();
                                    while (reader.Read())
                                    {
                                        databases.Add(reader["name"].ToString());
                                    }

                                    Dispatcher.Invoke(() =>
                                    {
                                        databaseComboBox.Items.Clear();
                                        foreach (var db in databases)
                                        {
                                            databaseComboBox.Items.Add(db);
                                        }
                                        LogMessage($"Loaded {databases.Count} databases from {serverName}", false);
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => LogMessage($"Error loading databases: {ex.Message}", true));
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading databases: {ex.Message}", true);
            }
        }

        private async Task<bool> TestConnection(string serverName, string databaseName, string userId, string password, bool useWindowsAuth, string connectionType, bool showMessages = true)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                if (showMessages) ShowError("Please specify a server name");
                return false;
            }

            try
            {
                LogMessage($"Testing {connectionType} connection...", false);

                return await Task.Run(() =>
                {
                    try
                    {
                        string connectionString;
                        if (useWindowsAuth)
                        {
                            connectionString = $"Server={serverName};Integrated Security=true;TrustServerCertificate=true;Encrypt=false;Connection Timeout=5;";
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                            {
                                if (showMessages)
                                {
                                    Dispatcher.Invoke(() => ShowError("User ID and Password are required for SQL Server authentication"));
                                }
                                return false;
                            }
                            connectionString = $"Server={serverName};User Id={userId};Password={password};TrustServerCertificate=true;Encrypt=false;Connection Timeout=5;";
                        }

                        if (!string.IsNullOrWhiteSpace(databaseName))
                        {
                            connectionString += $"Database={databaseName};";
                        }

                        using (var connection = new SqlConnection(connectionString))
                        {
                            connection.Open();
                            
                            Dispatcher.Invoke(() =>
                            {
                                LogMessage($"✅ {connectionType} connection successful!", false);
                                if (showMessages)
                                {
                                    MessageBox.Show($"{connectionType} connection test successful!", "Connection Test", 
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            });
                            
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogMessage($"❌ {connectionType} connection failed: {ex.Message}", true);
                            if (showMessages)
                            {
                                ShowError($"{connectionType} connection failed: {ex.Message}");
                            }
                        });
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error testing {connectionType} connection: {ex.Message}", true);
                return false;
            }
        }


        #endregion

        #region Password Encryption/Decryption

        private string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            try
            {
                // Use Data Protection API (DPAPI) for user-specific encryption
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] encryptedBytes = ProtectedData.Protect(passwordBytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                LogMessage($"Error encrypting password: {ex.Message}", true);
                throw;
            }
        }

        private string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
                return string.Empty;

            try
            {
                // Use Data Protection API (DPAPI) for user-specific decryption
                byte[] encryptedBytes = Convert.FromBase64String(encryptedPassword);
                byte[] passwordBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(passwordBytes);
            }
            catch (Exception ex)
            {
                LogMessage($"Error decrypting password: {ex.Message}", true);
                throw;
            }
        }

        #endregion

        #region Log File Management

        private void InitializeLogFile()
        {
            try
            {
                // Create the log file with initial header
                string header = $"Database Sync Utility Log File\n" +
                              $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                              $"Machine: {Environment.MachineName}\n" +
                              $"User: {Environment.UserName}\n" +
                              $"Working Directory: {Environment.CurrentDirectory}\n" +
                              $"Log File: {_logFilePath}\n" +
                              $"========================================\n\n";

                File.WriteAllText(_logFilePath, header);
                LogMessage($"Log file created: {_logFilePath}", false);
                LogMessage("All sync operations will be logged to this file", false);
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Could not create log file: {ex.Message}", false);
            }
        }

        private void OpenLogFileLocation()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    // Open Windows Explorer and select the log file
                    Process.Start("explorer.exe", $"/select,\"{_logFilePath}\"");
                    LogMessage("Opened log file location in Windows Explorer", false);
                }
                else
                {
                    // If log file doesn't exist, open the desktop folder
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Process.Start("explorer.exe", desktopPath);
                    LogMessage("Log file not yet created. Opened Desktop folder.", false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening log file location: {ex.Message}", true);
                ShowError($"Could not open log file location: {ex.Message}");
            }
        }

        #endregion

        #region Connection Management

        private void LoadSavedConnections()
        {
            try
            {
                if (File.Exists(_connectionsFilePath))
                {
                    string json = File.ReadAllText(_connectionsFilePath);
                    _savedConnections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json) ?? new List<DatabaseConnection>();
                }
                else
                {
                    _savedConnections = new List<DatabaseConnection>();
                }

                RefreshConnectionDropdowns();
                LogMessage($"Loaded {_savedConnections.Count} saved connections", false);
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading saved connections: {ex.Message}", true);
                _savedConnections = new List<DatabaseConnection>();
            }
        }

        private void SaveConnections()
        {
            try
            {
                string json = JsonSerializer.Serialize(_savedConnections, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_connectionsFilePath, json);
                LogMessage($"Saved {_savedConnections.Count} connections", false);
            }
            catch (Exception ex)
            {
                LogMessage($"Error saving connections: {ex.Message}", true);
            }
        }

        private void RefreshConnectionDropdowns()
        {
            // Store current selections
            string currentSourceConnection = SourceConnectionComboBox?.Text ?? "";
            string currentTargetConnection = TargetConnectionComboBox?.Text ?? "";

            // Clear and repopulate dropdowns
            SourceConnectionComboBox?.Items.Clear();
            TargetConnectionComboBox?.Items.Clear();

            foreach (var connection in _savedConnections)
            {
                SourceConnectionComboBox?.Items.Add(connection);
                TargetConnectionComboBox?.Items.Add(connection);
            }

            // Restore selections if they still exist
            if (!string.IsNullOrEmpty(currentSourceConnection))
            {
                var sourceConn = _savedConnections.FirstOrDefault(c => c.Name == currentSourceConnection);
                if (sourceConn != null && SourceConnectionComboBox != null)
                {
                    SourceConnectionComboBox.SelectedItem = sourceConn;
                }
            }

            if (!string.IsNullOrEmpty(currentTargetConnection))
            {
                var targetConn = _savedConnections.FirstOrDefault(c => c.Name == currentTargetConnection);
                if (targetConn != null && TargetConnectionComboBox != null)
                {
                    TargetConnectionComboBox.SelectedItem = targetConn;
                }
            }
        }

        private void OnSourceConnectionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (SourceConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                // Database field stays editable, other fields hidden since they're in the connection
                LogMessage($"Source connection selected: {connection.Name} ({connection.Server})", false);
            }
        }

        private void OnTargetConnectionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (TargetConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                // Set Windows Auth checkbox based on connection settings
                UseWindowsAuthCheckBox.IsChecked = connection.UseWindowsAuth;
                LogMessage($"Target connection selected: {connection.Name} ({connection.Server})", false);
            }
        }

        private void ManageConnectionsButton_Click(object sender, RoutedEventArgs e)
        {
            var managementWindow = new ConnectionManagementWindow(_savedConnections);
            if (managementWindow.ShowDialog() == true)
            {
                _savedConnections = managementWindow.Connections;
                SaveConnections();
                RefreshConnectionDropdowns();
            }
        }

        #endregion

        #region Utility Methods

        private void LogMessage(string message, bool isError)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}\n";

            // Update UI
            StatusTextBlock.Text += logEntry;

            // Auto-scroll to bottom if enabled
            if (AutoScrollLogCheckBox?.IsChecked == true)
            {
                StatusScrollViewer.ScrollToEnd();
            }

            // Write to log file
            try
            {
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                // If we can't write to log file, at least show it in UI
                StatusTextBlock.Text += $"[{timestamp}] WARNING: Could not write to log file: {ex.Message}\n";
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            LogMessage($"ERROR: {message}", true);
        }

        private void CleanupTempFiles()
        {
            try
            {
                if (File.Exists(_tempDacpacPath))
                {
                    File.Delete(_tempDacpacPath);
                    LogMessage($"Cleaned up temporary file: {_tempDacpacPath}", false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Could not delete temporary file {_tempDacpacPath}: {ex.Message}", false);
            }
        }

        #endregion
    }
}