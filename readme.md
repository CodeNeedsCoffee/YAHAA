<div align="center">

![Logo](https://github.com/CodeNeedsCoffee/YAHAA/blob/main/YAHAA/Assets/Logo-HA.png?raw=true)

# YAHAA: Yet Another Home Assistant App (for Windows)

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Download-blue?logo=microsoft)](https://apps.microsoft.com/detail/9NNS5XJH665F)
[![Windows 10+](https://img.shields.io/badge/Windows-10%20%28v2004%2B%29%20%7C%2011-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A two-way Windows companion app for [Home Assistant](https://www.home-assistant.io/). As a smart-home enthusiast who recently rebuilt my home lab without MQTT, I needed a companion app that works over the native HA WebSocket/REST APIs — and couldn't find one I liked, so I built my own.

</div>

---

## Requirements

| Requirement | Details |
|---|---|
| **OS** | Windows 10 build 19041 (Version 2004, May 2020) or later |
| **Runtime** | [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/) — bundled when installed via the Store or MSIX; no separate install needed |
| **Home Assistant** | Any modern HA instance accessible over HTTP/HTTPS with a Long-Lived Access Token |

> [!TIP]
> Installing via the **Microsoft Store** or **winget** handles all runtime dependencies automatically. For building locally, see [recommended dev environment setup](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=wingetconfig).

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

The easiest way to install is via the **[Microsoft Store](https://apps.microsoft.com/detail/9NNS5XJH665F)** or **winget**:

```
winget install YAHAA
```

---

## Build & Run Locally

> [!TIP]
> The easiest path is to open the solution in Visual Studio and use **Debug** or **Deploy** from there. If you haven't set up a Windows development environment before, follow the [Windows app development setup guide](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=wingetconfig) to install Visual Studio, the Windows App SDK workload, and .NET in one step.

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

## Building for the Microsoft Store

> [!NOTE]
> **Contributors don't need to do this.** Store submission is handled by the maintainer as part of the release process — see [Contributing](#contributing) below. These instructions are here for reference only.

> [!IMPORTANT]
> Before building for Store submission, you must reserve your app name in [Partner Center](https://partner.microsoft.com/dashboard) and update `Package.appxmanifest` with the identity values from **App management → Product identity**:
> ```xml
> <Identity Name="<PackageIdentityName>"
>           Publisher="<PublisherDN>"
>           Version="1.0.0.0" />
> <Properties>
>   <PublisherDisplayName>Your Display Name</PublisherDisplayName>
> </Properties>
> ```

**1. Clone the repo** (if not already done)

```bash
git clone https://github.com/CodeNeedsCoffee/YAHAA.git
cd YAHAA
```

**2. Build the Store upload package**

```powershell
msbuild YAHAA\YAHAA.csproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:PublishProfile=store-upload `
  /p:AppxBundle=Always `
  /p:AppxBundlePlatforms="x86|x64|arm64" `
  /t:Publish
```

This uses the `store-upload` publish profile, which:
- Disables local code-signing (Partner Center signs the package)
- Bundles all three architectures (x86, x64, ARM64) into a single file
- Outputs a `.msixupload` file ready for Partner Center

**3. Locate the output**

The `.msixupload` file will be in:
```
YAHAA\bin\Release\store-upload\
```

**4. Submit to Partner Center**

Upload the `.msixupload` file under **Packages** in your Partner Center submission.

---

## Contributing

Contributions are welcome! Please follow the branch workflow below so releases stay coordinated with the Microsoft Store submission process.

### Branch workflow

```
feature/* or fix/*  →  beta  →  main
```

1. **Open a PR into `beta`** — all feature branches and fixes target `beta`, not `main`. This is where code review and testing happen.
2. **`beta` → `main` PR** — once a set of changes is ready to ship, a PR from `beta` into `main` is opened. **This PR is not merged until Microsoft finishes processing the Store submission and the build is live.**
3. **Store upload** — the maintainer handles building the `.msixupload` and submitting it to Partner Center after the `beta` → `main` PR is opened. You don't need to do anything here.

> [!IMPORTANT]
> Do not target `main` directly in your PRs. Changes merged to `main` represent what is live in the Microsoft Store.

---

## Feedback & Issues

Found a bug or have a feature request? [Open an issue](https://github.com/CodeNeedsCoffee/YAHAA/issues) and I'll get back to you as soon as I can.
