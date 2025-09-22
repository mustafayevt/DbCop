using System;
using System.Text.Json;
using System.Windows;

namespace DbCop
{
    public partial class ImportPasteDialog : Window
    {
        public string JsonContent { get; private set; } = "";

        public ImportPasteDialog()
        {
            InitializeComponent();
            JsonTextBox.Focus();
        }

        private void JsonTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Reset validation status when text changes
            ValidationStatusTextBlock.Text = "JSON content modified - click 'Validate JSON' to check format";
            ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            ImportButton.IsEnabled = false;
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            string jsonText = JsonTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                ValidationStatusTextBlock.Text = "❌ JSON content is empty";
                ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                ImportButton.IsEnabled = false;
                return;
            }

            try
            {
                // Try to parse the JSON
                JsonDocument doc = JsonDocument.Parse(jsonText);
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
                    if (connectionsProperty.ValueKind != JsonValueKind.Array)
                    {
                        ValidationStatusTextBlock.Text = "❌ 'connections' property must be an array";
                        ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                        ImportButton.IsEnabled = false;
                        return;
                    }
                    connectionsArray = connectionsProperty;
                }
                else
                {
                    ValidationStatusTextBlock.Text = "❌ JSON must be either an array of connections or an object with 'connections' property";
                    ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    ImportButton.IsEnabled = false;
                    return;
                }

                int connectionCount = connectionsArray.GetArrayLength();

                // Validate each connection has required fields
                int validConnections = 0;
                foreach (var connElement in connectionsArray.EnumerateArray())
                {
                    bool hasName = (connElement.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString())) ||
                                   (connElement.TryGetProperty("Name", out var nameCapProp) && !string.IsNullOrWhiteSpace(nameCapProp.GetString()));

                    bool hasServer = (connElement.TryGetProperty("server", out var serverProp) && !string.IsNullOrWhiteSpace(serverProp.GetString())) ||
                                     (connElement.TryGetProperty("Server", out var serverCapProp) && !string.IsNullOrWhiteSpace(serverCapProp.GetString()));

                    if (hasName && hasServer)
                    {
                        validConnections++;
                    }
                }

                if (validConnections == 0)
                {
                    ValidationStatusTextBlock.Text = "❌ No valid connections found (Name and Server are required)";
                    ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    ImportButton.IsEnabled = false;
                }
                else if (validConnections < connectionCount)
                {
                    ValidationStatusTextBlock.Text = $"⚠️ {validConnections} of {connectionCount} connections are valid (some missing Name/Server)";
                    ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
                    ImportButton.IsEnabled = true;
                }
                else
                {
                    ValidationStatusTextBlock.Text = $"✅ JSON is valid - {connectionCount} connection(s) ready to import";
                    ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    ImportButton.IsEnabled = true;
                }

                doc.Dispose();
            }
            catch (JsonException ex)
            {
                ValidationStatusTextBlock.Text = $"❌ Invalid JSON format: {ex.Message}";
                ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                ImportButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                ValidationStatusTextBlock.Text = $"❌ Validation error: {ex.Message}";
                ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                ImportButton.IsEnabled = false;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            JsonTextBox.Clear();
            ValidationStatusTextBlock.Text = "JSON content cleared";
            ValidationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            ImportButton.IsEnabled = false;
            JsonTextBox.Focus();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            JsonContent = JsonTextBox.Text.Trim();
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