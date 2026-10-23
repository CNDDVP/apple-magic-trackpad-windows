"""Package verified tracked files, never personal runtime state or credentials."""
from pathlib import Path
import hashlib,json,subprocess,zipfile

ROOT=Path(__file__).resolve().parents[1]
BASELINE='2594280052ed327c5a328f9b0e3d38f2ec7545ce'
OUT=ROOT/'artifacts'/'release'
OUT.mkdir(parents=True,exist_ok=True)
def sha(data):return hashlib.sha256(data).hexdigest().upper()
def file_sha(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest().upper()
names=[n for n in subprocess.check_output(['git','-C',str(ROOT),'ls-files','-z']).decode().split('\0') if n]
# Installers verify the assembled archive as well as both source parts.
assembled=ROOT/'QuickLook_Portable_Clean.zip'
if not assembled.exists():
    with assembled.open('wb') as dst:
        for name in ['QuickLook_Portable_Clean.zip.part1','QuickLook_Portable_Clean.zip.part2']:
            with (ROOT/name).open('rb') as src:
                while chunk:=src.read(1024*1024):dst.write(chunk)
for item in json.loads((ROOT/'SHA256SUMS.json').read_text(encoding='utf-8-sig')):
    path=ROOT/item['Path']
    if path.stat().st_size!=item['Bytes'] or file_sha(path)!=item['SHA256']:
        raise RuntimeError('Package verification failed: '+item['Path'])
update=['MagicForceClick.exe','MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs','MagicForceClick.json','Open_MagicTrackpad_Settings.cmd','Update_MagicForceClick.ps1','Restore_MagicForceClick.ps1','README.md','RELEASE_NOTES_v1.1.0.md','TEST_REPORT_v1.1.0.md']
with zipfile.ZipFile(OUT/'MagicForceClick-v1.1.0-update.zip','w',zipfile.ZIP_DEFLATED) as z:
    for name in update:z.write(ROOT/name,name)
with zipfile.ZipFile(OUT/'MagicForceClick-baseline-2594280.zip','w',zipfile.ZIP_DEFLATED) as z:
    for name in ['MagicForceClick.cs','MagicForceClick.exe']:
        data=subprocess.check_output(['git','-C',str(ROOT),'show',BASELINE+':'+name])
        if name.endswith('.exe') and sha(data)!='FC0BB943B15A6F0397EAE54CB702151D75D5B36A267ADC158280A829189358A4':raise RuntimeError('Baseline binary mismatch')
        z.writestr(name,data)
    z.writestr('BASELINE.txt','Baseline '+BASELINE+'\nKeep your upgrade snapshot to restore personal settings and startup.\n')
with zipfile.ZipFile(OUT/'Apple-Magic-Trackpad-Windows-v1.1.0-full.zip','w',zipfile.ZIP_DEFLATED) as z:
    for name in names:z.write(ROOT/name,name)
    for filename in ['TestResults.txt','IntegrationResults.txt']:
        z.write(ROOT/'artifacts'/filename,'SELFTEST/'+filename)
(OUT/'SHA256SUMS-release.txt').write_text(''.join(file_sha(p)+'  '+p.name+'\n' for p in sorted(OUT.glob('*.zip'))),encoding='ascii')
print(json.dumps({'assets':[p.name for p in sorted(OUT.iterdir())]},ensure_ascii=False))
