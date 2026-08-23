import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(ROOT)
OUT  = os.environ.get("MEASURE_OUT", os.path.join(ROOT, "artifacts"))
os.makedirs(OUT, exist_ok=True)
os.chdir(ROOT)

import csv, collections, os, re, glob, json

ROOTS={'Grimoire.IntegrationTests':'backend/tests/Grimoire.IntegrationTests',
       'Grimoire.AgentEvals':'backend/tests/Grimoire.AgentEvals',
       'Grimoire.ArchTests':'backend/tests/Grimoire.ArchTests',
       'Grimoire.Domain.UnitTests':'backend/tests/Grimoire.Domain.UnitTests'}

# index: class simple name -> file path, by scanning for "class X"
cls2file={}
for asm,root in ROOTS.items():
    for f in glob.glob(root+'/**/*.cs',recursive=True):
        if '/bin/' in f or '/obj/' in f: continue
        src=open(f,encoding='utf-8-sig',errors='replace').read()
        for m in re.finditer(r'\b(?:public|internal|sealed|abstract|partial|\s)*class\s+(\w+)', src):
            cls2file.setdefault((asm,m.group(1)), f)

FEATURES={
 'host': r'WebApplicationFactory|TestServer|CreateHostBuilder|IHostBuilder|WebApplication\.|HubApplicationFactory|\.StartAsync\(',
 'process': r'Process\.Start|ProcessStartInfo',
 'http': r'HttpClient|SignalR|HubConnection',
 'sqlite': r'SqliteConnection|Microsoft\.Data\.Sqlite',
 'filesystem': r'Directory\.CreateDirectory|Path\.GetTempPath|File\.WriteAllText|Directory\.CreateTempSubdirectory',
 'otel': r'ActivityListener|MeterListener|InMemoryExporter|ActivitySource',
 'reflection': r'typeof\(|Assembly\.|GetTypes\(\)|NetArchTest',
 'replay': r'Replay|EvalWorkspace|Recording',
}
cache={}
def feats(path):
    if path in cache: return cache[path]
    try: src=open(path,encoding='utf-8-sig',errors='replace').read()
    except: src=''
    r={k:bool(re.search(v,src)) for k,v in FEATURES.items()}
    cache[path]=r; return r

rows=list(csv.DictReader(open(os.path.join(OUT, "raw-tests.csv"))))
out=[]
for r in rows:
    asm=r['assembly']; simple=r['class'].split('.')[-1]
    path=cls2file.get((asm,simple),'')
    f=feats(path) if path else {k:False for k in FEATURES}
    out.append({**r,'file':path,**{('f_'+k):int(v) for k,v in f.items()}})

with open(os.path.join(OUT, "tests-classified.csv"),'w',newline='') as fh:
    w=csv.DictWriter(fh,fieldnames=list(out[0].keys())); w.writeheader(); w.writerows(out)

unmatched=[r for r in out if not r['file']]
print('rows',len(out),'unmatched class->file',len(unmatched))
print('unmatched classes:',sorted({r['class'] for r in unmatched})[:10])
print()
ri=[r for r in out if r['assembly']=='Grimoire.IntegrationTests']
print('=== IntegrationTests: tests & time by capability need ===')
def show(name,pred):
    s=[r for r in ri if pred(r)]
    print(f'{name:34s} {len(s):4d} tests  {sum(float(r["ms"]) for r in s)/1000:8.1f}s')
show('needs host', lambda r:r['f_host'])
show('spawns process', lambda r:r['f_process'])
show('host OR process', lambda r:r['f_host'] or r['f_process'])
show('otel listeners', lambda r:r['f_otel'])
show('NEITHER host nor process', lambda r: not r['f_host'] and not r['f_process'])
show('  ...of those, <10ms', lambda r: not r['f_host'] and not r['f_process'] and float(r['ms'])<10)
show('  ...of those, filesystem only', lambda r: not r['f_host'] and not r['f_process'] and r['f_filesystem'])
show('  ...of those, no fs/otel/http', lambda r: not r['f_host'] and not r['f_process'] and not r['f_filesystem'] and not r['f_otel'] and not r['f_http'])
