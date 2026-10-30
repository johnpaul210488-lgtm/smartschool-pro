#!/usr/bin/env python3
"""Wire Firebase into a freshly generated parent Android project.

Run after `flutter create`, with android/app/google-services.json already in
place. The JSON must be the Firebase *Android app config*, never a service
account/private key. No configuration values are printed.
"""

import argparse
import json
import re
import sys
from pathlib import Path

PLUGIN = "com.google.gms.google-services"


def gradle_file(folder: Path, stem: str) -> Path:
    for suffix in (".gradle.kts", ".gradle"):
        candidate = folder / (stem + suffix)
        if candidate.is_file():
            return candidate
    raise ValueError(f"Missing {stem}.gradle[.kts] in {folder}; run flutter create first.")


def add_plugin(text: str, is_kotlin: bool, settings: bool) -> str:
    if PLUGIN in text:
        return text
    # Generated Flutter projects use top-level plugins blocks in both files.
    match = re.search(r"(?m)^plugins\s*\{", text)
    if not match:
        raise ValueError("Gradle plugins block not found; use a Flutter-generated Android project.")
    if is_kotlin:
        plugin = f'id("{PLUGIN}")'
        if settings:
            plugin += ' version "4.4.2" apply false'
    else:
        plugin = f"id '{PLUGIN}'"
        if settings:
            plugin += " version '4.4.2' apply false"
    position = match.end()
    if not settings:
        end = text.find("}", position)
        if end == -1:
            raise ValueError("Gradle app plugins block is incomplete.")
        android = re.search(
            r"(?m)^[ \t]*id(?:\(\s*['\"]com\.android\.application['\"]\s*\)|\s+['\"]com\.android\.application['\"])[^\n]*",
            text[position:end],
        )
        if not android:
            raise ValueError("Gradle app plugins block has no com.android.application plugin.")
        position += android.end()
    return text[:position] + "\n    " + plugin + text[position:]


def configure(app_dir: Path, expected_project: str | None = None) -> str:
    android = app_dir / "android"
    settings_file = gradle_file(android, "settings")
    app_file = gradle_file(android / "app", "build")
    manifest_file = android / "app" / "src" / "main" / "AndroidManifest.xml"
    firebase_file = android / "app" / "google-services.json"
    if not firebase_file.is_file():
        raise ValueError("android/app/google-services.json is missing; obtain the Firebase Android app config.")

    settings = settings_file.read_text(encoding="utf-8")
    app = app_file.read_text(encoding="utf-8")
    manifest = manifest_file.read_text(encoding="utf-8")
    data = json.loads(firebase_file.read_text(encoding="utf-8"))
    match = re.search(r"\bapplicationId\s*(?:=\s*)?['\"]([^'\"]+)['\"]", app)
    if not match:
        raise ValueError("Could not read the parent Android applicationId from app/build.gradle[.kts].")
    app_id = match.group(1)

    clients = data.get("client", []) if isinstance(data, dict) else []
    matching_clients = [client for client in clients if isinstance(client, dict) and
                        client.get("client_info", {}).get("android_client_info", {}).get("package_name") == app_id]
    if not matching_clients:
        raise ValueError(f"Firebase Android app config has no client for applicationId {app_id}.")
    project = data.get("project_info", {})
    if expected_project and project.get("project_id") != expected_project:
        raise ValueError(f"Firebase Android app config must belong to project {expected_project}.")
    client = matching_clients[0]
    keys = client.get("api_key", [])
    if not (project.get("project_id") and project.get("project_number") and
            client.get("client_info", {}).get("mobilesdk_app_id") and
            any(isinstance(key, dict) and key.get("current_key") for key in keys)):
        raise ValueError("Firebase Android app config is incomplete; download google-services.json again.")

    # Prepare every change before writing anything. This prevents a mismatched
    # JSON or unexpected Flutter template from leaving a half-configured build.
    new_settings = add_plugin(settings, settings_file.name.endswith(".kts"), True)
    kotlin = app_file.name.endswith(".kts")
    new_app = add_plugin(app, kotlin, False)
    # flutter_local_notifications needs core-library desugaring for Android.
    # Append an explicit block so local and CI builds use the same setup.
    marker = "// SmartSchool notification desugaring"
    if marker not in new_app:
        if kotlin:
            new_app += ('\n' + marker + '\nandroid { compileOptions { isCoreLibraryDesugaringEnabled = true } }\n'
                        'dependencies { coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4") }\n')
        else:
            new_app += ('\n' + marker + '\nandroid { compileOptions { coreLibraryDesugaringEnabled true } }\n'
                        "dependencies { coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4' }\n")
    new_manifest = manifest
    if "android.permission.POST_NOTIFICATIONS" not in new_manifest:
        manifest_tag = re.search(r"<manifest\b[^>]*>", new_manifest)
        if not manifest_tag:
            raise ValueError("AndroidManifest.xml has no manifest tag.")
        new_manifest = (new_manifest[:manifest_tag.end()] +
                        '\n    <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />' +
                        new_manifest[manifest_tag.end():])

    # FCM can receive while the Flutter UI is terminated. Declare the same
    # high-importance channel the app creates after login as Android's fallback.
    channel_name = "com.google.firebase.messaging.default_notification_channel_id"
    if channel_name not in new_manifest:
        metadata = ('<meta-data android:name="' + channel_name + '" '
                    'android:value="smartschool_alerts" />')
        self_closing = re.search(r"<application\b([^>]*)/>", new_manifest)
        if self_closing:
            replacement = '<application' + self_closing.group(1) + '>\n        ' + metadata + '\n    </application>'
            new_manifest = (new_manifest[:self_closing.start()] + replacement +
                            new_manifest[self_closing.end():])
        else:
            application = re.search(r"<application\b[^>]*>", new_manifest)
            if not application:
                raise ValueError("AndroidManifest.xml has no application tag.")
            new_manifest = (new_manifest[:application.end()] + '\n        ' + metadata +
                            new_manifest[application.end():])

    settings_file.write_text(new_settings, encoding="utf-8")
    app_file.write_text(new_app, encoding="utf-8")
    manifest_file.write_text(new_manifest, encoding="utf-8")
    return app_id


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app_dir", nargs="?", type=Path, default=Path("apps/parent_app"))
    parser.add_argument("--expected-project", help="Reject Android configs from a different Firebase project")
    args = parser.parse_args()
    try:
        app_id = configure(args.app_dir, args.expected_project)
    except (OSError, ValueError, TypeError, AttributeError) as error:
        print(f"Parent Firebase configuration error: {error}", file=sys.stderr)
        return 1
    print(f"Parent Android Firebase configured for {app_id}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
