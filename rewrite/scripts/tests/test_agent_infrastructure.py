import importlib.util
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / 'check_agent_infrastructure.py'
spec = importlib.util.spec_from_file_location('agent_infrastructure', MODULE)
infrastructure = importlib.util.module_from_spec(spec)
spec.loader.exec_module(infrastructure)


class AgentInfrastructureTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        (self.root / '.agents/skills/rewrite-example').mkdir(parents=True)
        (self.root / '.codex/agents').mkdir(parents=True)
        (self.root / 'docs').mkdir()
        (self.root / 'AGENTS.md').write_text('Use $rewrite-example.\n')
        self.skill = self.root / '.agents/skills/rewrite-example/SKILL.md'
        self.skill.write_text('---\nname: rewrite-example\ndescription: Example\n---\n')
        self.agent = self.root / '.codex/agents/example.toml'
        self.agent.write_text('name = "example"\ndescription = "Example"\ndeveloper_instructions = "Read instructions"\n')

    def test_valid_configuration_and_external_links(self):
        (self.root / 'AGENTS.md').write_text('[Docs](https://example.invalid) [Local](docs#anchor)\n')
        self.assertEqual(infrastructure.validate(self.root), ([], 1, 1))

    def test_missing_inventory_is_reported(self):
        self.skill.unlink()
        self.assertTrue(any('Missing discoverable' in error for error in infrastructure.validate(self.root)[0]))

    def test_skill_identity_links_and_unknown_routes_are_reported(self):
        self.skill.write_text('---\nname: wrong\ndescription: Example\n---\n[Missing](missing.md)\n$rewrite-unknown\n')
        errors, _, _ = infrastructure.validate(self.root)
        self.assertTrue(any('frontmatter' in error for error in errors))
        self.assertTrue(any('broken local target' in error for error in errors))
        self.assertTrue(any('unknown skill' in error for error in errors))

    def test_agent_identity_and_unexpected_overrides_are_reported(self):
        self.agent.write_text('name = "wrong"\ndescription = "Example"\ndeveloper_instructions = "Read"\nmodel = "override"\n')
        errors, _, _ = infrastructure.validate(self.root)
        self.assertTrue(any('unexpected or missing agent fields' in error for error in errors))
        self.assertTrue(any('agent identity' in error for error in errors))

    def test_malformed_agent_toml_is_reported(self):
        self.agent.write_text('name = [')
        self.assertTrue(infrastructure.validate(self.root)[0])
