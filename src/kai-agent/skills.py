"""KAI skill discovery — loads SKILL.md files from the filesystem."""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


def discover_file_skills(extra_dirs: list[str] | None = None) -> list[dict[str, Any]]:
    """Discover skills from SKILL.md files in the default and extra directories.

    Each skill directory contains a SKILL.md with YAML frontmatter (name, description)
    and a markdown body (instructions).
    """
    skill_dirs = [os.path.join(os.path.dirname(__file__), "skills")]
    if extra_dirs:
        skill_dirs.extend(extra_dirs)

    skills: list[dict[str, Any]] = []
    seen_names: set[str] = set()

    for base_dir in skill_dirs:
        if not os.path.isdir(base_dir):
            continue
        for entry in sorted(os.listdir(base_dir)):
            skill_path = os.path.join(base_dir, entry, "SKILL.md")
            if not os.path.isfile(skill_path):
                continue
            skill = _parse_skill_file(skill_path)
            if skill and skill["name"] not in seen_names:
                skills.append(skill)
                seen_names.add(skill["name"])

    logger.info("Discovered %d file skills from %d directories", len(skills), len(skill_dirs))
    return skills


def _parse_skill_file(path: str) -> dict[str, Any] | None:
    """Parse a SKILL.md file with YAML frontmatter."""
    try:
        content = Path(path).read_text(encoding="utf-8")
    except OSError as e:
        logger.warning("Cannot read skill file %s: %s", path, e)
        return None

    # Split frontmatter from body
    if not content.startswith("---"):
        return None

    parts = content.split("---", 2)
    if len(parts) < 3:
        return None

    frontmatter = parts[1].strip()
    body = parts[2].strip()

    # Simple YAML parsing (avoid PyYAML dependency)
    meta: dict[str, str] = {}
    current_key = ""
    current_value_lines: list[str] = []

    for line in frontmatter.split("\n"):
        if line.startswith("  ") and current_key:
            current_value_lines.append(line.strip())
        elif ":" in line:
            if current_key:
                meta[current_key] = " ".join(current_value_lines).strip()
            key, _, val = line.partition(":")
            current_key = key.strip()
            val = val.strip().strip(">").strip('"').strip("'")
            current_value_lines = [val] if val else []
        else:
            if current_key:
                current_value_lines.append(line.strip())

    if current_key:
        meta[current_key] = " ".join(current_value_lines).strip()

    name = meta.get("name", "")
    description = meta.get("description", "")

    if not name:
        return None

    return {
        "name": name,
        "description": description,
        "instructions": body,
        "source": "file",
        "path": path,
    }


def format_skills_for_prompt(skills: list[dict[str, Any]]) -> str:
    """Format loaded skills into a prompt section for the agent."""
    if not skills:
        return ""

    lines = ["\n## ACTIVE SKILLS\n"]
    lines.append("The following domain skills are loaded. Use them to guide your coaching:\n")

    for skill in skills:
        lines.append(f"### Skill: {skill['name']}")
        lines.append(f"*{skill['description']}*\n")
        lines.append(skill["instructions"])
        lines.append("")

    return "\n".join(lines)
