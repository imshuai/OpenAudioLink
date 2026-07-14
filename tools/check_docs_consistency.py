#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CANONICAL_DEFAULT_PORT = 39888
DEFAULT_PORT_SOURCES = [
    (
        "Android",
        ROOT / "sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt",
        re.compile(r"\bconst val DefaultPort = (\d+)\b"),
    ),
    (
        "Windows",
        ROOT / "receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs",
        re.compile(r"\bpublic const int DefaultPort = (\d+)\b;"),
    ),
]
DOCS = sorted((ROOT / "docs").glob("*.md"))
FILES = [ROOT / "README.md", *DOCS]
REQUIRED_DOCS = [
    "docs/01-Introduction.md",
    "docs/02-Architecture.md",
    "docs/03-Protocol.md",
    "docs/04-Android.md",
    "docs/05-Windows.md",
    "docs/06-Audio.md",
    "docs/07-Discovery.md",
    "docs/08-Configuration.md",
    "docs/09-Deployment.md",
    "docs/10-Testing.md",
    "docs/11-Roadmap.md",
]
STALE_PATTERNS = [
    "DLNA Player Roadmap",
    "Android 8.0+",
    ".NET 8",
    "Self-contained",
    "self-contained",
    "dotnet publish",
    "Host.CreateDefaultBuilder",
    "Generic Host",
    "12-FAQ.md",
    "Encoded Size matches Payload Length",
]


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def check_required_docs() -> list[str]:
    errors: list[str] = []
    for rel in REQUIRED_DOCS:
        if not (ROOT / rel).exists():
            errors.append(f"missing required document: {rel}")
    return errors


def check_markdown_fences() -> list[str]:
    errors: list[str] = []
    for path in FILES:
        if read(path).count("```") % 2:
            errors.append(f"unbalanced markdown fence: {path.relative_to(ROOT)}")
    return errors


def check_doc_refs() -> list[str]:
    errors: list[str] = []
    pattern = re.compile(r"docs/[0-9]{2}-[A-Za-z]+\.md")
    for path in FILES:
        for ref in pattern.findall(read(path)):
            if not (ROOT / ref).exists():
                errors.append(f"missing doc ref in {path.relative_to(ROOT)}: {ref}")
    return errors


def check_stale_text() -> list[str]:
    errors: list[str] = []
    for path in FILES:
        text = read(path)
        for pattern in STALE_PATTERNS:
            if pattern in text:
                errors.append(f"stale text in {path.relative_to(ROOT)}: {pattern}")
    return errors


def check_default_ports() -> list[str]:
    errors: list[str] = []
    for platform, path, pattern in DEFAULT_PORT_SOURCES:
        match = pattern.search(read(path))
        if match is None:
            errors.append(f"missing {platform} DefaultPort: {path.relative_to(ROOT)}")
        elif int(match.group(1)) != CANONICAL_DEFAULT_PORT:
            errors.append(
                f"default port mismatch in {path.relative_to(ROOT)}: "
                f"{match.group(1)} != {CANONICAL_DEFAULT_PORT}"
            )

    return errors


def main() -> int:
    errors = []
    errors.extend(check_required_docs())
    errors.extend(check_markdown_fences())
    errors.extend(check_doc_refs())
    errors.extend(check_stale_text())
    errors.extend(check_default_ports())
    if errors:
        for error in errors:
            print(error)
        return 1
    print(f"docs consistency ok: {len(FILES)} markdown files checked")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
