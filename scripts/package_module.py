#!/usr/bin/env python3
import argparse
import zipfile
from pathlib import Path

MODULE_ID = "RoTElephantThreadFix"
DLL_ARCHIVE_PATH = f"{MODULE_ID}/bin/Win64_Shipping_Client/{MODULE_ID}.dll"
SUBMODULE_ARCHIVE_PATH = f"{MODULE_ID}/SubModule.xml"


def build_package(dll_path, submodule_path, output_zip):
    dll_path = Path(dll_path)
    submodule_path = Path(submodule_path)
    output_zip = Path(output_zip)

    if not dll_path.is_file():
        raise FileNotFoundError(f"DLL not found: {dll_path}")
    if not submodule_path.is_file():
        raise FileNotFoundError(f"SubModule.xml not found: {submodule_path}")

    output_zip.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output_zip, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for source, archive_path in (
            (submodule_path, SUBMODULE_ARCHIVE_PATH),
            (dll_path, DLL_ARCHIVE_PATH),
        ):
            info = zipfile.ZipInfo(archive_path, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, source.read_bytes())

    return output_zip


def main():
    parser = argparse.ArgumentParser(description="Package RoTElephantThreadFix for Bannerlord.")
    parser.add_argument("--dll", required=True, type=Path)
    parser.add_argument("--submodule", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    build_package(args.dll, args.submodule, args.output)


if __name__ == "__main__":
    main()
