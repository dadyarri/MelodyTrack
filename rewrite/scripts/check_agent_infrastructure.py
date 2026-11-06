#!/usr/bin/env python3
"""Validate repo-local agent artifacts without network, Codex sessions or product builds."""
from pathlib import Path
import re
import sys
import tomllib

ROOT = Path(__file__).resolve().parents[1]


def validate(root=ROOT):
    errors = []
    skills = sorted((root / '.agents/skills').glob('*/SKILL.md'))
    agents = sorted((root / '.codex/agents').glob('*.toml'))
    if not skills or not agents:
        errors.append('Missing discoverable skills or custom agents')
    names = set()
    for path in skills:
        text = path.read_text()
        header = re.match(r'\A---\n(.*?)\n---\n', text, re.S)
        fields = dict(re.findall(r'^(name|description):\s*(.+)$', header[1], re.M)) if header else {}
        if fields.get('name') != path.parent.name or not fields.get('description'):
            errors.append(f'{path}: missing/mismatched frontmatter')
        if fields.get('name') in names:
            errors.append(f'{path}: duplicate skill name')
        names.add(fields.get('name'))
    for path in agents:
        try:
            data = tomllib.loads(path.read_text())
            if set(data) != {'name', 'description', 'developer_instructions'}:
                errors.append(f'{path}: unexpected or missing agent fields')
            if data.get('name') != path.stem or not data.get('description') or not data.get('developer_instructions'):
                errors.append(f'{path}: missing/mismatched agent identity')
        except tomllib.TOMLDecodeError as error:
            errors.append(f'{path}: {error}')
    files = [root / 'AGENTS.md', *skills, *root.glob('docs/agent-*.md')]
    for path in files:
        if not path.is_file():
            errors.append(f'{path}: missing')
            continue
        text = path.read_text()
        # Check real Markdown local targets and quoted relative paths in skills.
        targets = re.findall(r'\]\(([^)]+)\)', text)
        if path in skills:
            targets += re.findall(r'`((?:\.\./)+[^`]+)`', text)
        for target in targets:
            if re.match(r'[a-z]+:', target) or target.startswith('#'):
                continue
            target = target.split('#', 1)[0]
            if not (path.parent / target).exists():
                errors.append(f'{path}: broken local target {target}')
        for name in re.findall(r'\$(rewrite-[a-z-]+)', text):
            if name not in names:
                errors.append(f'{path}: unknown skill {name}')
    return errors, len(skills), len(agents)


def main():
    errors, skills, agents = validate()
    for error in errors:
        print(error, file=sys.stderr)
    print(f'{skills} skills, {agents} agents; {len(errors)} structural errors')
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
