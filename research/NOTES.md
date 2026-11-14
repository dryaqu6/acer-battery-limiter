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

## Việc tiếp
- Lấy SSDT có `_WDG` chứa GUID 79772EC5 từ gói BIOS V2.21 → dịch ngược phương thức WM?? → thấy chính xác lệnh / ô EC mà "giới hạn 80%" ghi.
- Giả lập bằng `acpiexec` (ACPICA): chạy phương thức đó, ghi lại EC offset + giá trị (EC trong acpiexec là bộ nhớ giả).
- Nếu cần: phân tích chương trình EC (nếu có trong gói BIOS) bằng Ghidra xem 80% là hằng số hay đọc từ ô nhớ.
