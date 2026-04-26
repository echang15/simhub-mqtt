using System;
using System.Windows;
using System.Windows.Controls;
using uPLibrary.Networking.M2Mqtt;

namespace SimHubMqttPlugin
{
    public partial class SettingsControl : UserControl
    {
        private LapTimeTrackerPlugin _plugin;

        public SettingsControl()
        {
            InitializeComponent();
        }

        public SettingsControl(LapTimeTrackerPlugin plugin) : this()
        {
            _plugin = plugin;
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (_plugin?.Settings != null)
            {
                DriverComboBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(_plugin.Settings.PreviousDrivers);
                DriverComboBox.Text = _plugin.Settings.CurrentDriver;
                BrokerIpTextBox.Text = _plugin.Settings.MqttBrokerIp;
                UsernameTextBox.Text = _plugin.Settings.MqttUsername;
                PasswordBox.Password = _plugin.Settings.MqttPassword;
                TopicTextBox.Text = _plugin.Settings.MqttTopic;
                RecentTopicTextBox.Text = _plugin.Settings.MqttRecentTopic;

                LoadLaps();
                LoadRecentLaps();
            }
        }

        public void RefreshData()
        {
            LoadLaps();
            LoadRecentLaps();
        }

        private void LoadLaps()
        {
            if (_plugin?.Settings?.BestLaps != null)
            {
                var lapRecords = new System.Collections.Generic.List<LapRecordDisplay>();
                foreach (var kvp in _plugin.Settings.BestLaps)
                {
                    var parts = kvp.Key.Split('|');
                    string driver = "Unknown";
                    string track = "Unknown";
                    string layout = "Unknown";
                    string car = "Unknown";

                    if (parts.Length == 4)
                    {
                        driver = parts[0];
                        track = parts[1];
                        layout = parts[2];
                        car = parts[3];
                    }
                    else
                    {
                        // Fallback for old format just in case
                        var oldParts = kvp.Key.Split(new[] { '_' }, 2);
                        track = oldParts.Length > 0 ? oldParts[0] : "Unknown";
                        car = oldParts.Length > 1 ? oldParts[1] : "Unknown";
                    }
                    
                    lapRecords.Add(new LapRecordDisplay
                    {
                        Driver = driver,
                        Track = track,
                        Layout = layout,
                        Car = car,
                        LapTime = kvp.Value.ToString(@"mm\:ss\.fff")
                    });
                }
                
                LapsDataGrid.ItemsSource = lapRecords;
            }
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Testing connection...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            
            try
            {
                var client = new MqttClient(BrokerIpTextBox.Text);
                string clientId = Guid.NewGuid().ToString();
                byte code = client.Connect(clientId, UsernameTextBox.Text, PasswordBox.Password);

                if (client.IsConnected)
                {
                    StatusTextBlock.Text = $"Success! Connected to {BrokerIpTextBox.Text}";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    client.Disconnect();
                }
                else
                {
                    StatusTextBlock.Text = $"Failed to connect. Code: {code}";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin != null)
            {
                string newDriver = DriverComboBox.Text;
                _plugin.Settings.CurrentDriver = newDriver;
                
                if (!string.IsNullOrWhiteSpace(newDriver) && !_plugin.Settings.PreviousDrivers.Contains(newDriver))
                {
                    _plugin.Settings.PreviousDrivers.Add(newDriver);
                    var itemsSource = DriverComboBox.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                    itemsSource?.Add(newDriver);
                }

                _plugin.Settings.MqttBrokerIp = BrokerIpTextBox.Text;
                _plugin.Settings.MqttUsername = UsernameTextBox.Text;
                _plugin.Settings.MqttPassword = PasswordBox.Password;
                _plugin.Settings.MqttTopic = TopicTextBox.Text;
                _plugin.Settings.MqttRecentTopic = RecentTopicTextBox.Text;

                _plugin.SavePluginSettings();

                // Auto-Reconnect
                _plugin.ConnectMqtt();
                
                SimHub.Logging.Current.Info("MQTT Settings saved and reconnected via UI");
                MessageBox.Show("Settings saved successfully and MQTT connection restarted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteDriverButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plugin != null && !string.IsNullOrWhiteSpace(DriverComboBox.Text))
            {
                string toDelete = DriverComboBox.Text;
                if (_plugin.Settings.PreviousDrivers.Contains(toDelete))
                {
                    _plugin.Settings.PreviousDrivers.Remove(toDelete);
                    var itemsSource = DriverComboBox.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                    itemsSource?.Remove(toDelete);
                    DriverComboBox.Text = "";
                    _plugin.SavePluginSettings();
                }
            }
        }

        private void LoadRecentLaps()
        {
            if (_plugin?.Settings?.RecentLaps != null)
            {
                var recentRecords = new System.Collections.Generic.List<RecentLapRecordDisplay>();
                foreach (var entry in _plugin.Settings.RecentLaps)
                {
                    var parts = entry.Split('|');
                    if (parts.Length == 6)
                    {
                        recentRecords.Add(new RecentLapRecordDisplay
                        {
                            Time = parts[0],
                            Driver = parts[1],
                            Track = parts[2],
                            Layout = parts[3],
                            Car = parts[4],
                            LapTime = parts[5]
                        });
                    }
                }
                RecentLapsDataGrid.ItemsSource = recentRecords;
            }
        }
    }

    public class RecentLapRecordDisplay
    {
        public string Time { get; set; }
        public string Driver { get; set; }
        public string Track { get; set; }
        public string Layout { get; set; }
        public string Car { get; set; }
        public string LapTime { get; set; }
    }

    public class LapRecordDisplay
    {
        public string Driver { get; set; }
        public string Track { get; set; }
        public string Layout { get; set; }
        public string Car { get; set; }
        public string LapTime { get; set; }
    }
}
