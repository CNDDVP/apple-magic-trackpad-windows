"""Build deterministic Windows-suite packages from tracked, verified files."""
from pathlib import Path
import hashlib,json,subprocess,zipfile
ROOT=Path(__file__).resolve().parents[1]
VERSION=(ROOT/'RELEASE_VERSION').read_text(encoding='ascii').strip()
OUT=ROOT/'artifacts'/'release'/VERSION
OUT.mkdir(parents=True,exist_ok=True)
def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest().upper()
def package(path, entries):
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as archive:
        for name,data in sorted(entries.items()):
            info=zipfile.ZipInfo(name.replace('\\','/'),(2026,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=0o100644<<16
            archive.writestr(info,data)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip():raise RuntimeError('ZIP CRC failed: '+str(path))
names=[name for name in subprocess.check_output(['git','-C',str(ROOT),'ls-files','-z']).decode().split('\0') if name]
assembled=ROOT/'QuickLook_Portable_Clean.zip'
if not assembled.exists():
    with assembled.open('wb') as dst:
        for name in ['QuickLook_Portable_Clean.zip.part1','QuickLook_Portable_Clean.zip.part2']:
            with (ROOT/name).open('rb') as src:
                while chunk:=src.read(1024*1024):dst.write(chunk)
for item in json.loads((ROOT/'SHA256SUMS.json').read_text(encoding='utf-8-sig')):
    path=ROOT/item['Path']
    if path.stat().st_size!=item['Bytes'] or digest(path)!=item['SHA256']:raise RuntimeError('Manifest mismatch: '+item['Path'])
proof=json.loads((ROOT/'ControlCenter_Acceptance.json').read_text(encoding='utf-8-sig'))
if digest(ROOT/'MagicControlCenter'/'MagicControlCenter.exe')!=proof['AppSha256']:raise RuntimeError('Unaccepted control-center binary')
for name,value in proof['SourceSHA256'].items():
    if digest(ROOT/name)!=value:raise RuntimeError('Control-center source differs from accepted binary: '+name)
raw=subprocess.check_output(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-Command',". ./Suite_Integration.ps1; ConvertTo-Json -InputObject @(Get-MagicTrackpadSuiteFiles) -Compress"],cwd=ROOT)
runtime=[name.replace('\\','/') for name in json.loads(raw.decode('utf-8-sig'))]
update_names=runtime+['README.md','RELEASE_NOTES_'+VERSION+'.md','TEST_REPORT_'+VERSION+'.md','MAGIC_CONTROL_CENTER.md','ControlCenter_Acceptance.json']+[name for name in names if name.startswith('docs/')]
update={name:(ROOT/name).read_bytes() for name in update_names}
checks=[{'Path':name,'Bytes':len(data),'SHA256':hashlib.sha256(data).hexdigest().upper()} for name,data in sorted(update.items())]
update['SHA256SUMS-update.json']=json.dumps(checks,ensure_ascii=False,indent=2).encode()
package(OUT/('MagicForceClick-'+VERSION+'-update.zip'),update)
def report_bytes(path):
    data=path.read_bytes()
    encoding='utf-16' if data.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig'
    return data.decode(encoding).replace('\r\n','\n').encode('utf-8')
full={name:(ROOT/name).read_bytes() for name in names}
for relative in ['TestResults.txt','IntegrationResults.txt','SuiteUpgradeResults.txt','UpdateRollbackResults.txt']:
    full['SELFTEST/'+relative]=report_bytes(ROOT/'artifacts'/relative)
full['SELFTEST/ControlCenterResults.txt']=report_bytes(ROOT/'artifacts'/'control-center'/'ControlCenterResults.txt')
package(OUT/('Apple-Magic-Trackpad-Windows-'+VERSION+'-full.zip'),full)
history=[
 ('MagicForceClick-baseline-2594280.zip','2594280052ed327c5a328f9b0e3d38f2ec7545ce',['MagicForceClick.cs','MagicForceClick.exe']),
 ('MagicForceClick-v1.1.0-rollback.zip','fc364b2d53f9a177f2e366ab8d639506298cb283',['MagicForceClick.exe','MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs','MagicForceClick.json','Open_MagicTrackpad_Settings.cmd','Update_MagicForceClick.ps1','Restore_MagicForceClick.ps1']),
 ('MagicForceClick-v1.1.1-rollback.zip','851a5aaa8724e6deaa027bc8a5171b7654847257',['MagicForceClick.exe','MagicForceClick.cs','MagicForceClick.Core.cs','MagicForceClick.Settings.cs','MagicForceClick.Preview.cs','MagicForceClick.json','Open_MagicTrackpad_Settings.cmd','Update_MagicForceClick.ps1','Restore_MagicForceClick.ps1'])
]
for filename,commit,paths in history:
    entries={name:subprocess.check_output(['git','-C',str(ROOT),'show',commit+':'+name]) for name in paths}
    entries['BASELINE.txt']=('Baseline '+commit+'\nFor exact personal configuration, GUI cleanup and startup restoration use your complete local upgrade snapshot. Do not overwrite personal JSON with package defaults.\n').encode()
    package(OUT/filename,entries)
(OUT/'SHA256SUMS-release.txt').write_text(''.join(digest(path)+'  '+path.name+'\n' for path in sorted(OUT.glob('*.zip'))),encoding='ascii')
print(json.dumps({'version':VERSION,'assets':[p.name for p in sorted(OUT.iterdir())]},ensure_ascii=False))
