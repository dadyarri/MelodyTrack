"""Strict, dependency-free readers for Vitest and Playwright native JSON reports."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

COUNT_KEYS = ("total", "passed", "failed", "skipped", "not_run", "errors")


def _empty() -> dict[str, Any]:
    return {
        "totals": dict.fromkeys(COUNT_KEYS, 0),
        "failures": [],
        "tests": [],
        "complete": False,
        "problem": None,
    }


def _incomplete(result: dict[str, Any], problem: str) -> dict[str, Any]:
    result["complete"] = False
    result["problem"] = problem
    return result


def _read(path: str | Path, result: dict[str, Any]) -> Any:
    try:
        with Path(path).open(encoding="utf-8") as report:
            return json.load(report)
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        _incomplete(result, f"Could not read JSON report: {error}")
        return None


def _counter(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ValueError(f"Invalid {name} counter")
    return value


def _string(value: Any, name: str, *, optional: bool = False) -> str | None:
    if optional and value is None:
        return None
    if not isinstance(value, str):
        raise ValueError(f"Invalid {name}")
    return value


def _vitest(path: str | Path, result: dict[str, Any]) -> dict[str, Any]:
    report = _read(path, result)
    if report is None:
        return result
    try:
        if not isinstance(report, dict):
            raise ValueError("Vitest report must be an object")

        names = {
            "total": "numTotalTests",
            "passed": "numPassedTests",
            "failed": "numFailedTests",
            "pending": "numPendingTests",
            "todo": "numTodoTests",
            "total_suites": "numTotalTestSuites",
            "passed_suites": "numPassedTestSuites",
            "failed_suites": "numFailedTestSuites",
            "pending_suites": "numPendingTestSuites",
        }
        counts = {key: _counter(report.get(field), field) for key, field in names.items()}
        if not isinstance(report.get("success"), bool):
            raise ValueError("Missing Vitest success flag")
        files = report.get("testResults")
        if not isinstance(files, list):
            raise ValueError("Missing Vitest testResults")

        observed = {"passed": 0, "failed": 0, "skipped": 0, "not_run": 0, "todo": 0}
        failed_files = 0
        for file_index, file_result in enumerate(files):
            if not isinstance(file_result, dict):
                raise ValueError("Invalid Vitest file result")
            file_name = _string(file_result.get("name"), "Vitest file name")
            file_status = _string(file_result.get("status"), "Vitest file status")
            if file_status not in {"passed", "failed"}:
                raise ValueError("Unknown Vitest file status")
            assertions = file_result.get("assertionResults")
            if not isinstance(assertions, list):
                raise ValueError("Missing Vitest assertionResults")
            file_failed = 0
            for assertion_index, assertion in enumerate(assertions):
                if not isinstance(assertion, dict):
                    raise ValueError("Invalid Vitest assertion result")
                status = _string(assertion.get("status"), "Vitest assertion status")
                title = _string(assertion.get("fullName"), "Vitest test name")
                ancestors, leaf = assertion.get("ancestorTitles"), assertion.get("title")
                filter_name = (" > ".join([*ancestors, leaf]) if isinstance(ancestors, list)
                               and all(isinstance(item, str) for item in ancestors) and isinstance(leaf, str) else None)
                result["tests"].append({"file": file_name, "test": title, "status": status,
                                        "filter_name": filter_name})
                location = assertion.get("location")
                line = None
                if location is not None:
                    if not isinstance(location, dict):
                        raise ValueError("Invalid Vitest test location")
                    line = _counter(location.get("line"), "Vitest test line")
                failure_messages = assertion.get("failureMessages")
                if failure_messages is None:
                    failure_messages = []
                if not isinstance(failure_messages, list) or any(not isinstance(item, str) for item in failure_messages):
                    raise ValueError("Invalid Vitest failureMessages")
                if status == "passed":
                    observed["passed"] += 1
                elif status == "failed":
                    observed["failed"] += 1
                    file_failed += 1
                    result["failures"].append({
                        "category": "test", "test": title, "file": file_name, "line": line,
                        "filter_name": filter_name,
                        "message": failure_messages[0] if failure_messages else "",
                        "stack": "\n".join(failure_messages),
                        "record": f"testResults[{file_index}]/assertionResults[{assertion_index}]",
                    })
                elif status in {"skipped", "todo", "disabled"}:
                    if status == "todo":
                        observed["todo"] += 1
                    else:
                        observed["skipped"] += 1
                elif status == "pending":
                    observed["not_run"] += 1
                else:
                    raise ValueError("Unknown Vitest assertion status")

            if file_status == "failed":
                failed_files += 1
                if file_failed == 0:
                    message = _string(file_result.get("message", ""), "Vitest file message")
                    result["failures"].append({
                        "category": "runner", "test": None, "file": file_name, "line": None,
                        "message": message or "Vitest file failed without a failed assertion",
                        "stack": message or "", "record": f"testResults[{file_index}]",
                    })

        if counts["total_suites"] < len(files):
            raise ValueError("Vitest total suite counter is below file record count")
        if failed_files > counts["failed_suites"]:
            raise ValueError("Vitest failed suite counter is below failed file count")
        if counts["passed_suites"] + counts["failed_suites"] + counts["pending_suites"] != counts["total_suites"]:
            raise ValueError("Vitest suite counters disagree")
        if counts["pending_suites"]:
            raise ValueError("Vitest has pending suites without file-level outcome records")
        if observed["passed"] != counts["passed"] or observed["failed"] != counts["failed"]:
            raise ValueError("Vitest assertion records and outcome counters disagree")
        if observed["skipped"] + observed["not_run"] != counts["pending"]:
            raise ValueError("Vitest pending assertion records and counter disagree")
        if observed["todo"] != counts["todo"]:
            raise ValueError("Vitest todo assertion records and counter disagree")
        if observed["passed"] + observed["failed"] + observed["skipped"] + observed["not_run"] + observed["todo"] != counts["total"]:
            raise ValueError("Vitest assertion records and total counter disagree")
        if report["success"] != (counts["failed_suites"] == 0 and counts["failed"] == 0 and bool(files)):
            raise ValueError("Vitest success flag disagrees with report records")

        result["totals"].update({
            "total": counts["total"], "passed": counts["passed"], "failed": counts["failed"],
            "skipped": observed["skipped"] + observed["todo"], "not_run": observed["not_run"],
            "errors": len([failure for failure in result["failures"] if failure["category"] == "runner"]),
        })
        if counts["total"] == 0 or counts["passed"] + counts["failed"] == 0:
            return _incomplete(result, "Vitest report contains no executed tests")
        if observed["not_run"]:
            return _incomplete(result, "Vitest report contains tests that did not finish")
        result["complete"] = True
        return result
    except (TypeError, ValueError) as error:
        return _incomplete(result, str(error))


def parse_vitest(path: str | Path) -> dict[str, Any]:
    """Parse and validate Vitest's native JSON reporter output."""
    return _vitest(path, _empty())


def _playwright(path: str | Path, result: dict[str, Any]) -> dict[str, Any]:
    report = _read(path, result)
    if report is None:
        return result
    try:
        if not isinstance(report, dict):
            raise ValueError("Playwright report must be an object")
        stats = report.get("stats")
        if not isinstance(stats, dict):
            raise ValueError("Missing Playwright stats")
        stats_count = {key: _counter(stats.get(key), f"Playwright {key}")
                       for key in ("expected", "skipped", "unexpected", "flaky")}
        suites = report.get("suites")
        errors = report.get("errors")
        if not isinstance(suites, list) or not isinstance(errors, list):
            raise ValueError("Missing Playwright suites or errors")

        observed = {"passed": 0, "failed": 0, "skipped": 0, "not_run": 0}
        test_count = 0

        def visit_suite(suite: Any, record_path: str, file_name: str | None = None,
                        titles: tuple[str, ...] = ()) -> None:
            nonlocal test_count
            if not isinstance(suite, dict):
                raise ValueError("Invalid Playwright suite")
            if "file" in suite:
                file_name = _string(suite["file"], "Playwright suite file")
            if record_path.count("/suites["):
                titles = (*titles, _string(suite.get("title"), "Playwright suite title"))
            specs = suite.get("specs", [])
            children = suite.get("suites", [])
            if not isinstance(specs, list) or not isinstance(children, list):
                raise ValueError("Invalid Playwright suite entries")
            for spec_index, spec in enumerate(specs):
                if not isinstance(spec, dict):
                    raise ValueError("Invalid Playwright spec")
                test_name = _string(spec.get("title"), "Playwright test title")
                line = _counter(spec.get("line"), "Playwright test line") if "line" in spec else None
                tests = spec.get("tests")
                if not isinstance(tests, list) or not tests:
                    raise ValueError("Missing Playwright test executions")
                for project_index, test in enumerate(tests):
                    if not isinstance(test, dict):
                        raise ValueError("Invalid Playwright test execution")
                    project = _string(test.get("projectName", test.get("projectId")), "Playwright project", optional=True)
                    status = _string(test.get("status"), "Playwright test outcome")
                    identity = {"file": file_name, "test": test_name, "project": project,
                                "title_path": [*titles, test_name]}
                    result["tests"].append(dict(identity, status=status))
                    attempts = test.get("results")
                    if not isinstance(attempts, list):
                        raise ValueError("Missing Playwright attempt results")
                    expected_status = _string(test.get("expectedStatus"), "Playwright expectedStatus")
                    if expected_status not in {"passed", "failed", "skipped"}:
                        raise ValueError("Unknown Playwright expectedStatus")
                    attempt_statuses = []
                    for attempt in attempts:
                        if not isinstance(attempt, dict):
                            raise ValueError("Invalid Playwright attempt")
                        attempt_status = _string(attempt.get("status"), "Playwright attempt status")
                        if attempt_status not in {"passed", "failed", "timedOut", "skipped", "interrupted"}:
                            raise ValueError("Unknown Playwright attempt status")
                        attempt_statuses.append(attempt_status)
                    test_count += 1
                    if status == "expected":
                        if expected_status == "skipped" or expected_status not in attempt_statuses:
                            raise ValueError("Expected Playwright outcome has no matching attempt")
                        if any(item not in {expected_status, "skipped", "interrupted"} for item in attempt_statuses):
                            raise ValueError("Expected Playwright outcome contains an unexpected attempt")
                        observed["passed"] += 1
                    elif status == "skipped":
                        if expected_status == "skipped":
                            if any(item != "skipped" for item in attempt_statuses):
                                raise ValueError("Skipped Playwright outcome has a non-skip attempt")
                            observed["skipped"] += 1
                        else:
                            if any(item not in {"skipped", "interrupted"} for item in attempt_statuses):
                                raise ValueError("Not-run Playwright outcome contains an executed attempt")
                            observed["not_run"] += 1
                    elif status in {"unexpected", "flaky"}:
                        expected_attempt = any(item == expected_status for item in attempt_statuses)
                        unexpected_attempt = any(item not in {expected_status, "skipped", "interrupted"}
                                                  for item in attempt_statuses)
                        if status == "unexpected" and (expected_attempt or not unexpected_attempt):
                            raise ValueError("Unexpected Playwright outcome disagrees with attempts")
                        if status == "flaky" and (not expected_attempt or not unexpected_attempt):
                            raise ValueError("Flaky Playwright outcome lacks both attempt outcomes")
                        observed["failed"] += 1
                        messages = []
                        stacks = []
                        for attempt in attempts:
                            if attempt.get("status") not in {expected_status, "skipped", "interrupted"}:
                                error = attempt.get("error")
                                if error is None:
                                    errors_for_attempt = attempt.get("errors", [])
                                    error = errors_for_attempt[0] if errors_for_attempt else {}
                                if not isinstance(error, dict):
                                    raise ValueError("Invalid Playwright attempt error")
                                message = error.get("message", "") or f"Unexpected attempt status: {attempt['status']}"
                                stack = error.get("stack", "")
                                if not isinstance(message, str) or not isinstance(stack, str):
                                    raise ValueError("Invalid Playwright error text")
                                messages.append(message)
                                stacks.append(stack)
                        label = f"{status} Playwright outcome"
                        result["failures"].append({
                            "category": "test", "test": test_name, "file": file_name, "line": line,
                            "message": "; ".join(item for item in messages if item) or label,
                            "stack": "\n".join(item for item in stacks if item),
                            "record": f"{record_path}/specs[{spec_index}]/tests[{project_index}]",
                            "project": project,
                            "title_path": identity["title_path"],
                        })
                    else:
                        raise ValueError("Unknown Playwright test outcome")
            for child_index, child in enumerate(children):
                visit_suite(child, f"{record_path}/suites[{child_index}]", file_name, titles)

        for suite_index, suite in enumerate(suites):
            visit_suite(suite, f"suites[{suite_index}]")

        for error_index, error in enumerate(errors):
            if not isinstance(error, dict):
                raise ValueError("Invalid Playwright runner error")
            message = error.get("message", "")
            stack = error.get("stack", "")
            if not isinstance(message, str) or not isinstance(stack, str):
                raise ValueError("Invalid Playwright runner error text")
            result["failures"].append({
                "category": "runner", "test": None, "file": None, "line": None,
                "message": message, "stack": stack, "record": f"errors[{error_index}]",
            })

        if test_count != sum(stats_count.values()):
            raise ValueError("Playwright test records and stats counters disagree")
        if observed["passed"] != stats_count["expected"]:
            raise ValueError("Playwright expected test records and counter disagree")
        if observed["failed"] != stats_count["unexpected"] + stats_count["flaky"]:
            raise ValueError("Playwright unexpected/flaky test records and counters disagree")
        if observed["skipped"] + observed["not_run"] != stats_count["skipped"]:
            raise ValueError("Playwright skipped/not-run test records and counter disagree")

        result["totals"].update({
            "total": test_count, "passed": observed["passed"], "failed": observed["failed"],
            "skipped": observed["skipped"], "not_run": observed["not_run"], "errors": len(errors),
        })
        if test_count == 0 or observed["passed"] + observed["failed"] == 0:
            return _incomplete(result, "Playwright report contains no executed tests")
        if observed["not_run"]:
            return _incomplete(result, "Playwright report contains tests that did not run")
        result["complete"] = True
        return result
    except (TypeError, ValueError) as error:
        return _incomplete(result, str(error))


def parse_playwright(path: str | Path) -> dict[str, Any]:
    """Parse and validate Playwright's native JSON reporter v2 output."""
    return _playwright(path, _empty())
