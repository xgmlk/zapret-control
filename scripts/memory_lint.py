#!/usr/bin/env python3
"""Validate project-memory files against .project-memory.json.

The linter never modifies files. It reports FAIL/WARN and exits with:
  0 - no errors (warnings allowed unless --strict)
  1 - at least one FAIL
  2 - config or I/O error
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from dataclasses import dataclass, field
from datetime import date, datetime
from pathlib import Path
from urllib.parse import unquote

CONFIG_NAME = ".project-memory.json"
FILE_KEYS = ("passport", "pitfalls", "bugs", "decisions", "todo", "style")
META_RE = re.compile(
    r"<!--\s*project-memory:\s*updated=(\d{4}-\d{2}-\d{2})\s+owner=([A-Za-z0-9_.-]+)\s*-->"
)
PLACEHOLDER_RE = re.compile(r"\{\{[A-Z0-9_]+\}\}")
BUG_ID_RE = re.compile(r"\bBUG-\d{3}\b")
BUG_ID_ANY_RE = re.compile(r"\bBUG-\d+\b")
OPEN_TODO_RE = re.compile(r"^\s*-\s*\[\s\]\s+", re.MULTILINE)
DONE_TODO_RE = re.compile(r"^\s*-\s*\[[xX]\]\s+", re.MULTILINE)
LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
ALLOWED_BUG_STATUSES = {"open", "in-progress", "fixed", "verified", "wontfix"}

REQUIRED_SECTIONS = {
    "passport": (
        ("## Что это", "## What this is"),
        ("## Быстрый старт", "## Quick start"),
        ("## Инварианты", "## Invariants"),
        ("## Память проекта", "## Project memory"),
        ("## Правила ведения памяти", "## Memory rules"),
    ),
    "pitfalls": (("## Как вести", "## How to maintain"),),
    "bugs": (("## Как вести", "## How to maintain"), ("## Активные", "## Active")),
    "decisions": (("## Как вести", "## How to maintain"),),
    "todo": (("## Как вести", "## How to maintain"), ("## Сейчас", "## Now")),
    "style": (("## Как вести", "## How to maintain"),),
}

SECRET_PATTERNS = (
    re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b"),
    re.compile(r"\bghp_[A-Za-z0-9]{20,}\b"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    re.compile(
        r"(?i)\b(?:api[_-]?key|token|password|secret)\b\s*[:=]\s*['\"][^'\"]{8,}['\"]"
    ),
)

PLACEHOLDER_HINTS = ("your", "example", "placeholder", "xxx", "***", "${", "{{", "[уточнить]")


@dataclass(frozen=True)
class Issue:
    level: str
    path: str
    message: str
    line: int | None = None


@dataclass
class LintResult:
    root: Path
    files: dict[str, int] = field(default_factory=dict)
    limits: dict[str, int] = field(default_factory=dict)
    issues: list[Issue] = field(default_factory=list)
    config_error: str | None = None
    strict: bool = False

    @property
    def errors(self) -> list[Issue]:
        return [issue for issue in self.issues if issue.level == "FAIL"]

    @property
    def warnings(self) -> list[Issue]:
        return [issue for issue in self.issues if issue.level == "WARN"]

    @property
    def total_lines(self) -> int:
        return sum(self.files.values())

    @property
    def total_limit(self) -> int:
        return sum(self.limits.values())

    @property
    def ok(self) -> bool:
        if self.config_error is not None:
            return False
        if self.errors:
            return False
        return not (self.strict and self.warnings)


def _rel(path: Path, root: Path) -> str:
    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return path.as_posix()


def _add(result: LintResult, level: str, path: str, message: str, line: int | None = None) -> None:
    result.issues.append(Issue(level=level, path=path, message=message, line=line))


def _is_safe_relative(value: object) -> bool:
    if not isinstance(value, str) or not value.strip():
        return False
    path = Path(value)
    return not path.is_absolute() and ".." not in path.parts


def _valid_date(value: object) -> bool:
    if not isinstance(value, str):
        return False
    try:
        datetime.strptime(value, "%Y-%m-%d")
    except ValueError:
        return False
    return True


def load_config(root: Path) -> tuple[dict | None, list[str]]:
    path = root / CONFIG_NAME
    if not path.exists():
        return None, [f"{CONFIG_NAME} not found"]

    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        return None, [f"{CONFIG_NAME} is not valid JSON: {exc}"]

    if not isinstance(raw, dict):
        return None, [f"{CONFIG_NAME} must contain a JSON object"]

    errors: list[str] = []
    if raw.get("version") != 1:
        errors.append("version must be 1")

    allowed_languages = {"ru", "en"}
    if raw.get("language") not in allowed_languages:
        errors.append("language must be 'ru' or 'en'")

    files = raw.get("files")
    if not isinstance(files, dict):
        errors.append("files must be an object")
        files = {}
    for key in FILE_KEYS:
        value = files.get(key)
        if not _is_safe_relative(value):
            errors.append(f"files.{key} must be a safe relative path")

    limits = raw.get("limits")
    if not isinstance(limits, dict):
        errors.append("limits must be an object")
        limits = {}
    for key in FILE_KEYS:
        value = limits.get(key)
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            errors.append(f"limits.{key} must be a positive integer")

    for key in ("archive_dir", "agents_file"):
        if not _is_safe_relative(raw.get(key)):
            errors.append(f"{key} must be a safe relative path")

    if not isinstance(raw.get("lint_command"), str) or not raw["lint_command"].strip():
        errors.append("lint_command must be a non-empty string")

    for key in ("created", "updated"):
        if not _valid_date(raw.get(key)):
            errors.append(f"{key} must be a valid YYYY-MM-DD date")

    return (raw if not errors else None), errors


def check_metadata(result: LintResult, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    lines = text.splitlines()
    match = None
    for line in lines[:5]:
        match = META_RE.search(line)
        if match:
            break
    if not match:
        _add(
            result,
            "FAIL",
            rel,
            "missing metadata comment in the first 5 lines: "
            "<!-- project-memory: updated=YYYY-MM-DD owner=agent -->",
        )
        return

    raw_date, owner = match.group(1), match.group(2)
    try:
        updated = datetime.strptime(raw_date, "%Y-%m-%d").date()
    except ValueError:
        _add(result, "FAIL", rel, f"invalid metadata date: {raw_date}")
        return

    if updated > date.today():
        _add(result, "FAIL", rel, f"metadata date is in the future: {raw_date}")
    elif (date.today() - updated).days > 90:
        _add(result, "WARN", rel, f"metadata is older than 90 days: {raw_date}")

    if not owner.strip():
        _add(result, "FAIL", rel, "metadata owner is empty")


def check_h1(result: LintResult, path: Path, text: str) -> None:
    for line in text.splitlines():
        if not line.strip():
            continue
        if not line.startswith("# "):
            _add(result, "FAIL", _rel(path, result.root), "first non-empty line must be an H1 heading")
        return


def check_sections(result: LintResult, key: str, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    lines = {line.strip() for line in text.splitlines()}
    for alternatives in REQUIRED_SECTIONS[key]:
        if not any(heading in lines for heading in alternatives):
            _add(result, "FAIL", rel, f"missing required section: {alternatives[0]}")


def check_placeholders(result: LintResult, path: Path, text: str) -> None:
    matches = PLACEHOLDER_RE.findall(text)
    if matches:
        unique = sorted(set(matches))
        shown = ", ".join(unique[:3])
        suffix = "" if len(unique) <= 3 else f" and {len(unique) - 3} more"
        _add(result, "FAIL", _rel(path, result.root), f"unresolved placeholders: {shown}{suffix}")


def check_secrets(result: LintResult, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    reported: set[int] = set()
    for number, line in enumerate(text.splitlines(), 1):
        if number in reported:
            continue
        for pattern in SECRET_PATTERNS:
            match = pattern.search(line)
            if not match:
                continue
            value = match.group(0)
            if any(hint in value.lower() for hint in PLACEHOLDER_HINTS):
                continue
            reported.add(number)
            _add(result, "FAIL", rel, "possible secret (value redacted)", number)
            break


def check_links(result: LintResult, key: str, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    for number, line in enumerate(text.splitlines(), 1):
        for raw_target in LINK_RE.findall(line):
            target = raw_target.strip()
            if not target or target.startswith(("#", "http://", "https://", "mailto:", "<")):
                continue
            if "{{" in target:
                continue
            target = target.split()[0].strip("'\"")
            target = target.split("#", 1)[0].split("?", 1)[0]
            if not target:
                continue
            candidate = Path(unquote(target))
            if candidate.is_absolute():
                continue
            if not (path.parent / candidate).exists():
                level = "FAIL" if key == "passport" else "WARN"
                _add(result, level, rel, f"local link does not resolve: {target}", number)


def check_code_blocks(result: LintResult, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    in_block = False
    start = 0
    length = 0
    for number, line in enumerate(text.splitlines(), 1):
        if line.strip().startswith("```"):
            if not in_block:
                in_block = True
                start = number
                length = 0
            else:
                if length > 25:
                    _add(result, "WARN", rel, f"code block is {length} lines (prefer <= 25)", start)
                in_block = False
            continue
        if in_block:
            length += 1
    if in_block and length > 25:
        _add(result, "WARN", rel, f"unclosed code block with {length} lines", start)


def _section_lines(text: str, headings: tuple[str, ...]) -> tuple[int, list[str]]:
    lines = text.splitlines()
    start = None
    for index, line in enumerate(lines):
        if line.strip() in headings:
            start = index + 1
            break
    if start is None:
        return 0, []
    end = len(lines)
    for index in range(start, len(lines)):
        if lines[index].startswith("## "):
            end = index
            break
    return start + 1, lines[start:end]


def check_bugs(result: LintResult, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    first_line, active = _section_lines(text, ("## Активные", "## Active"))
    ids: list[str] = []
    for number, line in enumerate(active, first_line):
        if not line.strip().startswith("|"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if not cells:
            continue
        if cells[0].lower() in {"id", "идентификатор"}:
            continue
        if all(set(cell) <= {"-", ":"} for cell in cells if cell):
            continue
        bug_id = cells[0]
        if not bug_id:
            continue
        if not BUG_ID_RE.fullmatch(bug_id):
            _add(result, "FAIL", rel, f"bug id must have the form BUG-###: {bug_id}", number)
        else:
            ids.append(bug_id)
        if len(cells) >= 3 and cells[2] and cells[2] not in ALLOWED_BUG_STATUSES:
            _add(result, "FAIL", rel, f"unknown bug status: {cells[2]}", number)

    for bug_id, count in Counter(ids).items():
        if count > 1:
            _add(result, "FAIL", rel, f"duplicate bug id: {bug_id}")

    for number, line in enumerate(text.splitlines(), 1):
        for bug_id in BUG_ID_ANY_RE.findall(line):
            if not BUG_ID_RE.fullmatch(bug_id):
                _add(result, "FAIL", rel, f"bug id must have the form BUG-###: {bug_id}", number)


def check_todo(result: LintResult, path: Path, text: str) -> None:
    rel = _rel(path, result.root)
    open_count = len(OPEN_TODO_RE.findall(text))
    if open_count > 7:
        _add(result, "FAIL", rel, f"{open_count} open items (limit 7)")
    if DONE_TODO_RE.search(text):
        _add(result, "WARN", rel, "remove completed [x] items instead of keeping them")


def check_agents(result: LintResult, config: dict) -> None:
    rel = config["agents_file"]
    path = result.root / rel
    if not path.exists():
        _add(result, "FAIL", rel, "file is missing")
        return
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as exc:
        _add(result, "FAIL", rel, f"cannot read file: {exc}")
        return

    start_marker = "<!-- project-memory:start -->"
    end_marker = "<!-- project-memory:end -->"
    start = text.find(start_marker)
    end = text.find(end_marker)
    if start == -1 or end == -1:
        _add(result, "FAIL", rel, "missing project-memory start/end markers")
        return
    if end < start:
        _add(result, "FAIL", rel, "project-memory end marker is before start marker")
        return
    if text.count(start_marker) != 1 or text.count(end_marker) != 1:
        _add(result, "FAIL", rel, "expected exactly one project-memory block")

    block = text[start : end + len(end_marker)]
    block_lines = len(block.splitlines())
    if block_lines > 25:
        _add(result, "FAIL", rel, f"project-memory block is {block_lines} lines (limit 25)")
    passport = config["files"]["passport"]
    if passport not in block:
        _add(result, "FAIL", rel, f"project-memory block must reference {passport}")
    if PLACEHOLDER_RE.search(block):
        _add(result, "FAIL", rel, "project-memory block contains unresolved placeholders")


def check_passport_router(result: LintResult, config: dict, passport_text: str) -> None:
    passport_path = config["files"]["passport"]
    for key in FILE_KEYS:
        if key == "passport":
            continue
        target = config["files"][key]
        if target not in passport_text:
            _add(
                result,
                "FAIL",
                passport_path,
                f"passport router does not mention {target}",
            )


def lint(root: Path | str, strict: bool = False) -> LintResult:
    root = Path(root).expanduser().resolve()
    result = LintResult(root=root, strict=strict)
    config, errors = load_config(root)
    if config is None:
        result.config_error = "; ".join(errors)
        return result

    result.limits = {key: config["limits"][key] for key in FILE_KEYS}

    archive_dir = root / config["archive_dir"]
    if not archive_dir.exists():
        _add(result, "FAIL", config["archive_dir"], "archive directory is missing")

    texts: dict[str, str] = {}
    for key in FILE_KEYS:
        rel = config["files"][key]
        path = root / rel
        if not path.exists():
            _add(result, "FAIL", rel, "file is missing")
            continue
        try:
            raw = path.read_bytes()
        except OSError as exc:
            _add(result, "FAIL", rel, f"cannot read file: {exc}")
            continue
        if raw.startswith(b"\xef\xbb\xbf"):
            _add(result, "WARN", rel, "file starts with a UTF-8 BOM")
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            _add(result, "FAIL", rel, "file is not valid UTF-8")
            text = raw.decode("utf-8", errors="replace")

        texts[key] = text
        line_count = len(text.splitlines())
        result.files[rel] = line_count
        limit = config["limits"][key]
        if line_count > limit:
            _add(result, "FAIL", rel, f"{line_count}/{limit} lines (archive or trim)")
        if key == "passport" and line_count > 120 and line_count <= limit:
            _add(result, "WARN", rel, f"{line_count} lines (target <= 120)")

        check_h1(result, path, text)
        check_metadata(result, path, text)
        check_sections(result, key, path, text)
        check_placeholders(result, path, text)
        check_secrets(result, path, text)
        check_links(result, key, path, text)
        check_code_blocks(result, path, text)

        if key == "bugs":
            check_bugs(result, path, text)
        elif key == "todo":
            check_todo(result, path, text)

    if "passport" in texts:
        check_passport_router(result, config, texts["passport"])

    check_agents(result, config)
    return result


def _sorted_issues(result: LintResult) -> list[Issue]:
    return sorted(
        result.issues,
        key=lambda issue: (0 if issue.level == "FAIL" else 1, issue.path, issue.line or 0, issue.message),
    )


def print_text(result: LintResult) -> None:
    print("project-memory lint")
    print(f"root: {result.root}")
    if result.config_error is not None:
        print(f"FAIL config: {result.config_error}")
        print("result: FAIL (config)")
        return

    print(f"files: {len(result.files)}, lines: {result.total_lines}/{result.total_limit}")
    for issue in _sorted_issues(result):
        location = issue.path if issue.line is None else f"{issue.path}:{issue.line}"
        print(f"{issue.level} {location}: {issue.message}")
    status = "OK" if result.ok else "FAIL"
    print(f"result: {status} ({len(result.errors)} errors, {len(result.warnings)} warnings)")


def print_json(result: LintResult) -> None:
    payload = {
        "root": str(result.root),
        "ok": result.ok,
        "config_error": result.config_error,
        "files": result.files,
        "total_lines": result.total_lines,
        "total_limit": result.total_limit,
        "issues": [
            {"level": issue.level, "path": issue.path, "line": issue.line, "message": issue.message}
            for issue in _sorted_issues(result)
        ],
    }
    print(json.dumps(payload, ensure_ascii=False, indent=2))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate project-memory files.")
    parser.add_argument("--root", default=".", help="project root (default: current directory)")
    parser.add_argument("--strict", action="store_true", help="treat warnings as errors")
    parser.add_argument("--json", action="store_true", help="print JSON")
    args = parser.parse_args(argv)

    try:
        result = lint(args.root, strict=args.strict)
    except Exception as exc:  # noqa: BLE001 - CLI must return a stable exit code
        print(f"FAIL internal: {exc}")
        return 2

    if args.json:
        print_json(result)
    else:
        print_text(result)

    if result.config_error is not None:
        return 2
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main())
