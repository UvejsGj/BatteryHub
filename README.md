# BatteryHub

A Windows 10/11 tray app that shows the battery level of every connected gamepad and
Bluetooth audio device in one place: DualShock 4, DualSense, Xbox pads, generic BLE
devices, Bluetooth headsets and AirPods.

BatteryHub is read-only. It never changes device state (no lightbar, rumble or mode
changes), needs no admin rights and installs no drivers.

## Status

| Milestone | State |
|---|---|
| 1. Scaffold: solution, Probe lists HID devices | Built, waiting for Probe output from real hardware |
| 2. Sony pads | Not started |
| 3. Tray shell | Not started |
| 4. Xbox, generic BLE, HFP headsets | Not started |
| 5. AirPods | Not started |
| 6. Polish | Not started |

## Hardware

A reader counts as tested only once its Probe output has been captured on that hardware
and added to the test fixtures. Everything else is built from the protocol references.

| Device | Connection | Status |
|---|---|---|
| DualShock 4 | USB / Bluetooth | untested |
| DualSense, DualSense Edge | USB / Bluetooth | untested |
| Xbox pads (XInput) | wireless | untested |
| Generic BLE Battery Service | Bluetooth LE | untested |
| Bluetooth headsets (HFP) | Bluetooth | untested |
| AirPods | BLE advertisements | untested |

## Layout

- `src/BatteryHub.Core`: models, parsers, providers, monitor. No UI references.
- `src/BatteryHub.Probe`: console tool that lists devices and dumps raw reports. This is how hardware gets verified.
- `src/BatteryHub.App`: the WPF tray app.
- `tests/BatteryHub.Tests`: parser tests against byte fixtures.

## Build

Needs the .NET 10 SDK. The solution targets `net10.0-windows10.0.19041.0`; it builds on
any OS but only runs on Windows.

```
dotnet build
dotnet test
```

Warnings are errors.

## Probe

```
dotnet run --project src/BatteryHub.Probe -- hid
dotnet run --project src/BatteryHub.Probe -- hid --vid 054C
```

`hid` lists every HID top-level collection with its VID, PID, top-level usage, maximum
report lengths and the report IDs its descriptor declares. Lengths are in bytes and
include the report ID byte. The connection column is a guess from the device path.

## Protocol references

Linux `drivers/hid/hid-playstation.c`, DS4Windows, AirPodsDesktop and OpenPods. They are
GPL; BatteryHub reads them for offsets and behaviour and does not copy their code.
