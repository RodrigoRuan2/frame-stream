from pathlib import Path
import json, shutil, xml.etree.ElementTree as E, urllib.request
root=Path(__file__).resolve().parent
packages=root.parent/'native-tools/packages'
licenses=root/'publish/licenses';licenses.mkdir(exist_ok=True)
assets=json.loads((root/'obj/project.assets.json').read_text(encoding='utf-8-sig'))
text=['FRAME 0.2.0 — COMPONENTES E LICENÇAS','',
'Aplicativo: https://github.com/RodrigoRuan2/frame-stream/tree/main/native',
'FFmpeg LGPL shared: N-129219-g359c825152-20261122',
'Distribuição: https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases',
'Build usado: https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip',
'SHA256 do arquivo baixado: 53d3d48398bc21198b3b18f24dccd2a0dcec6da73164f524e5cd073a2c02e311',
'Código-fonte FFmpeg: https://github.com/FFmpeg/FFmpeg/tree/359c825152',
'Scripts e dependências da build: https://github.com/FFmpeg-Builds/FFmpeg-Builds',
'FFmpeg é executado como processo separado. Seus arquivos em media podem ser substituídos por uma build compatível.',
'As licenças abaixo permanecem aplicáveis. Não há restrição à engenharia reversa necessária para depurar alterações em bibliotecas LGPL.','']
ns={'n':'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
for key,item in assets['libraries'].items():
    if item.get('type')!='package':continue
    path=packages/item['path']
    nuspec=next(path.glob('*.nuspec'),None)
    if not nuspec:continue
    tree=E.parse(nuspec);meta=next(x for x in tree.getroot() if x.tag.endswith('metadata'))
    fields={x.tag.rsplit('}',1)[-1]:x for x in meta}
    value=lambda name: fields[name].text if name in fields else ''
    text.extend([key,'Autores: '+(value('authors') or ''),'Copyright: '+(value('copyright') or ''),'Projeto: '+(value('projectUrl') or '')])
    lic=fields.get('license')
    if lic is not None:
        text.append('Licença: '+str(lic.text))
        if lic.attrib.get('type')=='file' and (path/lic.text).is_file():shutil.copy2(path/lic.text,licenses/(key.replace('/','-')+'-LICENSE.txt'))
        elif lic.attrib.get('type')=='expression':
            expression=lic.text
            if expression in ['MIT','Apache-2.0','BSD-3-Clause','BSD-2-Clause','ISC'] and not (licenses/(expression+'.txt')).exists():
                urllib.request.urlretrieve('https://raw.githubusercontent.com/spdx/license-list-data/main/text/'+expression+'.txt',licenses/(expression+'.txt'))
    else:
        text.append('Licença: '+(value('licenseUrl') or 'Consulte a distribuição original.'))
    for candidate in path.glob('*'):
        if candidate.is_file() and candidate.name.lower().startswith(('license','third-party','notice')):
            shutil.copy2(candidate,licenses/(key.replace('/','-')+'-'+candidate.name))
    text.append('')
shutil.copy2(root.parent/'native-tools/ffmpeg/ffmpeg-master-latest-win64-lgpl-shared/LICENSE.txt',licenses/'FFmpeg-LGPL-3.txt')
for name,url in [('GPL-3.0.txt','https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.GPLv3'),('NAudio-LICENSE.txt','https://raw.githubusercontent.com/naudio/NAudio/master/license.txt'),('dotnet-LICENSE.txt','https://raw.githubusercontent.com/dotnet/runtime/v8.0.34/LICENSE.TXT'),('dotnet-THIRD-PARTY-NOTICES.txt','https://raw.githubusercontent.com/dotnet/runtime/v8.0.34/THIRD-PARTY-NOTICES.TXT')]:
    try:urllib.request.urlretrieve(url,licenses/name)
    except Exception as e:print('Notice download:',name,str(e))
notice='\n'.join(text)
(root/'THIRD-PARTY-NOTICES.txt').write_text(notice,encoding='utf-8')
(root/'publish/THIRD-PARTY-NOTICES.txt').write_text(notice,encoding='utf-8')
print('Notices bundled:',len(list(licenses.iterdir())))
