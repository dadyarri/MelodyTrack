import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / 'frontend_check_reports.py'
spec = importlib.util.spec_from_file_location('frontend_check_reports', MODULE)
reports = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reports)


def vitest_report():
    return {
        'numTotalTestSuites': 1,
        'numPassedTestSuites': 1,
        'numFailedTestSuites': 0,
        'numPendingTestSuites': 0,
        'numTotalTests': 1,
        'numPassedTests': 1,
        'numFailedTests': 0,
        'numPendingTests': 0,
        'numTodoTests': 0,
        'success': True,
        'testResults': [{
            'name': 'src/example.test.ts',
            'status': 'passed',
            'message': '',
            'assertionResults': [{
                'fullName': 'example works',
                'status': 'passed',
                'failureMessages': [],
                'location': {'line': 3, 'column': 1},
            }],
        }],
    }


def playwright_execution(title, project, status, expected, results):
    return {
        'title': title,
        'line': 8,
        'tests': [{
            'projectId': project,
            'projectName': project,
            'expectedStatus': expected,
            'status': status,
            'results': results,
        }],
    }


def playwright_report():
    passed = {'status': 'passed', 'errors': []}
    skipped = {'status': 'skipped', 'errors': []}
    failed = {
        'status': 'failed',
        'error': {'message': 'first attempt failed', 'stack': 'synthetic stack'},
        'errors': [],
    }
    return {
        'stats': {'expected': 1, 'skipped': 2, 'unexpected': 0, 'flaky': 1},
        'errors': [{'message': 'synthetic runner issue', 'stack': 'runner stack'}],
        'suites': [{
            'title': 'example.spec.ts',
            'file': 'example.spec.ts',
            'specs': [
                playwright_execution('passes', 'chromium', 'expected', 'passed', [passed]),
                playwright_execution('is skipped', 'chromium', 'skipped', 'skipped', [skipped]),
                playwright_execution('did not run', 'webkit', 'skipped', 'passed', [skipped]),
                playwright_execution('is flaky', 'webkit', 'flaky', 'passed', [failed, passed]),
            ],
        }],
    }


class ReportFileTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name) / 'report.json'

    def parse(self, parser, value):
        self.path.write_text(json.dumps(value), encoding='utf-8')
        return parser(self.path)

    def test_playwright_inventory_preserves_nested_titles_and_project_pairs(self):
        report = playwright_report()
        specs = report['suites'][0].pop('specs')
        report['suites'][0]['suites'] = [{'title': 'outer', 'suites': [{'title': 'inner', 'specs': specs}]}]

        parsed = self.parse(reports.parse_playwright, report)

        self.assertEqual(parsed['tests'][0]['title_path'], ['outer', 'inner', 'passes'])
        failure = next(item for item in parsed['failures'] if item['category'] == 'test')
        self.assertEqual(failure['title_path'], ['outer', 'inner', 'is flaky'])
        self.assertEqual(failure['project'], 'webkit')

    def test_vitest_inventory_retains_passing_and_skipped_names_for_ambiguity_checks(self):
        parsed = self.parse(reports.parse_vitest, vitest_report())

        self.assertEqual(parsed['tests'], [{'file': 'src/example.test.ts', 'test': 'example works', 'status': 'passed',
                                           'filter_name': None}])

    def test_vitest_filter_uses_title_path_separator_instead_of_json_fullname(self):
        report = vitest_report()
        report['testResults'][0]['assertionResults'][0].update(ancestorTitles=['example'], title='works')

        parsed = self.parse(reports.parse_vitest, report)

        self.assertEqual(parsed['tests'][0]['filter_name'], 'example > works')

    def test_vitest_counts_and_failure_location_are_read_from_native_records(self):
        report = vitest_report()
        report['numTotalTests'] = report['numFailedTests'] = 1
        report['numPassedTests'] = 0
        report['numFailedTestSuites'] = 1
        report['numPassedTestSuites'] = 0
        report['success'] = False
        assertion = report['testResults'][0]['assertionResults'][0]
        assertion.update(status='failed', failureMessages=['failure message', 'stack frame'])
        report['testResults'][0].update(status='failed', message='file failure')

        parsed = self.parse(reports.parse_vitest, report)

        self.assertTrue(parsed['complete'])
        self.assertEqual(parsed['totals']['failed'], 1)
        self.assertEqual(parsed['failures'][0]['category'], 'test')
        self.assertEqual(parsed['failures'][0]['file'], 'src/example.test.ts')
        self.assertEqual(parsed['failures'][0]['line'], 3)
        self.assertEqual(parsed['failures'][0]['message'], 'failure message')
        self.assertIn('stack frame', parsed['failures'][0]['stack'])

    def test_vitest_rejects_empty_or_inconsistent_reports(self):
        empty = vitest_report()
        empty.update(numTotalTests=0, numPassedTests=0, numTotalTestSuites=0,
                     numPassedTestSuites=0, testResults=[])
        self.assertFalse(self.parse(reports.parse_vitest, empty)['complete'])

        inconsistent = vitest_report()
        inconsistent['numPassedTests'] = 2
        self.assertFalse(self.parse(reports.parse_vitest, inconsistent)['complete'])

    def test_vitest_missing_and_malformed_reports_are_incomplete(self):
        self.assertFalse(reports.parse_vitest(self.path)['complete'])
        self.path.write_text('{', encoding='utf-8')
        self.assertFalse(reports.parse_vitest(self.path)['complete'])

    def test_playwright_accounts_for_skips_not_run_flaky_and_runner_errors(self):
        parsed = self.parse(reports.parse_playwright, playwright_report())

        self.assertFalse(parsed['complete'])
        self.assertEqual(parsed['problem'], 'Playwright report contains tests that did not run')
        self.assertEqual(parsed['totals'], {
            'total': 4, 'passed': 1, 'failed': 1, 'skipped': 1, 'not_run': 1, 'errors': 1,
        })
        self.assertEqual([failure['category'] for failure in parsed['failures']], ['test', 'runner'])
        self.assertEqual(parsed['failures'][0]['project'], 'webkit')
        self.assertEqual(parsed['failures'][0]['file'], 'example.spec.ts')
        self.assertEqual(parsed['failures'][0]['line'], 8)
        self.assertIn('first attempt failed', parsed['failures'][0]['message'])

        clean = playwright_report()
        clean['stats']['skipped'] = 1
        clean['suites'][0]['specs'].pop(2)
        clean_parsed = self.parse(reports.parse_playwright, clean)
        self.assertTrue(clean_parsed['complete'])
        self.assertEqual(clean_parsed['totals']['not_run'], 0)

    def test_playwright_rejects_zero_tests_and_counter_mismatch(self):
        empty = {'stats': {'expected': 0, 'skipped': 0, 'unexpected': 0, 'flaky': 0},
                 'errors': [], 'suites': []}
        self.assertFalse(self.parse(reports.parse_playwright, empty)['complete'])

        inconsistent = playwright_report()
        inconsistent['stats']['flaky'] = 0
        self.assertFalse(self.parse(reports.parse_playwright, inconsistent)['complete'])

    def test_playwright_missing_and_malformed_reports_are_incomplete(self):
        self.assertFalse(reports.parse_playwright(self.path)['complete'])
        self.path.write_text('null', encoding='utf-8')
        self.assertFalse(reports.parse_playwright(self.path)['complete'])


if __name__ == '__main__':
    unittest.main()
