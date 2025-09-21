using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
        private readonly string _tempBacpacPath;
        private readonly string _logFilePath;
        private readonly string _connectionsFilePath;
        private List<DatabaseConnection> _savedConnections = new();
        private CancellationTokenSource? _syncCancellationTokenSource;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            _tempDacpacPath = Path.Combine(Path.GetTempPath(), $"DatabaseSync_{Guid.NewGuid():N}.dacpac");
            _tempBacpacPath = Path.Combine(Path.GetTempPath(), $"DatabaseSync_{Guid.NewGuid():N}.bacpac");
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
                // If already syncing, this becomes a cancel button
                LogMessage("🛑 Cancelling sync operation...", false);
                _syncCancellationTokenSource?.Cancel();
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            _isSyncing = true;
            _syncCancellationTokenSource = new CancellationTokenSource();

            // Update UI to show cancel option
            StartSyncButton.Content = "🛑 Cancel Sync";
            StartSyncButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightCoral);
            SyncProgressBar.Value = 0;
            StatusBarText.Text = "Starting sync...";

            try
            {
                await PerformDatabaseSync(_syncCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage("🛑 Sync operation cancelled by user", false);
                StatusBarText.Text = "Sync cancelled";
            }
            finally
            {
                _isSyncing = false;
                _syncCancellationTokenSource?.Dispose();
                _syncCancellationTokenSource = null;

                // Restore UI
                StartSyncButton.Content = "🚀 Start Sync";
                StartSyncButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightBlue);
                SyncProgressBar.Value = 0;

                if (StatusBarText.Text != "Sync cancelled")
                {
                    StatusBarText.Text = "Ready";
                }
            }
        }

        private void AnalyzeConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetConnectionComboBox.SelectedItem is DatabaseConnection connection)
            {
                string analysis = AnalyzeConnectionType(connection.Server);
                LogMessage("", false); // Empty line for readability
                LogMessage("═══ CONNECTION ANALYSIS REPORT ═══", false);
                foreach (string line in analysis.Split('\n'))
                {
                    LogMessage(line, false);
                }
                LogMessage("═══ END ANALYSIS REPORT ═══", false);
                LogMessage("", false); // Empty line for readability

                // Also show a summary in a message box
                string summary = $"Connection Analysis Summary\n\n" +
                               $"Server: {connection.Server}\n" +
                               $"Type: {(IsRemoteConnection(connection.Server) ? "🌐 REMOTE" : "🏠 LOCAL")}\n\n" +
                               $"Detailed analysis has been written to the log console below.\n" +
                               $"Please review the log for comprehensive safety information.";

                MessageBox.Show(summary, "Connection Analysis", MessageBoxButton.OK, 
                              IsRemoteConnection(connection.Server) ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            else
            {
                ShowError("Please select a target connection first");
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

            // Additional validation for remote target connections
            if (TargetConnectionComboBox.SelectedItem is DatabaseConnection targetConnection)
            {
                if (IsRemoteConnection(targetConnection.Server))
                {
                    string confirmMessage = $"🚨 FINAL CONFIRMATION REQUIRED 🚨\n\n" +
                                          $"You are about to sync to a REMOTE TARGET:\n" +
                                          $"• Server: {targetConnection.Server}\n" +
                                          $"• Database: {TargetDatabaseComboBox.Text}\n\n" +
                                          "This operation will modify data on a remote system.\n\n" +
                                          "Are you absolutely sure you want to proceed?";

                    var result = MessageBox.Show(confirmMessage, "Remote Target Confirmation", 
                                               MessageBoxButton.YesNo, MessageBoxImage.Warning, 
                                               MessageBoxResult.No);

                    if (result != MessageBoxResult.Yes)
                    {
                        LogMessage("🛑 Sync cancelled by user - remote target confirmation declined", false);
                        return false;
                    }
                    
                    LogMessage($"✅ User confirmed sync to remote target: {targetConnection.Server}", false);
                }
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

                // Determine if we should drop database based on sync mode
                Dispatcher.Invoke(() =>
                {
                    // Only drop database for BACPAC Full Copy mode
                    shouldDropDatabase = BacpacFullCopyRadio?.IsChecked == true;
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

                        if (dbCount > 0)
                        {
                            if (shouldDropDatabase)
                            {
                                LogMessage($"🗑️ BACPAC FULL COPY: Dropping existing target database '{targetDatabaseName}'...", false);

                                // First, terminate all connections to the database
                                LogMessage($"🔌 Terminating active connections to database '{targetDatabaseName}'...", false);

                                string killConnectionsQuery = @"
                                    DECLARE @sql NVARCHAR(MAX) = ''
                                    SELECT @sql = @sql + 'KILL ' + CAST(session_id AS NVARCHAR(10)) + '; '
                                    FROM sys.dm_exec_sessions
                                    WHERE database_id = DB_ID(@dbName)
                                    AND session_id != @@SPID
                                    EXEC sp_executesql @sql";

                                try
                                {
                                    using (var killCmd = new SqlCommand(killConnectionsQuery, connection))
                                    {
                                        killCmd.Parameters.AddWithValue("@dbName", targetDatabaseName);
                                        await killCmd.ExecuteNonQueryAsync();
                                    }
                                    LogMessage($"✅ Terminated active connections to database '{targetDatabaseName}'", false);
                                }
                                catch (Exception killEx)
                                {
                                    LogMessage($"⚠️ Warning: Could not terminate some connections: {killEx.Message}", false);
                                }

                                // Now drop the database
                                string dropDbQuery = $"DROP DATABASE [{targetDatabaseName}]";
                                using (var dropCmd = new SqlCommand(dropDbQuery, connection))
                                {
                                    await dropCmd.ExecuteNonQueryAsync();
                                }

                                LogMessage($"✅ Target database '{targetDatabaseName}' dropped successfully", false);
                                dbCount = 0; // Mark as non-existent so it gets created below
                            }
                            else
                            {
                                LogMessage($"🛡️ DACPAC MODE: Target database '{targetDatabaseName}' exists - preserving for schema deployment", false);
                                LogMessage($"📊 Database will be used as-is for schema sync operation", false);
                            }
                        }
                        else
                        {
                            LogMessage($"⚠️ Target database '{targetDatabaseName}' does not exist", false);
                        }

                        if (dbCount == 0)
                        {
                            LogMessage($"🔨 Creating target database '{targetDatabaseName}'...", false);

                            // Create the database
                            string createDbQuery = $"CREATE DATABASE [{targetDatabaseName}]";
                            using (var createCmd = new SqlCommand(createDbQuery, connection))
                            {
                                await createCmd.ExecuteNonQueryAsync();
                            }

                            LogMessage($"✅ Target database '{targetDatabaseName}' created successfully (empty)", false);
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

        private async Task PerformDatabaseSync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Clean up any existing temp file
                CleanupTempFiles();

                LogMessage("=== Starting Database Synchronization ===", false);
                LogMessage($"Temporary DACPAC file: {_tempDacpacPath}", false);

                // Pre-flight checks
                Dispatcher.Invoke(() => {
                    SyncProgressBar.Value = 5;
                    StatusBarText.Text = "Performing pre-flight checks...";
                });
                LogMessage("🔍 Performing pre-flight checks...", false);

                cancellationToken.ThrowIfCancellationRequested();

                // Test source connection first
                LogMessage("Testing source database connection...", false);
                bool sourceConnected = await TestSourceConnection();
                if (!sourceConnected)
                {
                    throw new Exception("Cannot connect to source database. Please check your source connection settings.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Test target server connection (to master database)
                LogMessage("Testing target server connection...", false);
                bool targetServerConnected = await TestTargetServerConnection();
                if (!targetServerConnected)
                {
                    throw new Exception("Cannot connect to target server. Please check your target server connection settings.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Create target database if it doesn't exist
                LogMessage("Ensuring target database exists...", false);
                bool databaseReady = await EnsureTargetDatabaseExists();
                if (!databaseReady)
                {
                    throw new Exception("Cannot create or access target database.");
                }

                LogMessage("✅ All pre-flight checks passed", false);

                // Determine sync mode from radio buttons
                string syncMode = "";
                Dispatcher.Invoke(() =>
                {
                    if (BacpacFullCopyRadio?.IsChecked == true)
                        syncMode = "BACPAC_FULL";
                    else if (DacpacSafeRadio?.IsChecked == true)
                        syncMode = "DACPAC_SAFE";
                    else if (DacpacForceRadio?.IsChecked == true)
                        syncMode = "DACPAC_FORCE";
                    else
                        syncMode = "BACPAC_FULL"; // default
                });

                if (syncMode == "BACPAC_FULL")
                {
                    // Full Copy Mode: Use BACPAC (Export/Import)
                    LogMessage("🔄 FULL COPY MODE: Using BACPAC for complete database transfer", false);

                    // Step 1: Export source database to BACPAC
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 10;
                        StatusBarText.Text = "Exporting source database to BACPAC...";
                    });
                    LogMessage("Step 1: Exporting source database to BACPAC file...", false);

                    bool exportSuccess = await ExecuteSqlPackageCommand(BuildExportCommand(), cancellationToken);
                    if (!exportSuccess)
                    {
                        throw new Exception("Database export failed. Check the error messages above for details.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 2: Import BACPAC to target database
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 60;
                        StatusBarText.Text = "Importing BACPAC to target database...";
                    });
                    LogMessage("Step 2: Importing BACPAC to target database...", false);

                    bool importSuccess = await ExecuteSqlPackageCommand(BuildImportCommand(), cancellationToken);
                    if (!importSuccess)
                    {
                        throw new Exception("Database import failed");
                    }
                }
                else if (syncMode == "DACPAC_SAFE")
                {
                    // Safe Schema Mode: Use DACPAC with safe parameters
                    LogMessage("📋 SAFE SCHEMA MODE: Using DACPAC for safe schema deployment", false);

                    // Step 1: Extract source database to DACPAC (schema only)
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 10;
                        StatusBarText.Text = "Extracting source schema to DACPAC...";
                    });
                    LogMessage("Step 1: Extracting source schema to DACPAC file...", false);

                    bool extractSuccess = await ExecuteSqlPackageCommand(BuildExtractCommand(), cancellationToken);
                    if (!extractSuccess)
                    {
                        throw new Exception("Schema extraction failed. Check the error messages above for details.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 2: Publish DACPAC to target database (safe schema deployment)
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 60;
                        StatusBarText.Text = "Publishing schema safely to target database...";
                    });
                    LogMessage("Step 2: Publishing schema safely to target database (preserving data)...", false);

                    bool publishSuccess = await ExecuteSqlPackageCommand(BuildPublishCommand(false), cancellationToken);
                    if (!publishSuccess)
                    {
                        throw new Exception("Safe schema publish failed");
                    }
                }
                else if (syncMode == "DACPAC_FORCE")
                {
                    // Force Schema Mode: Use DACPAC with destructive parameters
                    LogMessage("⚡ FORCE SCHEMA MODE: Using DACPAC for destructive schema sync", false);
                    LogMessage("⚠️ WARNING: This mode allows data loss to force schema matching", false);

                    // Step 1: Extract source database to DACPAC (schema only)
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 10;
                        StatusBarText.Text = "Extracting source schema to DACPAC...";
                    });
                    LogMessage("Step 1: Extracting source schema to DACPAC file...", false);

                    bool extractSuccess = await ExecuteSqlPackageCommand(BuildExtractCommand(), cancellationToken);
                    if (!extractSuccess)
                    {
                        throw new Exception("Schema extraction failed. Check the error messages above for details.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 2: Publish DACPAC to target database (force schema match)
                    Dispatcher.Invoke(() => {
                        SyncProgressBar.Value = 60;
                        StatusBarText.Text = "Force publishing schema to target database...";
                    });
                    LogMessage("Step 2: Force publishing schema to target database (allowing data loss)...", false);

                    bool publishSuccess = await ExecuteSqlPackageCommand(BuildPublishCommand(true), cancellationToken);
                    if (!publishSuccess)
                    {
                        throw new Exception("Force schema publish failed");
                    }
                }

                Dispatcher.Invoke(() => {
                    SyncProgressBar.Value = 100;
                    StatusBarText.Text = "Sync completed successfully";
                });
                LogMessage("=== Database Synchronization Completed Successfully ===", false);

                Dispatcher.Invoke(() => {
                    MessageBox.Show("Database synchronization completed successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (OperationCanceledException)
            {
                LogMessage("🛑 Database synchronization cancelled", false);
                throw; // Re-throw to be handled by caller
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR: {ex.Message}", true);
                Dispatcher.Invoke(() => ShowError($"Database synchronization failed: {ex.Message}"));
            }
            finally
            {
                CleanupTempFiles();
            }
        }

        private string BuildExportCommand()
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

            // Build BACPAC export command (includes schema + data)
            var command = $"/Action:Export " +
                   $"/SourceConnectionString:\"{connectionString}\" " +
                   $"/TargetFile:\"{_tempBacpacPath}\" " +
                   $"/p:CommandTimeout=300";

            LogMessage("📦 Exporting source database as BACPAC (schema + data)", false);

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
            LogMessage($"Export Command: {safeCommand}", false);

            return command;
        }

        private string BuildImportCommand()
        {
            if (TargetConnectionComboBox.SelectedItem is not DatabaseConnection targetConnection)
            {
                throw new Exception("No target connection selected");
            }

            string targetDatabaseName = TargetDatabaseComboBox.Text;

            // Build target connection string for BACPAC import
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

            // Build BACPAC import command (correct syntax from your reference)
            var command = $"/Action:Import " +
                   $"/SourceFile:\"{_tempBacpacPath}\" " +
                   $"/TargetConnectionString:\"{targetConnectionString}\" " +
                   $"/p:CommandTimeout=300";

            LogMessage("📥 Importing BACPAC to target database (complete replacement)", false);

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
            LogMessage($"Import Command: {safeCommand}", false);

            return command;
        }

        private string BuildExtractCommand()
        {
            if (SourceConnectionComboBox.SelectedItem is not DatabaseConnection sourceConnection)
            {
                throw new Exception("No source connection selected");
            }

            // Build DACPAC extract command (schema only) - Extract creates DACPAC, Export creates BACPAC
            var command = $"/Action:Extract " +
                   $"/SourceDatabaseName:{SourceDatabaseComboBox.Text} " +
                   $"/SourceServerName:{sourceConnection.Server} " +
                   $"/TargetFile:\"{_tempDacpacPath}\" " +
                   $"/p:CommandTimeout=300";

            // Add authentication parameters for source
            if (!sourceConnection.UseWindowsAuth)
            {
                command += $" /SourceUser:{sourceConnection.UserId}";
                command += $" /SourcePassword:{sourceConnection.GetDecryptedPassword()}";
            }
            command += " /SourceTrustServerCertificate:True";

            LogMessage("📋 Extracting source database as DACPAC (schema only)", false);

            // Log the command for debugging (without password)
            string safeCommand;
            if (sourceConnection.UseWindowsAuth)
            {
                safeCommand = command; // No password to hide
            }
            else
            {
                safeCommand = System.Text.RegularExpressions.Regex.Replace(command, @"/SourcePassword:[^\s]*", "/SourcePassword:***");
            }
            LogMessage($"Extract Command: {safeCommand}", false);

            return command;
        }

        private string BuildPublishCommand(bool forceMode = false)
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

            // Build DACPAC publish command - safe or force mode
            var command = $"/Action:Publish " +
                   $"/SourceFile:\"{_tempDacpacPath}\" " +
                   $"/TargetConnectionString:\"{targetConnectionString}\" " +
                   $"/p:CreateNewDatabase=false " +
                   $"/p:AllowIncompatiblePlatform=true " +
                   $"/p:CommandTimeout=300";

            if (forceMode)
            {
                // Force Schema Mode: Allow destructive changes to match source exactly
                command += " /p:DropObjectsNotInSource=True";
                command += " /p:BlockOnPossibleDataLoss=False";

                LogMessage("⚡ Publishing DACPAC schema with FORCE parameters (destructive)", false);
                LogMessage("🔧 Publish parameters: DropObjectsNotInSource=True, BlockOnPossibleDataLoss=False", false);
                LogMessage("⚠️ DESTRUCTIVE: Drops extra objects, allows data loss to force schema match", false);
                LogMessage("📊 Expected behavior: Target schema will match source exactly, data may be lost", false);
            }
            else
            {
                // Safe Schema Mode: Preserve data and extra objects
                command += " /p:BlockOnPossibleDataLoss=True";
                command += " /p:DropObjectsNotInSource=False";
                command += " /p:GenerateSmartDefaults=True";

                LogMessage("📋 Publishing DACPAC schema with SAFE parameters", false);
                LogMessage("🔧 Publish parameters: BlockOnPossibleDataLoss=True, DropObjectsNotInSource=False, GenerateSmartDefaults=True", false);
                LogMessage("🛡️ Safe schema deployment: Stops if data loss, keeps extra objects, smart defaults for new columns", false);
                LogMessage("📊 Expected behavior: Schema updated safely, all target data preserved", false);
            }

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

        private async Task<bool> ExecuteSqlPackageCommand(string arguments, CancellationToken cancellationToken = default)
        {
            string sqlPackagePath = "";
            await Dispatcher.InvokeAsync(() => sqlPackagePath = SqlPackagePathTextBox.Text);

            // Validate SqlPackage.exe path
            if (string.IsNullOrEmpty(sqlPackagePath) || !File.Exists(sqlPackagePath))
            {
                await Dispatcher.InvokeAsync(() => LogMessage($"ERROR: SqlPackage.exe not found at: {sqlPackagePath}", true));
                return false;
            }

            await Dispatcher.InvokeAsync(() => LogMessage($"Executing: {Path.GetFileName(sqlPackagePath)}", false));
            await Dispatcher.InvokeAsync(() => LogMessage($"Working Directory: {Path.GetDirectoryName(_tempDacpacPath)}", false));
            await Dispatcher.InvokeAsync(() => LogMessage($"🔧 Command Arguments: {arguments}", false));

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

            var outputBuffer = new List<string>();
            var errorBuffer = new List<string>();
            var lastUiUpdate = DateTime.Now;

            using (var process = new Process { StartInfo = processInfo })
            {
                try
                {
                    // Set up cancellation to kill the process
                    using (cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                Dispatcher.InvokeAsync(() => LogMessage("🛑 SqlPackage process killed due to cancellation", false));
                            }
                        }
                        catch { }
                    }))
                    {
                        process.Start();

                        await Dispatcher.InvokeAsync(() => LogMessage($"⚡ SqlPackage.exe process started (PID: {process.Id})", false));

                        // Read output in real-time with batching for UI performance
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                lock (outputBuffer)
                                {
                                    outputBuffer.Add(e.Data);
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                lock (errorBuffer)
                                {
                                    errorBuffer.Add(e.Data);
                                }
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        // Periodically update UI with batched messages to prevent freezing
                        var uiUpdateTask = Task.Run(async () =>
                        {
                            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                            {
                                await Task.Delay(500, cancellationToken); // Update every 500ms

                                List<string> outputToShow, errorsToShow;
                                lock (outputBuffer)
                                {
                                    outputToShow = new List<string>(outputBuffer);
                                    outputBuffer.Clear();
                                }
                                lock (errorBuffer)
                                {
                                    errorsToShow = new List<string>(errorBuffer);
                                    errorBuffer.Clear();
                                }

                                if (outputToShow.Any() || errorsToShow.Any())
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        foreach (var output in outputToShow)
                                            LogMessage($"OUT: {output}", false);
                                        foreach (var error in errorsToShow)
                                            LogMessage($"ERR: {error}", true);
                                    });
                                }
                            }
                        }, cancellationToken);

                        // Wait for process completion with cancellation support
                        await Task.Run(() =>
                        {
                            while (!process.WaitForExit(1000))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }, cancellationToken);

                        // Wait for UI updates to complete
                        try
                        {
                            await uiUpdateTask;
                        }
                        catch (OperationCanceledException) { }

                        // Final UI update with any remaining messages
                        List<string> finalOutput, finalErrors;
                        lock (outputBuffer)
                        {
                            finalOutput = new List<string>(outputBuffer);
                            outputBuffer.Clear();
                        }
                        lock (errorBuffer)
                        {
                            finalErrors = new List<string>(errorBuffer);
                            errorBuffer.Clear();
                        }

                        bool success = process.ExitCode == 0;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            // Show any remaining messages
                            foreach (var output in finalOutput)
                                LogMessage($"OUT: {output}", false);
                            foreach (var error in finalErrors)
                                LogMessage($"ERR: {error}", true);

                            LogMessage($"⏱️ Process completed at {DateTime.Now:HH:mm:ss}", false);

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

                                if (finalErrors.Any())
                                {
                                    LogMessage("Error details:", true);
                                    foreach (var error in finalErrors.Take(5))
                                    {
                                        LogMessage($"  {error}", true);
                                    }
                                }
                            }
                        });

                        return success;
                    }
                }
                catch (OperationCanceledException)
                {
                    await Dispatcher.InvokeAsync(() => LogMessage("🛑 SqlPackage operation cancelled", false));
                    throw;
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LogMessage($"❌ Exception executing SqlPackage.exe: {ex.Message}", true);
                        LogMessage($"Stack trace: {ex.StackTrace}", false);
                    });
                    return false;
                }
            }
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

                // Check if target is using a remote service and show warning
                if (IsRemoteConnection(connection.Server))
                {
                    string warningMessage = $"⚠️ WARNING: Target connection '{connection.Name}' is configured to use a remote server '{connection.Server}'.\n\n" +
                                          "This will modify data on a remote system. Please ensure:\n" +
                                          "• You have proper authorization to modify the remote database\n" +
                                          "• You have verified the target server and database names\n" +
                                          "• You understand the impact of this operation\n\n" +
                                          "Consider using a local target for testing purposes.";

                    MessageBox.Show(warningMessage, "Remote Target Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LogMessage($"🚨 REMOTE TARGET WARNING: Target server '{connection.Server}' is not local", false);
                }
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

        private void DataHandlingInfoButton_Click(object sender, RoutedEventArgs e)
        {
            string infoMessage = @"🔍 Three Sync Modes Explained:

📦 FULL COPY (BACPAC):
• Export → Import actions
• Complete replacement with schema + data
• Target recreated from scratch
• Use for: Development, testing, full migration
• Result: Exact copy, all target data lost

📋 SAFE SCHEMA (DACPAC):
• Extract → Publish with safe parameters
• BlockOnPossibleDataLoss=True, DropObjectsNotInSource=False
• Schema updates while preserving ALL target data
• Stops if any operation would cause data loss
• Use for: Production, safe schema updates
• Result: Schema updated, target data preserved

⚡ FORCE SCHEMA (DACPAC):
• Extract → Publish with destructive parameters
• BlockOnPossibleDataLoss=False, DropObjectsNotInSource=True
• Forces target schema to match source exactly
• Allows data loss to achieve schema consistency
• Use for: When schema must match regardless of data
• Result: Schema synchronized, data may be lost

🎯 WHEN TO USE:

Development/Testing: BACPAC Full Copy
Production Updates: DACPAC Safe Schema
Schema Fixes: DACPAC Force Schema (with caution)

⚠️ IMPORTANT:
• Always backup before destructive operations
• DACPAC Force can drop objects/data unexpectedly
• Consider preview with /Action:Script first";

            MessageBox.Show(infoMessage, "Three Sync Modes Explained", MessageBoxButton.OK, MessageBoxImage.Information);
            LogMessage("User viewed sync modes information", false);
        }

        #endregion

        #region Remote Connection Detection
        
        /// <summary>
        /// ENHANCED SAFETY: Multi-layered local connection detection system
        /// 
        /// Safety Features Implemented:
        /// 1. Pattern Matching: Detects common local server patterns (., localhost, (local), etc.)
        /// 2. Machine Name Validation: Checks current machine name and domain variations
        /// 3. IP Address Analysis: Identifies loopback, private, and link-local IP ranges
        /// 4. Network Interface Scanning: Verifies if IP addresses belong to local network interfaces  
        /// 5. DNS Resolution: Resolves hostnames to check if they point to local addresses
        /// 6. Comprehensive Logging: Detailed analysis logging for transparency and debugging
        /// 
        /// Private IP Ranges Detected:
        /// - IPv4: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16 (link-local)
        /// - IPv6: fe80::/10 (link-local), fc00::/7 (unique local)
        /// - Loopback: 127.0.0.1, ::1
        /// 
        /// Security Philosophy: "Fail Safe" - When in doubt, treat as remote to prevent accidents
        /// </summary>
        private bool IsRemoteConnection(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                LogMessage("🔍 Local Detection: Empty server name considered local", false);
                return false;
            }

            // Normalize the server name for comparison
            string normalizedServer = serverName.Trim().ToLowerInvariant();
            string originalServer = serverName.Trim();

            LogMessage($"🔍 Analyzing connection: '{originalServer}' (normalized: '{normalizedServer}')", false);

            // Step 1: Check for obvious local server patterns
            var localPatterns = new[]
            {
                ".",
                "localhost",
                "(local)",
                "127.0.0.1",
                "::1"
            };

            foreach (var pattern in localPatterns)
            {
                if (normalizedServer == pattern || normalizedServer.StartsWith(pattern + "\\"))
                {
                    LogMessage($"✅ Local Detection: Matched local pattern '{pattern}'", false);
                    return false;
                }
            }

            // Step 2: Check against current machine name and common variations
            string machineName = Environment.MachineName.ToLowerInvariant();
            var machineVariations = new[]
            {
                machineName,
                $"{machineName}.local",
                $"{machineName}.{Environment.UserDomainName?.ToLowerInvariant() ?? ""}".TrimEnd('.')
            };

            foreach (var variation in machineVariations)
            {
                if (!string.IsNullOrEmpty(variation) && 
                    (normalizedServer == variation || normalizedServer.StartsWith(variation + "\\")))
                {
                    LogMessage($"✅ Local Detection: Matched machine name variation '{variation}'", false);
                    return false;
                }
            }

            // Step 3: Check for private IP address ranges
            if (IsPrivateOrLocalIPAddress(normalizedServer))
            {
                LogMessage($"✅ Local Detection: Private/local IP address detected", false);
                return false;
            }

            // Step 4: Check against actual network interfaces on this machine
            if (IsLocalNetworkInterface(normalizedServer))
            {
                LogMessage($"✅ Local Detection: Found in local network interfaces", false);
                return false;
            }

            // Step 5: Try DNS resolution for hostnames
            if (ResolvesToLocalAddress(originalServer))
            {
                LogMessage($"✅ Local Detection: DNS resolves to local address", false);
                return false;
            }

            // If all checks fail, it's considered remote
            LogMessage($"🌐 Remote Detection: Server '{originalServer}' is considered REMOTE", false);
            return true;
        }

        private bool IsPrivateOrLocalIPAddress(string serverName)
        {
            try
            {
                // Extract just the IP portion if there's an instance name
                string ipPortion = serverName.Split('\\')[0];

                if (!IPAddress.TryParse(ipPortion, out IPAddress? ip))
                    return false;

                // Check for loopback addresses
                if (IPAddress.IsLoopback(ip))
                {
                    LogMessage($"  └─ Loopback address detected: {ip}", false);
                    return true;
                }

                byte[] bytes = ip.GetAddressBytes();

                // Check for private IPv4 ranges
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // 10.0.0.0/8
                    if (bytes[0] == 10)
                    {
                        LogMessage($"  └─ Private IPv4 range 10.x.x.x: {ip}", false);
                        return true;
                    }

                    // 172.16.0.0/12
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    {
                        LogMessage($"  └─ Private IPv4 range 172.16-31.x.x: {ip}", false);
                        return true;
                    }

                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168)
                    {
                        LogMessage($"  └─ Private IPv4 range 192.168.x.x: {ip}", false);
                        return true;
                    }

                    // 169.254.0.0/16 (link-local)
                    if (bytes[0] == 169 && bytes[1] == 254)
                    {
                        LogMessage($"  └─ Link-local IPv4 range 169.254.x.x: {ip}", false);
                        return true;
                    }
                }

                // Check for IPv6 private/local addresses
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    // Link-local addresses (fe80::/10)
                    if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                    {
                        LogMessage($"  └─ IPv6 link-local address: {ip}", false);
                        return true;
                    }

                    // Unique local addresses (fc00::/7)
                    if ((bytes[0] & 0xfe) == 0xfc)
                    {
                        LogMessage($"  └─ IPv6 unique local address: {ip}", false);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"  └─ Error parsing IP address '{serverName}': {ex.Message}", false);
                return false;
            }
        }

        private bool IsLocalNetworkInterface(string serverName)
        {
            try
            {
                // Extract just the IP/hostname portion
                string hostPortion = serverName.Split('\\')[0];

                if (!IPAddress.TryParse(hostPortion, out IPAddress? targetIP))
                    return false;

                // Get all network interfaces on this machine
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var ni in networkInterfaces)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProperties = ni.GetIPProperties();
                    foreach (var unicastAddr in ipProperties.UnicastAddresses)
                    {
                        if (unicastAddr.Address.Equals(targetIP))
                        {
                            LogMessage($"  └─ Found on network interface '{ni.Name}': {unicastAddr.Address}", false);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"  └─ Error checking network interfaces: {ex.Message}", false);
                return false;
            }
        }

        private bool ResolvesToLocalAddress(string serverName)
        {
            try
            {
                // Extract just the hostname portion
                string hostPortion = serverName.Split('\\')[0];

                // Skip if it's already an IP address
                if (IPAddress.TryParse(hostPortion, out _))
                    return false;

                // Try DNS resolution
                var hostEntry = Dns.GetHostEntry(hostPortion);
                
                foreach (var address in hostEntry.AddressList)
                {
                    // Check if resolved IP is loopback
                    if (IPAddress.IsLoopback(address))
                    {
                        LogMessage($"  └─ DNS resolved '{hostPortion}' to loopback: {address}", false);
                        return true;
                    }

                    // Check if resolved IP is on local network interfaces
                    if (IsLocalNetworkInterface(address.ToString()))
                    {
                        LogMessage($"  └─ DNS resolved '{hostPortion}' to local interface: {address}", false);
                        return true;
                    }

                    // Check if resolved IP is in private ranges
                    if (IsPrivateOrLocalIPAddress(address.ToString()))
                    {
                        LogMessage($"  └─ DNS resolved '{hostPortion}' to private IP: {address}", false);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"  └─ DNS resolution failed for '{serverName}': {ex.Message}", false);
                // If DNS fails, we can't determine if it's local, so err on the side of caution
                return false;
            }
        }

        /// <summary>
        /// Performs comprehensive analysis and returns detailed information about why a connection
        /// is considered local or remote. Used for debugging and user transparency.
        /// </summary>
        private string AnalyzeConnectionType(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                return "❓ Empty server name - defaulting to LOCAL";

            var analysis = new List<string>
            {
                $"🔍 CONNECTION ANALYSIS FOR: '{serverName.Trim()}'",
                ""
            };

            string normalizedServer = serverName.Trim().ToLowerInvariant();
            bool isRemote = IsRemoteConnection(serverName); // This will log detailed steps

            analysis.Add($"📊 FINAL DETERMINATION: {(isRemote ? "🌐 REMOTE" : "🏠 LOCAL")}");
            analysis.Add("");
            
            if (isRemote)
            {
                analysis.Add("⚠️  SAFETY RECOMMENDATIONS:");
                analysis.Add("   • Verify you have authorization for this server");
                analysis.Add("   • Double-check server name spelling");
                analysis.Add("   • Consider testing on local environment first");
                analysis.Add("   • Ensure proper backups before proceeding");
            }
            else
            {
                analysis.Add("✅ SAFETY STATUS: Connection identified as local");
                analysis.Add("   • Lower risk for accidental remote modifications");
                analysis.Add("   • Still recommend backups for production data");
            }

            return string.Join("\n", analysis);
        }

        /// <summary>
        /// Provides a way for advanced users to override remote detection if needed.
        /// This should be used sparingly and with extreme caution.
        /// </summary>
        private bool ShouldTreatAsLocal(string serverName)
        {
            // This could be extended to check a configuration file or registry setting
            // for servers that should be treated as local despite appearing remote
            // For now, it's a placeholder for future enhancement
            
            // Example: Check if server is in a "trusted local servers" list
            // var trustedServers = GetTrustedLocalServers();
            // return trustedServers.Contains(serverName, StringComparer.OrdinalIgnoreCase);
            
            _ = serverName; // Suppress unused parameter warning - used in future implementation
            return false;
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
                    LogMessage($"Cleaned up temporary DACPAC file: {_tempDacpacPath}", false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Could not delete temporary DACPAC file {_tempDacpacPath}: {ex.Message}", false);
            }

            try
            {
                if (File.Exists(_tempBacpacPath))
                {
                    File.Delete(_tempBacpacPath);
                    LogMessage($"Cleaned up temporary BACPAC file: {_tempBacpacPath}", false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Could not delete temporary BACPAC file {_tempBacpacPath}: {ex.Message}", false);
            }
        }

        #endregion
    }
}