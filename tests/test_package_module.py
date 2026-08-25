import os
import tempfile
import unittest
import zipfile
from pathlib import Path

from scripts.package_module import build_package


class PackageModuleTests(unittest.TestCase):
    def test_build_package_creates_exact_bannerlord_layout(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "RoTElephantThreadFix.dll"
            submodule = root / "SubModule.xml"
            output = root / "RoTElephantThreadFix_v1.0.3.zip"
            dll.write_bytes(b"MZ-test-dll")
            submodule.write_text("<Module />", encoding="utf-8")

            build_package(dll, submodule, output)

            self.assertTrue(output.is_file())
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(
                    sorted(archive.namelist()),
                    [
                        "RoTElephantThreadFix/SubModule.xml",
                        "RoTElephantThreadFix/bin/Win64_Shipping_Client/RoTElephantThreadFix.dll",
                    ],
                )
                self.assertEqual(
                    archive.read(
                        "RoTElephantThreadFix/bin/Win64_Shipping_Client/RoTElephantThreadFix.dll"
                    ),
                    b"MZ-test-dll",
                )

    def test_build_package_is_reproducible_for_same_contents(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "RoTElephantThreadFix.dll"
            submodule = root / "SubModule.xml"
            first = root / "first.zip"
            second = root / "second.zip"
            dll.write_bytes(b"MZ-test-dll")
            submodule.write_text("<Module />", encoding="utf-8")

            os.utime(dll, (946684800, 946684800))
            os.utime(submodule, (946684800, 946684800))
            build_package(dll, submodule, first)
            os.utime(dll, (1893456000, 1893456000))
            os.utime(submodule, (1893456000, 1893456000))
            build_package(dll, submodule, second)

            self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_build_package_rejects_missing_dll(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            submodule = root / "SubModule.xml"
            submodule.write_text("<Module />", encoding="utf-8")

            with self.assertRaisesRegex(FileNotFoundError, "DLL"):
                build_package(
                    root / "missing.dll",
                    submodule,
                    root / "out.zip",
                )


if __name__ == "__main__":
    unittest.main()
