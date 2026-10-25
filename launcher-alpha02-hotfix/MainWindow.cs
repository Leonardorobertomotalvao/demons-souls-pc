using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;

namespace DemonsSouls.Launcher;

public sealed class MainWindow : Window
{
    readonly TextBox source=new(), destination=new(), elf=new(), runtime=new(), runtimeProfile=new();
    readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap}, detail=new(){TextWrapping=TextWrapping.Wrap}, inventory=new(){TextWrapping=TextWrapping.Wrap};
    readonly TextBlock operationStage=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(216,189,124))};
    readonly TextBlock playReason=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(224,199,137))};
    readonly TextBlock prepChecklist=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(224,199,137)),Margin=new(0,4,0,2)};
    readonly Dictionary<string,(long Length,DateTime WriteUtc,bool Approved)> componentIdentity=new(StringComparer.OrdinalIgnoreCase);
    string lastIsoDetail="";
    readonly ProgressBar bar=new()
    {
        Height=8,Minimum=0,Maximum=100,
        Background=new SolidColorBrush(Color.FromRgb(35,43,51)),
        Foreground=new SolidColorBrush(Color.FromRgb(216,189,124)),
        BorderBrush=new SolidColorBrush(Color.FromRgb(83,97,109))
    };
    readonly CheckBox keyboard=new(){Content="Habilitar teclado e mouse"}, vsync=new(){Content="Sincronização vertical"}, mute=new(){Content="Silenciar áudio"};
    readonly ComboBox windowResolution=new(), displayMode=new(), monitorSelection=new();
    readonly ComboBox frameRate=new();
    readonly TextBlock frameRateStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(224,199,137))};
    readonly ListBox candidates=new(){MaxHeight=150,Visibility=Visibility.Collapsed};
    readonly TabControl tabs=new(){Background=new SolidColorBrush(Color.FromRgb(30,38,46)),Foreground=Brushes.WhiteSmoke,Padding=new(10)};
    readonly Button play, import, cancel, saves, diagnostics;
    Button? useExisting;
    Grid? headerLayout, settingsLayout;
    StackPanel? identityPanel, playPanel;
    StackPanel? videoSettingsPanel, controlsSettingsPanel, footerLayout;
    TextBlock? identityHeadline;
    bool compactHeader, compactSettings;
    bool runtime60Compatible;
    bool suppressErrorDialogsForTest;
    string runtime60Status="selecione uma instalação experimental preparada na aba Instalação.";
    CancellationTokenSource? runtimeCheck;
    CancellationTokenSource? cancellation;
    readonly Preferences preferences;
    string? lastStatusError;
    static string LauncherLogDirectory => Path.Combine(Preferences.UserDataDirectory,"logs");
    public MainWindow()
    {
        Title="Demon’s Souls PC";
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/DemonsSouls.Launcher;component/DemonsSouls.Launcher.ico"));
        Width=Math.Max(360,Math.Min(1080,SystemParameters.WorkArea.Width-32));
        Height=Math.Max(340,Math.Min(800,SystemParameters.WorkArea.Height-32));
        MinWidth=Math.Min(400,Width); MinHeight=Math.Min(360,Height);
        Background=(Brush)new BrushConverter().ConvertFromString("#12171C")!; Foreground=Brushes.WhiteSmoke;
        FontFamily=new("Segoe UI"); FontSize=14; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Resources[typeof(Button)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Foreground" Value="#F1EDE4"/><Setter Property="Background" Value="#303943"/>
          <Setter Property="Padding" Value="16,10"/><Setter Property="Margin" Value="0,4,8,4"/>
          <Setter Property="Cursor" Value="Hand"/><Setter Property="MinHeight" Value="42"/><Setter Property="HorizontalContentAlignment" Value="Center"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="6" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" BorderBrush="#53616D" BorderThickness="1"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/></Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#D8BD7C"/><Setter TargetName="b" Property="Background" Value="#3B4651"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Background" Value="#594A2F"/><Setter TargetName="b" Property="BorderBrush" Value="#E7CB88"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#79CFFF"/><Setter TargetName="b" Property="BorderThickness" Value="2"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Background" Value="#242B32"/><Setter TargetName="b" Property="BorderBrush" Value="#4C5862"/><Setter Property="Foreground" Value="#B4BDC5"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.WhiteSmoke));Resources[typeof(TextBlock)]=textStyle;
        var inputStyle=new Style(typeof(TextBox));inputStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(18,25,32))));inputStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.WhiteSmoke));inputStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.SlateGray));inputStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty,Brushes.White));Resources[typeof(TextBox)]=inputStyle;
        var listStyle=new Style(typeof(ListBox));listStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(18,25,32))));listStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.WhiteSmoke));Resources[typeof(ListBox)]=listStyle;
        Resources[typeof(ComboBoxItem)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ComboBoxItem}">
          <Setter Property="Foreground" Value="#F0F2F4"/><Setter Property="Background" Value="#18212B"/><Setter Property="Padding" Value="10,7"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBoxItem}"><Border x:Name="item" Background="{TemplateBinding Background}" BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter VerticalAlignment="Center" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" ContentTemplateSelector="{TemplateBinding ContentTemplateSelector}"/></Border><ControlTemplate.Triggers>
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
              <ToggleButton.Template><ControlTemplate TargetType="{x:Type ToggleButton}"><Border x:Name="button" Background="{Binding Background, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" BorderThickness="1" CornerRadius="5"><Grid Margin="10,0"><TextBlock Text="▼" FontSize="11" Foreground="#D8BD7C" HorizontalAlignment="Right" VerticalAlignment="Center"/><ContentPresenter x:Name="SelectionPresenter" Margin="0,0,24,0" VerticalAlignment="Center" Content="{Binding SelectedItem, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" ContentTemplate="{Binding ItemTemplate, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}" ContentTemplateSelector="{Binding ItemTemplateSelector, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}"/></Grid></Border><ControlTemplate.Triggers>
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
        var friendlyItemTemplate=(DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding Label}" Foreground="#F0F2F4" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/></DataTemplate>
        """);
        var frameRateItemTemplate=(DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <TextBlock x:Name="rateLabel" Text="{Binding Label}" Foreground="#F0F2F4" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
          <DataTemplate.Triggers><DataTrigger Binding="{Binding Available}" Value="False"><Setter TargetName="rateLabel" Property="Foreground" Value="#929BA4"/></DataTrigger></DataTemplate.Triggers>
        </DataTemplate>
        """);
        Resources[typeof(TabItem)]=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem"><Setter Property="Foreground" Value="#EEE8DD"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="tab" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#222C35" Padding="18,12" Margin="0,0,4,0" CornerRadius="5,5,0,0" BorderThickness="0,0,0,2" BorderBrush="Transparent"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="tab" Property="BorderBrush" Value="#D8BD7C"/><Setter TargetName="tab" Property="Background" Value="#303C47"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="tab" Property="BorderBrush" Value="#79CFFF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """);
        string? preferencesWarning=null;
        try { preferences=Preferences.Read(); } catch(Exception e) { preferences=new(); preferencesWarning=$"As configurações salvas não puderam ser lidas ({e.GetBaseException().Message}). O arquivo existente foi preservado; revise as opções antes de salvar novamente."; }
        destination.Text=preferences.Installation;
        runtimeProfile.Text=preferences.RuntimeProfile;
        string repo=FindProject();string defaultElf=Path.Combine(preferences.Installation,"runtime/EBOOT.ELF");
        if(!File.Exists(defaultElf))defaultElf=Path.Combine(repo,"work/game/EBOOT.ELF");
        string defaultRuntime=Path.Combine(repo,"port/build/des_boot.exe");
        elf.Text=!string.IsNullOrWhiteSpace(preferences.EbootPath)?preferences.EbootPath:File.Exists(defaultElf)?defaultElf:"";
        runtime.Text=string.IsNullOrWhiteSpace(preferences.RuntimeExecutable)?(File.Exists(defaultRuntime)?defaultRuntime:""):preferences.RuntimeExecutable;
        keyboard.IsChecked=preferences.KeyboardMouse; vsync.IsChecked=preferences.Vsync; mute.IsChecked=preferences.Mute;
        windowResolution.ItemsSource=new[]{new{Value="1280x720",Label="1280 × 720"},new{Value="1600x900",Label="1600 × 900"},new{Value="1920x1080",Label="1920 × 1080"}};
        windowResolution.ItemTemplate=friendlyItemTemplate;windowResolution.SelectedValuePath="Value";windowResolution.SelectedValue=preferences.WindowResolution;
        displayMode.ItemsSource=new[]{new{Value="windowed",Label="Janela"},new{Value="borderless",Label="Janela sem bordas"},new{Value="fullscreen",Label="Tela cheia sem bordas"}};
        displayMode.ItemTemplate=friendlyItemTemplate;displayMode.SelectedValuePath="Value";displayMode.SelectedValue=preferences.DisplayMode;
        var monitors=DisplayMonitorService.Enumerate();
        monitorSelection.ItemsSource=monitors;monitorSelection.ItemTemplate=friendlyItemTemplate;monitorSelection.SelectedValuePath="Device";
        monitorSelection.SelectedValue=monitors.Any(m=>m.Device==preferences.MonitorDevice)?preferences.MonitorDevice:monitors.FirstOrDefault()?.Device;
        frameRate.ItemTemplate=frameRateItemTemplate;frameRate.SelectedValuePath=nameof(FrameRateOption.Value);
        frameRate.ItemContainerStyle=new Style(typeof(ComboBoxItem),(Style)Resources[typeof(ComboBoxItem)]);
        frameRate.ItemContainerStyle.Setters.Add(new Setter(UIElement.IsEnabledProperty,new System.Windows.Data.Binding(nameof(FrameRateOption.Available))));
        RefreshFrameRateOptions();
        frameRate.SelectedValue=preferences.FrameRate;
        var page=new Grid{Margin=new(20)};
        page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        page.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid{Margin=new(0,0,0,14)};
        headerLayout=header;
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        identityPanel=identity;
        identity.Children.Add(new TextBlock{Text="DEMON’S SOULS",FontSize=13,Foreground=Brushes.Tan,FontWeight=FontWeights.Bold});
        identityHeadline=new TextBlock{Text="Uma jornada pronta para começar.",FontSize=26,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,2)};
        identity.Children.Add(identityHeadline);
        state.Text="Localize o jogo para preparar sua instalação."; state.FontSize=13;state.Foreground=Brushes.LightGray;
        identity.Children.Add(state);
        play=Button("JOGAR",Play);play.FontSize=19;play.MinWidth=150;play.MinHeight=52;play.Background=new SolidColorBrush(Color.FromRgb(119,92,49));
        var playPanel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,MaxWidth=220};
        this.playPanel=playPanel;
        playPanel.Children.Add(play);playReason.FontSize=12;playReason.TextAlignment=TextAlignment.Center;playPanel.Children.Add(playReason);
        Grid.SetColumn(playPanel,1);header.Children.Add(identity);header.Children.Add(playPanel);
        header.SizeChanged+=(_,e)=>ArrangeHeader(e.NewSize.Width);
        Grid.SetRow(header,0);page.Children.Add(header);
        TextBlock? videoHeading=null;
        SizeChanged+=(_,e)=>
        {
            bool shortWindow=e.NewSize.Height<500;
            page.Margin=shortWindow?new Thickness(8):new Thickness(20);
            header.Margin=shortWindow?new Thickness(0,0,0,4):new Thickness(0,0,0,14);
            if(identityHeadline is not null)
            {
                identityHeadline.FontSize=shortWindow?18:26;
                identityHeadline.Text=shortWindow?"Launcher Alpha 0.2":"Uma jornada pronta para começar.";
            }
            state.Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
            if(videoHeading is not null)videoHeading.Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
            bar.Visibility=shortWindow&&cancellation is null?Visibility.Collapsed:Visibility.Visible;
            if(footerLayout is not null)footerLayout.Margin=shortWindow?new Thickness(0,4,0,0):new Thickness(0,10,0,0);
            ArrangeHeader(header.ActualWidth);
        };
        var setup=new StackPanel();
        setup.Children.Add(new TextBlock{Text="Prepare sua cópia local do jogo",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,6)});
        setup.Children.Add(new TextBlock{Text="Compatibilidade: edição norte-americana · versão 1.00",Foreground=Brushes.LightGray,Margin=new(0,0,0,12)});
        setup.Children.Add(new TextBlock{Text="1. Escolha a origem",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,4)});
        Field(setup,"Pasta do jogo extraído",source);
        var originButtons=new WrapPanel(); originButtons.Children.Add(Button("SELECIONAR PASTA",Locate)); originButtons.Children.Add(Button("SELECIONAR ISO LOCAL",SelectIso)); originButtons.Children.Add(Button("PROCURAR ORIGEM",Search)); useExisting=Button("USAR INSTALAÇÃO EXISTENTE",SelectExistingInstallation);originButtons.Children.Add(useExisting);setup.Children.Add(originButtons);
        setup.Children.Add(candidates); candidates.SelectionChanged+=(_,_)=>{if(candidates.SelectedItem is Candidate c){source.Text=c.Path; detail.Text=c.Status;}};
        setup.Children.Add(new TextBlock{Text="Ou use uma instalação existente em modo somente leitura. A VFS será verificada e copiada para o perfil experimental antes de iniciar, pois o runtime pode criar caches ao lado dos dados.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,12,0,8)});
        setup.Children.Add(new TextBlock{Text="2. Escolha onde instalar",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,4)});
        Field(setup,"Pasta de instalação",destination);
        setup.Children.Add(Button("Escolher pasta de instalação",()=>ChooseFolder(destination)));
        setup.Children.Add(new TextBlock{Text="3. Prepare ou repare a instalação",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,14,0,4)});
        var operations=new WrapPanel();
        import=Button("PREPARAR / REPARAR",Install); cancel=Button("Cancelar",()=>cancellation?.Cancel()); cancel.IsEnabled=false;
        operations.Children.Add(import); operations.Children.Add(cancel); setup.Children.Add(operations);
        setup.Children.Add(new TextBlock{Text="Assistente de preparação a partir da ISO",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,2)});
        setup.Children.Add(prepChecklist);
        var componentShortcuts=new WrapPanel();
        componentShortcuts.Children.Add(Button("SELECIONAR EBOOT.ELF",()=>ChooseFile(elf,"ELF preparado|*.elf")));
        componentShortcuts.Children.Add(Button("SELECIONAR EXECUTÁVEL NATIVO",()=>ChooseFile(runtime,"Executável nativo|*.exe")));
        componentShortcuts.Children.Add(Button("DESCRIPTOGRAFAR EBOOT.BIN (FERRAMENTA LOCAL)",DecryptWithLocalTool));
        setup.Children.Add(componentShortcuts);
        setup.Children.Add(new TextBlock{Text="O assistente localiza automaticamente componentes já preparados (junto à imagem montada, no destino informado ou lembrados nas configurações) e confere a identidade SHA-256 antes de habilitar JOGAR. O launcher não descriptografa a EBOOT.BIN protegida sozinho: gere o EBOOT.ELF com o dump de executável do RPCS3 (docs/03-setup.md) ou com uma ferramenta local instalada por você. A imagem e a origem nunca são alteradas.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,8)});
        setup.Children.Add(new TextBlock{Text="PREPARAR / REPARAR atua somente em cópias locais gerenciadas pelo launcher; fica desativado no modo de instalação existente.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,8)});
        tabs.Items.Add(new TabItem{Header="Instalação",Content=new ScrollViewer{Content=setup,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=true,Padding=new(4)}});
        var settings=new Grid{Margin=new(0,0,0,8)};
        settingsLayout=settings;
        settings.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
        settings.ColumnDefinitions.Add(new ColumnDefinition{Width=new(1,GridUnitType.Star)});
        var videoSettings=new StackPanel{Margin=new(4,0,18,0)};
        var controlsSettings=new StackPanel{Margin=new(18,0,4,0)};
        videoSettingsPanel=videoSettings;controlsSettingsPanel=controlsSettings;
        settings.SizeChanged+=(_,e)=>ArrangeSettings(e.NewSize.Width);
        settings.Children.Add(videoSettings);settings.Children.Add(controlsSettings);
        videoHeading=new TextBlock{Text="Vídeo",FontSize=22,Margin=new(0,0,0,12)};
        videoSettings.Children.Add(videoHeading);
        videoSettings.Children.Add(new Label{Content="Resolução de saída",Foreground=Brushes.LightGray,Target=windowResolution});windowResolution.Margin=new(0,0,0,8);videoSettings.Children.Add(windowResolution);
        videoSettings.Children.Add(new Label{Content="Modo de exibição",Foreground=Brushes.LightGray,Target=displayMode});displayMode.Margin=new(0,0,0,8);videoSettings.Children.Add(displayMode);
        videoSettings.Children.Add(new Label{Content="Monitor",Foreground=Brushes.LightGray,Target=monitorSelection});monitorSelection.Margin=new(0,0,0,8);videoSettings.Children.Add(monitorSelection);
        videoSettings.Children.Add(new Label{Content="Taxa de quadros",Foreground=Brushes.LightGray,Target=frameRate});frameRate.Margin=new(0,0,0,4);videoSettings.Children.Add(frameRate);
        frameRateStatus.Margin=new(0,0,0,10);videoSettings.Children.Add(frameRateStatus);
        videoSettings.Children.Add(new TextBlock{Text="A imagem é renderizada internamente em 1280 × 720 e ajustada à resolução de saída escolhida.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,12)});
        vsync.Foreground=Brushes.WhiteSmoke;vsync.Margin=new(0,8,0,8);videoSettings.Children.Add(vsync);
        controlsSettings.Children.Add(new TextBlock{Text="Controles de teclado e mouse",FontSize=22,Margin=new(0,0,0,8)});
        controlsSettings.Children.Add(new TextBlock{Text="WASD: mover · mouse: câmera · botão esquerdo: ataque · botão direito: defesa · Espaço ou Shift: esquiva · E: interagir · setas: menus · Enter: confirmar · Tab: voltar · F/R: ações · Q/2: gatilhos. A captura do cursor termina ao perder foco ou usar Alt+Tab.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,0,0,10)});
        keyboard.Foreground=Brushes.WhiteSmoke;keyboard.Margin=new(0,8,0,8);controlsSettings.Children.Add(keyboard);
        controlsSettings.Children.Add(new TextBlock{Text="Gamepad compatível: detecção automática.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,0,0,16)});
        controlsSettings.Children.Add(new TextBlock{Text="Áudio",FontSize=22,Margin=new(0,0,0,8)});
        mute.Foreground=Brushes.WhiteSmoke;mute.Margin=new(0,8,0,8);controlsSettings.Children.Add(mute);
        controlsSettings.Children.Add(Button("Salvar configurações",async ()=>{try{await SaveValidatedPreferences();}catch(Exception e){Error(e);}}));
        tabs.Items.Add(new TabItem{Header="Configurações",Content=new ScrollViewer{Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=true,Padding=new(4)}});
        var advanced=new StackPanel();
        advanced.Children.Add(new TextBlock{Text="Ferramentas e caminhos avançados",FontSize=22,Margin=new(0,0,0,12)});
        advanced.Children.Add(new TextBlock{Text="Estas opções são destinadas à configuração e ao diagnóstico local. Arquivos do jogo não acompanham o launcher.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,0,0,12)});
        Field(advanced,"Perfil experimental separado (saves, HDD, shaders e logs)",runtimeProfile);
        advanced.Children.Add(Button("SELECIONAR PASTA DO PERFIL",()=>ChooseFolder(runtimeProfile)));
        advanced.Children.Add(new TextBlock{Text="O padrão cria um perfil experimental separado. Não reutiliza nem altera saves existentes ou perfis estáveis.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,0,0,12)});
        var components=new StackPanel();
        Field(components,"EBOOT.ELF compatível preparado localmente",elf);
        components.Children.Add(Button("Selecionar EBOOT.ELF",()=>ChooseFile(elf,"ELF preparado|*.elf")));
        Field(components,"Executável nativo compatível",runtime);
        components.Children.Add(Button("Selecionar executável nativo",()=>ChooseFile(runtime,"Executável nativo|*.exe")));
        components.Children.Add(new TextBlock{Text="O launcher não descriptografa arquivos. Não distribua executáveis recompilados ou arquivos do jogo sem concluir a auditoria jurídica e técnica.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,8,0,8)});
        advanced.Children.Add(new Expander{Header="Componentes locais necessários para jogar",Foreground=Brushes.WhiteSmoke,Margin=new(0,0,0,12),Content=components});
        saves=Button("Abrir pasta de saves",()=>RuntimeService.OpenFolder(Path.Combine(runtimeProfile.Text,"gamedata/dev_hdd0/home/00000001/savedata")));
        diagnostics=Button("Abrir logs de diagnóstico",()=>RuntimeService.OpenFolder(Path.Combine(runtimeProfile.Text,"logs")));
        advanced.Children.Add(saves);advanced.Children.Add(diagnostics);
        advanced.Children.Add(Button("Abrir logs do launcher",()=>RuntimeService.OpenFolder(LauncherLogDirectory)));
        advanced.Children.Add(Button("Abrir cache de shaders",()=>RuntimeService.OpenFolder(Path.Combine(runtimeProfile.Text,"shader_cache"))));
        advanced.Children.Add(Button("Ver tamanho do cache",()=>{long bytes=Directory.Exists(Path.Combine(runtimeProfile.Text,"shader_cache"))?Installation.Inventory(Path.Combine(runtimeProfile.Text,"shader_cache")).Sum(e=>e.Length):0;detail.Text=$"Cache de shaders: {bytes/1048576.0:N1} MiB.";}));
        tabs.Items.Add(new TabItem{Header="Avançado",Content=new ScrollViewer{Content=advanced,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=true,Padding=new(4)}});
        Grid.SetRow(tabs,1);page.Children.Add(tabs);
        var footer=new StackPanel{Margin=new(0,10,0,0)};
        footerLayout=footer;
        footer.Children.Add(bar);operationStage.Margin=new(0,5,0,0);footer.Children.Add(operationStage);
        detail.Margin=new(0,3,0,0);if(preferencesWarning is not null)detail.Text=preferencesWarning;footer.Children.Add(detail);
        inventory.Margin=new(0,3,0,0);inventory.Foreground=Brushes.LightGray;footer.Children.Add(inventory);
        footer.Children.Add(new TextBlock{Text="Launcher Alpha 0.2 · versão 0.2.1-alpha.1",Foreground=Brushes.Gray,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,5,0,0)});
        Grid.SetRow(footer,2);page.Children.Add(footer);
        Content=page;
        Closing+=(_,e)=>{if(cancellation!=null){cancellation.Cancel();e.Cancel=true;detail.Text="Cancelando com segurança. Aguarde a operação terminar antes de fechar.";}};
        destination.TextChanged+=(_,_)=>{Refresh();_ = RefreshRuntimeCompatibility();};
        source.TextChanged+=(_,_)=>Refresh();
        runtime.TextChanged+=(_,_)=>_ = RefreshRuntimeCompatibility();
        runtimeProfile.TextChanged+=(_,_)=>_ = RefreshRuntimeCompatibility();
        elf.TextChanged+=(_,_)=>_ = RefreshRuntimeCompatibility();
        frameRate.SelectionChanged+=(_,_)=>UpdateFrameRateStatus();
        Loaded+=async (_,_)=>{await PreferPackagedApprovedRuntimeAsync();await RefreshRuntimeCompatibility();};
        Refresh();
    }
    static string FindProject(){string? p=AppContext.BaseDirectory;while(p!=null){if(File.Exists(Path.Combine(p,"README.md"))&&Directory.Exists(Path.Combine(p,"port")))return p;p=Directory.GetParent(p)?.FullName;}return Environment.CurrentDirectory;}
    Task PreferPackagedApprovedRuntimeAsync()
    {
        string packaged=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Game","des_boot.exe"));
        if(!File.Exists(packaged))return Task.CompletedTask;
        bool alreadySelected=false;
        try{alreadySelected=string.Equals(Path.GetFullPath(runtime.Text),packaged,StringComparison.OrdinalIgnoreCase);}
        catch(Exception e) when(e is ArgumentException or IOException or NotSupportedException) { }
        if(!alreadySelected)runtime.Text=packaged;
        return Task.CompletedTask;
    }
    void RefreshFrameRateOptions()
    {
        int selected=frameRate.SelectedValue is int current?current:preferences.FrameRate;
        frameRate.ItemsSource=new[]
        {
            new FrameRateOption(60,"60 FPS",runtime60Compatible),
        };
        frameRate.SelectedValue=selected;
    }
    void UpdateFrameRateStatus()
    {
        frameRateStatus.Text=runtime60Compatible
            ?preferences.UseExistingInstallation
                ?"Build e EBOOT aprovados; o perfil é separado. Ao clicar JOGAR, a origem será verificada e sua VFS preparada no perfil isolado. 60 FPS é experimental."
                :"Build e EBOOT aprovados; perfil separado válido. Os arquivos da instalação serão verificados integralmente ao clicar JOGAR. Perfil padrão: 60 FPS."
            :"60 FPS indisponível: "+runtime60Status;
    }
    async Task RefreshRuntimeCompatibility()
    {
        runtimeCheck?.Cancel();
        runtimeCheck?.Dispose();
        var check=new CancellationTokenSource();
        runtimeCheck=check;
        runtime60Compatible=false;
        RefreshFrameRateOptions();
        runtime60Status="selecione uma instalação experimental preparada na aba Instalação.";
        frameRateStatus.Text="Verificando instalação, runtime aprovado, EBOOT e perfil experimental…";
        OperationTrace? trace=null;
        try
        {
            trace=OperationTrace.Start("Verificação de compatibilidade 60 FPS");
            var activeTrace=trace;
            string installation=destination.Text,profile=runtimeProfile.Text,selectedRuntime=runtime.Text,selectedElf=elf.Text;
            bool existing=preferences.UseExistingInstallation;
            string? blocker=await Task.Run(async()=>
            {
                using(activeTrace.Activate())
                    return await RuntimeService.Get60FpsCompatibilityBlockerAsync(installation,profile,check.Token,
                        existing,selectedRuntime,selectedElf);
            });
            if(check.IsCancellationRequested)
            {
                trace.Fail(new OperationCanceledException(check.Token));
                return;
            }
            trace.Complete();
            runtime60Compatible=blocker is null;
            runtime60Status=blocker??"instalação, hashes e perfil separados conferidos.";
            RefreshFrameRateOptions();
            UpdateFrameRateStatus();
            if(!runtime60Compatible&&frameRate.SelectedValue is int { } selected&&selected==60)
                frameRateStatus.Text="60 FPS está indisponível: "+runtime60Status+" Corrija os requisitos antes de iniciar.";
        }
        catch(OperationCanceledException e) when(check.IsCancellationRequested) { trace?.Fail(e); }
        catch(Exception e)
        {
            trace?.Fail(e);
            if(check.IsCancellationRequested)return;
            runtime60Compatible=false;
            runtime60Status=e.GetBaseException().Message;
            RefreshFrameRateOptions();
            UpdateFrameRateStatus();
            detail.Text=WriteErrorLog(e);
        }
        finally{trace?.Dispose();}
    }
    void ArrangeHeader(double width)
    {
        if(headerLayout is null||identityPanel is null||playPanel is null)return;
        compactHeader=width<680&&Height>=500;
        headerLayout.RowDefinitions.Clear();
        headerLayout.ColumnDefinitions[1].Width=compactHeader?new GridLength(0):GridLength.Auto;
        headerLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetColumn(identityPanel,0);Grid.SetRow(identityPanel,0);
        Grid.SetColumn(playPanel,compactHeader?0:1);
        if(compactHeader)
        {
            headerLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid.SetRow(playPanel,1);
            identityPanel.Margin=new Thickness(0);
            playPanel.Orientation=Orientation.Vertical;
            playReason.TextAlignment=TextAlignment.Center;
            playReason.Margin=new Thickness(0);
        }
        else
        {
            Grid.SetRow(playPanel,0);
            identityPanel.Margin=new Thickness(0,0,12,0);
            playPanel.Orientation=Orientation.Vertical;
            playReason.TextAlignment=TextAlignment.Center;
            playReason.Margin=new Thickness(0);
        }
    }
    void ArrangeSettings(double width)
    {
        if(settingsLayout is null||videoSettingsPanel is null||controlsSettingsPanel is null)return;
        compactSettings=width<880;
        settingsLayout.RowDefinitions.Clear();
        settingsLayout.ColumnDefinitions[0].MinWidth=0;
        settingsLayout.ColumnDefinitions[1].MinWidth=0;
        settingsLayout.ColumnDefinitions[1].Width=compactSettings?new GridLength(0):new GridLength(1,GridUnitType.Star);
        videoSettingsPanel.MinWidth=0;
        controlsSettingsPanel.MinWidth=0;
        videoSettingsPanel.Margin=compactSettings?new Thickness(0,0,0,14):new Thickness(4,0,18,0);
        controlsSettingsPanel.Margin=compactSettings?new Thickness(0):new Thickness(18,0,4,0);
        settingsLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetColumn(videoSettingsPanel,0);Grid.SetRow(videoSettingsPanel,0);
        Grid.SetColumn(controlsSettingsPanel,compactSettings?0:1);
        if(compactSettings)
        {
            settingsLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid.SetRow(controlsSettingsPanel,1);
        }
        else Grid.SetRow(controlsSettingsPanel,0);
    }
    static void Field(Panel parent,string label,TextBox box){var l=new Label{Content=label,Foreground=Brushes.LightGray,Target=box};parent.Children.Add(l);box.MinHeight=34;box.Padding=new(8);box.Margin=new(0,0,0,5);parent.Children.Add(box);}
    Button Button(string title,Action action){var b=new Button{Content=title};System.Windows.Automation.AutomationProperties.SetName(b,title);b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e);}};return b;}
    void Error(Exception e)
    {
        var cause=e.GetBaseException();
        string message=$"{FriendlyError(cause.Message)}{Environment.NewLine}{Environment.NewLine}Detalhe: {cause.Message}";
        message+=$"{Environment.NewLine}{WriteErrorLog(e)}";
        detail.Text=message;
        if(!suppressErrorDialogsForTest)
            MessageBox.Show(this,message,"Demon’s Souls PC",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    static string WriteErrorLog(Exception e)
    {
        string log=Path.Combine(LauncherLogDirectory,"launcher.log");
        try
        {
            Directory.CreateDirectory(LauncherLogDirectory);
            File.AppendAllText(log,$"[{DateTimeOffset.Now:O}]{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
            return $"Os detalhes foram salvos em {log}.";
        }
        catch(Exception logError)
        {
            return $"Não foi possível salvar o diagnóstico: {logError.Message}";
        }
    }
    internal static string FriendlyError(string message)
    {
        if(message.StartsWith("60 FPS bloqueado:",StringComparison.Ordinal))return "A configuração de 60 FPS não foi salva. Confira o requisito indicado abaixo.";
        if(message.Contains("inventário",StringComparison.OrdinalIgnoreCase)||message.Contains("VFS",StringComparison.OrdinalIgnoreCase))
            return "A VFS do perfil experimental não passou na validação. Confira os arquivos indicados no detalhe; os dados foram preservados.";
        if(message.Contains("integridade",StringComparison.OrdinalIgnoreCase)||message.Contains("derivado SDAT",StringComparison.OrdinalIgnoreCase))
            return "Foi encontrada uma divergência de integridade. Confira o arquivo indicado no detalhe; os dados foram preservados.";
        if(message.Contains("PS3_GAME",StringComparison.OrdinalIgnoreCase))return "Não foi possível localizar uma pasta de jogo compatível. Escolha outra pasta ou uma imagem montada pelo Windows.";
        if(message.Contains("BLUS30443",StringComparison.OrdinalIgnoreCase)||message.Contains("APP_VER",StringComparison.OrdinalIgnoreCase))return "Esta edição não é compatível. O launcher aceita somente a edição norte-americana, versão 1.00.";
        if(message.Contains("EBOOT.ELF",StringComparison.OrdinalIgnoreCase)||message.Contains("ELF PPC64",StringComparison.OrdinalIgnoreCase))return "Falta um componente local compatível. Use o assistente de preparação na aba Instalação (SELECIONAR EBOOT.ELF, dump do RPCS3 ou ferramenta local) para completar o requisito.";
        if(message.Contains("manifesto",StringComparison.OrdinalIgnoreCase)||message.Contains("integridade",StringComparison.OrdinalIgnoreCase)||message.Contains("corrompido",StringComparison.OrdinalIgnoreCase))return "Não foi possível validar a instalação. Tente reparar usando a mesma origem.";
        if(message.Contains("espaço insuficiente",StringComparison.OrdinalIgnoreCase))return "Não há espaço livre suficiente no destino escolhido.";
        if(message.Contains("ausente",StringComparison.OrdinalIgnoreCase)||message.Contains("não encontrado",StringComparison.OrdinalIgnoreCase))return "Faltam arquivos necessários. Confira a origem escolhida e tente reparar a instalação.";
        return "A operação não foi concluída. Consulte os logs do launcher em Avançado para ver os detalhes.";
    }
    void Refresh()
    {
        UpdatePrepChecklist();
        play.IsEnabled=saves.IsEnabled=diagnostics.IsEnabled=false;
        import.IsEnabled=cancellation==null&&!preferences.UseExistingInstallation;
        if(useExisting is not null)useExisting.IsEnabled=cancellation==null;
        if(!File.Exists(Path.Combine(destination.Text,"installation.json")))
        {
            lastStatusError=null;
            state.Text=preferences.UseExistingInstallation?"Instalação existente não selecionada ou manifesto ausente.":"Instalação ainda não preparada.";
            playReason.Text=preferences.UseExistingInstallation
                ?"Selecione uma instalação existente com installation.json válido; o launcher não a prepara nem modifica."
                :"Para jogar, escolha uma origem e prepare a instalação.";
            return;
        }
        try
        {
            var manifest=preferences.UseExistingInstallation
                ?Installation.ValidateExistingStructureReadOnly(destination.Text)
                :Installation.Load(destination.Text);
            lastStatusError=null;
            play.IsEnabled=saves.IsEnabled=diagnostics.IsEnabled=manifest.Complete&&!manifest.Synthetic&&cancellation==null;
            state.Text=preferences.UseExistingInstallation
                ?"Instalação existente reconhecida. A origem será tratada como somente leitura."
                :manifest.Complete?"Instalação pronta. A integridade será conferida antes de jogar.":"Instalação incompleta. Retome a preparação com a mesma origem.";
            playReason.Text=manifest.Synthetic?"Fixture de teste: não pode iniciar o jogo.":!manifest.Complete?"Conclua ou repare a instalação para habilitar JOGAR.":cancellation!=null?"Aguarde a operação em andamento.":preferences.UseExistingInstallation?"A VFS será verificada e espelhada no perfil experimental; a instalação original não será alterada.":"";
        }
        catch(Exception e)
        {
            var cause=e.GetBaseException();
            state.Text=$"{FriendlyError(cause.Message)}{Environment.NewLine}Detalhe: {cause.Message}";
            playReason.Text=$"JOGAR indisponível: {cause.Message}";
            string key=destination.Text+Environment.NewLine+e;
            if(!string.Equals(lastStatusError,key,StringComparison.Ordinal))
            {
                lastStatusError=key;
                detail.Text=WriteErrorLog(e);
            }
        }
    }
    void ChooseFolder(TextBox target){var d=new OpenFolderDialog();if(d.ShowDialog(this)==true){if(ReferenceEquals(target,destination))preferences.UseExistingInstallation=false;target.Text=d.FolderName;Refresh();}}
    void ChooseFile(TextBox target,string filter){var d=new OpenFileDialog{Filter=filter};if(d.ShowDialog(this)==true){target.Text=d.FileName;if(frameRate.SelectedValue is not int { } rate||rate!=60){try{SavePreferences();}catch(Exception e){Error(e);}}}}
    async void Locate(){try{var d=new OpenFolderDialog{Title="Selecione a pasta do jogo ou uma instalação preparada"};if(d.ShowDialog(this)!=true)return;if(File.Exists(Path.Combine(d.FolderName,"installation.json"))){Installation.Load(d.FolderName);preferences.UseExistingInstallation=false;destination.Text=d.FolderName;await SaveValidatedPreferences();Refresh();}else{source.Text=Installation.GameRoot(d.FolderName);detail.Text="Edição compatível encontrada. Confirme a pasta de destino e prepare a instalação.";}}catch(Exception e){Error(e);}}
    void SelectExistingInstallation()
    {
        var dialog=new OpenFolderDialog{Title="Selecione uma instalação existente com installation.json"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            string selected=Installation.Full(dialog.FolderName);
            string manifest=Path.Combine(selected,"installation.json");
            if(!File.Exists(manifest))
            {
                bool appearsToBeParent=File.Exists(Path.Combine(selected,"PS3_GAME","installation.json"))||
                    File.Exists(Path.Combine(selected,"GameData","installation.json"));
                string advice=appearsToBeParent
                    ?"A pasta escolhida parece ser um diretório pai. Selecione diretamente a subpasta que contém installation.json."
                    :"A pasta escolhida precisa conter installation.json. Se o manifesto estiver em uma subpasta, selecione essa subpasta, não o diretório pai.";
                Error(new IOException(advice+" A instalação original não foi alterada."));
                return;
            }
            _=Installation.ValidateExistingSelection(selected);
            RuntimeService.ValidateProfileRoot(selected,runtimeProfile.Text);
            preferences.UseExistingInstallation=true;
            if(frameRate.SelectedValue is int { } rate&&rate==60)
            {
                frameRate.SelectedValue=60;
                preferences.FrameRate=60;
                runtime60Compatible=false;
            }
            destination.Text=selected;
            SavePreferences();
            detail.Text="Instalação existente reconhecida sem cópia ou escrita na origem. A VFS será verificada e copiada apenas para o perfil isolado ao iniciar.";
            Refresh();
            _ = RefreshRuntimeCompatibility();
        }
        catch(Exception e){Error(e);}
    }
    async void SelectIso()
    {
        if(cancellation!=null)return;
        try
        {
            var dialog=new OpenFileDialog{Filter="Imagem ISO local|*.iso"};
            if(dialog.ShowDialog(this)!=true)return;
            string isoPath=dialog.FileName;
            // UI-affine values are captured on the UI thread; all IO and hashing
            // run inside the Work task, and every UI write returns afterwards.
            string destinationSelected=destination.Text,rememberedElf=preferences.EbootPath,rememberedRuntime=preferences.RuntimeExecutable;
            string? resolvedGameRoot=null,stepDetail=null,foundElf=null,foundRuntime=null;
            await Work("Preparação da ISO (montagem somente leitura e identificação)",async(_,ct)=>
            {
                var preparation=await IsoFlow.Prepare(isoPath,ct);
                stepDetail=preparation.Detail;
                if(preparation.GameRoot is not null)
                {
                    resolvedGameRoot=preparation.GameRoot;
                    foundElf=IsoFlow.FindEbootElf(preparation.GameRoot,destinationSelected,rememberedElf,false);
                    foundRuntime=IsoFlow.FindApprovedRuntime(destinationSelected,rememberedRuntime);
                    stepDetail=preparation.Detail
                        +(foundElf!=null?" EBOOT.ELF já preparado localizado e aprovado automaticamente.":" EBOOT.ELF preparado não encontrado ainda; gere-o com RPCS3/ferramenta local (instruções no assistente).")
                        +(foundRuntime!=null?" Executável nativo aprovado localizado automaticamente.":" Executável nativo aprovado não localizado; informe o arquivo no assistente de preparação.");
                }
            });
            lastIsoDetail=stepDetail??"";
            if(resolvedGameRoot!=null)
            {
                preferences.UseExistingInstallation=false;
                source.Text=resolvedGameRoot;
                if(foundElf!=null&&!ApprovalMatchesState(elf.Text,rememberedElf))
                {
                    elf.Text=foundElf;
                    MarkComponentApproval(foundElf,true);
                }
                if(foundRuntime!=null&&!ApprovalMatchesState(runtime.Text,rememberedRuntime))
                {
                    runtime.Text=foundRuntime;
                    MarkComponentApproval(foundRuntime,true);
                }
                detail.Text=lastIsoDetail;
            }
            else detail.Text=lastIsoDetail+" Revise o assistente de preparação abaixo.";
            Refresh();
        }
        catch(Exception e){Error(e);}
    }
    async void DecryptWithLocalTool()
    {
        if(cancellation!=null)return;
        if(string.IsNullOrWhiteSpace(source.Text))
        {
            Error(new IOException("Selecione a ISO (SELECIONAR ISO LOCAL) para montar a imagem antes de usar a ferramenta local de descriptografia."));
            return;
        }
        string gameRoot=source.Text;
        var probe=IsoFlow.FindScetool();
        if(probe.Path is null){Error(new IOException(probe.Detail));return;}
        string outputDirectory=Path.Combine(Preferences.UserDataDirectory,"prep");
        string? produced=null;
        await Work("Descriptografia local do EBOOT.BIN com ferramenta do usuário",async(_,ct)=>
        {
            produced=await IsoFlow.ScetoolDecryptAsync(probe.Path,gameRoot,outputDirectory,ct);
        });
        if(produced is null)return;
        elf.Text=produced;
        MarkComponentApproval(produced,true);
        detail.Text="EBOOT.ELF gerado pela ferramenta local e aprovado pela identidade do projeto. Ele permanece em "+produced+" e será copiado na preparação.";
        Refresh();
    }
    bool ApprovalMatchesState(string current,string remembered) =>
        (!string.IsNullOrWhiteSpace(current)&&FileExistsQuietForComponent(current))||
        (!string.IsNullOrWhiteSpace(remembered)&&string.Equals(current,remembered,StringComparison.OrdinalIgnoreCase));
    static bool FileExistsQuietForComponent(string path){try{return File.Exists(path);}catch(Exception){return false;}}
    void MarkComponentApproval(string path,bool approved)
    {
        try
        {
            var info=new FileInfo(path);
            componentIdentity[info.FullName]=(info.Length,info.LastWriteTimeUtc,approved);
        }
        catch(Exception){/* identity memo is best-effort only */}
    }
    bool? ApprovalStateForComponent(string path)
    {
        try
        {
            var info=new FileInfo(path);
            if(info.Exists&&componentIdentity.TryGetValue(info.FullName,out var memo)&&
                memo.Length==info.Length&&memo.WriteUtc==info.LastWriteTimeUtc)return memo.Approved;
        }
        catch(Exception){}
        return null;
    }
    string OriginCheckLine(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            return "✗ Origem: escolha a origem — SELECIONAR ISO LOCAL monta a imagem e preenche automaticamente; SELECIONAR PASTA aceita uma pasta PS3_GAME já extraída.";
        try
        {
            string fromRoot=File.Exists(Path.Combine(path,"PARAM.SFO"))?path:Path.Combine(path,"PS3_GAME");
            if(File.Exists(Path.Combine(fromRoot,"PARAM.SFO")))
                return "✓ Origem: pasta PS3_GAME detectada em "+Installation.Full(fromRoot);
        }
        catch(Exception){}
        return "✗ Origem: o caminho informado não contém uma pasta PS3_GAME legível.";
    }
    void UpdatePrepChecklist()
    {
        // Cheap, existence-only preview. The strict SHA-256 identity gate
        // happens inside PREPARAR and the async REFRESH of compatibility; the
        // checklist never claims an approval it did not actually perform.
        var lines=new List<string>
        {
            string.IsNullOrWhiteSpace(lastIsoDetail)
                ?"○ Imagem ISO: nenhuma selecionada (SELECIONAR ISO LOCAL monta a imagem em modo somente leitura e identifica a edição)."
                :lastIsoDetail,
            OriginCheckLine(source.Text)
        };
        string elfPath=elf.Text,runtimePath=runtime.Text,destinationPath=destination.Text;
        if(string.IsNullOrWhiteSpace(elfPath))
        {
            var candidates=IsoFlow.EbootElfCandidates(OriginRootForChecklist(source.Text),destinationPath,preferences.EbootPath);
            lines.Add(candidates.Count>0
                ?"△ EBOOT.ELF: candidato localizado junto à origem/instalação; a identidade será conferida no PREPARAR."
                :"✗ EBOOT.ELF: não localizado. Gere-o com o dump do RPCS3 ou use DESCRIPTOGRAFAR EBOOT.BIN (FERRAMENTA LOCAL); o launcher não descriptografa sozinho.");
        }
        else if(!FileExistsQuietForComponent(elfPath))
            lines.Add("✗ EBOOT.ELF: o caminho informado não existe em disco.");
        else
        {
            bool? approved=ApprovalStateForComponent(elfPath);
            lines.Add(approved==true
                ?"✓ EBOOT.ELF: identidade aprovada já conferida por este launcher."
                :"△ EBOOT.ELF: arquivo informado; identidade exata (SHA-256 90eaa5e9…) será conferida no PREPARAR.");
        }
        if(string.IsNullOrWhiteSpace(runtimePath))
        {
            var candidates=IsoFlow.RuntimeCandidates(destinationPath,preferences.RuntimeExecutable);
            lines.Add(candidates.Count>0
                ?"△ Executável nativo: candidato possivelmente aprovado localizado (pacote/repositório/instalação) — confirmação por SHA-256 no PREPARAR."
                :"✗ Executável nativo: nenhum des_boot.exe aprovado localizado. Informe a cópia aprovada (pasta Game do pacote ou arquivo salvo); o launcher não gera o executável a partir da ISO — o build de fonte exige o ambiente de desenvolvedor.");
        }
        else if(!FileExistsQuietForComponent(runtimePath))
            lines.Add("✗ Executável nativo: o caminho informado não existe em disco.");
        else
        {
            bool? approved=ApprovalStateForComponent(runtimePath);
            lines.Add(approved==true
                ?"✓ Executável nativo: identidade aprovada já conferida por este launcher."
                :"△ Executável nativo: arquivo informado; identidade (SHA-256) será conferida no PREPARAR.");
        }
        if(!string.IsNullOrWhiteSpace(destinationPath))
            lines.Add(Directory.Exists(destinationPath)
                ?File.Exists(Path.Combine(destinationPath,"installation.json"))
                    ?"✓ Destino: manifesto de instalação presente."
                    :"○ Destino: pasta definida, sem manifesto (o PREPARAR grava o manifesto no final)."
                :"○ Destino: pasta inexistente (o PREPARAR cria).");
        try
        {
            string root=Path.GetPathRoot(Path.GetFullPath(destinationPath))??Path.GetPathRoot(Path.GetFullPath(source.Text))??"";
            if(!string.IsNullOrWhiteSpace(root))
            {
                var drive=new DriveInfo(root);
                if(drive.IsReady)
                    lines.Add($"○ Espaço em {root}: {drive.AvailableFreeSpace:N0} bytes livres (prévia; a exigência exata é conferida na preparação).");
            }
        }
        catch(Exception){}
        prepChecklist.Text=string.Join(Environment.NewLine,lines);
    }
    string OriginRootForChecklist(string sourcePath)
    {
        if(string.IsNullOrWhiteSpace(sourcePath))return "";
        try{return File.Exists(Path.Combine(sourcePath,"PARAM.SFO"))?sourcePath:Path.Combine(sourcePath,"PS3_GAME");}
        catch(Exception){return "";}
    }
    void Search(){candidates.Items.Clear();foreach(var c in Discovery.Common())candidates.Items.Add(c);candidates.Visibility=Visibility.Visible;detail.Text="Escolha um local encontrado. A versão será conferida antes da instalação.";}
    async void Install()
    {
        if(cancellation!=null)return;
        if(preferences.UseExistingInstallation)
        {
            Error(new IOException("PREPARAR / REPARAR está bloqueado para uma instalação existente em modo somente leitura. Selecione uma pasta de destino local separada para preparar uma cópia gerenciada."));
            return;
        }
        if(MessageBox.Show(this,$"Copiar/verificar os recursos de:\n{source.Text}\n\nPara:\n{destination.Text}\n\nA origem e os saves existentes serão preservados. Continuar?","Preparar instalação local",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        string s=source.Text,d=destination.Text,e=elf.Text,r=runtime.Text;
        await Work("Preparação ou reparo da instalação",async (p,ct)=>{await Installation.Import(s,d,e,r,p,ct);});
        // After a verified import, point the component fields at the installed
        // copies: identical hashes, so the identity gate re-approves them and
        // future repairs/launches never depend on stray external paths.
        string runtimeInstalled=Path.Combine(d,"runtime","des_boot.exe");
        string elfInstalled=Path.Combine(d,"runtime","EBOOT.ELF");
        if(File.Exists(runtimeInstalled))
        {
            runtime.Text=runtimeInstalled;
            MarkComponentApproval(runtimeInstalled,true);
        }
        if(File.Exists(elfInstalled))
        {
            elf.Text=elfInstalled;
            MarkComponentApproval(elfInstalled,true);
        }
        Refresh();_ = RefreshRuntimeCompatibility();
        if(play.IsEnabled){try{await SaveValidatedPreferences();}catch(Exception ex){Error(ex);}}
    }
    async void Play()
    {
        if(cancellation!=null)return;
        string root=destination.Text;
        bool useExisting=preferences.UseExistingInstallation;
        string selectedRuntime=string.IsNullOrWhiteSpace(runtime.Text)?"":Installation.Full(runtime.Text);
        string selectedElf=string.IsNullOrWhiteSpace(elf.Text)?"":Installation.Full(elf.Text);
        preferences.FrameRate=frameRate.SelectedValue is int selected?selected:60;
        try{await SaveValidatedPreferences();}
        catch(Exception e){Error(e);return;}
        var launchPreferences=new Preferences
        {
            Installation=root,UseExistingInstallation=useExisting,RuntimeProfile=preferences.RuntimeProfile,
            RuntimeExecutable=selectedRuntime,EbootPath=selectedElf,MonitorDevice=preferences.MonitorDevice,
            KeyboardMouse=preferences.KeyboardMouse,Vsync=preferences.Vsync,Mute=preferences.Mute,
            WindowResolution=preferences.WindowResolution,DisplayMode=preferences.DisplayMode,FrameRate=preferences.FrameRate
        };
        bool ready=await Work("Validação e inicialização do jogo",async(p,ct)=>
        {
            var verification=useExisting
                ?await Installation.PrepareExistingRuntimeProfile(root,launchPreferences.RuntimeProfile,p,ct)
                :await Installation.VerifyForLaunch(root,p,ct);
            p.Report(new("Validando runtime aprovado e preparando inicialização","",0,0,0,0,0));
            using Process process=RuntimeService.Start(root,launchPreferences,verification,runtimeProfile:launchPreferences.RuntimeProfile,
                runtimeExecutable:selectedRuntime,useExistingInstallation:useExisting,ebootPath:selectedElf,
                existingVfsPreparedAndVerified:useExisting,cancellationToken:ct);
        });
        if(ready)Close();
    }
    // Persisted FPS is intent, never proof of executable identity. Revalidate the
    // current selection before a save; Start independently hashes again before launch.
    string CompatibilitySelection() => string.Join("\n",destination.Text,runtimeProfile.Text,
        runtime.Text,elf.Text,preferences.UseExistingInstallation);
    internal async Task SaveValidatedPreferences(string? path=null,Func<Task<string?>>? syntheticCheck=null)
    {
        if(frameRate.SelectedValue is int rate&&rate==60)
        {
            string selection=CompatibilitySelection();
            string installation=destination.Text,profile=runtimeProfile.Text,exe=runtime.Text,eboot=elf.Text;
            bool existing=preferences.UseExistingInstallation;
            string? blocker=syntheticCheck is null
                ?await Task.Run(()=>RuntimeService.Get60FpsCompatibilityBlockerAsync(installation,profile,
                    useExistingInstallation:existing,runtimeExecutable:exe,ebootPath:eboot))
                :await syntheticCheck();
            if(selection!=CompatibilitySelection())
                throw new IOException("60 FPS bloqueado: a seleção mudou durante a verificação. Aguarde e tente salvar novamente.");
            if(blocker is not null)
                throw new IOException("60 FPS bloqueado: "+blocker+" Confira os caminhos em Avançado e tente novamente.");
            // No await between the result for these paths and the atomic settings write.
            runtime60Compatible=true;
        }
        SavePreferences(path);
    }
    void SavePreferences(string? path=null)
    {
        int selectedRate=frameRate.SelectedValue is int selected?selected:60;
        if(selectedRate!=60)throw new IOException("Esta build Alpha 0.2 oferece somente o perfil aprovado de 60 FPS.");
        if(!runtime60Compatible)
            throw new IOException("60 FPS bloqueado: aguarde a confirmação SHA-256 do executável aprovado e tente novamente.");
        preferences.Installation=Installation.Full(destination.Text);
        preferences.RuntimeProfile=Installation.Full(runtimeProfile.Text);
        RuntimeService.ValidateProfileRoot(preferences.Installation,preferences.RuntimeProfile);
        preferences.RuntimeExecutable=string.IsNullOrWhiteSpace(runtime.Text)?"":Installation.Full(runtime.Text);
        preferences.EbootPath=string.IsNullOrWhiteSpace(elf.Text)?"":Installation.Full(elf.Text);
        preferences.MonitorDevice=monitorSelection.SelectedValue as string??"";
        preferences.KeyboardMouse=keyboard.IsChecked==true;preferences.Vsync=vsync.IsChecked==true;preferences.Mute=mute.IsChecked==true;
        preferences.WindowResolution=windowResolution.SelectedValue as string??"1280x720";
        preferences.DisplayMode=displayMode.SelectedValue as string??"windowed";
        preferences.FrameRate=selectedRate;
        preferences.Write(path??Preferences.FilePath);
        detail.Text="Configurações salvas; serão aplicadas na próxima inicialização.";
    }
    internal void SelectVideoOptionsForTest(string resolution,string mode,string settingsPath)
    {
        if(Icon is null)throw new InvalidOperationException("O ícone da janela não foi carregado.");
        windowResolution.SelectedValue=resolution;displayMode.SelectedValue=mode;
        if(!Equals(windowResolution.SelectedValue,resolution)||!Equals(displayMode.SelectedValue,mode)||monitorSelection.SelectedValue is not string)
            throw new InvalidOperationException("As opções de vídeo não puderam ser selecionadas no launcher.");
        SavePreferences(settingsPath);
    }
    internal void SelectFrameRateForTest(int rate,string settingsPath,bool approvedRuntime)
    {
        runtime60Compatible=approvedRuntime;
        runtime60Status=approvedRuntime?"validado somente pelo cenário sintético de teste.":"runtime não aprovado ou não verificado.";
        RefreshFrameRateOptions();
        frameRate.SelectedValue=rate;
        if(!Equals(frameRate.SelectedValue,rate))throw new InvalidOperationException("A taxa de quadros sintética não pôde ser selecionada.");
        SavePreferences(settingsPath);
        if(approvedRuntime)frameRateStatus.Text="Teste sintético: o gate foi habilitado apenas para testar persistência e mapeamento; isso não valida o runtime nem o perfil real.";
    }
    internal bool IsFrameRateAvailableForTest(int rate) =>
        frameRate.Items.OfType<FrameRateOption>().Any(option=>option.Value==rate&&option.Available);
    internal IReadOnlyList<(string Label,string BindingPath)> ComboBoxLabelsForTest()
    {
        var boxes=new[]{windowResolution,displayMode,monitorSelection,frameRate};
        return boxes.Select(box=>
        {
            object item=box.SelectedItem??throw new InvalidOperationException("Um ComboBox não possui item selecionado.");
            if(box.ItemTemplate?.LoadContent() is not TextBlock text)
                throw new InvalidOperationException("O ComboBox não usa o template visual de rótulo amigável.");
            string path=System.Windows.Data.BindingOperations.GetBinding(text,TextBlock.TextProperty)?.Path?.Path??"";
            string label=item.GetType().GetProperty(path)?.GetValue(item)?.ToString()
                ??throw new InvalidOperationException("O template do ComboBox não resolve um rótulo amigável.");
            return(label,path);
        }).ToArray();
    }
    internal IReadOnlyList<string> ComboBoxOptionLabelsForTest(ComboBox box)
    {
        if(box.ItemTemplate?.LoadContent() is not TextBlock text)
            throw new InvalidOperationException("O ComboBox não usa o template visual de rótulo amigável.");
        string path=System.Windows.Data.BindingOperations.GetBinding(text,TextBlock.TextProperty)?.Path?.Path??"";
        if(path!="Label")throw new InvalidOperationException("O dropdown não está vinculado ao rótulo amigável.");
        return box.Items.Cast<object>().Select(item=>item.GetType().GetProperty(path)?.GetValue(item)?.ToString()
            ??throw new InvalidOperationException("Uma opção do dropdown não possui rótulo amigável.")).ToArray();
    }
    internal IReadOnlyList<string> AllFrameRateOptionLabelsForTest=>ComboBoxOptionLabelsForTest(frameRate);
    internal IReadOnlyList<IReadOnlyList<string>> AllComboOptionLabelsForTest=>
        new[]{windowResolution,displayMode,monitorSelection,frameRate}
            .Select(box=>ComboBoxOptionLabelsForTest(box)).ToArray();
    internal bool SettingsWidthFitsForTest(double width)
    {
        if(settingsLayout is null)throw new InvalidOperationException("Layout de configurações não foi criado.");
        settingsLayout.Measure(new Size(width,1200));
        settingsLayout.Arrange(new Rect(0,0,width,1200));
        settingsLayout.UpdateLayout();
        Rect Bounds(StackPanel panel)=>panel.TransformToAncestor(settingsLayout).TransformBounds(new Rect(panel.RenderSize));
        Rect video=Bounds(videoSettingsPanel!);
        Rect controls=Bounds(controlsSettingsPanel!);
        return settingsLayout.ActualWidth<=width+0.5&&video.Left>=-0.5&&video.Right<=width+0.5&&
            controls.Left>=-0.5&&controls.Right<=width+0.5;
    }
    internal string FrameRateStatusForTest=>frameRateStatus.Text;
    internal int SelectedFrameRateForTest=>frameRate.SelectedValue is int selected?selected:0;
    internal bool PlayEnabledForTest=>play.IsEnabled;
    internal bool OperationBusyForTest=>cancellation is not null;
    internal double OperationProgressForTest=>bar.Value;
    internal bool ProgressBarVisibleForTest=>bar.Visibility==Visibility.Visible;
    internal void SuppressErrorDialogsForTest()=>suppressErrorDialogsForTest=true;
    internal Task<bool> WorkForTest(Func<IProgress<Transfer>,CancellationToken,Task> action)=>Work("Teste sintético de interface",action);
    internal string SetDestinationForTest(string path){destination.Text=path;return state.Text;}
    internal string GetPlayDisabledReasonForTest()=>playReason.Text;
    internal string SelectedRuntimeProfileForTest=>runtimeProfile.Text;
    internal void ArrangeLayoutForTest(double width){ArrangeHeader(width);ArrangeSettings(width);}
    internal (bool Header,bool Settings) CompactLayoutForTest=>(compactHeader,compactSettings);
    internal bool TabsUseVerticalScrollingOnlyForTest()=>tabs.Items.OfType<TabItem>().Count()==3&&tabs.Items.OfType<TabItem>().All(item=>item.Content is ScrollViewer viewer&&viewer.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&viewer.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled);
    async Task<bool> Work(string operation,Func<IProgress<Transfer>,CancellationToken,Task> action)
    {
        if(cancellation!=null)return false;
        OperationTrace trace;
        try{trace=OperationTrace.Start(operation);}
        catch(Exception e){Error(e);return false;}
        using(trace)
        {
        cancellation=new();var token=cancellation.Token;bar.Value=0;bar.Visibility=Visibility.Collapsed;play.IsEnabled=import.IsEnabled=false;playReason.Text="Aguarde a conclusão da operação em andamento.";cancel.IsEnabled=true;
        operationStage.Text="Etapa: iniciando";
        var elapsed=Stopwatch.StartNew();
        Transfer? latestProgress=null;
        var progressLock=new object();
        void ApplyProgress()
        {
            if(elapsed.ElapsedMilliseconds>=1000&&cancellation!=null)bar.Visibility=Visibility.Visible;
            Transfer? p;
            lock(progressLock){p=latestProgress;latestProgress=null;}
            if(p is null)return;
            double stageProgress=p.Total>0?100.0*p.Done/p.Total:0;
            bar.Value=Math.Min(99,stageProgress);
            operationStage.Text="Etapa atual: "+p.Phase+" — 100% somente após a finalização.";
            detail.Text=string.IsNullOrEmpty(p.File)?p.Phase:p.File;
            string remaining=elapsed.Elapsed.TotalSeconds>=3&&p.BytesRead>=64L*1024*1024&&stageProgress is >5 and <99
                ?$" · restante estimado: {TimeSpan.FromSeconds(elapsed.Elapsed.TotalSeconds*(100-stageProgress)/stageProgress):hh\\:mm\\:ss}"
                :" · estimativa indisponível";
            inventory.Text=$"{p.Done:N0} / {p.Total:N0} bytes processados · {p.Files:N0}/{p.Count:N0} arquivos · "+
                $"{trace.ContentBytesRead:N0} bytes lidos via API · {trace.ContentBytesHashed:N0} bytes SHA-256 · "+
                $"{trace.ContentBytesReused:N0} bytes reutilizados · {trace.ContentBytesWritten:N0} bytes gravados · tempo {elapsed.Elapsed:hh\\:mm\\:ss}{remaining}";
        }
        var progressTimer=new System.Windows.Threading.DispatcherTimer
        {
            Interval=TimeSpan.FromMilliseconds(200)
        };
        progressTimer.Tick+=(_,_)=>ApplyProgress();
        progressTimer.Start();
        using(trace.Activate())
        using(var uiStart=OperationTrace.Measure("Atualização inicial da interface",0,0))
            uiStart?.Complete();
        var progress=new InlineProgress<Transfer>(p=>
        {
            lock(progressLock)latestProgress=p;
        });
        bool completed=false;
        try
        {
            await Task.Run(async()=>
            {
                using(trace.Activate())await action(progress,token);
            });
            completed=true;
        }
        catch(OperationCanceledException e)
        {
            trace.Fail(e);
            bar.Value=0;
            operationStage.Text="Operação cancelada; nenhum manifesto incompleto foi marcado como concluído.";
            detail.Text="Cancelado com segurança. Retome usando a mesma origem e o mesmo destino.";
        }
        catch(Exception e)
        {
            trace.Fail(e);
            bar.Value=Math.Min(bar.Value,99);
            operationStage.Text="A operação falhou antes da conclusão.";
            Error(e);
        }
        finally
        {
            progressTimer.Stop();
            ApplyProgress();
            elapsed.Stop();
            using(trace.Activate())
            using(var uiFinish=OperationTrace.Measure("Atualização final da interface e liberação de JOGAR",0,0))
            {
                cancellation.Dispose();cancellation=null;
                bar.Visibility=Height<500?Visibility.Collapsed:Visibility.Visible;import.IsEnabled=true;cancel.IsEnabled=false;Refresh();
                if(completed)
                {
                    bar.Value=100;
                    bar.Visibility=Visibility.Collapsed;
                    operationStage.Text="PRONTO — validação concluída; requisitos satisfeitos.";
                    detail.Text="Operação concluída e verificada.";
                }
                uiFinish?.Complete();
            }
            if(completed)trace.Complete();
        }
        return completed;
        }
    }
}

public record Candidate(string Path,long Size,string Status){public override string ToString()=>$"{System.IO.Path.GetFileName(Path)} · {Size/1048576.0:N1} MiB · {Path} · {Status}";}
public record FrameRateOption(int Value,string Label,bool Available);
sealed class InlineProgress<T>(Action<T> report):IProgress<T>
{
    public void Report(T value)=>report(value);
}
public static class Discovery
{
    public static IEnumerable<Candidate> Common()
    {
        var roots=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)};
        roots.AddRange(DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>d.RootDirectory.FullName));
        foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!Directory.Exists(root))continue;
            if(Directory.Exists(Path.Combine(root,"PS3_GAME")))yield return new(Path.Combine(root,"PS3_GAME"),0,"Pasta possível; edição será confirmada");
            string[] images;try{images=Directory.EnumerateFiles(root,"*.iso",SearchOption.TopDirectoryOnly).Take(100).ToArray();}catch{continue;}
            foreach(string image in images)yield return new(image,new FileInfo(image).Length,"Imagem possível; edição será confirmada");
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
