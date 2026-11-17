"""Drop the stubs in src/Sim/Ai/AiFuncStubs.cs whose functions now exist in another source file (as the AI port
progresses). usage: python -I prune_ai_stubs.py"""
import glob
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(os.path.dirname(HERE), 'src')
stubs_path = os.path.join(SRC, 'Sim', 'Ai', 'AiFuncStubs.cs')
defined = set()
for f in glob.glob(os.path.join(SRC, '**', '*.cs'), recursive=True):
    if os.path.samefile(f, stubs_path):
        continue
    defined |= set(re.findall(r'^\s*(?:public |private |static )*void (\w+)\(Sprite e\)', open(f).read(), re.M))
lines = open(stubs_path).read().split('\n')
kept = [l for l in lines if not (m := re.match(r'\s*void (\w+)\(Sprite e\) \{ \}', l)) or m.group(1) not in defined]
open(stubs_path, 'w').write('\n'.join(kept))
left = sum(1 for l in kept if re.match(r'\s*void \w+\(Sprite e\) \{ \}', l))
print('stubs left:', left)
