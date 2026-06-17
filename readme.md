![Logo](https://github.com/CodeNeedsCoffee/YAHAA/blob/main/YAHAA/Assets/Logo-HA.png?raw=true)

# YAHAA — Yet Another Home Assistant App (for Windows)

A two-way Windows companion app for [Home Assistant](https://www.home-assistant.io/). As a smart-home enthusiast who recently rebuilt my home lab without MQTT, I needed a companion app that works over the native HA WebSocket/REST APIs — and couldn't find one I liked, so I built my own.

> [!IMPORTANT]
> Windows 10 build 19041 (Version 2004, May 2020) or later is required.

---

## Features

- **System theme support** — follows Windows light/dark mode and accent color
- **Sensor reporting** — publishes PC state to Home Assistant as binary sensors:
  - **Active** — whether the PC has been used within the configured idle threshold
  - **Camera** — whether the webcam is currently in use
  - **Microphone** — whether the microphone is currently in use
  - **Camera or Microphone** — combined sensor (useful for detecting calls/meetings)
- **Trigger automations** — fire HA webhook automations directly from the app dashboard
- **Script bridge** — expose `.ps1` / `.bat` files in the `Scripts` folder as Home Assistant `input_button` helpers; press the button in HA to run the script on your PC
- **Optional location tracking** — report device location to Home Assistant
- **Guided setup wizard** — connect to your HA instance with a step-by-step flow; credentials are stored securely with Windows DPAPI

---

## Installation

The easiest way to install is via the **Microsoft Store** or **winget**:

```
winget install YAHAA
```

---

## Build & Run Locally

> [!TIP]
> The easiest path is to open the solution in Visual Studio and use **Debug** or **Deploy** from there.

> [!IMPORTANT]
> Enable **Developer Mode** in Windows Settings before installing a locally built MSIX.

**1. Clone the repo**

```bash
git clone https://github.com/CodeNeedsCoffee/YAHAA.git
cd YAHAA
```

**2. Build a package**

```bash
dotnet publish -p:PublishProfile=win-x64 -c Release
```

**3. Install the MSIX**

Navigate to the publish output folder and run the generated `.msix` installer.

---

## Feedback & Issues

Found a bug or have a feature request? [Open an issue](https://github.com/CodeNeedsCoffee/YAHAA/issues) and I'll get back to you as soon as I can.
