import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(ROOT)
OUT  = os.environ.get("MEASURE_OUT", os.path.join(ROOT, "artifacts"))
os.makedirs(OUT, exist_ok=True)
os.chdir(ROOT)

import collections, csv
commits=[];cur=None
LOG = os.path.join(OUT, "git-log.txt")
if not os.path.exists(LOG):
    import subprocess
    with open(LOG, "w") as fh:
        subprocess.run(["git", "log", "--name-only",
                        "--pretty=format:C %H %ad", "--date=short", "--", "backend/"],
                       stdout=fh, check=True)

for line in open(LOG):
    line=line.rstrip()
    if line.startswith('C '):
        _,sha,date=line.split(' '); cur={'sha':sha,'date':date,'files':[]}; commits.append(cur)
    elif line.strip() and cur: cur['files'].append(line.strip())

is_test=lambda p: p.startswith('backend/tests/') and p.endswith('.cs')
is_prod=lambda p: p.startswith('backend/src/') and p.endswith('.cs')

import sys
LIMIT=int(sys.argv[1]) if len(sys.argv)>1 else 15
small=[c for c in commits if len(c['files'])<=LIMIT]
test_commits=collections.Counter(); test_with_prod=collections.Counter()
test_only=collections.Counter(); pair=collections.Counter()
for c in small:
    tests=[f for f in c['files'] if is_test(f)]
    prods=[f for f in c['files'] if is_prod(f)]
    for t in tests:
        test_commits[t]+=1
        if prods: test_with_prod[t]+=1
        else: test_only[t]+=1
        for p in prods: pair[(t,p)]+=1

rows=[]
for t,n in test_commits.items():
    partners=sorted([(p,k) for (tt,p),k in pair.items() if tt==t], key=lambda x:-x[1])
    top,topn=partners[0] if partners else ('',0)
    total=sum(k for _,k in partners)
    rows.append(dict(test_file=t, commits=n, commits_with_prod=test_with_prod[t],
        test_only_commits=test_only[t], top_partner=top, top_partner_count=topn,
        distinct_prod_partners=len(partners),
        concentration=round(topn/total,3) if total else 0.0))
rows.sort(key=lambda r:(-r['concentration'],-r['top_partner_count']))
with open(os.path.join(OUT, "cochange-small-commits.csv"),'w',newline='') as fh:
    w=csv.DictWriter(fh,fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)

print(f"commits <= {LIMIT} backend files: {len(small)}/{len(commits)}")
print()
print("=== Highest concentration, min 3 co-changes (structural-coupling candidates) ===")
print(f"{'n':>3} {'conc':>5} {'dist':>4} {'tonly':>5}  test -> top partner")
shown=0
for r in rows:
    if r['top_partner_count']>=3 and shown<30:
        print(f"{r['top_partner_count']:3d} {r['concentration']:5.2f} {r['distinct_prod_partners']:4d} {r['test_only_commits']:5d}  "
              f"{r['test_file'].split('/')[-1]:48s} -> {r['top_partner'].split('/')[-1]}")
        shown+=1
