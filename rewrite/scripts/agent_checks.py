#!/usr/bin/env python3
"""Compact reports for rewrite backend, frontend and browser checks.

Run only after verification is authorized; dotnet invocations require an elevated shell.
Native report reference: https://xunit.net/docs/format-xml-v2
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import signal
import shutil
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from frontend_check_reports import parse_vitest, parse_playwright

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / '.artifacts' / 'agent-checks'
PROJECTS = {scope: f'tests/MelodyTrack.Rewrite.{name}.Tests/MelodyTrack.Rewrite.{name}.Tests.csproj'
            for scope, name in [('domain', 'Domain'), ('api', 'Api'), ('init', 'Init')]}
COUNT_KEYS = ('total', 'passed', 'failed', 'skipped', 'not_run', 'errors')
# Compiler/MSBuild text is a fallback only; the complete original log remains available.
BUILD_ERROR = re.compile(r'^(?P<location>.*?)\s*:\s*error\s+(?P<code>[A-Z]+\d+)\s*:\s*(?P<message>.*)$')


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def empty_totals():
    return dict.fromkeys(COUNT_KEYS, 0)


def parse_xunit(path):
    """Validate completed xUnit v2+ XML, retaining each distinct failure occurrence."""
    totals, failures = empty_totals(), []
    try:
        root = ET.parse(path).getroot()
        if root.tag != 'assemblies' or not root.findall('assembly'):
            raise ValueError('No xUnit assemblies')
        for assembly in root.findall('assembly'):
            counts = {key: int(assembly.get(key.replace('_', '-'), '0')) for key in COUNT_KEYS}
            if any(value < 0 for value in counts.values()):
                raise ValueError('Negative counters')
            tests = assembly.findall('./collection/test')
            observed = dict.fromkeys(('Pass', 'Fail', 'Skip', 'NotRun'), 0)
            for index, test in enumerate(tests):
                result = test.get('result')
                if result not in observed:
                    raise ValueError('Unknown test outcome')
                observed[result] += 1
                if result == 'Fail':
                    failure = test.find('failure')
                    failures.append({'category': 'test', 'test': test.get('name'),
                                     'type': test.get('type'), 'method': test.get('method'),
                                     'file': test.get('source-file'), 'line': test.get('source-line'),
                                     'message': failure.findtext('message', '') if failure is not None else '',
                                     'stack': failure.findtext('stack-trace', '') if failure is not None else '',
                                     'record': f'assembly[{assembly.get("name", "")}]/test[{index}]'})
            expected = {'Pass': counts['passed'], 'Fail': counts['failed'],
                        'Skip': counts['skipped'], 'NotRun': counts['not_run']}
            if observed != expected or len(tests) != counts['total']:
                raise ValueError('Test records and assembly counters disagree')
            errors = assembly.findall('./errors/error')
            if len(errors) != counts['errors']:
                raise ValueError('Error records and assembly counters disagree')
            for index, error in enumerate(errors):
                failures.append({'category': 'runner', 'test': error.get('name'),
                                 'message': error.findtext('./failure/message', ''),
                                 'stack': error.findtext('./failure/stack-trace', ''),
                                 'record': f'assembly[{assembly.get("name", "")}]/error[{index}]'})
            for key in COUNT_KEYS:
                totals[key] += counts[key]
        if totals['total'] == 0 or totals['passed'] + totals['failed'] == 0:
            raise ValueError('No tests executed (empty or entirely skipped/not-run)')
        return {'totals': totals, 'failures': failures, 'complete': True, 'problem': None}
    except (OSError, ET.ParseError, ValueError) as error:
        return {'totals': totals, 'failures': failures, 'complete': False, 'problem': str(error)}


def build_diagnostics(log):
    grouped = {}
    for line in log.read_text(errors='replace').splitlines():
        match = BUILD_ERROR.match(line.strip())
        if match:
            key = tuple(match.group(name) for name in ('location', 'code', 'message'))
            if key in grouped:
                grouped[key]['occurrences'] += 1
            else:
                grouped[key] = {'category': 'build', 'file': key[0], 'code': key[1],
                                'message': key[2], 'occurrences': 1,
                                'parser': 'text-fallback; no application/infrastructure attribution'}
    return list(grouped.values())


def invoke(command, cwd, log, timeout, env=None):
    """Stream directly to a private file; stop the complete process group on cancellation."""
    started = time.monotonic()
    process = None
    result = {'command': command, 'exit_code': None, 'status': 'running', 'log': str(log)}
    with log.open('w') as output:
        try:
            process = subprocess.Popen(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT,
                                       env={**os.environ, **env} if env else None,
                                       start_new_session=(os.name == 'posix'))
            result['exit_code'] = process.wait(timeout=timeout)
            result['status'] = 'success' if result['exit_code'] == 0 else 'command_failed'
        except OSError as error:
            result.update(status='tool_error', problem=str(error))
        except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
            result['status'] = 'timeout' if isinstance(error, subprocess.TimeoutExpired) else 'interrupted'
            if process is not None:
                try:
                    if os.name == 'posix':
                        os.killpg(process.pid, signal.SIGTERM)
                    else:
                        process.terminate()
                    try:
                        process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        if os.name == 'posix':
                            os.killpg(process.pid, signal.SIGKILL)
                        else:
                            process.kill()
                except ProcessLookupError:
                    pass
                result['exit_code'] = process.wait()
    result['duration_ms'] = round((time.monotonic() - started) * 1000)
    return result


def npm_command():
    fnm = shutil.which('fnm')
    fallback = Path.home() / '.local/share/fnm/fnm'
    if fnm or fallback.is_file():
        return [fnm or str(fallback), 'exec', '--using', '26', 'npm']
    return ['npm']


def plan(scope, directory, filters=None, browser_projects=None, browser_port=5174):
    commands = []
    if scope == 'frontend' and filters:
        for index, target in enumerate(filters):
            name = f'frontend-{index + 1}'
            native_report = directory / name / 'result.json'
            pattern = '^' + re.sub(r'([\\^$.*+?()\[\]{}|])', r'\\\1', target['filter_name']) + '(?![\\s\\S])'
            commands.append({'name': name, 'kind': scope, 'cwd': str(ROOT / 'web'),
                             'command': npm_command() + ['run', 'test', '--', '--testNamePattern', pattern],
                             'env': {'AGENT_CHECKS_VITEST_REPORT': str(native_report),
                                     'AGENT_CHECKS_VITEST_FILES': json.dumps([target['file']])},
                             'report': str(native_report), 'targets': [target]})
        return commands
    if scope in ('frontend', 'browser'):
        report_dir = directory / scope
        native_report = report_dir / 'result.json'
        command = npm_command() + ['run', 'verify:fix' if scope == 'frontend' else 'test:browser']
        environment = ({'AGENT_CHECKS_VITEST_REPORT': str(native_report)} if scope == 'frontend' else
                       {'AGENT_CHECKS_BROWSER_DIR': str(report_dir),
                        'AGENT_CHECKS_BROWSER_PORT': str(browser_port)})
        if scope == 'browser' and browser_projects:
            command += ['--', *(f'--project={project}' for project in browser_projects)]
        if filters:
            command += ['--', '--test-list', str(report_dir / 'failed-tests.txt')]
            environment['AGENT_CHECKS_BROWSER_PORT'] = filters['port']
        return [{'name': scope, 'kind': scope, 'command': command, 'cwd': str(ROOT / 'web'),
                 'env': environment, 'report': str(native_report),
                 'selection': browser_projects or [],
                 **({'targets': filters['tests']} if filters else {})}]
    if scope in ('all', 'build'):
        commands.append({'name': 'build', 'kind': 'build', 'command':
                         ['dotnet', 'build', 'MelodyTrack.Rewrite.slnx', '--verbosity', 'minimal',
                          '-bl:' + str(directory / 'build.binlog')]})
    for item in PROJECTS if scope == 'all' else ([scope] if scope in PROJECTS else []):
        report_dir = directory / item
        command = ['dotnet', 'test', '--project', PROJECTS[item], '--report-xunit-xml',
                   '--report-xunit-xml-filename', 'result.xunit.xml', '--results-directory', str(report_dir),
                   '--explicit', 'off']
        if scope == 'all':
            command += ['--no-build', '--no-restore']
        if filters:
            command += ['--filter-method', *filters]
        commands.append({'name': item, 'kind': 'test', 'command': command,
                         'report': str(report_dir / 'result.xunit.xml')})
    return commands


def short_summary(report):
    lines = [f"{report['run_id']}: {report['status']} (exit {report['exit_code']}, scope {report['scope']})",
             ' '.join(f'{key}={value}' for key, value in report['totals'].items()),
             f"Report: {report['artifacts']['report']}"]
    if report.get('parent_run'):
        lines.append(f"Subset rerun of {report['parent_run']}; full suite remains unverified by this run.")
    for check in report['checks']:
        if check.get('selection'):
            lines.append(f"{check['name']} projects: {', '.join(check['selection'])}; other projects not verified.")
    for index, failure in enumerate(report['failures'][:5]):
        # Deliberately omit arbitrary test assertion values from the compact summary.
        lines.append(f"[{index}] {failure['check']}: {failure['category']}; details {index}")
    if len(report['failures']) > 5:
        lines.append(f"{len(report['failures']) - 5} more failures: use details --all")
    return '\n'.join(lines) + '\n'


def save_report(report, directory):
    # Atomic metadata replacement; concurrent runs have independent directories.
    temporary = directory / 'report.json.tmp'
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    temporary.replace(directory / 'report.json')
    (directory / 'summary.txt').write_text(short_summary(report))


def run_checks(scope, timeout=600, parent=None, filters_by_scope=None, artifacts=ARTIFACTS,
               browser_projects=None, browser_port=5174):
    os.umask(0o077)
    started = time.monotonic()
    run_id = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S') + '-' + uuid.uuid4().hex[:8]
    directory = artifacts.resolve() / run_id
    directory.mkdir(parents=True)
    report = {'schema_version': 1, 'run_id': run_id, 'started_at': utc_now(), 'duration_ms': 0,
              'scope': scope, 'parent_run': parent, 'status': 'running', 'exit_code': None,
              'checks': [], 'totals': empty_totals(), 'failures': [],
              'artifacts': {'directory': str(directory), 'report': str(directory / 'report.json'),
                            'summary': str(directory / 'summary.txt')}}
    save_report(report, directory)
    specs = plan(scope, directory, browser_projects=browser_projects, browser_port=browser_port)
    if filters_by_scope is not None:
        specs = [entry for item, filters in filters_by_scope.items()
                 for entry in plan(item, directory, filters)]
    halted = False
    for spec in specs:
        log = directory / (spec['name'] + '.log')
        if halted:
            check = dict(spec, status='skipped', exit_code=None, reason='Previous check could not complete')
        else:
            if 'report' in spec:
                Path(spec['report']).parent.mkdir(parents=True)
            if spec['kind'] == 'browser' and spec.get('targets'):
                lines = [f"[{target['project']}] › {target['file']} › " + ' › '.join(target['title_path'])
                         for target in spec['targets']]
                (Path(spec['report']).parent / 'failed-tests.txt').write_text('\n'.join(lines) + '\n')
            check = dict(spec, **invoke(spec['command'], spec.get('cwd', ROOT), log, timeout,
                                       **({'env': spec['env']} if 'env' in spec else {})))
            if check['status'] in ('timeout', 'interrupted', 'tool_error'):
                halted = True
            elif spec['kind'] == 'build':
                if check['exit_code'] != 0:
                    check['status'] = 'build_failed'
                    check['failures'] = build_diagnostics(log)
                    halted = True
            else:
                parse = {'frontend': parse_vitest, 'browser': parse_playwright}.get(spec['kind'], parse_xunit)
                parsed = parse(spec['report'])
                if spec.get('targets') and parsed['complete']:
                    fields = ('file', 'test') if spec['kind'] == 'frontend' else ('file', 'project', 'title_path')
                    def identity(test):
                        file = Path(test['file'])
                        if spec['kind'] == 'frontend' and file.is_absolute():
                            file = file.relative_to(ROOT / 'web')
                        return (str(file), *(json.dumps(test.get(key), ensure_ascii=False) for key in fields[1:]))
                    actual = [identity(test) for test in parsed['tests']
                              if test['status'] not in ('skipped', 'todo', 'disabled', 'pending')]
                    expected = [identity(test) for test in spec['targets']]
                    if sorted(actual) != sorted(expected):
                        parsed.update(complete=False, problem='Rerun selection differs from executed tests; inspect native report')
                check.update(parsed)
                check['status'] = ('test_failed' if parsed['complete'] and parsed['failures'] else
                                   'incomplete' if not parsed['complete'] else
                                   'command_failed' if check['exit_code'] != 0 else 'success')
                for key in COUNT_KEYS:
                    report['totals'][key] += parsed['totals'][key]
            for failure in check.get('failures', []):
                report['failures'].append(dict(failure, check=spec['name'], log=str(log),
                                               scope=spec['kind'] if spec['kind'] in ('frontend', 'browser') else spec['name'],
                                               structured_report=spec.get('report')))
            if check['status'] != 'success' and not check.get('failures'):
                report['failures'].append({'check': spec['name'], 'category': check['status'],
                                           'message': check.get('problem', 'See complete command log'),
                                           'log': str(log), 'structured_report': spec.get('report')})
        report['checks'].append(check)
        save_report(report, directory)
    failing = [check for check in report['checks'] if check['status'] != 'success']
    report['status'] = failing[0]['status'] if failing else ('subset_passed' if parent else 'success')
    # Original exit codes remain attached to every check; wrappers use 1 for invalid zero-code output.
    report['exit_code'] = next((check['exit_code'] for check in failing if check['exit_code']), 1 if failing else 0)
    report['duration_ms'] = round((time.monotonic() - started) * 1000)
    save_report(report, directory)
    return report


def load_report(run, artifacts=ARTIFACTS):
    if run == 'latest':
        candidates = list(artifacts.glob('*/report.json'))
        if not candidates:
            raise ValueError('No recorded checks (not_run)')
        path = max(candidates, key=lambda path: (path.stat().st_mtime_ns, str(path)))
    else:
        if Path(run).name != run or run in ('.', '..'):
            raise ValueError('Use a run ID, not a path')
        path = artifacts / run / 'report.json'
    return json.loads(path.read_text())


def rerun_filters(report):
    result = {}
    for failure in report['failures']:
        if failure['category'] != 'test':
            continue
        scope = failure.get('scope', failure['check'])
        if scope in ('frontend', 'browser'):
            check = next((item for item in report.get('checks', []) if item['name'] == failure['check']), None)
            if not check or not check.get('complete') or not check.get('tests'):
                raise ValueError('No complete test inventory for precise rerun; rerun the affected scope')
            fields = ('file', 'test') if scope == 'frontend' else ('file', 'test', 'project', 'title_path')
            target = {key: failure.get(key) for key in fields}
            if not all(target.values()) or sum(all(test.get(key) == target[key] for key in fields)
                                               for test in check['tests']) != 1:
                raise ValueError('Missing or ambiguous failed test identity; rerun the affected scope')
            file = Path(target['file'])
            if scope == 'frontend' and file.is_absolute():
                file = file.relative_to(ROOT / 'web')
            if file.is_absolute() or '..' in file.parts:
                raise ValueError('Test file must be inside its test root')
            target['file'] = file.as_posix()
            if scope == 'frontend':
                if not isinstance(failure.get('filter_name'), str) or not failure['filter_name']:
                    raise ValueError('Missing Vitest title path for precise rerun; rerun frontend scope')
                target['filter_name'] = failure['filter_name']
                if sum(test.get('file') == failure['file'] and test.get('filter_name') == target['filter_name']
                       for test in check['tests']) != 1:
                    raise ValueError('Ambiguous Vitest title path; rerun frontend scope')
                if re.search(r'[*?\[\]{}()!+@\\]', target['file']):
                    raise ValueError('Cannot safely express test file glob; rerun frontend scope')
                result.setdefault(scope, []).append(target)
            else:
                parts = [target['file'], target['project'], *target['title_path']]
                if any(not isinstance(part, str) or not part or part != part.strip()
                       or any(char in part for char in '\r\n›>') for part in parts):
                    raise ValueError('Cannot safely express Playwright test list; rerun browser scope')
                matches = [test for test in check['tests'] if test.get('file') == failure['file']
                           and test.get('project') == target['project']
                           and test.get('title_path', [])[:len(target['title_path'])] == target['title_path']]
                if len(matches) != 1 or ':' in target['file']:
                    raise ValueError('Ambiguous Playwright test list; rerun browser scope')
                port = check.get('env', {}).get('AGENT_CHECKS_BROWSER_PORT', '5174')
                if not str(port).isdigit() or not 1 <= int(port) <= 65535:
                    raise ValueError('Invalid original browser port')
                result.setdefault(scope, {'tests': [], 'port': str(port)})['tests'].append(target)
            continue
        cls, method = failure.get('type'), failure.get('method')
        if scope not in PROJECTS or not cls or not method:
            raise ValueError('Failed test has no reliable project/type/method; rerun its scope instead')
        target = f'{cls}.{method}'
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_.+`]*', target):
            raise ValueError('Cannot safely express this method filter; rerun its scope instead')
        result.setdefault(scope, set()).add(target)
    if not result:
        raise ValueError('No failed tests to rerun; inspect failures or run the affected scope')
    return {scope: sorted(methods) if scope in PROJECTS else methods for scope, methods in result.items()}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    for action in ('plan', 'run'):
        command = sub.add_parser(action)
        command.add_argument('--scope', choices=[*PROJECTS, 'all', 'build', 'frontend', 'browser'], default='domain')
        command.add_argument('--timeout', type=float, default=600)
        command.add_argument('--browser-project', action='append', help='Playwright project; repeat to select several')
        command.add_argument('--browser-port', type=int, default=5174)
    for action in ('summary', 'details', 'rerun-failed'):
        command = sub.add_parser(action)
        command.add_argument('--run', default='latest')
        if action == 'details':
            command.add_argument('--index', type=int, default=0)
            command.add_argument('--all', action='store_true')
        if action == 'rerun-failed':
            command.add_argument('--timeout', type=float, default=600)
    args = parser.parse_args(argv)
    try:
        if hasattr(args, 'timeout') and args.timeout <= 0:
            raise ValueError('Timeout must be positive')
        if args.action in ('plan', 'run'):
            if not 1 <= args.browser_port <= 65535:
                raise ValueError('Browser port must be between 1 and 65535')
            if args.scope != 'browser' and (args.browser_project or args.browser_port != 5174):
                raise ValueError('Browser options require --scope browser')
        if args.action == 'plan':
            print(json.dumps(plan(args.scope, ARTIFACTS / '<run-id>', browser_projects=args.browser_project,
                                  browser_port=args.browser_port), indent=2))
            return 0
        if args.action == 'run':
            report = run_checks(args.scope, args.timeout, browser_projects=args.browser_project,
                                browser_port=args.browser_port)
        else:
            report = load_report(args.run)
            if args.action == 'rerun-failed':
                filters = rerun_filters(report)
                report = run_checks('failed-tests', args.timeout, report['run_id'], filters)
            elif args.action == 'details':
                if args.index < 0:
                    raise ValueError('Index must be nonnegative')
                print(json.dumps(report['failures'] if args.all else report['failures'][args.index],
                                 ensure_ascii=False, indent=2))
                return 0
        print(short_summary(report), end='')
        return report['exit_code'] or (1 if report['status'] == 'running' else 0)
    except (ValueError, OSError, IndexError, KeyError) as error:
        print(f'agent_checks: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    # Convert a normal termination request to the same cleanup path as Ctrl-C.
    def interrupt(signum, frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupt)
    sys.exit(main())
