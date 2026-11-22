using System.Diagnostics;

namespace Frame;
public sealed class MainForm : Form
{
    readonly ComboBox source=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox quality=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox fps=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly ComboBox bitrate=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly CheckBox audio=new(){Text="Incluir áudio do PC (todos os sons da saída padrão)",Checked=true,AutoSize=true};
    readonly Button refresh=new(){Text="Atualizar lista",AutoSize=true};
    readonly Button start=new(){Text="Criar sala e transmitir",Height=48,Dock=DockStyle.Fill};
    readonly Button end=new(){Text="Encerrar",Enabled=false,AutoSize=true};
    readonly Button copy=new(){Text="Copiar link",Enabled=false,AutoSize=true};
    readonly TextBox invite=new(){ReadOnly=true,Dock=DockStyle.Fill};
    readonly Label status=new(){Text="Pronto para transmitir",AutoSize=true};
    readonly Label metrics=new(){Text="A captura só começa quando você clicar em Transmitir.",AutoSize=true};
    readonly Label info=new(){Text="Escolha a tela ou uma janela. Quem recebe o link assiste no navegador.",AutoSize=true,MaximumSize=new Size(660,0)};
    readonly TextBox details=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
    readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    Room? room;VideoEncoder? video;AudioCapture? sound;
    bool busy, closing;
    long lastFrames,lastBytes;DateTime lastSample;
    public MainForm()
    {
        Text="Frame · Transmitir para amigos";ClientSize=new Size(740,690);MinimumSize=new Size(650,700);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10F);BackColor=Color.FromArgb(17,23,26);ForeColor=Color.FromArgb(235,241,239);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),ColumnCount=1,RowCount=15};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Row(Control c,int height=0) {int n=root.Controls.Count;root.RowStyles.Add(height==0?new RowStyle(SizeType.AutoSize):new RowStyle(SizeType.Absolute,height));root.Controls.Add(c,0,n);c.Margin=new Padding(0,0,0,12);}
        Row(new Label{Text="frame",Font=new Font("Segoe UI",30,FontStyle.Bold),AutoSize=true});
        Row(new Label{Text="Seu jogo. Seus amigos. Um link.",Font=new Font("Segoe UI",15),AutoSize=true});
        Row(info);Row(new Label{Text="O que compartilhar",AutoSize=true});
        var sources=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Height=42};sources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sources.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));sources.Controls.Add(source,0,0);sources.Controls.Add(refresh,1,0);Row(sources,46);
        quality.Items.AddRange(["1080p","720p · mais leve"]);quality.SelectedIndex=0;
        fps.Items.AddRange(["Até 60 FPS","Até 30 FPS"]);fps.SelectedIndex=0;
        bitrate.Items.AddRange(["8 Mbps","12 Mbps","16 Mbps","25 Mbps"]);bitrate.SelectedIndex=2;
        var settings=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Height=70};
        for(int i=0;i<3;i++)settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33F));
        settings.Controls.Add(new Label{Text="Resolução",AutoSize=true},0,0);settings.Controls.Add(new Label{Text="Fluidez",AutoSize=true},1,0);settings.Controls.Add(new Label{Text="Vídeo por amigo",AutoSize=true},2,0);
        settings.Controls.Add(quality,0,1);settings.Controls.Add(fps,1,1);settings.Controls.Add(bitrate,2,1);Row(settings,78);
        Row(audio);Row(start,56);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};buttons.Controls.Add(copy);buttons.Controls.Add(end);Row(buttons);
        Row(invite,42);Row(status);Row(metrics);
        Row(new Label{Text="Windows 10/11 · NVIDIA NVENC · até 4 espectadores\nSem microfone, gravação ou inicialização automática.",AutoSize=true,ForeColor=Color.FromArgb(160,180,175)});
        root.Controls.Add(details,0,root.Controls.Count);root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(root);
        foreach(Control c in Walk(root)) {
            if(c is Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(60,80,75);b.BackColor=Color.FromArgb(35,49,45);b.ForeColor=ForeColor;b.Padding=new Padding(8,4,8,4);}
            if(c is ComboBox or TextBox){c.BackColor=Color.FromArgb(28,36,39);c.ForeColor=ForeColor;}
        }
        start.BackColor=Color.FromArgb(174,235,196);start.ForeColor=Color.FromArgb(16,35,26);
        refresh.Click+=(_,_)=>RefreshSources();start.Click+=async(_,_)=>await StartRoom();end.Click+=(_,_)=>StopRoom();
        copy.Click+=(_,_)=>{if(room!=null){try{Clipboard.SetText(room.Invite);status.Text="Link copiado · envie aos amigos";}catch{invite.SelectAll();invite.Focus();}}};
        timer.Tick+=(_,_)=>UpdateMetrics();
        FormClosing+=(_,e)=>{if(room!=null&&MessageBox.Show("Encerrar a transmissão e fechar o Frame?","Frame",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.No){e.Cancel=true;return;}closing=true;StopRoom();};
        RefreshSources();
    }
    static IEnumerable<Control> Walk(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in Walk(c))yield return child;}}
    void RefreshSources(){source.Items.Clear();source.Items.AddRange(CaptureSource.List().ToArray());if(source.Items.Count>0)source.SelectedIndex=0;}
    void UI(Action action){if(!IsDisposed&&!closing){if(InvokeRequired){try{BeginInvoke(action);}catch{}}else action();}}
    void Log(string text)=>UI(()=>{if(details.TextLength>5000)details.Clear();details.AppendText(text+Environment.NewLine);});
    void ControlsActive(bool active){source.Enabled=quality.Enabled=fps.Enabled=bitrate.Enabled=audio.Enabled=refresh.Enabled=start.Enabled=!active;end.Enabled=active&&!busy;copy.Enabled=active&&!busy&&room!=null;}
    async Task StartRoom()
    {
        if(busy||room!=null||source.SelectedItem is not CaptureSource selected)return;
        busy=true;ControlsActive(true);details.Clear();status.Text="Preparando captura na GPU…";
        int height=quality.SelectedIndex==0?1080:720,frames=fps.SelectedIndex==0?60:30,rate=new[]{8,12,16,25}[bitrate.SelectedIndex];
        try {
            if(audio.Checked&&!selected.Synthetic){sound=new AudioCapture();sound.Failed+=e=>Log("Áudio: "+e);}
            room=new Room(sound!=null);room.Status+=s=>UI(()=>status.Text=s);room.Diagnostic+=Log;
            video=new VideoEncoder();video.Packet+=room.SendVideo;
            var thisVideo=video;video.Failed+=e=>UI(()=>{if(video==thisVideo&&!busy){Log(e);StopRoom();status.Text="Captura interrompida. Selecione a origem e tente novamente.";}});
            if(sound!=null){sound.Packet+=room.SendAudio;sound.Start();}
            await video.Start(selected,height,frames,rate);
            status.Text="Conectando serviço de salas…";await room.Start();
            invite.Text=room.Invite;lastFrames=video.Frames;lastBytes=video.Bytes;lastSample=DateTime.UtcNow;timer.Start();
            Log("NVIDIA NVENC H.264 · "+height+"p · meta "+frames+" FPS");
            Log(selected.Synthetic?"Fonte sintética. Não mede captura do jogo.":"Windows Graphics Capture → GPU → NVENC. Prévia local desativada.");
            Log("Quem tiver o link pode assistir enquanto a sala estiver aberta. Upload cresce por espectador.");
            Log("Conexão direta; redes restritivas podem exigir TURN, ainda não configurado nesta versão.");
        }catch(Exception e){StopRoom();status.Text="Não foi possível iniciar";Log(e.Message);}
        finally{busy=false;ControlsActive(room!=null);}
    }
    void UpdateMetrics()
    {
        if(video==null||room==null)return;var now=DateTime.UtcNow;double dt=(now-lastSample).TotalSeconds;
        var f=Interlocked.Read(ref video.Frames);var b=Interlocked.Read(ref video.Bytes);
        metrics.Text=$"{(f-lastFrames)/dt:F0} FPS codificados · {(b-lastBytes)*8/dt/1e6:F1} Mbps por amigo · {room.Count}/4 conectados";
        lastFrames=f;lastBytes=b;lastSample=now;
    }
    void StopRoom()
    {
        timer.Stop();sound?.Dispose();sound=null;video?.Dispose();video=null;room?.Dispose();room=null;
        invite.Clear();metrics.Text="Captura encerrada. O link anterior não funciona mais.";status.Text="Pronto para transmitir";ControlsActive(false);
    }
}
