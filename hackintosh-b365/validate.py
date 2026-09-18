#!/usr/bin/env python3
import plistlib
import sys
from pathlib import Path

root = Path(__file__).resolve().parent / "dist"
efi = root / "EFI"
oc = efi / "OC"
cfg_path = oc / "config.plist"

with cfg_path.open("rb") as f:
    cfg = plistlib.load(f)

missing = []

for item in cfg["ACPI"]["Add"]:
    if item.get("Enabled") and not (oc / "ACPI" / item["Path"]).exists():
        missing.append(f"ACPI/{item['Path']}")

for item in cfg["Kernel"]["Add"]:
    if not item.get("Enabled"):
        continue
    bundle = oc / "Kexts" / item["BundlePath"]
    if not bundle.exists():
        missing.append(f"Kexts/{item['BundlePath']}")
        continue
    plist_path = item.get("PlistPath")
    if plist_path and not (bundle / plist_path).exists():
        missing.append(f"Kexts/{item['BundlePath']}/{plist_path}")
    exe = item.get("ExecutablePath")
    if exe and not (bundle / exe).exists():
        missing.append(f"Kexts/{item['BundlePath']}/{exe}")

for item in cfg["UEFI"]["Drivers"]:
    if item.get("Enabled") and not (oc / "Drivers" / item["Path"]).exists():
        missing.append(f"Drivers/{item['Path']}")

required = [
    efi / "BOOT" / "BOOTx64.efi",
    oc / "OpenCore.efi",
    root / "Finalize-EFI.cmd",
    root / "Tools" / "macserial.exe",
]
for path in required:
    if not path.exists():
        missing.append(str(path.relative_to(root)))

generic = cfg["PlatformInfo"]["Generic"]
if generic["SystemProductName"] != "iMac19,1":
    raise SystemExit("Unexpected SMBIOS")
if "wegnoegpu" not in cfg["NVRAM"]["Add"]["7C436110-AB2A-4BBB-A880-FE41995C9F82"]["boot-args"]:
    raise SystemExit("RX 6500 XT disable boot arg is missing")
if cfg["Kernel"]["Quirks"]["XhciPortLimit"]:
    raise SystemExit("XhciPortLimit must remain disabled for this build")
if missing:
    print("Missing referenced files:")
    for item in missing:
        print(" -", item)
    sys.exit(1)

print("Layout/config reference validation passed")
