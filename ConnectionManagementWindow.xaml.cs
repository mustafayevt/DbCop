using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;

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
    }
}