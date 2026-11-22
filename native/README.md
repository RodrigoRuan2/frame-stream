# Frame para Windows — 0.1.0

Transmissor nativo .NET/WinForms, Windows Graphics Capture e NVIDIA NVENC. O espectador abre `watch.html` no Chrome, sem instalar nada. A versão web original continua disponível.

## Requisitos e uso

- Windows 10 versão 2004 ou mais recente / Windows 11, x64, GPU NVIDIA com NVENC e driver atualizado. Esta versão foi preparada para testar na RTX 4060 Ti.
- Instale o MSI. Ele inclui o runtime .NET e o motor de vídeo; não requer SDK, OBS, plugins ou serviços em segundo plano. A instalação é por usuário, em `%LOCALAPPDATA%\Programs\Frame`.
- Abra Frame no menu Iniciar, escolha uma tela ou janela e clique em **Criar sala e transmitir**. O botão **Copiar link** fica disponível quando a captura e o serviço de conexão estão prontos.
- Não minimize a janela capturada. Jogos podem precisar de janela sem bordas. Não se trata de um hook/injeção no jogo.
- **Incluir áudio do PC** transmite TODOS os sons do dispositivo de reprodução padrão, inclusive outros aplicativos. Não usa o microfone. Se trocar a saída de som, reinicie a transmissão.
- Encerre para liberar captura e invalidar o convite. Fechar o app encerra. Não inicia junto do Windows nem continua escondido na bandeja.
- Desinstale em Aplicativos instalados do Windows.

## Arquitetura

Windows Graphics Capture (`gfxcapture` no FFmpeg) captura tela ou HWND selecionado, escala preservando proporção na GPU e entrega texturas D3D11 ao H.264 NVENC. Não há cópia de vídeo bruto pela aplicação C# nem renderização de prévia. Um encoder atende até quatro conexões WebRTC; cada espectador ainda multiplica o upload e o custo de criptografia.

FFmpeg envia RTP apenas para uma porta UDP efêmera em 127.0.0.1. O Frame reencaminha o payload por SIPSorcery, usando ICE/DTLS/SRTP. A captura de áudio usa WASAPI loopback/NAudio, resampling para 48 kHz estéreo quando necessário e Opus/Concentus. Não há arquivo de gravação.

PeerServer Cloud (`0.peerjs.com`) faz sinalização por WSS; Google STUN auxilia a conexão. Ambos recebem os metadados necessários à conexão. O vídeo segue diretamente entre os dispositivos. A página estática fica no GitHub Pages da conta **RodrigoRuan2**. Convites têm chave aleatória de 192 bits no fragmento; a chave é validada antes de negociar ou enviar vídeo. Quem possui o convite pode assistir. Não há contas de espectadores, identidade verificada ou senha adicional.

## Limites importantes da primeira versão

- Ainda sem TURN/SFU: redes com NAT/firewalls restritivos podem não conectar. Não há garantia de disponibilidade do serviço público de salas. Nenhum serviço pago foi contratado.
- Bitrate configurável, sem controlador automático de congestionamento do encoder nesta versão. Comece em 8–16 Mbps conforme seu upload e a conexão do amigo. H.264 sem B-frames, preset p3, keyframe a cada segundo. Perdas podem causar artefatos até o próximo keyframe.
- FPS são os quadros codificados/decodificados. Windows Graphics Capture pode repetir quadros quando não há movimento. 60 FPS é uma meta, não garantia de 60 imagens distintas ou latência total. Valide jogando com movimento, com receptor em outro PC.
- Compatibilidade inicial NVIDIA. Não usa fallback de software para mascarar falta de suporte.
- Áudio da saída padrão inteira, não isolamento por aplicativo. HDR não é uma modalidade validada.
- Binário de desenvolvimento sem assinatura de editor. Não desative proteções do Windows; assinatura comercial não está incluída.

## Compilar

1. SDK .NET 8 x64 e WiX 5.0.2 local.
2. `dotnet publish Frame.csproj -c Release -r win-x64 --self-contained true -o publish`
3. Em `publish/media`, coloque `ffmpeg.exe` e as DLLs da distribuição LGPL shared indicada em `THIRD-PARTY-NOTICES.txt`. Preserve licenças e possibilidade de substituir esses arquivos.
4. `wix extension add WixToolset.UI.wixext/5.0.2`
5. `python build-installer.py`

`Integration` é um executável de teste com encerramento automático. Por padrão transmite um padrão sintético; `--window HWND --audio` testa captura de uma janela e áudio da saída padrão. Não é instalado no usuário. A chave de convite de teste fica apenas no diretório temporário de saída.

O código do aplicativo está disponível para inspeção e alterações. Dependências mantêm suas próprias licenças, incluídas no pacote.
