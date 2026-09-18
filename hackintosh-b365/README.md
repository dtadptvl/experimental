# OpenCore build: B365 M AORUS ELITE + i7-8700

This workspace builds and validates an OpenCore EFI for the detected machine hardware.

## Target

- macOS Sequoia 15.x
- OpenCore 1.0.7
- SMBIOS: iMac19,1
- Graphics: Intel UHD 630
- AMD RX 6500 XT: intentionally disabled in macOS with `-wegnoegpu`
- Audio: Realtek ALC892 via AppleALC
- Ethernet: Intel I219-V via IntelMausi
- NVMe: Samsung 970 EVO Plus via native macOS NVMe + NVMeFix
- USB: USBToolBox + UTBDefault for install only; replace with a machine-specific map after installation

## Why the RX 6500 XT is disabled

The installed RX 6500 XT is Navi 24 and is not a viable accelerated macOS GPU. The i7-8700 includes UHD 630, so the minimal reliable path is to let macOS use the iGPU and leave the RX 6500 XT for Windows.

## Build

GitHub Actions on this branch downloads the pinned upstream releases, generates `config.plist`, validates every referenced ACPI/kext/driver, runs the matching OpenCore `ocvalidate`, and publishes a ZIP artifact.

The repository intentionally never contains a final SMBIOS serial/MLB/UUID. The artifact contains `Finalize-EFI.cmd`, which generates them locally on Windows using OpenCore's own `macserial.exe`.

## First boot prerequisites

1. Run `Finalize-EFI.cmd` once.
2. Connect the macOS display to a motherboard video output.
3. BIOS: UEFI only; disable CSM, Secure Boot and Fast Boot; SATA = AHCI.
4. Enable Internal Graphics/iGPU. If available, set DVMT Pre-Allocated to at least 64 MB.
5. Disable CFG Lock if exposed. The initial config also enables OpenCore's CFG-lock workaround as a fallback.
6. Keep the generated serial private. Before using Apple services, check that it is not assigned to a real Mac.

## USB

The hardware dump cannot identify every physical USB port. `UTBDefault.kext` is therefore temporary. After macOS is installed, make a proper USBToolBox map, replace `UTBDefault.kext` with the generated `UTBMap.kext`, and update `config.plist`.
