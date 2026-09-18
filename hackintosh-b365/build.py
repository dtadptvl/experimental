#!/usr/bin/env python3
import json
import os
import plistlib
import shutil
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
VENDOR = ROOT / "vendor"
DIST = ROOT / "dist"
EFI = DIST / "EFI"
OC = EFI / "OC"

VERSIONS = {
    "OpenCorePkg": ("acidanthera/OpenCorePkg", "1.0.7"),
    "Lilu": ("acidanthera/Lilu", "1.7.2"),
    "WhateverGreen": ("acidanthera/WhateverGreen", "1.7.0"),
    "AppleALC": ("acidanthera/AppleALC", "1.9.7"),
    "VirtualSMC": ("acidanthera/VirtualSMC", "1.3.7"),
    "IntelMausi": ("acidanthera/IntelMausi", "1.0.8"),
    "NVMeFix": ("acidanthera/NVMeFix", "1.1.3"),
    "USBToolBox": ("USBToolBox/kext", "1.2.0"),
}

UA = {"User-Agent": "dtadptvl-experimental-hackintosh-builder"}


def http_json(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as response:
        return json.load(response)


def download(url, dst):
    dst.parent.mkdir(parents=True, exist_ok=True)
    print("download", url)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as response, dst.open("wb") as f:
        shutil.copyfileobj(response, f)


def release_zip(repo, tag, out):
    data = http_json(f"https://api.github.com/repos/{repo}/releases/tags/{tag}")
    assets = data.get("assets", [])
    candidates = [a for a in assets if a["name"].upper().endswith("RELEASE.ZIP")]
    if not candidates:
        candidates = [
            a for a in assets
            if a["name"].lower().endswith(".zip") and "debug" not in a["name"].lower()
        ]
    if not candidates:
        raise RuntimeError(
            f"No release zip for {repo} {tag}: {[a['name'] for a in assets]}"
        )
    project = repo.split("/")[-1].lower()
    candidates.sort(key=lambda a: (project not in a["name"].lower(), len(a["name"])))
    download(candidates[0]["browser_download_url"], out)


def extract(archive, dst):
    dst.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        z.extractall(dst)


def find_one(root, suffix):
    hits = [
        p for p in root.rglob("*")
        if p.as_posix().lower().endswith(suffix.lower())
    ]
    if len(hits) != 1:
        raise RuntimeError(f"Expected exactly 1 *{suffix} under {root}, got {hits}")
    return hits[0]


def copytree(src, dst):
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)


def copy_kext(extracted, name):
    info = find_one(extracted, f"/{name}/Contents/Info.plist")
    copytree(info.parent.parent, OC / "Kexts" / name)


def kext_entry(name, exe=None):
    return {
        "Arch": "Any",
        "BundlePath": name,
        "Comment": "",
        "Enabled": True,
        "ExecutablePath": "" if exe is None else f"Contents/MacOS/{exe}",
        "MaxKernel": "",
        "MinKernel": "",
        "PlistPath": "Contents/Info.plist",
    }


def acpi_entry(name, comment):
    return {"Comment": comment, "Enabled": True, "Path": name}


def driver_entry(name, load_early=False):
    return {
        "Arguments": "",
        "Comment": "",
        "Enabled": True,
        "LoadEarly": load_early,
        "Path": name,
    }


def configure_plist(sample, out):
    with sample.open("rb") as f:
        p = plistlib.load(f)

    p["ACPI"]["Add"] = [
        acpi_entry("SSDT-PLUG-DRTNIA.aml", "CPU power management"),
        acpi_entry("SSDT-EC-USBX-DESKTOP.aml", "Fake EC + USB power properties"),
        acpi_entry("SSDT-AWAC.aml", "300-series RTC/AWAC fix"),
        acpi_entry("SSDT-PMC.aml", "Native NVRAM on 300-series chipset"),
    ]
    p["ACPI"]["Delete"] = []
    p["ACPI"]["Patch"] = []

    p["Booter"]["Quirks"].update({
        "AvoidRuntimeDefrag": True,
        "DevirtualiseMmio": True,
        "EnableSafeModeSlide": True,
        "EnableWriteUnprotector": False,
        "ProtectUefiServices": False,
        "ProvideCustomSlide": True,
        "RebuildAppleMemoryMap": True,
        "ResizeAppleGpuBars": -1,
        "SetupVirtualMap": True,
        "SyncRuntimePermissions": True,
    })

    # UHD 630 drives macOS displays. RX 6500 XT is disabled by -wegnoegpu.
    p["DeviceProperties"]["Add"] = {
        "PciRoot(0x0)/Pci(0x2,0x0)": {
            "AAPL,ig-platform-id": bytes.fromhex("07009B3E"),
        }
    }
    p["DeviceProperties"]["Delete"] = {}

    p["Kernel"]["Add"] = [
        kext_entry("Lilu.kext", "Lilu"),
        kext_entry("VirtualSMC.kext", "VirtualSMC"),
        kext_entry("WhateverGreen.kext", "WhateverGreen"),
        kext_entry("AppleALC.kext", "AppleALC"),
        kext_entry("IntelMausi.kext", "IntelMausi"),
        kext_entry("NVMeFix.kext", "NVMeFix"),
        kext_entry("USBToolBox.kext", "USBToolBox"),
        kext_entry("UTBDefault.kext", None),
    ]
    p["Kernel"]["Block"] = []
    p["Kernel"]["Force"] = []
    p["Kernel"]["Patch"] = []
    p["Kernel"]["Quirks"].update({
        "AppleCpuPmCfgLock": False,
        "AppleXcpmCfgLock": True,
        "CustomSMBIOSGuid": False,
        "DisableIoMapper": True,
        "DisableLinkeditJettison": True,
        "LapicKernelPanic": False,
        "PanicNoKextDump": True,
        "PowerTimeoutKernelPanic": True,
        "XhciPortLimit": False,
    })
    p["Kernel"]["Scheme"]["KernelArch"] = "Auto"

    p["Misc"]["Boot"]["HideAuxiliary"] = True
    p["Misc"]["Boot"]["PickerMode"] = "Builtin"
    p["Misc"]["Debug"].update({
        "AppleDebug": True,
        "ApplePanic": True,
        "DisableWatchDog": True,
        "SysReport": False,
        "Target": 3,
    })
    p["Misc"]["Security"].update({
        "AllowSetDefault": True,
        "BlacklistAppleUpdate": True,
        "DmgLoading": "Signed",
        "ScanPolicy": 0,
        "SecureBootModel": "Default",
        "Vault": "Optional",
    })
    p["Misc"]["Tools"] = []
    p["Misc"]["Entries"] = []

    apple = p["NVRAM"]["Add"]["7C436110-AB2A-4BBB-A880-FE41995C9F82"]
    apple["boot-args"] = "-v keepsyms=1 debug=0x100 alcid=1 -wegnoegpu"
    apple["csr-active-config"] = bytes.fromhex("00000000")
    apple["run-efi-updater"] = "No"
    apple["prev-lang:kbd"] = "en-US:0"
    p["NVRAM"]["WriteFlash"] = True

    p["PlatformInfo"]["Generic"].update({
        "AdviseFeatures": False,
        "MLB": "REPLACE_MLB",
        "MaxBIOSVersion": False,
        "ProcessorType": 0,
        "ROM": bytes.fromhex("112233000000"),
        "SpoofVendor": True,
        "SystemMemoryStatus": "Auto",
        "SystemProductName": "iMac19,1",
        "SystemSerialNumber": "REPLACE_SERIAL",
        "SystemUUID": "00000000-0000-0000-0000-000000000000",
    })
    p["PlatformInfo"]["Automatic"] = True
    p["PlatformInfo"]["UpdateDataHub"] = True
    p["PlatformInfo"]["UpdateNVRAM"] = True
    p["PlatformInfo"]["UpdateSMBIOS"] = True
    p["PlatformInfo"]["UpdateSMBIOSMode"] = "Create"

    p["UEFI"]["ConnectDrivers"] = True
    p["UEFI"]["Drivers"] = [
        driver_entry("OpenRuntime.efi", False),
        driver_entry("HfsPlus.efi", False),
    ]
    p["UEFI"]["APFS"]["MinDate"] = -1
    p["UEFI"]["APFS"]["MinVersion"] = -1
    p["UEFI"]["Quirks"]["RequestBootVarRouting"] = True
    p["UEFI"]["Quirks"]["UnblockFsConnect"] = False

    for key in list(p.keys()):
        if isinstance(key, str) and key.startswith("#WARNING"):
            p.pop(key, None)

    with out.open("wb") as f:
        plistlib.dump(p, f, fmt=plistlib.FMT_XML, sort_keys=False)


def make_finalize_script(path):
    content = r"""@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$cfg=Join-Path $PWD 'EFI\OC\config.plist'; $mac=Join-Path $PWD 'Tools\macserial.exe';" ^
  "if(!(Test-Path $cfg)){throw 'EFI\OC\config.plist not found'}; if(!(Test-Path $mac)){throw 'Tools\macserial.exe not found'};" ^
  "$line=& $mac -g -m 'iMac19,1' -n 1 | Select-Object -First 1;" ^
  "if($line -notmatch '^\s*([^|\s]+)\s*\|\s*([^|\s]+)'){throw ('Unexpected macserial output: '+$line)}; $serial=$Matches[1]; $mlb=$Matches[2];" ^
  "$uuid=[guid]::NewGuid().ToString().ToUpper();" ^
  "$nic=Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object {$_.InterfaceDescription -match 'I219-V'} | Select-Object -First 1;" ^
  "if($nic -and $nic.MacAddress){$hex=$nic.MacAddress -replace '[-:]',''}else{$hex='112233000000'};" ^
  "$rom=[Convert]::ToBase64String([byte[]]@(for($i=0;$i -lt 12;$i+=2){[Convert]::ToByte($hex.Substring($i,2),16)}));" ^
  "[xml]$x=Get-Content -LiteralPath $cfg -Raw;" ^
  "function SetVal([string]$key,[string]$val,[string]$node='string'){ $keys=$x.SelectNodes('//key'); $k=$keys|Where-Object {$_.InnerText -eq $key}|Select-Object -Last 1; if(!$k){throw ('Missing plist key '+$key)}; $n=$k.NextSibling; while($n -and $n.NodeType -ne [System.Xml.XmlNodeType]::Element){$n=$n.NextSibling}; if($n.Name -ne $node){throw ('Unexpected node for '+$key+': '+$n.Name)}; $n.InnerText=$val };" ^
  "SetVal 'MLB' $mlb; SetVal 'SystemSerialNumber' $serial; SetVal 'SystemUUID' $uuid; SetVal 'ROM' $rom 'data';" ^
  "$x.Save($cfg); Write-Host ''; Write-Host 'EFI finalised successfully.' -ForegroundColor Green; Write-Host ('Serial: '+$serial); Write-Host ('UUID:   '+$uuid); Write-Host 'Do not publish this finalised config.plist.'"
if errorlevel 1 (
  echo.
  echo ERROR: EFI finalisation failed.
  pause
  exit /b 1
)
echo.
echo Next: copy the EFI folder to the FAT32 EFI partition.
if defined CI exit /b 0
pause
"""
    path.write_text(content, encoding="utf-8", newline="\r\n")


def main():
    if VENDOR.exists():
        shutil.rmtree(VENDOR)
    if DIST.exists():
        shutil.rmtree(DIST)

    VENDOR.mkdir(parents=True)
    (OC / "ACPI").mkdir(parents=True)
    (OC / "Drivers").mkdir(parents=True)
    (OC / "Kexts").mkdir(parents=True)
    (OC / "Tools").mkdir(parents=True)
    (EFI / "BOOT").mkdir(parents=True)

    extracted = {}
    for name, (repo, tag) in VERSIONS.items():
        archive = VENDOR / f"{name}-{tag}.zip"
        release_zip(repo, tag, archive)
        dst = VENDOR / f"{name}-{tag}"
        extract(archive, dst)
        extracted[name] = dst

    ocroot = extracted["OpenCorePkg"]
    shutil.copy2(
        find_one(ocroot, "/X64/EFI/BOOT/BOOTx64.efi"),
        EFI / "BOOT" / "BOOTx64.efi",
    )
    shutil.copy2(
        find_one(ocroot, "/X64/EFI/OC/OpenCore.efi"),
        OC / "OpenCore.efi",
    )
    shutil.copy2(
        find_one(ocroot, "/X64/EFI/OC/Drivers/OpenRuntime.efi"),
        OC / "Drivers" / "OpenRuntime.efi",
    )
    sample = find_one(ocroot, "/Docs/Sample.plist")
    shutil.copy2(
        find_one(ocroot, "/Utilities/ocvalidate/ocvalidate.exe"),
        DIST / "ocvalidate.exe",
    )

    download(
        "https://raw.githubusercontent.com/acidanthera/OcBinaryData/master/Drivers/HfsPlus.efi",
        OC / "Drivers" / "HfsPlus.efi",
    )
    ssdt_base = (
        "https://raw.githubusercontent.com/dortania/"
        "Getting-Started-With-ACPI/master/extra-files/compiled/"
    )
    for name in [
        "SSDT-PLUG-DRTNIA.aml",
        "SSDT-EC-USBX-DESKTOP.aml",
        "SSDT-AWAC.aml",
        "SSDT-PMC.aml",
    ]:
        download(ssdt_base + name, OC / "ACPI" / name)

    for key, kext in [
        ("Lilu", "Lilu.kext"),
        ("VirtualSMC", "VirtualSMC.kext"),
        ("WhateverGreen", "WhateverGreen.kext"),
        ("AppleALC", "AppleALC.kext"),
        ("IntelMausi", "IntelMausi.kext"),
        ("NVMeFix", "NVMeFix.kext"),
        ("USBToolBox", "USBToolBox.kext"),
        ("USBToolBox", "UTBDefault.kext"),
    ]:
        copy_kext(extracted[key], kext)

    configure_plist(sample, OC / "config.plist")

    macserial = [
        p for p in ocroot.rglob("*")
        if p.name.lower() == "macserial.exe"
    ]
    if len(macserial) != 1:
        raise RuntimeError(f"Expected one macserial.exe, got {macserial}")
    tools = DIST / "Tools"
    tools.mkdir()
    shutil.copy2(macserial[0], tools / "macserial.exe")
    make_finalize_script(DIST / "Finalize-EFI.cmd")

    readme = """B365 M AORUS ELITE / i7-8700 OpenCore EFI

Target: macOS Sequoia 15.x
OpenCore: 1.0.7
SMBIOS: iMac19,1 (placeholders until Finalize-EFI.cmd runs)
Graphics: Intel UHD 630 drives display; RX 6500 XT disabled in macOS via -wegnoegpu.

IMPORTANT BEFORE FIRST BOOT
- Run Finalize-EFI.cmd once on Windows before using this EFI.
- Connect the macOS display to a motherboard video output, not the RX 6500 XT.
- BIOS: UEFI only / CSM disabled, Secure Boot disabled, SATA AHCI.
- Enable Internal Graphics/iGPU. Set DVMT Pre-Allocated to 64 MB or higher if available.
- Disable Fast Boot. Disable CFG Lock if the BIOS exposes it.
- RX 6500 XT remains usable by Windows; macOS intentionally ignores it.
- USBToolBox + UTBDefault are temporary pre-map support. Build a real USB map
  after installation, replace UTBDefault.kext with UTBMap.kext, then reboot.
- Before signing into Apple services, check the generated serial with Apple's
  coverage checker. A newly generated serial should not belong to a real Mac.
"""
    (DIST / "README-FIRST.txt").write_text(readme, encoding="utf-8")

    cfg = (OC / "config.plist").read_text(encoding="utf-8")
    assert "REPLACE_SERIAL" in cfg and "REPLACE_MLB" in cfg
    print("Built", EFI)


if __name__ == "__main__":
    main()
