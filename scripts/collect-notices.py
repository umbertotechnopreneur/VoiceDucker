# VBWR B
# Project: VoiceDucker
# Repository: https://github.com/umbertotechnopreneur/VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# VibeWare: Human intent. AI implementation. Accountable human review.
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
# Created with AI: OpenAI Codex; restored dependency notice collection, 2026-10-09.
# Human guidance: Umberto Giacobbi requested public distribution.
# Evidence: Locked dependency metadata; see Git history and CI.
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# VBWR E

"""Copy restored dependency license notices into a distribution directory."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mode', choices=['nuget', 'cargo'], required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--target')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
args.output.mkdir(parents=True, exist_ok=True)
records = []

if args.mode == 'cargo':
    command = ['cargo', 'metadata', '--locked', '--format-version', '1']
    if args.target:
        command += ['--filter-platform', args.target]
    metadata = json.loads(subprocess.check_output(command, cwd=root, text=True))
    for package in metadata['packages']:
        if package['source'] is None:
            continue
        records.append((package['name'], package['version'], package.get('license') or 'See supplied license files', package.get('repository') or '', Path(package['manifest_path']).parent))
else:
    cache = Path(subprocess.check_output(['dotnet', 'nuget', 'locals', 'global-packages', '--list'], cwd=root, text=True).strip().split(': ', 1)[1])
    lock = json.loads((root / 'packages.lock.json').read_text())
    seen = set()
    for dependencies in lock['dependencies'].values():
        for name, dependency in dependencies.items():
            version = dependency['resolved']
            if (name, version) in seen:
                continue
            seen.add((name, version))
            directory = cache / name.lower() / version.lower()
            nuspec = next(directory.glob('*.nuspec'))
            document = ET.parse(nuspec)
            metadata = next(item for item in document.getroot() if item.tag.endswith('metadata'))
            fields = {item.tag.rsplit('}', 1)[-1]: item.text or '' for item in metadata}
            records.append((name, version, fields.get('license') or fields.get('licenseUrl') or 'See supplied license files', fields.get('projectUrl', ''), directory))

    if args.target:
        runtime_config = root / 'artifacts' / 'app' / args.target / 'VoiceDucker.runtimeconfig.json'
        runtime = json.loads(runtime_config.read_text())['runtimeOptions']['includedFrameworks']
        for framework in runtime:
            if framework['name'] == 'Microsoft.NETCore.App':
                name = 'Microsoft.NETCore.App.Runtime.' + args.target
                version = framework['version']
                records.append((name, version, 'MIT', 'https://github.com/dotnet/runtime', cache / name.lower() / version))

index = ['# Dependency notices', '', 'Generated from the locked, restored package metadata. This inventory can include build-time components; it does not mean every listed package is embedded in the executable.', '', 'Project code remains MIT licensed. Dependencies retain their own licenses.', '', '| Dependency | Declared license | Source |', '| --- | --- | --- |']
for name, version, license_name, url, directory in sorted(records):
    destination = args.output / (name + '-' + version)
    candidates = []
    for item in directory.iterdir():
        if item.is_file() and re.match(r'(?i)^(licen[sc]e|copying|copyright|notice|third.?party)', item.name):
            candidates.append(item)
        elif item.is_dir() and item.name.lower() in {'licenses', 'licences'}:
            candidates += [file for file in item.rglob('*') if file.is_file()]
    declared_file = directory / license_name
    if declared_file.is_file() and declared_file not in candidates:
        candidates.append(declared_file)
    if not candidates and license_name == 'MIT' and args.mode == 'nuget':
        destination.mkdir(parents=True, exist_ok=True)
        if name.startswith('NAudio.'):
            shutil.copyfile(root / 'docs/licenses/naudio.txt', destination / 'LICENSE.txt')
        else:
            nuspec = next(directory.glob('*.nuspec'))
            metadata = next(item for item in ET.parse(nuspec).getroot() if item.tag.endswith('metadata'))
            fields = {item.tag.rsplit('}', 1)[-1]: item.text or '' for item in metadata}
            copyright_notice = fields.get('copyright') or ('Copyright ' + fields.get('authors', name))
            license_text = (root / 'LICENSE').read_text().replace('Copyright (c) 2026 Umberto Giacobbi', copyright_notice)
            (destination / 'LICENSE.txt').write_text(license_text, encoding='utf-8')
    for item in candidates:
        relative = item.relative_to(directory)
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(item, target)
    index.append(f'| {name} {version} | {license_name.replace("|", "/")} | {url} |')
(args.output / 'README.md').write_text('\n'.join(index) + '\n', encoding='utf-8')
print(f'Collected notices for {len(records)} locked dependencies in {args.output}')
