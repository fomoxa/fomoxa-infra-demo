#!/usr/bin/env python3
"""Kiem tra lien ket noi bo trong docs/*.md (va README goc): file ton tai, #anchor ton tai (theo quy tac slug cua GitHub)."""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = sorted(glob.glob(os.path.join(ROOT, "docs", "*.md")) + glob.glob(os.path.join(ROOT, "docs", "en", "*.md"))) + [os.path.join(ROOT, "README.md"), os.path.join(ROOT, "MyGame", "README.md"),
                                                                  os.path.join(ROOT, "tools", "MockCluster", "README.md")]


def slug(heading: str) -> str:
    text = re.sub(r"[`*~]", "", heading.strip())
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)  # [text](url) -> text
    text = text.lower()
    text = re.sub(r"[^\w\- ]", "", text, flags=re.UNICODE)
    return text.replace(" ", "-")


def anchors(path: str) -> set:
    result, seen, in_code = set(), {}, False
    for line in open(path, encoding="utf-8"):
        if line.startswith("```"):
            in_code = not in_code
            continue
        match = None if in_code else re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", line)
        if match:
            base = slug(match.group(2))
            count = seen.get(base, 0)
            seen[base] = count + 1
            result.add(base if count == 0 else f"{base}-{count}")
    return result


cache, errors = {}, 0
for path in FILES:
    if not os.path.exists(path):
        continue
    in_code = False
    for number, line in enumerate(open(path, encoding="utf-8"), 1):
        if line.startswith("```"):
            in_code = not in_code
            continue
        if in_code:
            continue
        for target in re.findall(r"\]\(([^)\s]+)\)", re.sub(r"`[^`]*`", "", line)):
            if re.match(r"^(https?:|mailto:)", target):
                continue
            file_part, _, anchor = target.partition("#")
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), file_part)) if file_part else path
            if not os.path.exists(resolved):
                print(f"{os.path.relpath(path, ROOT)}:{number}: thieu file {target}")
                errors += 1
                continue
            if anchor and resolved.endswith(".md"):
                cache.setdefault(resolved, anchors(resolved))
                if anchor not in cache[resolved]:
                    print(f"{os.path.relpath(path, ROOT)}:{number}: thieu anchor #{anchor} trong {os.path.relpath(resolved, ROOT)}")
                    errors += 1

print("Lien ket noi bo: OK" if errors == 0 else f"{errors} lien ket hong")
sys.exit(1 if errors else 0)
