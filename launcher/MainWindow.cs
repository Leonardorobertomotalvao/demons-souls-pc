using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace DemonsSouls.Launcher;

public sealed class MainWindow : Window
{
    readonly TextBox source=new(), destination=new(), elf=new(), runtime=new();
    readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap}, detail=new(){TextWrapping=TextWrapping.Wrap}, inventory=new(){TextWrapping=TextWrapping.Wrap};
    readonly ProgressBar bar=new(){Height=8,Minimum=0,Maximum=100};
    readonly CheckBox keyboard=new(){Content="Habilitar teclado e mouse"}, vsync=new(){Content="Sincronização vertical"}, mute=new(){Content="Silenciar áudio"};
    readonly TextBlock audioState=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};
    readonly ComboBox windowResolution=new(), displayMode=new(), monitorSelection=new();
    readonly ListBox candidates=new(){MaxHeight=150,Visibility=Visibility.Collapsed};
    readonly Button play, import, repair, cancel, saves, diagnostics;
    CancellationTokenSource? cancellation;
    readonly Preferences preferences;
    DateTime lastProgress;
    bool applyingFromGame;
    internal static string? AudioStatePathForTest;
    internal static bool? GameRunningForTest;
    public MainWindow()
    {
        Title="Demon’s Souls PC · Instalação e launcher";
        Width=Math.Min(1080,SystemParameters.WorkArea.Width-32); Height=Math.Min(800,SystemParameters.WorkArea.Height-32);
        MinWidth=Math.Min(820,Width); MinHeight=Math.Min(620,Height);
        Background=(Brush)new BrushConverter().ConvertFromString("#12171C")!; Foreground=Brushes.WhiteSmoke;
        FontFamily=new("Segoe UI"); FontSize=14; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Resources[typeof(Button)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Foreground" Value="#F1EDE4"/><Setter Property="Background" Value="#303943"/>
          <Setter Property="Padding" Value="16,10"/><Setter Property="Margin" Value="0,4,8,4"/>
          <Setter Property="Cursor" Value="Hand"/><Setter Property="MinHeight" Value="42"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="6" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" BorderBrush="#53616D" BorderThickness="1"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
            <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#D8BD7C"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#79CFFF"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.WhiteSmoke));Resources[typeof(TextBlock)]=textStyle;
        var inputStyle=new Style(typeof(TextBox));inputStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(18,25,32))));inputStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.WhiteSmoke));inputStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.SlateGray));inputStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty,Brushes.White));Resources[typeof(TextBox)]=inputStyle;
        var listStyle=new Style(typeof(ListBox));listStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(18,25,32))));listStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.WhiteSmoke));Resources[typeof(ListBox)]=listStyle;
        Resources[typeof(ComboBoxItem)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ComboBoxItem}">
          <Setter Property="Foreground" Value="#F0F2F4"/><Setter Property="Background" Value="#18212B"/><Setter Property="Padding" Value="10,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBoxItem}"><Border x:Name="item" Background="{TemplateBinding Background}" BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter VerticalAlignment="Center"/></Border><ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#304252"/><Setter TargetName="item" Property="BorderBrush" Value="#68839A"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="item" Property="Background" Value="#594A2F"/><Setter TargetName="item" Property="BorderBrush" Value="#D8BD7C"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="item" Property="BorderBrush" Value="#79CFFF"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="item" Property="Background" Value="#20262D"/><Setter Property="Foreground" Value="#929BA4"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        Resources[typeof(ComboBox)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ComboBox}">
          <Setter Property="Foreground" Value="#F0F2F4"/><Setter Property="Background" Value="#121920"/><Setter Property="BorderBrush" Value="#657584"/>
          <Setter Property="Padding" Value="10,7"/><Setter Property="MinHeight" Value="40"/><Setter Property="MaxDropDownHeight" Value="320"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBox}"><Grid>
            <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
              <ToggleButton.Template><ControlTemplate TargetType="{x:Type ToggleButton}"><Border x:Name="button" Background="{Binding Background, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" BorderThickness="1" CornerRadius="5"><Grid Margin="10,0"><TextBlock Text="▼" FontSize="11" Foreground="#D8BD7C" HorizontalAlignment="Right" VerticalAlignment="Center"/><ContentPresenter Margin="0,0,24,0" VerticalAlignment="Center" Content="{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}"/></Grid></Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="button" Property="BorderBrush" Value="#D8BD7C"/><Setter TargetName="button" Property="Background" Value="#1A2530"/></Trigger>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="button" Property="BorderBrush" Value="#D8BD7C"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="button" Property="Background" Value="#20262D"/><Setter TargetName="button" Property="BorderBrush" Value="#424C55"/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <Border x:Name="focusBorder" BorderBrush="Transparent" BorderThickness="2" CornerRadius="6" IsHitTestVisible="False"/>
            <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
              <Border Background="#18212B" BorderBrush="#657584" BorderThickness="1" CornerRadius="5" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
              </Border>
            </Popup>
          </Grid><ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="focusBorder" Property="BorderBrush" Value="#79CFFF"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#929BA4"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        Resources[typeof(TabItem)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem"><Setter Property="Foreground" Value="#EEE8DD"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="tab" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#222C35" Padding="18,12" Margin="0,0,4,0" CornerRadius="5,5,0,0" BorderThickness="0,0,0,2" BorderBrush="Transparent"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="tab" Property="BorderBrush" Value="#D8BD7C"/><Setter TargetName="tab" Property="Background" Value="#303C47"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="tab" Property="BorderBrush" Value="#79CFFF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """);
        try { preferences=Preferences.Read(); } catch { preferences=new(); }
        destination.Text=preferences.Installation;
        string repo=FindProject(); elf.Text=Path.Combine(repo,"work/game/EBOOT.ELF"); runtime.Text=string.IsNullOrWhiteSpace(preferences.RuntimeExecutable)?Path.Combine(repo,"port/build/des_boot.exe"):preferences.RuntimeExecutable;
        keyboard.IsChecked=preferences.KeyboardMouse; vsync.IsChecked=preferences.Vsync; mute.IsChecked=preferences.Mute;
        windowResolution.ItemsSource=new[]{new{Value="1280x720",Label="1280 × 720"},new{Value="1600x900",Label="1600 × 900"},new{Value="1920x1080",Label="1920 × 1080"}};
        windowResolution.DisplayMemberPath="Label";windowResolution.SelectedValuePath="Value";windowResolution.SelectedValue=preferences.WindowResolution;
        displayMode.ItemsSource=new[]{new{Value="windowed",Label="Janela"},new{Value="borderless",Label="Janela sem bordas"},new{Value="fullscreen",Label="Tela cheia sem bordas"}};
        displayMode.DisplayMemberPath="Label";displayMode.SelectedValuePath="Value";displayMode.SelectedValue=preferences.DisplayMode;
        var monitors=DisplayMonitorService.Enumerate();
        monitorSelection.ItemsSource=monitors;monitorSelection.DisplayMemberPath="Label";monitorSelection.SelectedValuePath="Device";
        monitorSelection.SelectedValue=monitors.Any(m=>m.Device==preferences.MonitorDevice)?preferences.MonitorDevice:monitors.FirstOrDefault()?.Device;
        var page=new Grid{Margin=new(24)};
        for(int i=0;i<9;i++)page.RowDefinitions.Add(new RowDefinition{Height=i==4?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        void AddPage(UIElement element){Grid.SetRow(element,page.Children.Count);page.Children.Add(element);}
        AddPage(new TextBlock{Text="DEMON’S SOULS  /  PC",FontSize=14,Foreground=Brushes.Tan,FontWeight=FontWeights.SemiBold});
        AddPage(new TextBlock{Text="Seu jogo. Sua instalação local.",FontSize=30,FontWeight=FontWeights.SemiBold,Margin=new(0,6,0,4)});
        state.Text="Primeiro acesso: localize o jogo e prepare uma instalação independente."; AddPage(state);
        var top=new WrapPanel{Margin=new(0,15,0,15)};
        play=Button("JOGAR",Play); play.FontSize=22; play.MinWidth=235; play.Background=new SolidColorBrush(Color.FromRgb(119,92,49)); top.Children.Add(play);
        saves=Button("Pasta de saves",()=>RuntimeService.OpenFolder(Path.Combine(destination.Text,"userdata/gamedata/dev_hdd0/home/00000001/savedata")));top.Children.Add(saves);
        diagnostics=Button("Diagnóstico",()=>RuntimeService.OpenFolder(Path.Combine(destination.Text,"userdata/logs")));top.Children.Add(diagnostics); AddPage(top);
        var tabs=new TabControl{Background=new SolidColorBrush(Color.FromRgb(30,38,46)),Foreground=Brushes.WhiteSmoke,Padding=new(18)};
        var setup=new StackPanel();
        setup.Children.Add(new TextBlock{Text="1 · Localize a origem legítima",FontSize=20,Margin=new(0,0,0,12)});
        Field(setup,"Pasta PS3_GAME ou raiz da mídia",source);
        var originButtons=new WrapPanel(); originButtons.Children.Add(Button("LOCALIZAR JOGO",Locate)); originButtons.Children.Add(Button("SELECIONAR ISO",SelectIso)); originButtons.Children.Add(Button("Procurar locais comuns",Search)); setup.Children.Add(originButtons);
        setup.Children.Add(candidates); candidates.SelectionChanged+=(_,_)=>{if(candidates.SelectedItem is Candidate c){source.Text=c.Path; detail.Text=c.Status;}};
        setup.Children.Add(new TextBlock{Text="2 · Prepare a instalação local",FontSize=20,Margin=new(0,18,0,8)});
        Field(setup,"Destino separado do Git e dos backups",destination);
        setup.Children.Add(Button("Escolher destino",()=>ChooseFolder(destination)));
        var components=new StackPanel();Field(components,"EBOOT.ELF local já preparado (sem descriptografia pelo launcher)",elf);
        components.Children.Add(Button("Escolher ELF",()=>ChooseFile(elf,"ELF preparado|*.elf")));
        Field(components,"Executável nativo da versão em desenvolvimento",runtime);
        components.Children.Add(Button("Escolher executável",()=>ChooseFile(runtime,"Executável nativo|*.exe")));
        setup.Children.Add(new Expander{Header="Componentes locais do port",Foreground=Brushes.WhiteSmoke,Margin=new(0,12,0,12),Content=components});
        var operations=new WrapPanel();
        import=Button("INSTALAR / PREPARAR",Install); repair=Button("REPARAR ARQUIVOS",Install); cancel=Button("Cancelar",()=>cancellation?.Cancel()); cancel.IsEnabled=false;
        operations.Children.Add(import); operations.Children.Add(repair); operations.Children.Add(cancel); setup.Children.Add(operations);
        setup.Children.Add(new TextBlock{Text="A origem é somente leitura. Arquivos completos são reaproveitados ao retomar; um arquivo parcial reinicia. Saves existentes não são importados.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,8,0,8)});
        tabs.Items.Add(new TabItem{Header="Instalação",Content=new ScrollViewer{Content=setup,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}});
        var settings=new Grid{Margin=new(0,0,0,8)};
        settings.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
        settings.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
        var videoSettings=new StackPanel{Margin=new(4,0,18,0)};
        var controlsSettings=new StackPanel{Margin=new(18,0,4,0)};
        Grid.SetColumn(controlsSettings,1);settings.Children.Add(videoSettings);settings.Children.Add(controlsSettings);
        videoSettings.Children.Add(new TextBlock{Text="Vídeo",FontSize=22,Margin=new(0,0,0,12)});
        videoSettings.Children.Add(new Label{Content="Tamanho da janela",Foreground=Brushes.LightGray,Target=windowResolution});windowResolution.Margin=new(0,0,0,8);videoSettings.Children.Add(windowResolution);
        videoSettings.Children.Add(new Label{Content="Modo de exibição",Foreground=Brushes.LightGray,Target=displayMode});displayMode.Margin=new(0,0,0,8);videoSettings.Children.Add(displayMode);
        videoSettings.Children.Add(new Label{Content="Monitor",Foreground=Brushes.LightGray,Target=monitorSelection});monitorSelection.Margin=new(0,0,0,8);videoSettings.Children.Add(monitorSelection);
        videoSettings.Children.Add(new TextBlock{Text="Renderização interna: 1280 × 720. A resolução escolhida define a saída; janela sem bordas mantém esse tamanho no monitor selecionado, e tela cheia sem bordas ocupa o monitor inteiro. Resolução interna superior ainda não está habilitada.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,12)});
        videoSettings.Children.Add(new TextBlock{Text="Limite: 30 FPS. 60 FPS continua indisponível enquanto a física não for validada.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10)});
        controlsSettings.Children.Add(new TextBlock{Text="Controles de teclado e mouse",FontSize=22,Margin=new(0,0,0,8)});
        controlsSettings.Children.Add(new TextBlock{Text="WASD: mover · mouse: câmera · botão esquerdo: ataque · botão direito: defesa · Espaço ou Shift: esquiva · E: interagir · setas: menus · Enter: confirmar · Tab: voltar · F/R: ações · Q/2: gatilhos. A captura do cursor termina ao perder foco ou usar Alt+Tab.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,0,0,10)});
        foreach(var option in new[]{keyboard,vsync,mute}){option.Foreground=Brushes.WhiteSmoke;option.Margin=new(0,8,0,8);controlsSettings.Children.Add(option);}
        mute.Checked+=(_,_)=>{if(!applyingFromGame)ReflectAudioState();};mute.Unchecked+=(_,_)=>{if(!applyingFromGame)ReflectAudioState();};
        controlsSettings.Children.Add(audioState);
        controlsSettings.Children.Add(Button("Salvar configurações",()=>SavePreferences()));
        controlsSettings.Children.Add(Button("Abrir cache gráfico",()=>{if(!Installation.Load(destination.Text).Complete)throw new IOException("Prepare a instalação primeiro.");RuntimeService.OpenFolder(Path.Combine(destination.Text,"userdata/cache/graphics"));}));
        controlsSettings.Children.Add(Button("Medir tamanho do cache",()=>{long bytes=Directory.Exists(Path.Combine(destination.Text,"userdata/cache/graphics"))?Installation.Inventory(Path.Combine(destination.Text,"userdata/cache/graphics")).Sum(e=>e.Length):0;detail.Text=$"Cache gráfico: {bytes/1048576.0:N1} MiB. Exclusão automática e quotas não implementadas.";}));
        tabs.Items.Add(new TabItem{Header="Configurações e cache",Content=new ScrollViewer{Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}}); AddPage(tabs);
        AddPage(bar); detail.Margin=new(0,8,0,0); AddPage(detail); AddPage(inventory);
        AddPage(new TextBlock{Text="V14 · Alpha 0.2 em desenvolvimento · Launcher .NET 8 / WPF\nNão contém arquivos do jogo. ISO original, Alpha 0.1 e backups permanecem preservados.",Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)});
        Content=page;
        Closing+=(_,e)=>{if(cancellation!=null){cancellation.Cancel();e.Cancel=true;detail.Text="Cancelando com segurança. Aguarde a operação terminar antes de fechar.";}};
        destination.TextChanged+=(_,_)=>Refresh();Refresh();
        var audioTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};audioTimer.Tick+=(_,_)=>ReflectAudioState();audioTimer.Start();ReflectAudioState();
    }
    // Estado do áudio: com o jogo em execução (des_boot), cortesia do arquivo audio-state.txt que o
    // próprio jogo reescreve a cada mudança (poll de 1 s, atualização atômica no lado do jogo);
    // sem jogo, reflete a preferência salva, que o launcher aplica via AUDIO_MUTE na inicialização.
    void ReflectAudioState()
    {
        try
        {
            if(GameRunningForTest??(Process.GetProcessesByName("des_boot").Length>0))
            {
                var stateFile=AudioStatePathForTest??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"DemonsSoulsRecomp","DemonsSouls","audio-state.txt");
                if(File.Exists(stateFile))
                {
                    var value=File.ReadAllText(stateFile).Trim();
                    if(value=="0"||value=="1")
                    {
                        applyingFromGame=true;mute.IsChecked=value=="1";applyingFromGame=false;
                        audioState.Text=value=="1"?"Áudio: DESATIVADO · o jogo está em execução; a tecla Insert alterna o áudio dentro do jogo.":"Áudio: ATIVADO · o jogo está em execução; a tecla Insert alterna o áudio dentro do jogo.";
                        return;
                    }
                }
                audioState.Text="Áudio: aguardando o estado do jogo · a tecla Insert alterna o áudio dentro do jogo.";
                return;
            }
            audioState.Text=mute.IsChecked==true?"Áudio: DESATIVADO · preferência aplicada na próxima inicialização.":"Áudio: ATIVADO · preferência aplicada na próxima inicialização; durante o jogo, a tecla Insert alterna o áudio.";
        }
        catch { /* o indicador é best-effort; nunca bloqueia o launcher */ }
    }
    internal CheckBox MuteForTest=>mute;internal TextBlock AudioStateForTest=>audioState;internal void ReflectAudioStateForTest()=>ReflectAudioState();
    static string FindProject(){string? p=AppContext.BaseDirectory;while(p!=null){if(File.Exists(Path.Combine(p,"README.md"))&&Directory.Exists(Path.Combine(p,"port")))return p;p=Directory.GetParent(p)?.FullName;}return Environment.CurrentDirectory;}
    static void Field(Panel parent,string label,TextBox box){var l=new Label{Content=label,Foreground=Brushes.LightGray,Target=box};parent.Children.Add(l);box.MinHeight=34;box.Padding=new(8);box.Margin=new(0,0,0,5);parent.Children.Add(box);}
    Button Button(string title,Action action){var b=new Button{Content=title};System.Windows.Automation.AutomationProperties.SetName(b,title);b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e);}};return b;}
    void Error(Exception e){detail.Text=e.Message;MessageBox.Show(this,e.Message,"Demon’s Souls PC",MessageBoxButton.OK,MessageBoxImage.Warning);}
    void Refresh(){try{var m=Installation.Load(destination.Text);play.IsEnabled=m.Complete&&!m.Synthetic&&cancellation==null;saves.IsEnabled=diagnostics.IsEnabled=m.Complete&&!m.Synthetic;state.Text=m.Complete?"BLUS30443 · 01.00 · Instalação local preparada. Integridade será conferida antes de jogar.":"Importação pendente. Localize a mesma origem e retome.";}catch{play.IsEnabled=saves.IsEnabled=diagnostics.IsEnabled=false;}}
    void ChooseFolder(TextBox target){var d=new OpenFolderDialog();if(d.ShowDialog(this)==true){target.Text=d.FolderName;Refresh();}}
    void ChooseFile(TextBox target,string filter){var d=new OpenFileDialog{Filter=filter};if(d.ShowDialog(this)==true)target.Text=d.FileName;}
    void Locate(){var d=new OpenFolderDialog{Title="Selecione PS3_GAME ou uma instalação já preparada"};if(d.ShowDialog(this)!=true)return;if(File.Exists(Path.Combine(d.FolderName,"installation.json"))){Installation.Load(d.FolderName);destination.Text=d.FolderName;SavePreferences();Refresh();}else{source.Text=Installation.GameRoot(d.FolderName);detail.Text="Origem BLUS30443 01.00 identificada. Confirme o destino e clique em Instalar.";}}
    async void SelectIso(){try{var d=new OpenFileDialog{Filter="Imagem ISO local|*.iso"};if(d.ShowDialog(this)!=true)return;detail.Text="Consultando a imagem pelo Windows…";source.Text=d.FileName;string? folder=await Discovery.MountedIso(d.FileName);if(folder!=null){source.Text=Installation.GameRoot(folder);detail.Text="ISO já montada e compatível. A origem não será alterada.";}else detail.Text="A ISO não está montada ou não expõe um sistema de arquivos legível. Esta versão não extrai UDF/ISO diretamente. Monte-a pelo Explorador do Windows e use Localizar jogo. Se for criptografada/não suportada, use uma instalação extraída legitimamente; o launcher não remove proteções.";}catch(Exception e){Error(e);}}
    void Search(){candidates.Items.Clear();foreach(var c in Discovery.Common())candidates.Items.Add(c);candidates.Visibility=Visibility.Visible;detail.Text="Busca rasa: raiz das unidades prontas, Downloads e Área de Trabalho. Nenhuma varredura recursiva. Selecione e confirme uma origem.";}
    async void Install()
    {
        if(cancellation!=null)return;
        if(MessageBox.Show(this,$"Copiar/verificar os recursos de:\n{source.Text}\n\nPara:\n{destination.Text}\n\nA origem e os saves existentes serão preservados. Continuar?","Preparar instalação local",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        string s=source.Text,d=destination.Text,e=elf.Text,r=runtime.Text;
        await Work(async (p,ct)=>{await Installation.Import(s,d,e,r,p,ct);});
        Refresh();if(play.IsEnabled){try{SavePreferences();}catch(Exception ex){Error(ex);}}
    }
    async void Play(){if(cancellation!=null)return;string root=destination.Text;LaunchVerification? verification=null;bool ready=await Work(async(p,ct)=>verification=await Installation.VerifyForLaunch(root,p,ct));if(!ready||verification is null)return;try{SavePreferences();detail.Text=verification.UsedMetadataFastPath?$"Instalação validada por metadados em {verification.ElapsedMilliseconds} ms. Iniciando…":$"Integridade SHA-256 validada em {verification.ElapsedMilliseconds} ms. Iniciando…";RuntimeService.Start(root,preferences,verification,runtimeExecutable:string.IsNullOrWhiteSpace(runtime.Text)?null:runtime.Text);Close();}catch(Exception e){Error(e);}}
    void SavePreferences(string? path=null){preferences.Installation=Installation.Full(destination.Text);preferences.RuntimeExecutable=string.IsNullOrWhiteSpace(runtime.Text)?"":Installation.Full(runtime.Text);preferences.MonitorDevice=monitorSelection.SelectedValue as string??"";preferences.KeyboardMouse=keyboard.IsChecked==true;preferences.Vsync=vsync.IsChecked==true;preferences.Mute=mute.IsChecked==true;preferences.WindowResolution=windowResolution.SelectedValue as string??"1280x720";preferences.DisplayMode=displayMode.SelectedValue as string??"windowed";preferences.Write(path??Preferences.FilePath);detail.Text="Configurações salvas; serão aplicadas na próxima inicialização.";}
    internal void SelectVideoOptionsForTest(string resolution,string mode,string settingsPath)
    {
        windowResolution.SelectedValue=resolution;displayMode.SelectedValue=mode;
        if(!Equals(windowResolution.SelectedValue,resolution)||!Equals(displayMode.SelectedValue,mode)||monitorSelection.SelectedValue is not string)
            throw new InvalidOperationException("As opções de vídeo não puderam ser selecionadas no launcher.");
        SavePreferences(settingsPath);
    }
    async Task<bool> Work(Func<IProgress<Transfer>,CancellationToken,Task> action)
    {
        cancellation=new();var token=cancellation.Token;play.IsEnabled=import.IsEnabled=repair.IsEnabled=false;cancel.IsEnabled=true;
        var progress=new Progress<Transfer>(p=>{if((DateTime.UtcNow-lastProgress).TotalMilliseconds<100&&p.Done!=p.Total)return;lastProgress=DateTime.UtcNow;bar.Value=p.Total>0?100.0*p.Done/p.Total:0;detail.Text=$"{p.Phase} · {p.File}";inventory.Text=$"{p.Done:N0} / {p.Total:N0} bytes · {p.Files:N0}/{p.Count:N0} arquivos · {p.Copied:N0} bytes transferidos nesta execução";});
        try{await Task.Run(()=>action(progress,token));detail.Text="Operação concluída e verificada.";return true;}catch(OperationCanceledException){detail.Text="Cancelado com segurança. Retome na mesma origem e destino.";return false;}catch(Exception e){Error(e);return false;}finally{cancellation.Dispose();cancellation=null;import.IsEnabled=repair.IsEnabled=true;cancel.IsEnabled=false;Refresh();}
    }
}

public record Candidate(string Path,long Size,string Status){public override string ToString()=>$"{System.IO.Path.GetFileName(Path)} · {Size/1048576.0:N1} MiB · {Path} · {Status}";}
public static class Discovery
{
    public static IEnumerable<Candidate> Common()
    {
        var roots=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)};
        roots.AddRange(DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>d.RootDirectory.FullName));
        foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!Directory.Exists(root))continue;
            if(Directory.Exists(Path.Combine(root,"PS3_GAME")))yield return new(Path.Combine(root,"PS3_GAME"),0,"Pasta candidata; versão será validada ao confirmar");
            string[] images;try{images=Directory.EnumerateFiles(root,"*.iso",SearchOption.TopDirectoryOnly).Take(100).ToArray();}catch{continue;}
            foreach(string image in images)yield return new(image,new FileInfo(image).Length,"ISO candidata; versão não identificada sem acesso ao volume");
        }
    }
    public static async Task<string?> MountedIso(string path)
    {
        if(!File.Exists(path))throw new IOException("ISO ausente.");
        var p=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        p.Environment["DS_SELECTED_ISO"]=Installation.Full(path);
        p.ArgumentList.Add("-NoProfile");p.ArgumentList.Add("-NonInteractive");p.ArgumentList.Add("-Command");
        p.ArgumentList.Add("$ErrorActionPreference='Stop'; Get-DiskImage -ImagePath $env:DS_SELECTED_ISO | Get-Volume | Where-Object DriveLetter | ForEach-Object { $_.DriveLetter + ':\\' }");
        using var process=Process.Start(p)!;Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill();throw new IOException("O Windows não respondeu ao consultar a ISO. Localize a pasta montada manualmente.");}
        string output=await stdout;_ = await stderr;
        return output.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(r=>Directory.Exists(Path.Combine(r,"PS3_GAME")));
    }
}
