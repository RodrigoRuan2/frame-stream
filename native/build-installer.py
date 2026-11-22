"""Package an already published, self-contained Windows build with local WiX 5."""
from pathlib import Path
import hashlib, xml.etree.ElementTree as E, subprocess, uuid

root=Path(__file__).resolve().parent
payload=root/'publish'
ns='http://wixtoolset.org/schemas/v4/wxs'
ui='http://wixtoolset.org/schemas/v4/wxs/ui'
E.register_namespace('',ns);E.register_namespace('ui',ui)
def tag(name):return '{'+ns+'}'+name
def sub(parent,name,**attrs):return E.SubElement(parent,tag(name),attrs)
def ident(path):return 'f'+hashlib.sha256(str(path).replace('\\','/').encode()).hexdigest()[:24]
wix=E.Element(tag('Wix'))
package=sub(wix,'Package',Name='Frame',Manufacturer='Frame',Version='0.1.0',UpgradeCode='C645113D-692F-4DCA-A2F4-1EC20D17EF43',Scope='perUser',Language='1046',Codepage='1252')
sub(package,'MajorUpgrade',DowngradeErrorMessage='Já existe uma versão mais recente do Frame instalada.')
sub(package,'MediaTemplate',EmbedCab='yes',CompressionLevel='high')
local=sub(package,'StandardDirectory',Id='LocalAppDataFolder')
programs=sub(local,'Directory',Id='LocalPrograms',Name='Programs')
install=sub(programs,'Directory',Id='INSTALLFOLDER',Name='Frame')
dirs={Path('.'):install}
feature=sub(package,'Feature',Id='Main',Title='Frame',Level='1')
cleanup=set()
for path in sorted(payload.rglob('*')):
    if not path.is_file():continue
    rel=path.relative_to(payload)
    parent=rel.parent
    if parent not in dirs:
        for ancestor in reversed([parent,*parent.parents]):
            if ancestor==Path('.') or ancestor in dirs:continue
            dirs[ancestor]=sub(dirs[ancestor.parent],'Directory',Id='d'+ident(ancestor),Name=ancestor.name)
    cid='c'+ident(rel);fid=ident(rel)
    component=sub(dirs[parent],'Component',Id=cid,Guid=str(uuid.uuid5(uuid.UUID('c645113d-692f-4dca-a2f4-1ec20d17ef43'),str(rel).replace('\\','/'))))
    if parent not in cleanup:
        sub(component,'RemoveFolder',Id='r'+ident(parent),On='uninstall');cleanup.add(parent)
    sub(component,'File',Id=fid,Source=str(path))
    sub(component,'RegistryValue',Root='HKCU',Key=r'Software\Frame\Components',Name=cid,Value='0.1.0',Type='string',KeyPath='yes')
    sub(feature,'ComponentRef',Id=cid)
sub(package,'StandardDirectory',Id='ProgramMenuFolder')
shortcut=sub(install,'Component',Id='StartMenuShortcut',Guid='*')
sub(shortcut,'Shortcut',Id='FrameShortcut',Directory='ProgramMenuFolder',Name='Frame',Target='[#'+ident(Path('Frame.exe'))+']',WorkingDirectory='INSTALLFOLDER')
sub(shortcut,'RegistryValue',Root='HKCU',Key=r'Software\Frame',Name='InstallPath',Value='[INSTALLFOLDER]',Type='string',KeyPath='yes')
sub(feature,'ComponentRef',Id='StartMenuShortcut')
E.SubElement(package,'{'+ui+'}WixUI',{'Id':'WixUI_InstallDir','InstallDirectory':'INSTALLFOLDER'})
custom=sub(package,'UI')
sub(custom,'Publish',Dialog='WelcomeDlg',Control='Next',Event='NewDialog',Value='InstallDirDlg',Order='2',Condition='1')
sub(custom,'Publish',Dialog='InstallDirDlg',Control='Back',Event='NewDialog',Value='WelcomeDlg',Order='2',Condition='1')
sub(package,'Property',Id='ARPCOMMENTS',Value='Transmissor nativo de tela/janela NVIDIA. Seus amigos assistem pelo link.')
source=root/'Frame.wxs';E.ElementTree(wix).write(source,encoding='utf-8',xml_declaration=True)
output=root.parent.parent/'outputs'/'Frame-0.1.0-win-x64.msi'
output.parent.mkdir(exist_ok=True)
with (root/'installer-build.log').open('w',encoding='utf-8') as log:
    result=subprocess.run([str(root.parent/'native-tools'/'wix'/'wix.exe'),'build',str(source),'-arch','x64','-ext','WixToolset.UI.wixext','-culture','pt-BR','-o',str(output)],cwd=root,stdout=log,stderr=log)
if result.returncode:
    print((root/'installer-build.log').read_text(encoding='utf-8')[:5000]);raise SystemExit(result.returncode)
print(output)
