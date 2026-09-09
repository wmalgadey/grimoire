"""
Self-test for scripts/token-report.py — the bucketing and aggregation only. The tiktoken
dependency is injected as a stub counter, so this runs without it installed and asserts
arithmetic this script owns rather than tokenizer behaviour it does not.

Run: python3 -m pytest scripts/test_token_report.py -v
"""

import importlib.util
import os
import sys

_SPEC = importlib.util.spec_from_file_location(
    "token_report", os.path.join(os.path.dirname(os.path.abspath(__file__)), "token-report.py"))
token_report = importlib.util.module_from_spec(_SPEC)
sys.modules["token_report"] = token_report
_SPEC.loader.exec_module(token_report)


def _words(text):
    """Stub counter: one token per whitespace-separated word, so totals are hand-checkable."""
    return len(text.split())


def _reader(contents):
    return lambda _repo, path: contents.get(path)


class TestAreaFor:
    def test_backend_src_and_tests_are_separate_areas(self):
        assert token_report.area_for("backend/src/Grimoire.Hub/Program.cs") == "backend-src"
        assert token_report.area_for("backend/tests/Grimoire.ArchTests/X.cs") == "backend-tests"

    def test_backend_root_config_falls_through_to_its_own_area(self):
        assert token_report.area_for("backend/Directory.Build.props") == "backend-buildconfig"

    def test_named_root_documents_are_agent_instructions(self):
        assert token_report.area_for("CLAUDE.md") == "agent-instructions"
        assert token_report.area_for(".specify/memory/constitution.md") == "agent-instructions"

    def test_unmatched_path_is_other(self):
        assert token_report.area_for("compose.yaml") == "other"


class TestProjectFor:
    def test_backend_paths_resolve_to_their_project(self):
        assert token_report.project_for("backend/src/Grimoire.Hub/Cli/X.cs") == "src/Grimoire.Hub"
        assert token_report.project_for("backend/tests/Grimoire.ArchTests/X.cs") == "tests/Grimoire.ArchTests"

    def test_frontend_subtrees_and_config_split(self):
        assert token_report.project_for("frontend/src/lib/api.ts") == "frontend/lib"
        assert token_report.project_for("frontend/vite.config.ts") == "frontend/(config)"

    def test_paths_outside_the_code_trees_have_no_project(self):
        assert token_report.project_for("docs/adr/index.md") is None


class TestCollect:
    def test_totals_are_the_sum_of_the_areas(self):
        contents = {
            "backend/src/a.cs": "one two three\n",
            "backend/tests/b.cs": "four five\n",
            "frontend/src/lib/c.ts": "six\n",
        }
        result = token_report.collect(
            ".", list(contents), _words, read=_reader(contents))

        assert result["areas"]["backend-src"]["tokens"] == 3
        assert result["areas"]["backend-tests"]["tokens"] == 2
        assert result["totals"] == {"tokens": 6, "files": 3, "lines": 3, "bytes": 28}

    def test_bytes_count_utf8_not_characters(self):
        contents = {"docs/a.md": "ä\n"}
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        assert result["totals"]["bytes"] == 3

    def test_always_excluded_paths_are_skipped_not_counted(self):
        contents = {"frontend/bun.lock": "a b c", "frontend/src/lib/a.ts": "d"}
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        assert result["totals"]["tokens"] == 1
        assert result["skipped"] == ["frontend/bun.lock"]

    def test_generated_fixtures_only_drop_out_when_asked(self):
        path = "backend/tests/Grimoire.AgentEvals/Fixtures/recordings/survey/sample-01.json"
        contents = {path: "a b c d", "backend/tests/x.cs": "e"}

        included = token_report.collect(".", list(contents), _words, read=_reader(contents))
        excluded = token_report.collect(
            ".", list(contents), _words, exclude_generated=True, read=_reader(contents))

        assert included["totals"]["tokens"] == 5
        assert excluded["totals"]["tokens"] == 1
        assert path in excluded["skipped"]

    def test_unreadable_file_is_skipped_without_failing_the_run(self):
        contents = {"backend/src/a.cs": None, "backend/src/b.cs": "x"}
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        assert result["totals"]["files"] == 1
        assert result["skipped"] == ["backend/src/a.cs"]

    def test_projects_are_aggregated_alongside_areas(self):
        contents = {
            "backend/src/Grimoire.Hub/a.cs": "one two",
            "backend/src/Grimoire.Hub/b.cs": "three",
            "docs/adr/index.md": "four",
        }
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        assert result["projects"]["src/Grimoire.Hub"] == {
            "tokens": 3, "files": 2, "lines": 0, "bytes": 12}
        assert "docs" not in result["projects"]


class TestRender:
    def test_table_carries_the_approximation_caveat_and_a_total_row(self):
        contents = {"backend/src/a.cs": "one two\n"}
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        rendered = token_report.render(result, "o200k_base")

        assert "not a Claude token count" in rendered
        assert "TOTAL" in rendered
        assert "backend-src" in rendered

    def test_by_project_section_is_opt_in(self):
        contents = {"backend/src/Grimoire.Hub/a.cs": "one"}
        result = token_report.collect(".", list(contents), _words, read=_reader(contents))

        assert "By project:" not in token_report.render(result, "o200k_base")
        assert "src/Grimoire.Hub" in token_report.render(result, "o200k_base", by_project=True)

    def test_empty_repository_renders_without_dividing_by_zero(self):
        result = token_report.collect(".", [], _words, read=_reader({}))

        assert "TOTAL" in token_report.render(result, "o200k_base")
