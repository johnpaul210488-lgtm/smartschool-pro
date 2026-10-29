import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "configure_parent_android.py"
APP_ID = "com.smartschool.smartschool_parent"


class ConfigureParentAndroidTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.app = Path(self.tmp.name)
        self.android = self.app / "android"
        self.main = self.android / "app" / "src" / "main"
        self.main.mkdir(parents=True)
        self.manifest = self.main / "AndroidManifest.xml"
        self.manifest.write_text('<manifest xmlns:android="http://schemas.android.com/apk/res/android">\n'
                                 '    <application android:name="${applicationName}" />\n</manifest>\n')
        self.firebase = self.android / "app" / "google-services.json"
        self.write_firebase(APP_ID)

    def tearDown(self):
        self.tmp.cleanup()

    def write_firebase(self, package):
        self.firebase.write_text(json.dumps({
            "project_info": {"project_id": "example-project", "project_number": "123"},
            "client": [{
                "client_info": {
                    "mobilesdk_app_id": "1:123:android:abcd",
                    "android_client_info": {"package_name": package},
                },
                "api_key": [{"current_key": "example-public-config-key"}],
            }],
        }))

    def setup_gradle(self, kotlin):
        ext = ".gradle.kts" if kotlin else ".gradle"
        self.settings = self.android / ("settings" + ext)
        self.gradle = self.android / "app" / ("build" + ext)
        android_plugin = 'id("com.android.application")' if kotlin else "id 'com.android.application'"
        self.settings.write_text(f'pluginManagement {{ }}\nplugins {{\n    {android_plugin}\n}}\n')
        assignment = f'applicationId = "{APP_ID}"' if kotlin else f'applicationId "{APP_ID}"'
        self.gradle.write_text(f'plugins {{\n    {android_plugin}\n}}\nandroid {{\n {assignment}\n}}\n')

    def run_script(self):
        return subprocess.run([sys.executable, str(SCRIPT), str(self.app)], capture_output=True, text=True)

    def test_kotlin_gradle_configuration_is_idempotent(self):
        self.setup_gradle(kotlin=True)
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(APP_ID, result.stdout)
        self.assertIn('id("com.google.gms.google-services") version "4.4.2" apply false', self.settings.read_text())
        self.assertIn('id("com.google.gms.google-services")', self.gradle.read_text())
        self.assertLess(self.gradle.read_text().index('id("com.android.application")'),
                        self.gradle.read_text().index('id("com.google.gms.google-services")'))
        self.assertIn("android.permission.POST_NOTIFICATIONS", self.manifest.read_text())
        self.assertIn('isCoreLibraryDesugaringEnabled = true', self.gradle.read_text())
        self.assertIn('coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")', self.gradle.read_text())
        before = [p.read_text() for p in (self.settings, self.gradle, self.manifest)]
        self.assertEqual(self.run_script().returncode, 0)
        self.assertEqual(before, [p.read_text() for p in (self.settings, self.gradle, self.manifest)])

    def test_groovy_gradle_is_supported(self):
        self.setup_gradle(kotlin=False)
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("id 'com.google.gms.google-services' version '4.4.2' apply false", self.settings.read_text())
        self.assertIn("id 'com.google.gms.google-services'", self.gradle.read_text())
        self.assertIn("coreLibraryDesugaringEnabled true", self.gradle.read_text())
        self.assertIn("coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'", self.gradle.read_text())

    def test_wrong_package_fails_without_modifying_any_files(self):
        self.setup_gradle(kotlin=True)
        self.write_firebase("com.smartschool.parent")  # The outdated documented ID.
        before = [p.read_text() for p in (self.settings, self.gradle, self.manifest)]
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("no client", result.stderr)
        self.assertEqual(before, [p.read_text() for p in (self.settings, self.gradle, self.manifest)])

    def test_missing_or_incomplete_config_fails(self):
        self.setup_gradle(kotlin=True)
        self.firebase.unlink()
        self.assertNotEqual(self.run_script().returncode, 0)
        self.firebase.write_text('{"project_info": {}, "client": []}')
        self.assertNotEqual(self.run_script().returncode, 0)

    def test_invalid_gradle_does_not_partially_modify_files(self):
        self.setup_gradle(kotlin=True)
        self.gradle.write_text(f'android {{ applicationId = "{APP_ID}" }}')
        before = [p.read_text() for p in (self.settings, self.gradle, self.manifest)]
        result = self.run_script()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(before, [p.read_text() for p in (self.settings, self.gradle, self.manifest)])


if __name__ == "__main__":
    unittest.main()
