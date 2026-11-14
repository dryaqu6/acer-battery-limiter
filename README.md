# Acer Battery Limiter

Keep the battery of an Acer laptop at a healthy level while it stays plugged in.

Acer Battery Limiter is a small Windows app (service + tray icon + settings window) that uses the same battery health
interface as Acer Care Center, but keeps your choice applied: after Windows updates, restarts, sleep and hibernate,
and even when Care Center turns the limit off by itself.

> Not affiliated with Acer. Tested on **Acer Nitro 5 AN515-58** (BIOS Insyde V2.21, EC 2.12, Windows 11).
> Other Acer laptops whose Care Center offers *Battery Charge Limit* expose the same interface and should work;
> on unsupported laptops the app says **Not supported** and changes nothing.

[Tiếng Việt ↓](#hướng-dẫn-nhanh-tiếng-việt)

## Features

| | |
|---|---|
| **Limit charging** | Stop charging at **80%** (Acer's built-in hardware stop) or at any level **81–100%** (the service switches the hardware stop on and off around your level). |
| **Advice slider** | 0–100% slider with colour-coded advice for each level. Levels below 80% are explained but held at 80% (hardware minimum, see [Limitations](#limitations)). |
| **Discharge when above limit** | If the battery is (almost) full — for example after a full charge or after Care Center lifted the limit — drain it back down to the limit **while the charger stays connected**. On by default. |
| **Scheduled full charge** | Every 1–6 months (default 3) charge to 100% once, then return to the limit. Lets the battery balance its cells and keeps the % reading accurate. |
| **Charge to 100% once** | For a trip: charges to 100% and stays there until you unplug; back on the charger the limit applies again. |
| **Tray icon** | Battery %, what the limiter is doing, notifications when a discharge or full charge starts and ends. |
| **Background service** | Re-applies the setting every 30 s, on plug / unplug and on resume; never leaves the battery draining when the service stops or Windows shuts down. |
| **Clean installer** | Start Menu + Desktop shortcuts, *Apps & features* entry, `Uninstall.exe` that removes everything. No runtime to install (.NET Framework 4.8 ships with Windows 10/11). |

## Install

1. Download `AcerBatteryLimiter-Setup-<version>.exe` from the [Releases](../../releases) page.
2. Run it. Windows SmartScreen or your antivirus may warn because the installer is not code-signed: choose *More info → Run anyway* (or allow it in your antivirus).
3. Finish → *Open Acer Battery Limiter*. Defaults: limit **80%**, discharge when above limit **on**, full charge **every 3 months**.

Silent install (IT / scripts): `AcerBatteryLimiter-Setup-<version>.exe /S`

## Use

- **Window** — Start Menu or Desktop shortcut *Acer Battery Limiter*, or click the tray icon. Change the options, then **Apply**.
- **Tray icon** — hover for battery % and status; left-click opens the window; right-click → *Hide tray icon*.
- **Status line** examples: `Holding at 80%` · `Charging to 85%` · `Topping up to 100%, then discharging to 80%` · `Discharging to 80%` · `Charging to 100% (scheduled full charge)` · `On battery · limit 80%`.

## Uninstall

*Settings → Apps → Acer Battery Limiter → Uninstall*, the Start Menu entry *Uninstall Acer Battery Limiter*, or
`C:\Program Files\Acer Battery Limiter\Uninstall.exe`. Removes the service, tray autostart, shortcuts, settings and logs,
and makes sure the battery is not left discharging. The hardware 80% stop keeps its last state (Acer Care Center manages it from then on).

## How it works

Acer laptops expose a WMI class `root\wmi\BatteryControl` (GUID `79772EC5-04B1-4bfd-843C-61E7F77B6CC9`), used by Acer Care Center:

| Function | What the hardware does |
|---|---|
| 1 — Battery charge limit | On: the embedded controller stops charging at **80%** (fixed). Off: charges to 100%. |
| 2 — Battery calibration | Charges to **100% first**, then drains the battery while the charger is connected, then recharges. Switching it off stops it immediately. |

The service builds the features on top of these two switches:

- **Limit X = 80** → function 1 on.
- **Limit X > 80** → function 1 off until the battery reaches X, then on (the battery is already above 80%, so charging stops); off again when it drops below X − 3%.
- **Discharge** → function 2 on only when the battery is ≥ 95% (so the "charge to 100% first" step is short), off when it reaches the limit. Kept on for at least 2 minutes to avoid switching the controller too quickly.

Only the Windows service (LocalSystem) calls the WMI methods. The window and tray run as the normal user and talk to the
service through files in `C:\ProgramData\AcerBatteryLimiter\`.

## Limitations

- **No level below 80%.** The embedded controller only knows "stop at 80%" (a single on/off bit). Holding e.g. 60% would need a smart plug that switches the charger off — planned for a later version. Writing the embedded controller directly is possible in theory but risky (needs a kernel driver, can brick the laptop) and is not done.
- **Discharge only from an (almost) full battery.** Acer's calibration mode always tops up to 100% before draining, so starting it at, say, 85% would first charge the battery to 100%.
- The 80% stop lives in the controller's RAM and can be lost after hibernation or a full battery drain; the service re-applies it automatically.

## Files

| Path | Content |
|---|---|
| `C:\Program Files\Acer Battery Limiter\` | `AcerBatteryLimiter.exe` (window / tray / service), `Uninstall.exe`, `LICENSE` |
| `C:\ProgramData\AcerBatteryLimiter\config.json` | Settings (written by the window) |
| `C:\ProgramData\AcerBatteryLimiter\status.json`, `state.json` | Current state, schedule (written by the service) |
| `C:\ProgramData\AcerBatteryLimiter\service.log` | Log — *Open log* in the window |
| Service `AcerBatteryLimiter` | Automatic start, restarts on failure |
| `HKLM\…\Run\AcerBatteryLimiterTray` | Tray icon at sign-in |

Advanced settings in `config.json` (defaults): `HysteresisPercent` 3, `PollSeconds` 30, `MinDischargeSeconds` 120, `DischargeStartPercent` 95.

Command line: `AcerBatteryLimiter.exe` (window) · `--tray` · `--service` · `--apply-once` (one control step, elevated) · `--stop-discharge` · `--render <png>` (draw the window to an image).

## Build

Works on macOS, Linux or Windows.

```bash
# requirements: .NET SDK 8+ (https://dot.net), NSIS 3 (brew install makensis / apt install nsis)
./build.sh            # → dist/AcerBatteryLimiter-Setup-<version>.exe
```

The app targets .NET Framework 4.8 through the `Microsoft.NETFramework.ReferenceAssemblies` package, so no Windows machine is needed to build.
The icon is drawn by `assets/make_icon.py` (Pillow). Development notes (Vietnamese): `PROGRESS.md`, `research/NOTES.md`.

## Hướng dẫn nhanh (tiếng Việt)

**Cài đặt:** chạy `AcerBatteryLimiter-Setup-<phiên bản>.exe` → nếu Windows / phần mềm diệt virus cảnh báo (file chưa ký số) chọn *More info → Run anyway* → Next → Install → Finish. Mặc định: giữ pin **80%**, tự xả khi pin gần đầy, sạc đầy **3 tháng một lần**.

**Dùng:** mở *Acer Battery Limiter* ở Desktop / Start Menu hoặc bấm biểu tượng pin cạnh đồng hồ.
- **Limit charging** + thanh kéo: mức giữ pin. **80% là khuyên dùng.** 81–100% được; dưới 80% máy Acer không hỗ trợ nên vẫn giữ 80%.
- **Discharge when above limit**: pin gần đầy (≥ 95%) thì tự xả về mức đặt dù vẫn cắm sạc (máy sạc nốt lên 100% rồi mới xả — đó là cách phần cứng Acer làm).
- **Full charge**: sạc đầy định kỳ rồi quay về mức đặt (cân bằng cell, % pin chính xác).
- **Charge to 100% once**: trước khi mang máy đi; rút sạc là hết, cắm lại thì về mức đặt.
- Bấm **Apply** để lưu.

**Gỡ:** *Settings → Apps → Acer Battery Limiter → Uninstall* hoặc chạy `Uninstall.exe` trong `C:\Program Files\Acer Battery Limiter\`.

## License

MIT — see [LICENSE](LICENSE).
