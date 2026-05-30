# PRD — Windows 11 Custom ISO Builder

> Công cụ desktop (GUI) tạo file ISO cài đặt Windows 11 tùy biến: tự động cài Microsoft 365 Apps,
> trình duyệt và các app người dùng chọn; cấu hình sẵn Windows (bỏ TPM/SecureBoot, local account,
> gỡ bloatware, đặt tên máy theo serial). Đầu ra: 1 file `.iso` bootable (UEFI + Legacy).

## 1. Mục tiêu (Goal)

Người dùng (kỹ thuật viên IT) chọn 1 file Win11 ISO gốc + tick app cần cài + tùy chỉnh Windows,
bấm Build → nhận về 1 file ISO tùy biến cài đặt hoàn toàn không cần thao tác (unattended), app cài
**offline** (không cần internet trên máy đích).

## 2. Phạm vi (Scope)

### In-scope
- GUI desktop app: **.NET (C#) + WPF**, MVVM, Windows-only, yêu cầu quyền Administrator.
- Nguồn ISO: **người dùng tự cung cấp** file Win11 ISO (`.iso`). Tool mount + tùy biến, không tự tải.
- Cài app **offline**: nhúng bộ cài vào ISO (đặt trong thư mục trên media, chạy lúc OOBE/first-boot;
  **KHÔNG** inject thẳng vào `install.wim` để tránh phình mọi lần cài).
- **Microsoft 365 Apps** qua Office Deployment Tool (ODT): sinh `configuration.xml`, tải sẵn nguồn
  Office offline (~3.5GB), cài bằng `setup.exe /configure`. License riêng — người dùng tự đăng nhập
  tài khoản M365 để kích hoạt. **Chỉ làm phần ODT hợp pháp, KHÔNG crack/KMS.**
- App catalog: danh sách dựng sẵn (Chrome, Firefox, VLC, 7-Zip, Zalo, …) tick chọn + cho phép user
  thêm installer `.exe`/`.msi` riêng kèm tham số cài im lặng (silent flags).
- Tùy biến Windows qua `autounattend.xml`:
  - Bỏ kiểm tra **TPM 2.0 / Secure Boot / RAM** (cài được trên máy không đủ điều kiện).
  - **Skip Microsoft account** → tạo **local account**.
  - **Gỡ bloatware** mặc định (Candy Crush, Xbox, …) qua DISM provisioned appx.
  - Cấu hình sẵn: ngôn ngữ/region, timezone, layout bàn phím, computer name.
  - **Computer name = serial number của máy** (lấy động lúc first-boot, có sanitize + fallback).
- Đầu ra: **1 file `.iso`** bootable UEFI + Legacy BIOS (đóng gói bằng `oscdimg`, UDF).

### Out-of-scope (vòng này)
- Ghi thẳng USB bootable (chỉ xuất ISO; user tự dùng Rufus/`dd`).
- Tự tải Win11 ISO từ Microsoft.
- Bất kỳ hình thức kích hoạt license lậu (crack/KMS).
- Cài app online qua winget lúc first-boot (đã chọn hướng offline).
- Hybrid online/offline.

## 3. Yêu cầu chức năng (Functional Requirements)

| ID | Yêu cầu |
|----|---------|
| FR-1 | Chọn file Win11 ISO gốc; validate là ISO Windows hợp lệ (có `sources/install.wim` hoặc `install.esd`). |
| FR-2 | Mount/extract ISO ra thư mục làm việc; tự dọn khi xong/hủy. |
| FR-3 | Xử lý cả `install.wim` và `install.esd` (ESD → export sang WIM khi cần chỉnh sửa). |
| FR-4 | Chọn Windows edition (index) từ WIM (Home/Pro/…). |
| FR-5 | Gỡ bloatware: liệt kê provisioned appx, cho tick xóa, áp bằng DISM. |
| FR-6 | Sinh `autounattend.xml`: bypass TPM/SecureBoot/RAM; local account (user/pass); region/timezone/keyboard; computer name. |
| FR-7 | Sinh script first-boot đặt computer name = serial BIOS (sanitize ≤15 ký tự NetBIOS, fallback nếu rỗng/“To be filled by O.E.M.”). |
| FR-8 | Office: tải ODT, sinh `configuration.xml` (chọn ngôn ngữ, 32/64-bit, app loại trừ), tải nguồn offline, nhúng vào ISO. |
| FR-9 | App catalog dựng sẵn (JSON) hiển thị tick chọn; mỗi app có URL/đường dẫn + silent flag. |
| FR-10 | User thêm installer riêng (.exe/.msi) + nhập silent flag tùy chỉnh. |
| FR-11 | Sinh `SetupComplete.cmd` chạy lần lượt các installer offline từ media, ghi log. |
| FR-12 | Repack ISO bootable (UEFI+BIOS) bằng `oscdimg`; đặt tên file đầu ra. |
| FR-13 | UI wizard từng bước; thanh tiến trình + log realtime cho quá trình build. |
| FR-14 | Phát hiện Windows ADK/oscdimg; nếu thiếu → hướng dẫn cài (hoặc bundle oscdimg). |

## 4. Yêu cầu phi chức năng (Non-Functional)

- **Quyền:** app yêu cầu Administrator (DISM mount/unmount cần admin).
- **Hiệu năng:** build lần đầu chậm (tải Office ~3.5GB); cache nguồn Office để build sau nhanh.
- **Kích thước ISO:** chấp nhận phình 6–10GB (offline). Cảnh báo user nếu vượt mốc.
- **Tin cậy:** mọi bước có log; lỗi giữa chừng phải unmount WIM sạch (tránh mount treo).
- **Bảo trì:** module hóa, mỗi file code < 200 dòng; tách service rõ ràng (MVVM).
- **An toàn:** không xử lý/ chứa bất kỳ thành phần kích hoạt lậu nào.

## 5. Kiến trúc tổng quan (High-level Architecture)

```
WPF (MVVM, /Views, /ViewModels)
  └── Services
       ├── ToolDetectionService   (ADK/oscdimg/DISM)
       ├── IsoService             (mount/extract/repack qua oscdimg)
       ├── WimService             (DISM: mount/unmount, ESD→WIM, remove appx)
       ├── UnattendBuilder        (autounattend.xml)
       ├── FirstBootScriptBuilder (SetupComplete.cmd + computer-name-from-serial + app runner)
       ├── OfficeOdtService       (tải ODT, configuration.xml, tải nguồn offline)
       ├── AppCatalogService      (catalog JSON dựng sẵn + installer user thêm)
       └── BuildOrchestrator      (chạy pipeline, progress, logging)
```

**Pipeline build:** Validate ISO → Extract → (ESD→WIM nếu cần) → Remove bloatware (DISM) →
Sinh autounattend.xml + SetupComplete.cmd → Chuẩn bị payload (Office offline + app installers) vào
thư mục media → Repack ISO (oscdimg UEFI+BIOS) → Xuất `.iso`.

## 6. Rủi ro chính (Key Risks)

| Rủi ro | Giảm thiểu |
|--------|-----------|
| Windows ADK/oscdimg chưa cài (đã xác nhận thiếu trên máy) | Tự phát hiện; hướng dẫn cài ADK hoặc bundle `oscdimg.exe` (kiểm tra license redistribution). |
| `install.esd` không sửa trực tiếp được | Export ESD → WIM bằng DISM trước khi chỉnh. |
| Serial number lỗi (rỗng/quá dài/ký tự lạ) | Sanitize ký tự hợp lệ, cắt ≤15, fallback `WIN-<random>`. |
| Mount WIM treo khi lỗi | try/finally luôn `Dism /Unmount /Discard`; lệnh dọn mount mồ côi. |
| ISO >4GB không vừa FAT32 USB (UEFI) | Out-of-scope vòng này; ghi chú; cân nhắc split-wim sau. |
| Registry bypass TPM thay đổi theo build Win11 | Dùng bộ key chuẩn cộng đồng; cho phép cập nhật. |

## 7. Tiêu chí nghiệm thu (Acceptance Criteria)

- AC-1: Từ 1 Win11 ISO gốc + chọn 2–3 app + tick các tùy biến → tạo ra file `.iso` không lỗi.
- AC-2: ISO boot được trên VM (Hyper-V/VirtualBox) UEFI, cài Win11 **không cần thao tác**.
- AC-3: Sau cài: máy dùng **local account** đã đặt, **không** bị chặn bởi TPM/SecureBoot.
- AC-4: Computer name = serial của VM/máy (đã sanitize), bloatware đã chọn bị gỡ.
- AC-5: Sau first-boot, Office M365 + các app đã chọn được cài **offline** (không cần internet).
- AC-6: Office mở được, chờ user đăng nhập M365 để kích hoạt (không tự crack).

## 8. Quyết định đã chốt (Resolved — Validation Session 1, 2026-05-30)

1. ✅ **oscdimg** → bundle sẵn `oscdimg.exe` trong app (user không cần cài ADK). *TODO build: kiểm tra điều khoản redistribute ADK.*
2. ✅ **Catalog app** → Chrome, Firefox, 7-Zip, VLC, Notepad++, Zalo, Unikey + user tự thêm installer.
3. ✅ **Office** → M365 Apps for business (`O365BusinessRetail`), ngôn ngữ **en-US**, **64-bit**.
4. ✅ **Preset** → YAGNI, bỏ vòng này (BuildConfig vẫn serialize được nếu cần sau).
5. ✅ **Phân vùng đĩa** → dừng cho user chọn ổ (an toàn, không tự wipe disk 0); phần còn lại unattended.
6. ✅ **First-boot** → SetupComplete.cmd (quyền SYSTEM), không dùng AutoLogon.
