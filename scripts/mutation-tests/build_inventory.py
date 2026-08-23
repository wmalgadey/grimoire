import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(ROOT)
OUT  = os.environ.get("MEASURE_OUT", os.path.join(ROOT, "artifacts"))
os.makedirs(OUT, exist_ok=True)
os.chdir(ROOT)

import csv, collections

rows=list(csv.DictReader(open(os.path.join(OUT, "tests-classified.csv"))))

REPLAY_SERIAL={'QueryReplayEvalTests','LintReplayEvalTests','IngestReplayEvalTests',
               'RemediationReVerificationEvalTests','LintRemediationProposalRelevanceEvalTests'}
BUILD_CLASSES={'AgentDirBuildContractTests'}
REFLECTION_SHAPE={('HubHelpUsageTests','HubPathSettings_DeclaresExactlyOneCommandOptionPerPathSwitchCatalogEntry'),
                  ('HubHelpUsageTests','HubPathSettings_DescriptionsMatchThePathSwitchCatalogEntryTheyMirror')}

def decide(r):
    asm=r['assembly']; cls=r['class'].split('.')[-1]; ms=float(r['ms'])
    host=r['f_host']=='1'; proc=r['f_process']=='1'; replay=r['f_replay']=='1'
    meth=r['method'].split('.')[-1] if r['method'] else ''

    if asm=='Grimoire.ArchTests':
        return ('DevLoop','keep',
                'IL structural rule with a Red/Green probe; no host, no process. Gate for Principles I/III.')
    if asm=='Grimoire.Domain.UnitTests':
        return ('DevLoop','keep',
                f'Pure domain unit test, {ms:.1f} ms; its target scores 82.86 % mutation - demonstrably carries weight.')
    if asm=='Grimoire.AgentEvals':
        if cls in REPLAY_SERIAL:
            return ('SlowEval','speed up',
                    'Genuine replay eval (agent judgment, Principle II). Belongs in SlowEval, but the shared '
                    '"EvalRunnerReplayScenarios" collection serialises all five classes (0 of 10 class pairs '
                    'overlap in time) - give each class its own collection.')
        if replay or ms>=500:
            return ('Integration','keep',
                    f'Hermetic eval-harness mechanics, but {ms:.0f} ms of workspace/recording setup - '
                    'too expensive for the DevLoop budget.')
        return ('DevLoop','move',
                f'Hermetic, deterministic, {ms:.1f} ms, no host or process. Already [Trait("Tier","Fast")] - '
                'the trait simply is not enforceable while the class lives in AgentEvals.')

    # Grimoire.IntegrationTests
    if (cls,meth) in REFLECTION_SHAPE:
        return ('DevLoop','speed up / rewrite',
                'Reflection-based cardinality/enumeration assertion over the options shape. Principle III '
                'explicitly forbids this for Feature-Scoped Invariants - rewrite as a classicist behavioural '
                'test, do NOT delete (Principle II: "rewritten, not deleted").')
    if cls in BUILD_CLASSES:
        return ('BuildE2E','move to a slow tier',
                f'Invokes a real `dotnet build` of the whole Grimoire.slnx and launches the result. '
                f'{ms/1000:.1f} s; the two tests of this class are 30.5 % of the integration suite. '
                'Verifies the MSBuild target PublishAgentRuntime - build verification, not integration.')
    if proc:
        return ('BuildE2E','move to a slow tier',
                f'Spawns real child processes ({ms:.0f} ms). Genuine need (Principle II), but toolchain-dependent '
                'and therefore not in the blocking dev-loop path.')
    if host:
        if ms>=500:
            return ('Integration','speed up',
                    f'Needs a real host but costs {ms:.0f} ms - review setup cost '
                    '(shared host fixture instead of a host per test).')
        return ('Integration','keep',
                f'Needs a real host ({ms:.0f} ms). Exactly the case Principle II designates integration tests '
                'as the primary means of verification for.')
    if ms>=500:
        return ('Integration','speed up',
                f'No host, no process, yet {ms:.0f} ms - the cost comes from filesystem setup or waiting, '
                'not from genuine integration need.')
    return ('DevLoop','move',
            f'No host, no process, {ms:.1f} ms. Verifies a decision, not wiring - belongs in the fast tier.')

out=[]
for r in rows:
    tier,cat,why=decide(r)
    out.append({
        'assembly':r['assembly'],
        'class':r['class'].split('.')[-1],
        'namespace':r['class'].rsplit('.',1)[0],
        'test':r['test'],
        'runtime_ms':r['ms'],
        'category':cat,
        'target_tier':tier,
        'rationale':why,
        'source_file':r['file'],
    })
out.sort(key=lambda r:(r['target_tier'],r['assembly'],r['class'],r['test']))
with open(os.path.join(OUT, "test-inventory.csv"),'w',newline='') as fh:
    w=csv.DictWriter(fh,fieldnames=list(out[0].keys())); w.writeheader(); w.writerows(out)

print('=== Categories ===')
c=collections.Counter(r['category'] for r in out)
ms=collections.Counter()
for r in out: ms[r['category']]+=float(r['runtime_ms'])
for k,v in c.most_common(): print(f'{k:36s} {v:5d} tests  {ms[k]/1000:8.1f}s')
print()
print('=== Target tiers (sum of CPU time, todays run) ===')
c2=collections.Counter(r['target_tier'] for r in out); ms2=collections.Counter()
for r in out: ms2[r['target_tier']]+=float(r['runtime_ms'])
for k in ['DevLoop','Integration','BuildE2E','SlowEval']:
    print(f'{k:14s} {c2[k]:5d} tests  {ms2[k]/1000:8.1f}s CPU')
print(f'{"TOTAL":14s} {sum(c2.values()):5d} tests  {sum(ms2.values())/1000:8.1f}s CPU')
