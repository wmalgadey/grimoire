import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(ROOT)
OUT  = os.environ.get("MEASURE_OUT", os.path.join(ROOT, "artifacts"))
os.makedirs(OUT, exist_ok=True)
os.chdir(ROOT)

import xml.etree.ElementTree as ET, csv, sys, glob, os, re
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def dur_ms(s):
    if not s: return 0.0
    # format hh:mm:ss.fffffff
    h,m,rest=s.split(':')
    return (int(h)*3600+int(m)*60+float(rest))*1000.0

rows=[]
for f in sorted(glob.glob('backend/tests/*/TestResults/*.trx')):
    asm=f.split('/')[2]
    root=ET.parse(f).getroot()
    # map testId -> (className, methodName)
    defs={}
    for ut in root.iter('{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTest'):
        tid=ut.get('id')
        tm=ut.find('t:TestMethod',NS)
        if tm is not None:
            defs[tid]=(tm.get('className'),tm.get('name'))
    for r in root.iter('{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult'):
        tid=r.get('testId')
        cls,meth=defs.get(tid,('?','?'))
        rows.append({
            'assembly':asm,
            'class':cls,
            'test':r.get('testName'),
            'method':meth,
            'ms':round(dur_ms(r.get('duration')),3),
            'outcome':r.get('outcome'),
        })
with open(os.path.join(OUT, "raw-tests.csv"),'w',newline='') as fh:
    w=csv.DictWriter(fh,fieldnames=['assembly','class','test','method','ms','outcome'])
    w.writeheader(); w.writerows(rows)
print('rows:',len(rows))
