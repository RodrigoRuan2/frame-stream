# Frame

Transmissão de tela ou janela de aplicativo pelo Chrome, com salas acessíveis por link. Aplicação estática para GitHub Pages, sem build, instalação ou login dos espectadores.

## Usar

1. Quem transmite abre o site e escolhe **Criar sala e compartilhar**.
2. No seletor do Chrome, escolhe a tela, janela ou aba e, quando disponível, o áudio.
3. Copia o link da sala e envia aos amigos.
4. Os amigos abrem o link. A conexão começa automaticamente; o áudio pode exigir um clique em **Ativar áudio**.
5. **Encerrar** ou fechar a página libera a captura e invalida a sala. Um novo início gera outro link.

**Criar sala de teste** compartilha uma animação sintética sem capturar sua tela. É útil para verificar a conexão; não representa o custo de capturar um jogo.

## Arquitetura e limites

- HTML/CSS/JavaScript, sem framework. PeerJS 1.5.5 vendorizado (~87 KB), licença em `vendor/PEERJS-LICENSE`.
- GitHub Pages entrega os arquivos. PeerServer Cloud faz a sinalização automática (descrições e informações de conexão). STUN do Google ajuda a descobrir os endereços.
- Vídeo e áudio passam diretamente entre navegadores por WebRTC. Não há gravação, analytics, upload de arquivos ou armazenamento de conteúdo no servidor.
- Até quatro espectadores, cada um com sua conexão. Upload e processamento aumentam com o número de espectadores. Não há SFU.
- Sem TURN: NATs/firewalls restritivos podem bloquear a conexão. O serviço público de sinalização pode ficar indisponível. Esta é uma versão de teste, sem garantia de disponibilidade.
- Cada sala usa um identificador aleatório e uma chave de convite no fragmento da URL. A chave é validada antes do envio de vídeo. Qualquer pessoa com o link pode entrar enquanto a sala estiver aberta; não há identidade/autenticação dos espectadores.
- Os limites de resolução, FPS e bitrate são objetivos, não garantias. H.264 é preferido por padrão; o Chrome decide se usa hardware. AV1 pode usar software.
- O seletor do Chrome tem a decisão final sobre a origem e o áudio disponíveis. Jogos em modo exclusivo podem precisar de modo janela sem bordas.
- O painel soma o bitrate de todas as conexões de vídeo; resolução, FPS e RTT são de uma conexão. RTT não representa o atraso ponta a ponta.

## Diagnóstico de FPS baixos

O padrão prioriza movimento e permite ao WebRTC reduzir resolução em vez de preservar 1080p sacrificando os FPS. A opção **Priorizar nitidez** mantém a alternativa anterior. A prévia da captura real começa oculta para reduzir o trabalho local, e pode ser reativada sem interromper a transmissão.

O painel separa FPS da origem, FPS codificados/decodificados, encoder, limitação de CPU/rede, tempo de codificação, tempo de decodificação e buffer no receptor. Valores ausentes não são interpretados como aceleração por GPU. O alerta de OpenH264 identifica software; a página não pode ler ou alterar a configuração de aceleração gráfica do Chrome.

O receptor solicita o menor buffer de reprodução quando `jitterBufferTarget` está disponível. É uma preferência de baixa latência; o navegador mantém os limites necessários à rede, e isso não resolve captura ou codificação lentas.

Se aparecer OpenH264 com limitação de processamento, verifique **Configurações → Sistema → Usar aceleração gráfica quando disponível** no Chrome e reinicie o navegador para aplicar. Compare o encoder e os FPS durante o mesmo jogo, com um espectador. Habilitar a configuração permite tentar aceleração, mas não garante que todo codec use hardware ou que o jogo mantenha 60 FPS.

## Desenvolvimento

Sirva esta pasta por localhost ou HTTPS, por exemplo `python -m http.server 8767 --bind 127.0.0.1`.

O conteúdo é servido da raiz de `main` via GitHub Pages. `.nojekyll` evita processamento Jekyll. Nenhum token ou credencial é necessário no código.
