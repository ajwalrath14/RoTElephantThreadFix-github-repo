import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "RoTElephantThreadFix" / "RoTElephantThreadFix.csproj"
SUBMODULE = ROOT / "module" / "SubModule.xml"
SOURCE = ROOT / "src" / "RoTElephantThreadFix" / "RoTElephantThreadFix.cs"
README = ROOT / "README.md"
WORKFLOW = ROOT / ".github" / "workflows" / "build.yml"


class RepoContractTests(unittest.TestCase):
    def test_project_targets_expected_bannerlord_build_dependencies(self):
        tree = ET.parse(PROJECT)
        root = tree.getroot()
        target = root.findtext(".//TargetFramework")
        self.assertEqual(target, "net472")

        packages = {
            element.attrib["Include"]: element.attrib.get("Version")
            or element.findtext("Version")
            for element in root.findall(".//PackageReference")
        }
        self.assertEqual(packages["Bannerlord.ReferenceAssemblies"], "1.4.8.119303")
        self.assertEqual(packages["Lib.Harmony"], "2.4.2")
        self.assertIn("Microsoft.NETFramework.ReferenceAssemblies", packages)
        self.assertEqual(root.findtext(".//Version"), "1.0.3")
        self.assertEqual(root.findtext(".//AssemblyVersion"), "1.0.3.0")
        self.assertEqual(root.findtext(".//FileVersion"), "1.0.3.0")

    def test_project_does_not_reference_rot_or_local_game_dlls(self):
        text = PROJECT.read_text(encoding="utf-8")
        self.assertNotIn("ROT.dll", text)
        self.assertNotIn("TaleWorlds.MountAndBlade.dll", text)
        self.assertNotRegex(text, r"<Reference\s+Include=")

    def test_submodule_identity_and_version_are_v103(self):
        root = ET.parse(SUBMODULE).getroot()
        self.assertEqual(root.find("./Id").attrib["value"], "RoTElephantThreadFix")
        self.assertEqual(root.find("./Version").attrib["value"], "v1.0.3")
        dll = root.find(".//DLLName").attrib["value"]
        self.assertEqual(dll, "RoTElephantThreadFix.dll")

    def test_fix_uses_same_tick_post_agent_flush(self):
        text = SOURCE.read_text(encoding="utf-8")
        self.assertIn('AccessTools.TypeByName(\n                "RoT_Elephants.RoTElephantAgentComponent")', text)
        self.assertIn("protected override void AfterAsyncTickTick(float dt)", text)
        self.assertIn("public Mission SourceMission;", text)
        self.assertIn("!victim.IsActive()", text)
        self.assertIn("victim.RegisterBlow(pending.Blow, in pending.CollisionData);", text)
        self.assertIn("if (replacements != 1)", text)
        self.assertNotIn("ElephantMissionMainThreadPatch", text)
        self.assertNotIn("registerBlow.Invoke", text)
        self.assertNotIn("OriginalRegisterBlow", text)

    def test_readme_documents_install_and_ci_artifacts(self):
        text = README.read_text(encoding="utf-8")
        for required in (
            "Bannerlord v1.4.8.119303",
            "RoTElephantThreadFix.dll",
            "RoTElephantThreadFix_v1.0.3.zip",
            "AfterAsyncTickTick",
            "in-game",
            "Modules\\RoTElephantThreadFix",
            "GitHub Actions",
        ):
            self.assertIn(required, text)

    def test_workflow_builds_packages_and_uploads_both_artifacts(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        for required in (
            "actions/setup-dotnet@v4",
            "dotnet restore",
            "dotnet build",
            "python -m unittest discover",
            "scripts/package_module.py",
            "actions/upload-artifact@v4",
            "RoTElephantThreadFix.dll",
            "tests/BehaviorHarness/BehaviorHarness.csproj",
            "RoTElephantThreadFix_v1.0.3.zip",
            "RoTElephantThreadFix-v1.0.3",
        ):
            self.assertIn(required, text)
        self.assertRegex(text, re.compile(r"configuration\s*:\s*Release", re.IGNORECASE))
        stage_index = text.index("- name: Stage raw DLL artifact")
        raw_path_index = text.index("path: RoTElephantThreadFix.dll")
        self.assertLess(stage_index, raw_path_index)


if __name__ == "__main__":
    unittest.main()
