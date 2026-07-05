# Order Tracker

Order Tracker is a lightweight Windows desktop app for keeping online purchases organized from checkout to delivery. It tracks orders, accounts, reusable items, costs, shipping links, and spending trends in one focused WPF workspace.

## Highlights

- Dashboard metrics for open orders, total spend, monthly spend, completed orders, and status breakdowns.
- Spend charts grouped by month, year, and merchant.
- Full order tracking with account/email, merchant, order number/link, item, quantity, unit price, shipping, tax, other costs, important dates, notes, status, and multiple tracking numbers.
- Searchable, sortable, and groupable Orders page with quick actions for duplicate, delete, complete, link opening, and copy-all-tracking.
- Account presets with favorites, merchant hints, notes, usage counts, search, sort, apply, edit, duplicate, and delete.
- Items with defaults for quantity, price, shipping, tax, category, merchant hint, favorites, notes, usage counts, search, sort, apply, edit, duplicate, and delete.
- Carrier recognition for UPS, FedEx, USPS, and Amazon Logistics tracking formats.
- Amazon order ID support for opening Amazon order detail pages directly.
- Theme support for Light, Dark, and OLED modes.
- Settings for browser preference, autosave, storage location, remembered window placement, optional Discord webhook stats, and Orders-list column visibility.

## Tech Stack

- C# / WPF
- .NET `net10.0-windows`
- Local MVVM helpers with minimal dependencies
- JSON persistence under `%APPDATA%\OrderTrackerDesktop\orders.json`

## Getting Started

### Requirements

- Windows
- .NET SDK with `net10.0-windows` support

### Run From Source

```powershell
dotnet run --project .\OrderTracker.Desktop\OrderTracker.Desktop.csproj
```

### Create A Runnable Build

```powershell
.\build.ps1
```

The build script publishes the app to:

```text
.\build\OrderTracker.Desktop.exe
```

Optional build arguments:

```powershell
.\build.ps1 -Configuration Release -Runtime win-x64
.\build.ps1 -SelfContained
```

### Create An Installer

Install Inno Setup 6, then run:

```powershell
.\build.ps1 -Installer
```

The installer is written to:

```text
.\installer-output\OrderTrackerSetup.exe
```

Use `.\build.ps1 -Installer -SelfContained` for a larger installer that includes the .NET runtime.

## Project Layout

```text
OrderTracker.Desktop/
  Commands/      Command helpers
  Converters/    WPF value converters
  Models/        App, order, preset, metric, and settings models
  Services/      Persistence, browser launching, carrier recognition, Discord webhook
  Utilities/     Observable and binding helpers
  ViewModels/    Main app state, filtering, grouping, charts, commands, persistence flow
  Assets/        App icon PNG and ICO resources
  MainWindow.*   Primary WPF UI
installer/       Inno Setup 6 installer script
build.ps1        Publish script
build.cmd        Convenience wrapper
```

## Notes

- The app stores user data locally as JSON; no server is required.
- The Settings page is intentionally a first-class page, not a popup.
- Orders-list column visibility is user-configurable from Settings.
- Generated build folders such as `bin/`, `obj/`, and `build/` are ignored by Git.
