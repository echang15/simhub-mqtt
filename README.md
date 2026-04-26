# SimHub Home Assistant Lap Time Tracker Plugin

## Overview
A custom SimHub plugin designed to track your all-time best and recent lap times, supporting multiple drivers, and automatically publishing them to an MQTT broker (like Home Assistant) in real-time. 

With built-in Home Assistant Auto-Discovery support, building a dynamic dashboard to display your sim racing achievements has never been easier!

## Features
- **Multi-Driver Tracking:** Easily switch between different drivers to keep individual track records separate.
- **Recent Laps Dashboard:** View your 5 most recent laps across any track or car directly inside SimHub.
- **All-Time Best Lap Tracking:** Persistently saves personal best laps per driver, track, layout, and car combination.
- **Home Assistant Auto-Discovery:** Automatically creates `sensor.simhub_best_lap` and `sensor.simhub_recent_lap` in Home Assistant.
- **Real-Time MQTT Publishing:** Pushes JSON payloads containing Driver, Car, Track, Layout, and formatted Lap Time on every lap completion or new personal best.

## Installation
1. Compile the project in Visual Studio (`Release` mode using the provided FolderProfile publish configuration) or grab the pre-compiled `.dll`.
2. Copy `SimHubMqttPlugin.dll` (and `M2Mqtt.dll` if required) into your SimHub installation folder (usually `C:\Program Files (x86)\SimHub`).
3. Restart SimHub.
4. Go to **Settings -> Plugins** in SimHub and enable the **HA Lap Time Tracker** plugin.

## Configuration
Inside SimHub, click on **Home Assistant Lap Time Tracker** in the left sidebar menu. 

You will see two tabs:

### 1. MQTT Configuration
- **Broker IP:** Your MQTT Broker address (e.g., `homeassistant.local` or `192.168.1.100`).
- **Username / Password:** Your MQTT credentials.
- **Best Lap Topic:** The topic to publish personal bests to (default: `simhub/laps/best`).
- **Recent Lap Topic:** The topic to publish every completed lap to (default: `simhub/laps/recent`).
- *Note: Click **Save & Reconnect** to apply any changes to the connection.*

### 2. Dashboard & Laps
- **Current Driver:** Select or type a driver name to track times for that specific person.
- **Manage Drivers:** Easily add new drivers or delete existing ones.
- **Recent Laps:** Displays the last 5 completed laps across any game.
- **All-Time Best Laps:** Displays all recorded personal bests. You can delete specific entries using the trash can icon if a lap was recorded incorrectly.

## Home Assistant Integration
Because this plugin supports Home Assistant Auto-Discovery, as soon as SimHub successfully connects to your MQTT broker, two new entities will automatically appear in Home Assistant:

- `sensor.simhub_best_lap`
- `sensor.simhub_recent_lap`

These sensors use the formatted lap time as their primary state, but they also contain rich JSON attributes with every update:
```json
{
  "timestamp": "2026-04-26T15:00:00.0000000Z",
  "driver": "Player 1",
  "car": "Porsche 911 GT3 R",
  "track": "Nurburgring",
  "layout": "Nordschleife",
  "lap_time": "08:15.321"
}
```

You can use these attributes to build beautiful custom Lovelace cards, track history, or trigger smart home automations (e.g., flashing lights when you set a new PB!).

## Requirements
- SimHub (latest version recommended)
- An MQTT Broker (such as the Mosquitto Add-on in Home Assistant)
- .NET Framework 4.8 (standard for SimHub plugins)
