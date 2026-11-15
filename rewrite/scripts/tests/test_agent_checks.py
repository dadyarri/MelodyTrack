import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[1] / 'agent_checks.py'
spec = importlib.util.spec_from_file_location('agent_checks', MODULE)
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)


def xml(outcomes=('Pass',), errors=0):
    counters = {key: outcomes.count(value) for key, value in
                [('passed', 'Pass'), ('failed', 'Fail'), ('skipped', 'Skip'), ('not-run', 'NotRun')]}
    attrs = ' '.join(f'{key}="{value}"' for key, value in counters.items())
    tests = ''.join(f'<test name="case {i}" type="Tests.Subject" method="Operation" result="{result}">'
                    '<failure><message>same failure</message><stack-trace>frame</stack-trace></failure></test>'
                    for i, result in enumerate(outcomes))
    error_nodes = ''.join('<error type="fatal"><failure><message>fatal</message></failure></error>'
                          for _ in range(errors))
    return (f'<assemblies schema-version="3"><assembly name="test.dll" total="{len(outcomes)}" '
            f'errors="{errors}" {attrs}><collection>{tests}</collection><errors>{error_nodes}</errors>'
            '</assembly></assemblies>')


class XmlTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name) / 'result.xml'

    def parse(self, content):
        self.path.write_text(content)
        return checks.parse_xunit(self.path)

    def test_success_and_skips(self):
        result = self.parse(xml(('Pass', 'Skip', 'NotRun')))
        self.assertTrue(result['complete'])
        self.assertEqual(result['totals']['passed'], 1)
        self.assertEqual(result['totals']['skipped'], 1)
        self.assertEqual(result['totals']['not_run'], 1)

    def test_failed_cases_remain_distinct(self):
        result = self.parse(xml(('Pass', 'Fail', 'Fail')))
        self.assertTrue(result['complete'])
        self.assertEqual(result['totals']['failed'], 2)
        self.assertEqual(len(result['failures']), 2)
        self.assertNotEqual(result['failures'][0]['record'], result['failures'][1]['record'])

    def test_environment_errors_are_failures(self):
        result = self.parse(xml(errors=1))
        self.assertTrue(result['complete'])
        self.assertEqual(result['failures'][0]['category'], 'runner')

    def test_incomplete_inputs_never_pass(self):
        inputs = ['', '<assemblies>', '<junit/>', '<assemblies/>', xml(()), xml(('Skip',)),
                  xml().replace('total="1"', 'total="2"'),
                  xml().replace('passed="1"', 'passed="-1"'),
                  xml().replace('result="Pass"', 'result="Unknown"'),
                  xml().replace('total="1"', 'total="bad"')]
        for content in inputs:
            with self.subTest(content=content):
                self.assertFalse(self.parse(content)['complete'])

    def test_missing_report(self):
        self.assertFalse(checks.parse_xunit(self.path)['complete'])

    def test_multiple_assemblies_accumulate_totals_and_preserve_failures(self):
        first = xml(('Pass', 'Fail')).removeprefix('<assemblies schema-version="3">').removesuffix('</assemblies>')
        second = xml(('Pass', 'Skip'), errors=1).removeprefix('<assemblies schema-version="3">').removesuffix('</assemblies>')
        result = self.parse(f'<assemblies>{first}{second}</assemblies>')
        self.assertTrue(result['complete'])
        self.assertEqual(result['totals'], {'total': 4, 'passed': 2, 'failed': 1,
                                          'skipped': 1, 'not_run': 0, 'errors': 1})
        self.assertEqual([item['category'] for item in result['failures']], ['test', 'runner'])

    def test_inconsistent_error_counters_never_pass(self):
        self.assertFalse(self.parse(xml(errors=1).replace('errors="1"', 'errors="0"'))['complete'])

    def test_large_report_retains_rare_failures(self):
        result = self.parse(xml(('Pass',) * 3000 + ('Fail', 'Fail')))
        self.assertEqual(result['totals']['passed'], 3000)
        self.assertEqual(len(result['failures']), 2)


class ProcessTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.log = self.root / 'process.log'

    def invoke(self, code, timeout=10):
        return checks.invoke([sys.executable, '-c', code], self.root, self.log, timeout)

    def test_success_and_nonzero_exit_preserved(self):
        for code in (0, 7):
            result = self.invoke(f'print("output"); raise SystemExit({code})')
            self.assertEqual(result['exit_code'], code)
            self.assertIn('output', self.log.read_text())

    def test_missing_tool(self):
        result = checks.invoke([str(self.root / 'missing')], self.root, self.log, 1)
        self.assertEqual(result['status'], 'tool_error')
        self.assertIsNone(result['exit_code'])

    def test_timeout_is_not_success(self):
        result = self.invoke('import time; time.sleep(30)', 0.05)
        self.assertEqual(result['status'], 'timeout')
        self.assertNotEqual(result['exit_code'], 0)

    def test_interruption(self):
        original_wait = subprocess.Popen.wait
        first = True

        def interrupt_once(process, *args, **kwargs):
            nonlocal first
            if first:
                first = False
                raise KeyboardInterrupt
            return original_wait(process, *args, **kwargs)

        with patch.object(subprocess.Popen, 'wait', interrupt_once):
            result = self.invoke('import time; time.sleep(30)')
        self.assertEqual(result['status'], 'interrupted')
        self.assertEqual(result['exit_code'], -9)

    def test_build_dedup_keeps_different_errors(self):
        self.log.write_text('file.cs(2,3): error CS1000: broken\n' * 2 +
                            'other.cs(1,1): error CS1000: broken\nnoise\n')
        errors = checks.build_diagnostics(self.log)
        self.assertEqual(len(errors), 2)
        self.assertEqual(errors[0]['occurrences'], 2)


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.artifacts = Path(self.tmp.name)

    def fake(self, content, exit_code=0, status=None):
        def execute(command, cwd, log, timeout):
            log.write_text('complete original log')
            directory = Path(command[command.index('--results-directory') + 1])
            if content is not None:
                (directory / 'result.xunit.xml').write_text(content)
            return {'command': command, 'exit_code': exit_code, 'duration_ms': 1,
                    'log': str(log), 'status': status or ('success' if exit_code == 0 else 'command_failed')}
        return execute

    def run_fake(self, content, exit_code=0, status=None, **kwargs):
        with patch.object(checks, 'invoke', side_effect=self.fake(content, exit_code, status)):
            return checks.run_checks('domain', artifacts=self.artifacts, **kwargs)

    def test_failure_with_zero_exit_never_passes(self):
        report = self.run_fake(xml(('Fail',)))
        self.assertEqual(report['status'], 'test_failed')
        self.assertEqual(report['exit_code'], 1)
        self.assertEqual(report['checks'][0]['exit_code'], 0)

    def test_nonzero_with_successful_xml_never_passes(self):
        report = self.run_fake(xml(), 7)
        self.assertEqual(report['status'], 'command_failed')
        self.assertEqual(report['exit_code'], 7)

    def test_missing_or_partial_output_with_nonzero_exit(self):
        for content in (None, '<assemblies>'):
            with self.subTest(content=content):
                report = self.run_fake(content, 8)
                self.assertEqual(report['status'], 'incomplete')
                self.assertEqual(report['exit_code'], 8)

    def test_real_status_is_retained_over_partial_xml(self):
        for status in ('tool_error', 'timeout', 'interrupted'):
            with self.subTest(status=status):
                self.assertEqual(self.run_fake(None, None, status)['status'], status)

    def test_rerun_uses_methods_and_does_not_claim_full_pass(self):
        original = self.run_fake(xml(('Fail', 'Fail')), 2)
        filters = checks.rerun_filters(original)
        self.assertEqual(filters, {'domain': ['Tests.Subject.Operation']})
        result = self.run_fake(xml(), parent=original['run_id'], filters_by_scope=filters)
        self.assertEqual(result['status'], 'subset_passed')
        self.assertIn('full suite remains unverified', checks.short_summary(result))
        self.assertNotEqual(original['run_id'], result['run_id'])
        self.assertEqual(checks.load_report(original['run_id'], self.artifacts)['status'], 'test_failed')

    def test_rerun_without_methods_stops(self):
        for failures in ([], [{'category': 'test', 'check': 'domain', 'type': None}]):
            with self.assertRaises(ValueError):
                checks.rerun_filters({'failures': failures})

    def test_summary_bounds_failures_without_dropping_records(self):
        report = self.run_fake(xml(('Fail',) * 7), 2)
        summary = checks.short_summary(report)
        self.assertIn('2 more failures', summary)
        self.assertEqual(len(json.loads(Path(report['artifacts']['report']).read_text())['failures']), 7)

    def test_latest_absence_is_not_run(self):
        with self.assertRaisesRegex(ValueError, 'not_run'):
            checks.load_report('latest', self.artifacts)

    def test_plan_does_not_create_artifacts(self):
        directory = self.artifacts / 'not-created'
        plan = checks.plan('all', directory)
        self.assertEqual(len(plan), 4)
        self.assertFalse(directory.exists())
        self.assertIn('--project', plan[1]['command'])
        explicit_index = plan[1]['command'].index('--explicit')
        self.assertEqual(plan[1]['command'][explicit_index + 1], 'off')
        self.assertNotIn('--logger', plan[1]['command'])
        self.assertNotIn('--', plan[1]['command'])

    def test_build_failure_skips_all_tests_and_preserves_diagnostics(self):
        def fail_build(command, cwd, log, timeout):
            log.write_text('file.cs(2,3): error CS1000: synthetic compiler failure\n')
            return {'command': command, 'exit_code': 3, 'duration_ms': 1,
                    'log': str(log), 'status': 'command_failed'}

        with patch.object(checks, 'invoke', side_effect=fail_build) as invoke:
            report = checks.run_checks('all', artifacts=self.artifacts)

        self.assertEqual(invoke.call_count, 1)
        self.assertEqual(report['status'], 'build_failed')
        self.assertEqual(report['exit_code'], 3)
        self.assertEqual([check['status'] for check in report['checks']],
                         ['build_failed', 'skipped', 'skipped', 'skipped'])
        self.assertEqual(report['failures'][0]['code'], 'CS1000')
        self.assertEqual(report['totals']['total'], 0)

    def test_api_failure_does_not_hide_independent_init_results(self):
        def execute(command, cwd, log, timeout):
            log.write_text('synthetic log')
            is_build = command[1] == 'build'
            is_api = any('Api.Tests.csproj' in argument for argument in command)
            if not is_build:
                directory = Path(command[command.index('--results-directory') + 1])
                (directory / 'result.xunit.xml').write_text(xml(('Fail',) if is_api else ('Pass',)))
            return {'command': command, 'exit_code': 1 if is_api else 0, 'duration_ms': 1,
                    'log': str(log), 'status': 'command_failed' if is_api else 'success'}

        with patch.object(checks, 'invoke', side_effect=execute) as invoke:
            report = checks.run_checks('all', artifacts=self.artifacts)

        self.assertEqual(invoke.call_count, 4)
        self.assertEqual(report['status'], 'test_failed')
        self.assertEqual(report['checks'][-1]['status'], 'success')
        self.assertEqual(report['totals']['passed'], 2)
        self.assertEqual(report['totals']['failed'], 1)

    def test_report_ids_cannot_escape_artifact_directory(self):
        for value in ('../outside', '/tmp/report', '.', '..'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                checks.load_report(value, self.artifacts)

    def test_rerun_refuses_unrepresentable_method_filters(self):
        with self.assertRaises(ValueError):
            checks.rerun_filters({'failures': [{'category': 'test', 'check': 'domain',
                                               'type': 'Tests.Subject', 'method': 'Unsafe*'}]})


if __name__ == '__main__':
    unittest.main()
