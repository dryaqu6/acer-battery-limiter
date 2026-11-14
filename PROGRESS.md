# Acer Battery Limiter — progress / tiến độ

Tiến độ làm app (tiếng Việt, cho người phát triển). Hướng dẫn dùng xem `README.md`.

Yêu cầu (14/11/2026):
- Giao diện tiếng Anh. 2 ô chọn: **Limit charging**, **Discharge when above limit**; thanh kéo **0–100**; kéo tới đâu hiện lời khuyên / cảnh báo theo mức (máy dùng bình thường).
- Biểu tượng khay (cạnh đồng hồ): % pin + trạng thái; thông báo khi bắt đầu / xong xả.
- File cài `.exe`; cài tạo shortcut (Desktop + Start Menu) có icon pin; "Open file location" ra `C:\Program Files\Acer Battery Limiter\`; trong đó có `Uninstall.exe` gỡ sạch.
- Máy không có lớp WMI của Acer thì báo "Not supported".
- Repo riêng tư; README tiếng Anh + mục tiếng Việt.

## Thiết kế
- **.NET Framework 4.8** (có sẵn trên Windows 10/11 → exe nhỏ, không cần cài runtime), C# WinForms; dựng trên macOS bằng `dotnet` + gói reference assemblies.
- Một exe, 3 chế độ: không tham số = cửa sổ cài đặt · `--tray` = biểu tượng khay (chạy khi đăng nhập) · `--service` = dịch vụ Windows `AcerBatteryLimiter` (LocalSystem), chỉ dịch vụ ghi WMI.
- Phần cứng: `root\wmi` lớp `BatteryControl` (của Acer Care Center) — chức năng 1 = dừng sạc ở 80% cố định, chức năng 2 = hiệu chuẩn (xả khi đang cắm sạc). Mức khác 80% do phần mềm điều khiển:
  - X = 80: bật giới hạn phần cứng.
  - X > 80: tắt giới hạn để sạc; chạm X bật giới hạn (pin > 80 nên dừng sạc); tụt dưới X − trễ thì tắt lại.
  - X < 80: phần cứng chỉ dừng ở 80; muốn giữ X phải bật xả → xả khi > X + trễ, dừng khi ≤ X (dao động, mòn pin hơn — UI cảnh báo).
  - Rút sạc: luôn tắt xả.
- Giao diện ↔ dịch vụ qua tệp trong `C:\ProgramData\AcerBatteryLimiter\`: `config.json` (giao diện ghi), `status.json` (dịch vụ ghi), `service.log`. Thông số (trễ, chu kỳ kiểm, …) nằm trong config, có mặc định.
- Cài: NSIS (`makensis` chạy trên macOS) → `AcerBatteryLimiter-Setup-<version>.exe`; gỡ: `Uninstall.exe`.
- Exe không ký số → SmartScreen / phần mềm diệt virus có thể hỏi.

## Các bước
- [x] 1. Repo + công cụ trên Mac (dotnet SDK, makensis, Pillow)
- [x] 2. Icon pin (.ico nhiều cỡ)
- [x] 3. Code C#: lõi WMI + chính sách, dịch vụ, khay, cửa sổ cài đặt
- [x] 4. Dựng exe trên Mac
- [x] 5. Script NSIS cài / gỡ
- [ ] 6. Thử trên Laptop văn phòng (Acer Nitro): cài im lặng, kiểm dịch vụ + chính sách (80 / 85 / 70 + xả), khay; gỡ; cài lại; gỡ tác vụ cũ BatteryLimit80 / BatteryDischarge; để file cài ở `D:\User\Downloads`
- [ ] 7. README (EN + VI), phát hành bản dựng (GitHub Release), ghi memory

## Nhật ký
- 14/11 — lập kế hoạch. User chốt: thanh 0–100 kèm lời khuyên theo mức, có khay, cài thì gỡ tác vụ cũ, file cài ở D:\User\Downloads, repo riêng tư `acer-battery-limiter`, README EN + VI.
- 14/11 — xong bước 1–4: .NET SDK 10 cài vào ~/.dotnet (brew cask cần sudo), makensis 3.13 (brew), Pillow trong .venv; icon `assets/app.ico` (vẽ bằng `assets/make_icon.py`); code C# (AcerWmi, Policy, LimiterService, TrayContext, SettingsForm); `dotnet build -c Release` ra AcerBatteryLimiter.exe (net48) không lỗi.
- 14/11 — xong bước 5: `installer/installer.nsi` + `build.sh` → `dist/AcerBatteryLimiter-Setup-1.0.0.exe` (106 KB). Exe app 65 KB (net48, Acer không cần cài thêm gì).
