# Hackintosh hardware baseline

Source: hardware dump collected on 2026-09-18.

## Detected hardware

- Motherboard: GIGABYTE B365 M AORUS ELITE (B365)
- BIOS: F1, UEFI, Secure Boot disabled
- CPU: Intel Core i7-8700 (Coffee Lake, 6C/12T)
- Supported iGPU available in CPU: Intel UHD Graphics 630
- Installed dGPU: AMD Radeon RX 6500 XT (Navi 24)
- Audio: Realtek ALC892
- Ethernet: Intel I219-V
- NVMe: Samsung 970 EVO Plus 250 GB, firmware 2B2QEXM7
- SATA: Intel 300 Series AHCI
- No physical Wi-Fi/Bluetooth adapter detected
- USB controller: Intel 300 Series xHCI

## OpenCore design target

- Primary target: macOS Sequoia 15.x
- SMBIOS family: iMac19,1
- macOS display output: Intel UHD 630
- Disable RX 6500 XT in macOS with WhateverGreen; retain it for Windows
- Ethernet: IntelMausi
- Audio: AppleALC, start with ALC892 layout 1 and verify after first boot
- NVMe: NVMeFix
- Required Coffee Lake/B365 ACPI baseline:
  - SSDT-PLUG
  - SSDT-EC-USBX
  - SSDT-AWAC
  - SSDT-PMC

## Important first-boot constraint

The monitor must be connected to a motherboard video output for macOS because RX 6500 XT / Navi 24 has no supported macOS graphics acceleration.

USB port mapping is still hardware-specific. The static dump identifies the xHCI controller but cannot determine every physical port; build/test should treat any generic USB configuration as temporary until a proper map is made.
