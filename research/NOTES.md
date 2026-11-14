# Nghiên cứu: giữ mức pin < 80% trên Acer Nitro AN515-58

Chỉ đọc / giả lập. **Chưa ghi gì vào chip EC thật.** Tệp ACPI thô (`research/acpi/*.dat`, `*.dsl`) không đưa lên git.

## Máy
- Acer Nitro AN515-58 (Nitro 5), bo mạch `ADL Jimny_ADH V2.21`, BIOS Insyde V2.21, chương trình EC 2.12.
- WMI `root\wmi BatteryControl` GUID `{79772EC5-04B1-4bfd-843C-61E7F77B6CC9}`: GetBattInfoInterface (19), GetBatteryHealthControlStatus (20), SetBatteryHealthControl (21).

## Đã tìm được (14/11)
- `GetSystemFirmwareTable` đọc được DSDT (478 KB) + 1/22 SSDT. GUID BatteryControl **không** nằm trong DSDT / SSDT đầu → phương thức WMI của Acer Care Center nằm ở một SSDT khác (API Windows không trả). Lấy từ gói cập nhật BIOS V2.21 (chưa làm).
- Bảng MSDM (khoá bản quyền Windows) đã xoá ngay, không chép.
- EC (`\_SB.PC00.LPCB.EC0`):
  - `ERAM` (EmbeddedControl, cổng 0x62/0x66): chỉ khai `ERBD` 0x5F, `SMPR` 0x60.
  - `ECMM` (bộ nhớ ánh xạ 0xFE0B0800, 0x1000): 117 ô — pin: `BST0` 0xC1, `BRC0` 0xC2, `BFC0` 0xCC, `GAU0` 0xCE, `BCG0` 0xD4, `BCC0` 0xF5, `ADPT` 0xA3.7 … **không có ô "mức dừng sạc"**.
  - Hộp thư lệnh EC (`ECCD` 0xFE0B0800+0x582…): `ECMD`, `EDT1..5`, trả `ERN1..8`. `ERCD(buf)` gửi lệnh; `ERRD(off)` = lệnh 0xB0 (đọc), `ERWT(off,v)` = lệnh 0xB1 (ghi).
  - Vùng mở rộng đánh số: `EIDR(idx)` / `EIDW(idx,v)` — ghi chỉ số vào `ERIB` (16 bit, 0x5D) rồi đọc/ghi qua 0x5F. Nghi Acer Care Center dùng vùng này cho giới hạn 80%.
  - `SBAT` / `ESMC`: gửi lệnh SMBus tới chip pin (địa chỉ 0x16) qua EC.
- Hiệu chuẩn (WMI chức năng 2): lúc pin 79% EC **sạc lên** trước; lúc 100% xả ngay → quy trình cố định sạc đầy → xả → sạc.

## Tra cứu (agent phụ, 14/11) — tóm tắt
- **Chưa ai công bố** cách giữ < 80% / mức tuỳ ý trên AN515-58. Mọi giao diện (WMI BatteryControl, driver Linux acer-wmi-battery / Linuwu-Sense, bản đưa vào kernel 9/2026 chỉ có `charge_types` Standard / Long_Life) chỉ là **bật/tắt mốc 80%**.
- EC RAM **0x03** (AN515-57/58, theo nbfc-linux #65): cờ cộng dồn +0x10 = NitroSense điều khiển quạt, **+0x40 = giới hạn 80%**, +0x80 = hiệu chuẩn. Không ai thấy ô chứa số 80 → nhiều khả năng 80 là hằng số trong chương trình EC (chưa xác minh).
- Máy này chỉ có 3 phương thức WMI (19/20/21) — **không có** Get/SetBatteryFunctionData (uBAC…) mà máy Acer khác có.
- Hiệu chuẩn luôn: tắt health → sạc 100% → xả sâu dù cắm sạc → sạc lại (khớp quan sát 14/11). ANV15-52: sau hiệu chuẩn, health bật, pin **ngừng sạc hẳn kể cả dưới 80%** tới khi tắt health (acer-wmi-battery #130).
- Cờ 80% nằm trong RAM EC: mất sau hibernate / reset EC → dịch vụ phải áp lại khi resume (app đã làm: mỗi 30 giây + sự kiện nguồn).
- Duy nhất **Aspire E5-576G** (EC ITE IT8987 + IC sạc TI BQ24780S) dừng ở % tuỳ ý được: bật/tắt bit CHRG_INHIBIT của IC sạc qua EC (dự án Linux tự viết, 1 máy). AN515-58: bo Compal LA-L973P, chip EC chưa rõ.
- Công cụ ghi EC trên Windows: chỉ **PawnIO** (ký số, module LpcACPIEC, ghi thô cổng 0x62/0x66, phải giữ mutex `Access_EC`) là chấp nhận được; WinRing0 / RwDrv bị Defender coi là driver lỗ hổng. Ghi EC sai có thể brick.
- Nguồn: docs.kernel.org/wmi/devices/acer-wmi-battery.html · github.com/frederik-h/acer-wmi-battery (#57, #130) · github.com/0x7375646F/Linuwu-Sense · github.com/nbfc-linux/nbfc-linux/issues/65 · gitea.com/sanjeevdotpage/acer-e576g-batterycontrol · pawnio.eu.

## Giả thuyết đáng thử (chỉ dùng WMI chính thức, không ghi EC, không driver)
Sự cố 11:44 (bật hiệu chuẩn rồi tắt sau 4 giây lúc pin 80%) → EC **ngắt nguồn sạc vào, máy chạy pin** (PowerOnline=False, xả ~16 W) suốt ~50 phút tới khi (có lẽ) cắm lại sạc lúc 12:33. Lần 12:58 giữ hiệu chuẩn 2,5 phút thì EC lại sạc. Nếu:
1. tái tạo được trạng thái "ép xả" này ở mức pin bất kỳ, và
2. thoát được bằng phần mềm (vd tắt rồi bật lại health mode như báo cáo ANV15-52),
thì có thể **xả từ mức bất kỳ** và **giữ mức < 80%** bằng dao động nông (sạc thường tới X+3, ép xả về X) — chỉ dùng WMI. Rủi ro khi thử: kẹt ép xả → pin cạn dần; cách cứu đã biết: rút sạc cắm lại. **Cần user đồng ý trước khi thử.**

## Thử nghiệm 14/11 13:40 (user đồng ý) — giả thuyết ép xả: KHÔNG tái tạo được
| Lần | Pin lúc bật hiệu chuẩn | EC làm gì | Tắt hiệu chuẩn |
|---|---|---|---|
| 11:04 | 100% | xả ~16 W, PowerOnline vẫn True | ngừng xả ngay |
| 12:58 | 79% | **sạc** | ngừng sạc (82% giữ tới 13:05) |
| 13:05 (app 1.1.0 tự bật vì 82% > 81) | 82% | **sạc tới 98%** | (dịch vụ dừng 13:40) |
| 13:40 thử | 98% | sạc, PowerOnline True suốt 25 s (đọc mỗi 0,3 s) | ngừng ngay |
→ Quy luật: hiệu chuẩn **sạc đầy trước rồi mới xả**; tắt cờ là dừng ngay. Sự cố 11:44 (PowerOnline False ~50 phút, pin 80→73%) không lặp lại được — nhiều khả năng sạc bị rút / lỏng đúng lúc, hoặc lỗi thoáng qua. **Không có cách chỉ-WMI để giữ < 80%.** Hướng còn lại: ổ cắm thông minh qua LAN (bản 1.2.0), hoặc dò bit ngắt sạc của IC sạc qua EC (PawnIO, rủi ro cao — chưa làm).
Sửa app 1.1.0 theo đó: chỉ bắt đầu xả khi pin ≥ `DischargeStartPercent` (mặc định 95); đích xả = max(giới hạn, 80); báo "Topping up to 100%, then discharging to X%" khi EC đang ở bước sạc.

## Việc tiếp
- Lấy SSDT có `_WDG` chứa GUID 79772EC5 từ gói BIOS V2.21 → dịch ngược phương thức WM?? → thấy chính xác lệnh / ô EC mà "giới hạn 80%" ghi.
- Giả lập bằng `acpiexec` (ACPICA): chạy phương thức đó, ghi lại EC offset + giá trị (EC trong acpiexec là bộ nhớ giả).
- Nếu cần: phân tích chương trình EC (nếu có trong gói BIOS) bằng Ghidra xem 80% là hằng số hay đọc từ ô nhớ.
