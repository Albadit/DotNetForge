"""Checks DotNetForge documentation links.

- Markdown links [text](target#anchor) in .docs/, .claude/skills/, .github/agents/ and the root *.md files:
  the target file must exist and the #anchor must match a heading (GitHub slug rules).
- Backticked repository paths such as `Services/PageService.cs` must exist (reported as warnings).

Usage (from the repository root):  python .claude/skills/verify/check_links.py
Exit code 1 when a broken link is found.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]
SCAN = [ROOT / ".docs", ROOT / ".claude" / "skills", ROOT / ".github" / "agents"]
ROOT_FILES = ["README.md", "CLAUDE.md"]
SKIP_DIRS: set[str] = set()

LINK = re.compile(r"(?<!\!)\[[^\]]*\]\(([^)\s]+)\)")
CODE_PATH = re.compile(r"`([A-Za-z0-9_.\-/]+/[A-Za-z0-9_.\-]+\.(?:cs|cshtml|json|js|css|csproj|props|md|yml|py))`")
FENCE = re.compile(r"^(```|~~~)")


def slug(heading: str) -> str:
    text = heading.strip().lower()
    text = re.sub(r"[`*_]", lambda m: "_" if m.group(0) == "_" else "", text)
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


def anchors(path: pathlib.Path) -> set[str]:
    seen: dict[str, int] = {}
    result = set()
    in_fence = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if FENCE.match(line.strip()):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = re.match(r"^(#{1,6})\s+(.*)$", line)
        if m:
            base = slug(m.group(2))
            n = seen.get(base, 0)
            result.add(base if n == 0 else f"{base}-{n}")
            seen[base] = n + 1
    return result


def files():
    for root in SCAN:
        for p in root.rglob("*.md"):
            if not SKIP_DIRS.intersection(p.relative_to(ROOT).parts):
                yield p
    for name in ROOT_FILES:
        p = ROOT / name
        if p.exists():
            yield p


def repo_paths() -> list[str]:
    ignored = {"bin", "obj", ".git", "node_modules"}
    return [
        p.relative_to(ROOT).as_posix()
        for p in ROOT.rglob("*")
        if p.is_file() and not ignored.intersection(p.relative_to(ROOT).parts)
    ]


def main() -> int:
    errors, warnings = [], []
    known = repo_paths()
    cache: dict[pathlib.Path, set[str]] = {}
    for md in files():
        in_fence = False
        for no, line in enumerate(md.read_text(encoding="utf-8").splitlines(), 1):
            if FENCE.match(line.strip()):
                in_fence = not in_fence
                continue
            if in_fence:
                continue
            for target in LINK.findall(line):
                if re.match(r"^(https?:|mailto:)", target):
                    continue
                file_part, _, anchor = target.partition("#")
                dest = md if not file_part else (md.parent / file_part).resolve()
                where = f"{md.relative_to(ROOT)}:{no}"
                if not dest.exists():
                    errors.append(f"{where}: missing target {target}")
                    continue
                if anchor and dest.is_file() and dest.suffix == ".md":
                    if dest not in cache:
                        cache[dest] = anchors(dest)
                    if anchor not in cache[dest]:
                        errors.append(f"{where}: missing anchor #{anchor} in {dest.relative_to(ROOT)}")
            for ref in CODE_PATH.findall(line):
                candidates = [ROOT / ref, md.parent / ref]
                # A partial path (e.g. `Security/ApiTokenFactory.cs` inside a project table) is fine when some
                # file in the repository ends with it.
                suffix_hit = any(k == ref or k.endswith("/" + ref) for k in known)
                if "X.Y.Z" in ref:
                    continue
                if not suffix_hit and not any(c.exists() for c in candidates):
                    warnings.append(f"{md.relative_to(ROOT)}:{no}: path not found `{ref}`")
    for w in sorted(set(warnings)):
        print("WARN ", w)
    for e in errors:
        print("ERROR", e)
    print(f"{len(errors)} broken link(s), {len(set(warnings))} unresolved path(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
