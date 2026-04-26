using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using GameReaderCommon;
using SimHub.Plugins;
using Newtonsoft.Json;
using uPLibrary.Networking.M2Mqtt;

namespace SimHubMqttPlugin
{
    // Settings class for the plugin
    public class LapTimeSettings
    {
        // Topic and connection configuration for MQTT
        public string MqttBrokerIp { get; set; } = "homeassistant.local";
        public string MqttUsername { get; set; } = "mqtt-user";
        public string MqttPassword { get; set; } = "mqtt-user";
        public string MqttTopic { get; set; } = "simhub/laps/best";
        public string MqttRecentTopic { get; set; } = "simhub/laps/recent";
        
        // Driver Tracking
        public string CurrentDriver { get; set; } = "Player 1";
        public List<string> PreviousDrivers { get; set; } = new List<string> { "Player 1" };
        
        // Recent Laps tracking (stored as Time|Driver|Track|Layout|Car|LapTime)
        public List<string> RecentLaps { get; set; } = new List<string>();
        
        // Dictionary mapping "Driver|TrackId|TrackCode|CarId" -> Best Lap Time (in TimeSpan format)
        // Kept public so it serializes automatically with SimHub settings
        public Dictionary<string, TimeSpan> BestLaps { get; set; } = new Dictionary<string, TimeSpan>();
    }

    [PluginDescription("Tracks and publishes all-time best lap times via MQTT")]
    [PluginAuthor("echang15")]
    [PluginName("HA Lap Time Tracker")]
    public class LapTimeTrackerPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public LapTimeSettings Settings;
        private MqttClient mqttClient;
        public PluginManager PluginManager { get; set; }
        
        public string LeftMenuTitle => "Home Assistant Lap Time Tracker";
        public System.Windows.Media.ImageSource PictureIcon => null;

        private SettingsControl _settingsControl;

        /// <summary>
        /// Returns the WPF control to be used in the SimHub plugin UI.
        /// You can build a real UI later to configure the MQTT Settings.
        /// </summary>
        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            _settingsControl = new SettingsControl(this);
            return _settingsControl;
        }

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("Starting Home Assistant Lap Time Tracker plugin");
            this.PluginManager = pluginManager;
            
            // Load settings
            Settings = this.ReadCommonSettings<LapTimeSettings>("LapTimeTrackerSettings", () => new LapTimeSettings());
            
            // Clean slate: remove all old lap records that do not use the new format (delimited by '|')
            var keysToRemove = new List<string>();
            foreach (var key in Settings.BestLaps.Keys)
            {
                if (!key.Contains("|"))
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                Settings.BestLaps.Remove(key);
            }
            if (keysToRemove.Count > 0)
            {
                SavePluginSettings();
            }
            
            ConnectMqtt();
        }

        /// <summary>
        /// Initializes the MQTT Client based on the current settings
        /// </summary>
        public void ConnectMqtt()
        {
            // Close existing connection if reconnecting
            if (mqttClient != null && mqttClient.IsConnected)
            {
                mqttClient.Disconnect();
            }

            try 
            {
                if (!string.IsNullOrEmpty(Settings.MqttBrokerIp))
                {
                    mqttClient = new MqttClient(Settings.MqttBrokerIp);
                    string clientId = Guid.NewGuid().ToString();
                    byte code = mqttClient.Connect(clientId, Settings.MqttUsername, Settings.MqttPassword);
                    SimHub.Logging.Current.Info($"MQTT Client Connected to {Settings.MqttBrokerIp} with status code {code}");
                    
                    if (mqttClient.IsConnected)
                    {
                        PublishHomeAssistantDiscovery();
                    }
                }
            } 
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("MQTT Connection Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Called when the plugin is stopped. Clean up routines go here.
        /// </summary>
        public void End(PluginManager pluginManager)
        {
            SavePluginSettings();
            if (mqttClient != null && mqttClient.IsConnected)
            {
                mqttClient.Disconnect();
            }
        }

        /// <summary>
        /// Saves the current plugin settings
        /// </summary>
        public void SavePluginSettings()
        {
            this.SaveCommonSettings("LapTimeTrackerSettings", Settings);
        }

        /// <summary>
        /// Called at every SimHub telemetry tick
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning || data.NewData == null) return;

            // Wait until we have a valid TrackId and CarId
            string trackId = data.NewData.TrackId;
            string carId = data.NewData.CarId;
            string trackCode = data.NewData.TrackCode ?? "Unknown";

            if (string.IsNullOrEmpty(trackId) || string.IsNullOrEmpty(carId)) return;

            // Check if a lap was just completed
            if (data.OldData != null)
            {
                if (data.NewData.CompletedLaps > data.OldData.CompletedLaps && data.NewData.LastLapTime.TotalMilliseconds > 0)
                {
                    TimeSpan lastLap = data.NewData.LastLapTime;
                    string formattedRecentLap = lastLap.ToString(@"mm\:ss\.fff");
                    SimHub.Logging.Current.Info($"Lap Completed for {trackId} ({trackCode})_{carId} by {Settings.CurrentDriver}: {formattedRecentLap}");
                    PublishMqtt(trackId, trackCode, carId, Settings.CurrentDriver, formattedRecentLap, Settings.MqttRecentTopic);

                    // Add to Recent Laps
                    string currentTime = DateTime.Now.ToString("HH:mm:ss");
                    string recentLapEntry = $"{currentTime}|{Settings.CurrentDriver}|{trackId}|{trackCode}|{carId}|{formattedRecentLap}";
                    Settings.RecentLaps.Insert(0, recentLapEntry);
                    while (Settings.RecentLaps.Count > 5)
                    {
                        Settings.RecentLaps.RemoveAt(Settings.RecentLaps.Count - 1);
                    }
                    SavePluginSettings();

                    if (_settingsControl != null)
                    {
                        _settingsControl.Dispatcher.BeginInvoke(new Action(() => {
                            _settingsControl.RefreshData();
                        }));
                    }
                }
            }

            // Only run logic if there is actually a best lap time in the current session
            if (data.NewData.BestLapTime.TotalMilliseconds > 0)
            {
                TimeSpan currentBest = data.NewData.BestLapTime;
                string recordKey = $"{Settings.CurrentDriver}|{trackId}|{trackCode}|{carId}";

                bool isNewRecord = false;

                if (!Settings.BestLaps.ContainsKey(recordKey))
                {
                    isNewRecord = true;
                }
                else
                {
                    TimeSpan allTimeBest = Settings.BestLaps[recordKey];
                    // Strict comparison - lap has to be faster
                    if (currentBest < allTimeBest)
                    {
                        isNewRecord = true;
                    }
                }

                if (isNewRecord)
                {
                    Settings.BestLaps[recordKey] = currentBest;
                    SavePluginSettings();
                    
                    string formattedLapTime = currentBest.ToString(@"mm\:ss\.fff");
                    SimHub.Logging.Current.Info($"New All-Time Best Lap for {recordKey}: {formattedLapTime}");
                    
                    PublishMqtt(trackId, trackCode, carId, Settings.CurrentDriver, formattedLapTime, Settings.MqttTopic);

                    if (_settingsControl != null)
                    {
                        _settingsControl.Dispatcher.BeginInvoke(new Action(() => {
                            _settingsControl.RefreshData();
                        }));
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper to publish the HA discovery payload
        /// </summary>
        private void PublishHomeAssistantDiscovery()
        {
            if (mqttClient == null || !mqttClient.IsConnected) return;

            try
            {
                // Best Lap Sensor
                var bestPayload = new
                {
                    name = "SimHub Best Lap",
                    unique_id = "simhub_best_lap",
                    state_topic = Settings.MqttTopic,
                    value_template = "{{ value_json.lap_time }}",
                    json_attributes_topic = Settings.MqttTopic,
                    json_attributes_template = "{{ value_json | tojson }}",
                    icon = "mdi:flag-checkered",
                    device = new
                    {
                        identifiers = new[] { "simhub_mqtt" },
                        name = "SimHub Lap Tracker",
                        manufacturer = "SimHub"
                    }
                };

                string discoveryTopic = "homeassistant/sensor/simhub_best_lap/config";
                mqttClient.Publish(discoveryTopic, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(bestPayload)), 
                                   uPLibrary.Networking.M2Mqtt.Messages.MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, 
                                   true);

                // Recent Lap Sensor
                var recentPayload = new
                {
                    name = "SimHub Recent Lap",
                    unique_id = "simhub_recent_lap",
                    state_topic = Settings.MqttRecentTopic,
                    value_template = "{{ value_json.lap_time }}",
                    json_attributes_topic = Settings.MqttRecentTopic,
                    json_attributes_template = "{{ value_json | tojson }}",
                    icon = "mdi:timer-outline",
                    device = new
                    {
                        identifiers = new[] { "simhub_mqtt" },
                        name = "SimHub Lap Tracker",
                        manufacturer = "SimHub"
                    }
                };
                
                string recentDiscoveryTopic = "homeassistant/sensor/simhub_recent_lap/config";
                mqttClient.Publish(recentDiscoveryTopic, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(recentPayload)), 
                                   uPLibrary.Networking.M2Mqtt.Messages.MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, 
                                   true);
                                   
                SimHub.Logging.Current.Info("Published Home Assistant auto-discovery payloads.");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("Failed to publish HA discovery: " + ex.Message);
            }
        }

        private void PublishMqtt(string trackId, string trackCode, string carId, string driver, string formattedLapTime, string topic)
        {
            if (mqttClient == null || !mqttClient.IsConnected) return;

            var payload = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                driver = driver,
                car = carId,
                track = trackId,
                layout = trackCode,
                lap_time = formattedLapTime
            };

            string jsonPayload = JsonConvert.SerializeObject(payload);

            try
            {
                mqttClient.Publish(topic, Encoding.UTF8.GetBytes(jsonPayload), 
                                   uPLibrary.Networking.M2Mqtt.Messages.MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, 
                                   false);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("Failed to publish MQTT message: " + ex.Message);
            }
        }
    }
}
