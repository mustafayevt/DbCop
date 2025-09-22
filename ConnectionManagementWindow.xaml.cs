using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace DbCop
{
    public partial class ConnectionManagementWindow : Window
    {
        public List<DatabaseConnection> Connections { get; private set; }
        private DatabaseConnection? _currentConnection;

        public ConnectionManagementWindow(List<DatabaseConnection> connections)
        {
            InitializeComponent();
            Connections = new List<DatabaseConnection>(connections);
            RefreshConnectionsList();
            UpdateAuthenticationUI();
        }

        private void RefreshConnectionsList()
        {
            ConnectionsListBox.Items.Clear();
            foreach (var connection in Connections)
            {
                ConnectionsListBox.Items.Add(connection);
            }
        }

        private void ConnectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectionsListBox.SelectedItem is DatabaseConnection connection)
            {
                LoadConnectionDetails(connection);
                ConnectionDetailsGrid.IsEnabled = true;
                RemoveConnectionButton.IsEnabled = true;
            }
            else
            {
                ClearConnectionDetails();
                ConnectionDetailsGrid.IsEnabled = false;
                RemoveConnectionButton.IsEnabled = false;
            }
        }

        private void LoadConnectionDetails(DatabaseConnection connection)
        {
            _currentConnection = connection;
            ConnectionNameTextBox.Text = connection.Name;
            ServerTextBox.Text = connection.Server;
            UseWindowsAuthCheckBox.IsChecked = connection.UseWindowsAuth;
            UserIdTextBox.Text = connection.UserId;
            PasswordBox.Password = connection.GetDecryptedPassword();
            TestResultTextBlock.Text = "";
            UpdateAuthenticationUI();
        }

        private void ClearConnectionDetails()
        {
            _currentConnection = null;
            ConnectionNameTextBox.Text = "";
            ServerTextBox.Text = "";
            UseWindowsAuthCheckBox.IsChecked = false;
            UserIdTextBox.Text = "";
            PasswordBox.Password = "";
            TestResultTextBlock.Text = "";
            UpdateAuthenticationUI();
        }

        private void UseWindowsAuthCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateAuthenticationUI();
        }

        private void UseWindowsAuthCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateAuthenticationUI();
        }

        private void UpdateAuthenticationUI()
        {
            bool useWindowsAuth = UseWindowsAuthCheckBox.IsChecked ?? false;
            UserIdLabel.Visibility = useWindowsAuth ? Visibility.Collapsed : Visibility.Visible;
            UserIdTextBox.Visibility = useWindowsAuth ? Visibility.Collapsed : Visibility.Visible;
            PasswordLabel.Visibility = useWindowsAuth ? Visibility.Collapsed : Visibility.Visible;
            PasswordBox.Visibility = useWindowsAuth ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AddConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var newConnection = new DatabaseConnection
            {
                Name = $"New Connection {Connections.Count + 1}",
                Server = "localhost\\SQLEXPRESS",
                UseWindowsAuth = true
            };

            Connections.Add(newConnection);
            RefreshConnectionsList();
            ConnectionsListBox.SelectedItem = newConnection;
        }

        private void RemoveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsListBox.SelectedItem is DatabaseConnection connection)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to remove the connection '{connection.Name}'?",
                    "Confirm Remove",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Connections.Remove(connection);
                    RefreshConnectionsList();
                    ClearConnectionDetails();
                }
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnectionDetails())
                return;

            TestConnectionButton.IsEnabled = false;
            TestResultTextBlock.Text = "Testing connection...";

            try
            {
                string connectionString;
                if (UseWindowsAuthCheckBox.IsChecked ?? false)
                {
                    connectionString = $"Server={ServerTextBox.Text};Integrated Security=true;TrustServerCertificate=true;Encrypt=false;Connection Timeout=5;";
                }
                else
                {
                    connectionString = $"Server={ServerTextBox.Text};User Id={UserIdTextBox.Text};Password={PasswordBox.Password};TrustServerCertificate=true;Encrypt=false;Connection Timeout=5;";
                }

                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    TestResultTextBlock.Text = "✅ Connection successful!";
                    TestResultTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                TestResultTextBlock.Text = $"❌ Connection failed: {ex.Message}";
                TestResultTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void SaveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnectionDetails())
                return;

            if (_currentConnection == null)
            {
                MessageBox.Show("No connection selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check for duplicate names (excluding current connection)
            if (Connections.Any(c => c != _currentConnection && c.Name.Equals(ConnectionNameTextBox.Text, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A connection with this name already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentConnection.Name = ConnectionNameTextBox.Text;
            _currentConnection.Server = ServerTextBox.Text;
            _currentConnection.UseWindowsAuth = UseWindowsAuthCheckBox.IsChecked ?? false;
            _currentConnection.UserId = UserIdTextBox.Text;
            _currentConnection.SetEncryptedPassword(PasswordBox.Password);

            RefreshConnectionsList();
            TestResultTextBlock.Text = "💾 Connection saved!";
            TestResultTextBlock.Foreground = System.Windows.Media.Brushes.Blue;
        }

        private bool ValidateConnectionDetails()
        {
            if (string.IsNullOrWhiteSpace(ConnectionNameTextBox.Text))
            {
                MessageBox.Show("Connection name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(ServerTextBox.Text))
            {
                MessageBox.Show("Server name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!(UseWindowsAuthCheckBox.IsChecked ?? false))
            {
                if (string.IsNullOrWhiteSpace(UserIdTextBox.Text))
                {
                    MessageBox.Show("User ID is required for SQL Server authentication.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(PasswordBox.Password))
                {
                    MessageBox.Show("Password is required for SQL Server authentication.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            return true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ExportConnectionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Connections.Count == 0)
            {
                MessageBox.Show("No connections to export.", "Export Connections",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ask user about password inclusion
            var includePasswordsResult = MessageBox.Show(
                "Do you want to include passwords in the export?\n\n" +
                "• YES: Passwords included (only works on same machine/user)\n" +
                "• NO: Passwords excluded (safer, you'll need to re-enter them)\n" +
                "• CANCEL: Cancel export",
                "Export Options",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (includePasswordsResult == MessageBoxResult.Cancel)
                return;

            bool includePasswords = includePasswordsResult == MessageBoxResult.Yes;

            // Show save file dialog
            var saveDialog = new SaveFileDialog
            {
                Title = "Export Database Connections",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"DbCop_Connections_{DateTime.Now:yyyy-MM-dd}.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    ExportConnectionsToFile(saveDialog.FileName, includePasswords);
                    MessageBox.Show($"Successfully exported {Connections.Count} connection(s) to:\n{saveDialog.FileName}",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export connections:\n{ex.Message}",
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportConnectionsButton_Click(object sender, RoutedEventArgs e)
        {
            // Show choice dialog: File or Paste
            var choiceResult = MessageBox.Show(
                "How would you like to import connections?\n\n" +
                "• YES: Import from file\n" +
                "• NO: Paste JSON text\n" +
                "• CANCEL: Cancel import",
                "Import Method",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choiceResult == MessageBoxResult.Cancel)
                return;

            try
            {
                List<DatabaseConnection> importedConnections;

                if (choiceResult == MessageBoxResult.Yes)
                {
                    // Import from file
                    importedConnections = ImportConnectionsFromFile();
                }
                else
                {
                    // Import from paste
                    importedConnections = ImportConnectionsFromPaste();
                }

                if (importedConnections != null && importedConnections.Count > 0)
                {
                    ProcessImportedConnections(importedConnections);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import connections:\n{ex.Message}",
                    "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportConnectionsToFile(string filePath, bool includePasswords)
        {
            var exportData = new
            {
                exportDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                exportedBy = "DbCop Database Sync Tool",
                includePasswords = includePasswords,
                connections = Connections.Select(conn => new
                {
                    name = conn.Name,
                    server = conn.Server,
                    userId = conn.UserId,
                    passwordEncrypted = includePasswords ? conn.PasswordEncrypted : "",
                    useWindowsAuth = conn.UseWindowsAuth
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
        }

        private List<DatabaseConnection> ImportConnectionsFromFile()
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Import Database Connections",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openDialog.ShowDialog() == true)
            {
                string jsonContent = File.ReadAllText(openDialog.FileName);
                return ParseImportJson(jsonContent);
            }

            return null;
        }

        private List<DatabaseConnection> ImportConnectionsFromPaste()
        {
            var pasteDialog = new ImportPasteDialog();
            if (pasteDialog.ShowDialog() == true)
            {
                return ParseImportJson(pasteDialog.JsonContent);
            }

            return null;
        }

        private List<DatabaseConnection> ParseImportJson(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new ArgumentException("JSON content is empty");

            JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement connectionsArray;

            // Handle both formats: direct array or object with connections property
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Direct array format (raw connections)
                connectionsArray = doc.RootElement;
            }
            else if (doc.RootElement.TryGetProperty("connections", out var connectionsProperty))
            {
                // DbCop export format (object with connections property)
                connectionsArray = connectionsProperty;
            }
            else
            {
                throw new ArgumentException("JSON must be either an array of connections or an object with 'connections' property");
            }

            var importedConnections = new List<DatabaseConnection>();

            foreach (var connElement in connectionsArray.EnumerateArray())
            {
                var connection = new DatabaseConnection();

                // Handle both property name formats (capitalized and lowercase)
                connection.Name = GetStringProperty(connElement, "name", "Name");
                connection.Server = GetStringProperty(connElement, "server", "Server");
                connection.UserId = GetStringProperty(connElement, "userId", "UserId") ?? "";
                connection.UseWindowsAuth = GetBoolProperty(connElement, "useWindowsAuth", "UseWindowsAuth");

                // Handle password if present
                string encryptedPass = GetStringProperty(connElement, "passwordEncrypted", "PasswordEncrypted");
                if (!string.IsNullOrEmpty(encryptedPass))
                {
                    connection.PasswordEncrypted = encryptedPass;
                }

                importedConnections.Add(connection);
            }

            return importedConnections;
        }

        private string GetStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (string propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop))
                {
                    return prop.GetString() ?? "";
                }
            }
            return "";
        }

        private bool GetBoolProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (string propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop))
                {
                    return prop.GetBoolean();
                }
            }
            return false;
        }

        private void ProcessImportedConnections(List<DatabaseConnection> importedConnections)
        {
            var duplicates = new List<(DatabaseConnection imported, DatabaseConnection existing)>();
            var newConnections = new List<DatabaseConnection>();

            // Check for duplicates
            foreach (var imported in importedConnections)
            {
                var existing = Connections.FirstOrDefault(c =>
                    c.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    duplicates.Add((imported, existing));
                }
                else
                {
                    newConnections.Add(imported);
                }
            }

            // Handle duplicates if any
            if (duplicates.Count > 0)
            {
                var duplicateResult = MessageBox.Show(
                    $"Found {duplicates.Count} connection(s) with duplicate names:\n" +
                    string.Join("\n", duplicates.Take(5).Select(d => $"• {d.imported.Name}")) +
                    (duplicates.Count > 5 ? $"\n... and {duplicates.Count - 5} more" : "") +
                    "\n\nDo you want to overwrite existing connections?\n" +
                    "• YES: Overwrite existing\n" +
                    "• NO: Skip duplicates\n" +
                    "• CANCEL: Cancel import",
                    "Duplicate Connections Found",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (duplicateResult == MessageBoxResult.Cancel)
                    return;

                if (duplicateResult == MessageBoxResult.Yes)
                {
                    // Overwrite existing connections
                    foreach (var (imported, existing) in duplicates)
                    {
                        existing.Server = imported.Server;
                        existing.UserId = imported.UserId;
                        existing.PasswordEncrypted = imported.PasswordEncrypted;
                        existing.UseWindowsAuth = imported.UseWindowsAuth;
                    }
                }
                // If No, duplicates are just ignored (not added to newConnections)
            }

            // Add new connections
            Connections.AddRange(newConnections);

            // Refresh the UI
            RefreshConnectionsList();

            // Show success message
            int totalImported = newConnections.Count + (duplicates.Count > 0 ? duplicates.Count : 0);
            MessageBox.Show(
                $"Import completed:\n" +
                $"• New connections: {newConnections.Count}\n" +
                $"• Duplicates handled: {duplicates.Count}\n" +
                $"• Total processed: {totalImported}",
                "Import Successful",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}