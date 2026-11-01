#!/usr/bin/env python3
"""Check the documentation: links and anchors, SUMMARY coverage, and the Vietnamese mirror.

Run from anywhere: python3 docs/tools/check_docs.py
"""
import os
import re
import sys

DOCS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(DOCS)
SPACES = {"en": DOCS, "vi": os.path.join(DOCS, "vi")}
EXTRA = [os.path.join(ROOT, p) for p in ("README.md", "MyGame/README.md", "tools/MockCluster/README.md")]
problems = []


def pages(space):
    found = []
    for folder, dirs, files in os.walk(SPACES[space]):
        dirs[:] = [d for d in dirs if d not in ("vi", "tools", ".gitbook") or (d == "vi" and False)]
        if space == "en":
            dirs[:] = [d for d in dirs if d != "vi"]
        found += [os.path.join(folder, f) for f in files if f.endswith(".md")]
    return sorted(found)


def slug(heading):
    text = re.sub(r"<a [^>]*></a>", "", heading)
    text = re.sub(r"[`*~]", "", text.strip())
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text).lower()
    return re.sub(r"[^\w\- ]", "", text, flags=re.UNICODE).replace(" ", "-")


def headings(path):
    result, in_code = [], False
    for line in open(path, encoding="utf-8"):
        if line.startswith("```"):
            in_code = not in_code
        elif not in_code and (m := re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", line)):
            result.append(m.group(2))
    return result


def anchors(path):
    seen, result = {}, set()
    for h in headings(path):
        base = slug(h)
        n = seen.get(base, 0)
        seen[base] = n + 1
        result.add(base if n == 0 else f"{base}-{n}")
        result.update(re.findall(r'id="([^"]+)"', h))
    return result


def links(path):
    in_code = False
    for number, line in enumerate(open(path, encoding="utf-8"), 1):
        if line.startswith("```"):
            in_code = not in_code
        elif not in_code:
            for target in re.findall(r"\]\(([^)\s]+)\)", re.sub(r"`[^`]*`", "", line)):
                if not re.match(r"^(https?:|mailto:)", target):
                    yield number, target


cache = {}
for space, base in SPACES.items():
    files = pages(space)
    for path in files + (EXTRA if space == "en" else []):
        for number, target in links(path):
            file_part, _, anchor = target.partition("#")
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), file_part)) if file_part else path
            where = f"{os.path.relpath(path, ROOT)}:{number}"
            if not os.path.exists(resolved):
                problems.append(f"{where}: missing file {target}")
                continue
            inside = resolved.startswith(base + os.sep) and (space == "vi" or not resolved.startswith(SPACES["vi"] + os.sep))
            if path in files and resolved.endswith(".md") and resolved.startswith(DOCS) and not inside:
                problems.append(f"{where}: link leaves its space: {target}")
            if anchor and resolved.endswith(".md"):
                cache.setdefault(resolved, anchors(resolved))
                if anchor not in cache[resolved]:
                    problems.append(f"{where}: missing anchor #{anchor} in {os.path.relpath(resolved, ROOT)}")
    summary = os.path.join(base, "SUMMARY.md")
    listed = {os.path.normpath(os.path.join(base, t.partition("#")[0])) for _, t in links(summary)}
    for path in files:
        if os.path.basename(path) != "SUMMARY.md" and path not in listed:
            problems.append(f"{os.path.relpath(path, ROOT)}: not listed in {os.path.relpath(summary, ROOT)}")

english = {os.path.relpath(p, SPACES["en"]) for p in pages("en")}
vietnamese = {os.path.relpath(p, SPACES["vi"]) for p in pages("vi")}
for name in sorted(english - vietnamese):
    problems.append(f"vi/{name}: missing Vietnamese page")
for name in sorted(vietnamese - english):
    problems.append(f"vi/{name}: has no English page")
for name in sorted(english & vietnamese):
    a, b = headings(os.path.join(SPACES["en"], name)), headings(os.path.join(SPACES["vi"], name))
    if len(a) != len(b):
        problems.append(f"vi/{name}: {len(b)} headings, English page has {len(a)}")

for p in problems:
    print(p)
print(f"{len(problems)} problems")
sys.exit(1 if problems else 0)
