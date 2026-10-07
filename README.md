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
| 2. Sony pads | Built, waiting for Probe output from real hardware |
| 3. Tray shell | Built, waiting to be tried on Windows |
| 4. Xbox, generic BLE, HFP headsets | Not started |
| 5. AirPods | Not started |
| 6. Polish | Not started |

## Hardware

A reader counts as tested only once its Probe output has been captured on that hardware
and added to the test fixtures. Everything else is built from the protocol references.

Hardware on hand for testing: a DualShock 4 and AirPods Pro 3.

| Device | Connection | Hardware on hand | Status |
|---|---|---|---|
| DualShock 4 | USB / Bluetooth | yes | untested, awaiting Probe output |
| Sony wireless adapter | USB | no | untested |
| DualSense, DualSense Edge | USB / Bluetooth | no | untested |
| Xbox pads (XInput) | wireless | no | untested |
| Generic BLE Battery Service | Bluetooth LE | no | untested |
| Bluetooth headsets (HFP) | Bluetooth | no | untested |
| AirPods | BLE advertisements | AirPods Pro 3 | untested |

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

## Tray app

```
dotnet run --project src/BatteryHub.App
```

BatteryHub runs in the notification area. Left-click the battery icon for the list of
devices with their level and charge state; hovering shows the same as a tooltip.
Right-click for:

- **Refresh**: read every device now instead of waiting for the next poll.
- **Start with Windows**: adds or removes BatteryHub in your user's Run key
  (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). No admin rights needed. If Task
  Manager has disabled the entry, it shows as off.
- **Read Bluetooth PlayStation controllers**: see "Sony pads over Bluetooth" below.
- **Exit**.

Only one copy runs per Windows session; starting it again opens the running copy's list.
Settings live in `%AppData%\BatteryHub\settings.json`. If that file is not valid JSON it is
renamed to `settings.json.bad` and defaults are used; if it cannot be read at all, defaults
are used and the file is left as it is.

Keyboard: Win+B, arrow to the battery icon, then Enter opens the list and the Menu key (or
Shift+F10) opens the menu.

## Probe

```
dotnet run --project src/BatteryHub.Probe -- hid
dotnet run --project src/BatteryHub.Probe -- hid --vid 054C
```

`hid` lists every HID top-level collection with its VID, PID, top-level usage, maximum
report lengths and the report IDs its descriptor declares. Lengths are in bytes and
include the report ID byte. The connection column is a guess from the device path.

```
dotnet run --project src/BatteryHub.Probe -- sony
dotnet run --project src/BatteryHub.Probe -- sony --no-switch
```

`sony` reads every DualShock 4 and DualSense once, the same way the app does, and prints
the raw battery report, the decoded reading, and a fixture block that can be pasted into
`tests/BatteryHub.Tests/Fixtures/Sony/` as a new test.

## Sony pads over Bluetooth

Over Bluetooth a DualShock 4 or DualSense sends a minimal report with no battery data
until something reads its calibration feature report (0x05). BatteryHub does that read,
because without it there is no battery level to show. It is a read, not a write, but the
pad stays in its full-report mode until it disconnects. Programs that only understand the
minimal report, such as DirectInput games running without Steam Input, may stop seeing
input from the pad until it reconnects. Steam and DS4Windows switch the pad the same way,
so with either running nothing changes.

The tray menu's **Read Bluetooth PlayStation controllers** turns the read off (it is on by
default); the pad then shows "no data" until another program switches it. `sony --no-switch`
does the same in the Probe.

Other things the Sony reader does that the protocol references taught it:

- Battery levels follow Linux `hid-playstation.c` (`level * 10 + 5`, 10% steps). Fault
  codes show as a fault, never as a percentage.
- Bluetooth reports carry a CRC-32 and are dropped if it does not match.
- A Sony wireless adapter with no pad paired is not shown (its zeros would read as 5%).
- Virtual pads created by software (DS4Windows through ViGEm) report a real pad's IDs; they
  are recognised from the Windows device tree, as DS4Windows does, and skipped.
- A pad keeps one ID on USB and Bluetooth: its Bluetooth address, read from the serial
  number (Bluetooth) or a pairing feature report (USB).

## Protocol references

Linux `drivers/hid/hid-playstation.c`, DS4Windows, AirPodsDesktop and OpenPods. They are
GPL; BatteryHub reads them for offsets and behaviour and does not copy their code.
