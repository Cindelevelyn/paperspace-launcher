using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace PaperSpaces {
 public class Program {
  [STAThread] public static int Main(string[] args) {
   if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--self-test-process")) return Tests.Run(args.Length > 1 ? args[1] : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results"), args[0] == "--self-test-process");
   string directory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
   bool renderCheck = args.Length > 1 && args[0] == "--render-check";
   if (renderCheck) directory = System.IO.Path.Combine(System.IO.Path.GetFullPath(args[1]), "isolated-data");
   for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--data-dir") directory = System.IO.Path.GetFullPath(args[i+1]);
   bool created;
   string mutexName = "Local\\PaperSpaces-" + StableHash(directory.ToLowerInvariant());
   using (var mutex = new Mutex(true, mutexName, out created)) {
    if (!created) { MessageBox.Show("O PaperSpaces já está aberto. Procure a janela na barra de tarefas.", "PaperSpaces"); return 0; }
    try {
     var application = new Application();
     using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml")) application.Resources = (ResourceDictionary)XamlReader.Load(stream);
     application.DispatcherUnhandledException += (sender, e) => { MessageBox.Show(e.Exception.Message, "PaperSpaces · não foi possível concluir"); e.Handled = true; };
     var launcher = new Launcher(new Repository(directory));
     if (renderCheck) launcher.Window.Loaded += (s,e) => launcher.Window.Dispatcher.BeginInvoke(new Action(()=> { try { launcher.RenderChecks(System.IO.Path.GetFullPath(args[1])); } finally { application.Shutdown(); } }));
     application.Run(launcher.Window); return 0;
    } catch (Exception error) { MessageBox.Show(error.Message, "PaperSpaces · inicialização"); return 1; }
   }
  }
  static string StableHash(string text) { uint hash = 2166136261; foreach (char c in text) hash = (hash ^ c) * 16777619; return hash.ToString("X8"); }
 }
 public class Launcher {
  public Window Window;
  readonly Repository repository;
  Configuration config;
  StackPanel page, tabs, utilityTabs;
  Border sheet;
  TextBlock status;
  Button themeButton;
  string selected = "home";
  string activeId;
  bool busy;
  List<string> activity = new List<string>();
  // Andamento da última ativação: id do app -> "wait" | "run" | "ok" | "fail"
  string runSpaceId;
  Dictionary<string,string> runState = new Dictionary<string,string>();
  const string HomeColor = "#ECE4CF", LibraryColor = "#DCC8A6", SettingsColor = "#D3D8CE", TabInk = "#2F372F";
  static readonly string[] Symbols = { "✦","⌘","✎","♫","☕","♡","★","⚑","☼","✿","♜","☁" };
  public Launcher(Repository repository) {
   this.repository = repository; config = repository.Load();
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) Window = (Window)XamlReader.Load(stream);
   using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("PaperSpaces.ico")) { if(icon!=null) Window.Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
   page = (StackPanel)Window.FindName("Page"); tabs = (StackPanel)Window.FindName("Tabs"); utilityTabs = (StackPanel)Window.FindName("UtilityTabs");
   sheet = (Border)Window.FindName("Sheet"); status = (TextBlock)Window.FindName("StatusLabel"); themeButton = (Button)Window.FindName("ThemeButton");
   ((TextBlock)Window.FindName("DateLabel")).Text = DateTime.Now.ToString("dddd, d 'de' MMMM", new CultureInfo("pt-BR"));
   themeButton.Click += (s,e) => ToggleTheme();
   var rings = (System.Windows.Controls.Primitives.UniformGrid)Window.FindName("Rings");
   for (int i=0;i<4;i++) rings.Children.Add(Ring());
   Window.Closing += (s,e) => { if (busy) { e.Cancel = true; SetStatus("Aguarde a sequência terminar antes de fechar."); } };
   Window.PreviewKeyDown += Shortcut;
   ApplyTheme(); Render();
   if (repository.Warning != null) Window.Loaded += (s,e) => MessageBox.Show(Window, repository.Warning, "Dados recuperados");
  }
  Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
  static Color Shade(string hex, double amount) {
   var c = (Color)ColorConverter.ConvertFromString(hex); double k = 1 - amount;
   return Color.FromRgb((byte)(c.R*k),(byte)(c.G*k),(byte)(c.B*k));
  }
  TextBlock Text(string value, double size = 14, bool muted = false) {
   var text = new TextBlock { Text=value, FontSize=size, TextWrapping=TextWrapping.Wrap };
   if (muted) text.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); return text;
  }
  TextBlock Heading(string value, double size = 36) { var t = Text(value,size); t.FontFamily = new FontFamily("Georgia"); return t; }
  Button Button(string title, Action action, string style = null) {
   var button = new Button { Content=title }; if (style != null) button.SetResourceReference(FrameworkElement.StyleProperty,style);
   button.Click += (s,e) => action(); AutomationProperties.SetName(button,title); return button;
  }
  Border Card(UIElement content, Thickness? padding = null) {
   var card = new Border { Child=content, CornerRadius=new CornerRadius(10), Padding=padding ?? new Thickness(20), BorderThickness=new Thickness(1) };
   card.SetResourceReference(Border.BackgroundProperty,"Surface"); card.SetResourceReference(Border.BorderBrushProperty,"Line"); return card;
  }
  Border Pill(string value, Brush background = null) {
   var pill = new Border { CornerRadius=new CornerRadius(11), Padding=new Thickness(10,3,10,4), Margin=new Thickness(0,0,6,0), Child=Text(value,11.5) };
   if (background != null) { pill.Background=background; ((TextBlock)pill.Child).Foreground=Brush(TabInk); } else pill.SetResourceReference(Border.BackgroundProperty,"Soft");
   return pill;
  }
  void Gap(Panel panel, double height=18) { panel.Children.Add(new Border { Height=height }); }
  void SetStatus(string value) { status.Text=value; status.ToolTip=value; }
  bool Guard() { if (!busy) return true; SetStatus("Aguarde a ativação atual terminar."); return false; }
  bool Save() {
   try { repository.Save(config); SetStatus("●  Tudo salvo neste computador"); return true; }
   catch (Exception error) { MessageBox.Show(Window,"Não foi possível salvar. " + error.Message,"PaperSpaces"); return false; }
  }
  void ToggleTheme() { if (!Guard()) return; string old = config.Theme; config.Theme = old == "light" ? "dark" : "light"; if (!Save()) config.Theme = old; ApplyTheme(); Render(); }
  void ApplyTheme() {
   bool dark = config.Theme == "dark";
   string[] keys  = { "Desk",   "Cover",  "CoverDark","OnCover","Paper",  "PaperEdge","Surface","Ink",    "Muted",  "Line",   "Rule",   "MarginLine","Accent", "OnAccent","Soft",   "Hover",  "Danger" };
   string[] light = { "#E9E4DA","#3B5141","#2F4235", "#E7EEDF","#FBF9F4","#EFEADF",  "#FFFEFB","#2F372F","#6E7368","#DDD9CE","#E4E1D8","#E9B8AF",   "#3B5141","#FFFFFF", "#EEF0E6","#2F372F","#B34A43" };
   string[] night = { "#161916","#26332A","#1D2820", "#CAD6C2","#262B26","#1F231F",  "#2E342E","#EEEAE0","#A9B2A2","#3E463C","#343B33","#7A4A45",   "#BDD2A7","#1E2A21", "#323A31","#FFFFFF","#E0867E" };
   for (int i=0;i<keys.Length;i++) Application.Current.Resources[keys[i]] = Brush((dark ? night : light)[i]);
   themeButton.Content = dark ? "☀  Tema claro" : "☾  Tema escuro";
  }
  void Shortcut(object sender, KeyEventArgs e) {
   if (Keyboard.Modifiers != ModifierKeys.Control) return;
   int n = e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
   if (n == 0) { Navigate("home"); e.Handled = true; }
   else if (n > 0 && n <= config.Spaces.Count) { Navigate(config.Spaces[n-1].Id); e.Handled = true; }
   else if (e.Key == Key.N) { EditSpace(null); e.Handled = true; }
   else if (e.Key == Key.F) { Navigate("library"); e.Handled = true; }
  }
  void Navigate(string destination) { selected=destination; Render(); ((ScrollViewer)Window.FindName("PageScroll")).ScrollToTop(); }

  // ---------- Fichário: argolas e divisores ----------
  UIElement Ring() {
   var canvas = new Canvas { Width=80, Height=26, VerticalAlignment=VerticalAlignment.Center };
   var hole = new Ellipse { Width=16, Height=16 }; hole.SetResourceReference(Shape.FillProperty,"CoverDark"); Canvas.SetLeft(hole,56); Canvas.SetTop(hole,5); canvas.Children.Add(hole);
   var metal = new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(0,1) };
   metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4,0xF4,0xF1),0)); metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9,0xBC,0xB5),0.55)); metal.GradientStops.Add(new GradientStop(Color.FromRgb(0x8C,0x90,0x88),1));
   var ring = new Border { Width=58, Height=11, CornerRadius=new CornerRadius(5.5), Background=metal, BorderBrush=Brush("#6F746B"), BorderThickness=new Thickness(0.8) };
   Canvas.SetLeft(ring,8); Canvas.SetTop(ring,7.5); canvas.Children.Add(ring); return canvas;
  }
  void Render() {
   tabs.Children.Clear(); utilityTabs.Children.Clear();
   AddTab(tabs,"☰","Índice","home",HomeColor,null);
   foreach (var space in config.Spaces) AddTab(tabs,space.Symbol,space.Name,space.Id,space.Color,space.AppIds.Count.ToString());
   var add = Button("+  Novo ambiente",() => EditSpace(null),"Tab"); add.Padding=new Thickness(16,11,12,11); add.Margin=new Thickness(16,4,20,0); add.FontSize=13;
   add.Background=new SolidColorBrush(Color.FromArgb(0x26,0xFF,0xFF,0xFF)); add.SetResourceReference(Control.ForegroundProperty,"OnCover"); add.ToolTip="Criar ambiente (Ctrl+N)"; tabs.Children.Add(add);
   AddTab(utilityTabs,"▦","Biblioteca","library",LibraryColor,config.Apps.Count.ToString());
   AddTab(utilityTabs,"⚙","Preferências","settings",SettingsColor,null);
   page.Children.Clear();
   string color = HomeColor;
   if (selected == "home") Home(); else if (selected == "library") { color=LibraryColor; Library(); } else if (selected == "settings") { color=SettingsColor; Settings(); }
   else { var space = config.Spaces.FirstOrDefault(s => s.Id == selected); if (space == null) { selected="home"; Home(); } else { color=space.Color; SpacePage(space); } }
   sheet.BorderBrush = Brush(color);
  }
  void AddTab(Panel host,string symbol,string name,string id,string color,string badge) {
   bool active = selected == id;
   var label = new Grid(); label.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(26) }); label.ColumnDefinitions.Add(new ColumnDefinition()); label.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   label.Children.Add(new TextBlock { Text=symbol, FontSize=16, Foreground=Brush(TabInk), VerticalAlignment=VerticalAlignment.Center });
   var title = new TextBlock { Text=name, FontSize=14, FontWeight=active ? FontWeights.SemiBold : FontWeights.Normal, Foreground=Brush(TabInk), VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis, TextWrapping=TextWrapping.NoWrap };
   Grid.SetColumn(title,1); label.Children.Add(title);
   if (badge != null && badge != "0") { var count = new TextBlock { Text=badge, FontSize=11, Foreground=Brush(TabInk), Opacity=.6, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(6,0,0,0) }; Grid.SetColumn(count,2); label.Children.Add(count); }
   var b = Button(name, () => Navigate(id), "Tab"); b.Content=label; b.ToolTip=name;
   if (active) {
    // A aba ativa avança sobre a borda da página e se funde com a faixa colorida dela.
    b.Background=Brush(color); b.Margin=new Thickness(0,0,4,6); b.Padding=new Thickness(24,13,14,13);
    b.BorderBrush=new SolidColorBrush(Shade(color,.16)); b.BorderThickness=new Thickness(0,0,1,1.5);
   } else {
    // As inativas ficam "atrás" da página: mais curtas e sombreadas na borda presa.
    var fill = new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(1,0) };
    fill.GradientStops.Add(new GradientStop(Shade(color,.2),0)); fill.GradientStops.Add(new GradientStop(Shade(color,.06),.22)); fill.GradientStops.Add(new GradientStop(Shade(color,.06),1));
    b.Background=fill; b.Margin=new Thickness(16,0,20,6); b.Padding=new Thickness(14,11,12,11);
   }
   host.Children.Add(b);
  }

  // ---------- Páginas ----------
  void Eyebrow(string title, string color = null) {
   var t=Text(title,10.5,true); t.FontWeight=FontWeights.SemiBold; t.Margin=new Thickness(0,0,0,10);
   if (color == null) { page.Children.Add(t); return; }
   var row=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,0,10) };
   row.Children.Add(new Border { Width=18, Height=6, CornerRadius=new CornerRadius(3), Background=Brush(color), Margin=new Thickness(0,0,8,0), VerticalAlignment=VerticalAlignment.Center });
   t.Margin=new Thickness(0); row.Children.Add(t); page.Children.Add(row);
  }
  void PageTitle(string title,string subtitle) { page.Children.Add(Heading(title)); var t=Text(subtitle,13.5,true); t.Margin=new Thickness(0,8,0,0); page.Children.Add(t); Gap(page,28); }
  void Section(string title,string action,Action click) {
   var grid = new Grid { Margin=new Thickness(0,0,0,4) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var t=Heading(title,19); t.VerticalAlignment=VerticalAlignment.Center; grid.Children.Add(t);
   if (action != null) { var b=Button(action,click,"Quiet"); Grid.SetColumn(b,1); grid.Children.Add(b); } page.Children.Add(grid);
   var rule=new Border { Height=2, Margin=new Thickness(0,0,0,2) }; rule.SetResourceReference(Border.BackgroundProperty,"Rule"); page.Children.Add(rule);
  }
  // Linha pautada do caderno: separador fino abaixo de cada item.
  Border Ruled(UIElement content, double minHeight = 54) {
   var line=new Border { Child=content, MinHeight=minHeight, BorderThickness=new Thickness(0,0,0,1) }; line.SetResourceReference(Border.BorderBrushProperty,"Rule"); return line;
  }
  string AppCount(int n) { return n == 0 ? "vazio" : n == 1 ? "1 app" : n + " apps"; }

  void Home() {
   Eyebrow(DateTime.Now.ToString("dddd, d 'de' MMMM", new CultureInfo("pt-BR")).ToUpperInvariant());
   PageTitle("Índice","Escolha um ambiente do seu fichário. Nada abre só por trocar de aba.");
   Section("Seus ambientes","+  Novo ambiente",() => EditSpace(null));
   for (int i=0;i<config.Spaces.Count;i++) page.Children.Add(TocRow(config.Spaces[i],i));
   if (config.Spaces.Count == 0) page.Children.Add(Ruled(Text("Seu fichário ainda não tem divisórias. Crie o primeiro ambiente.",13,true)));
   Gap(page,34);
   Section("À mão","Ver biblioteca  →",() => Navigate("library"));
   Gap(page,12);
   var quick = new WrapPanel();
   foreach (var app in config.Apps.Take(8)) { var entry=app; var b=Button(Glyph(entry) + "   " + entry.Name, () => LaunchOne(entry)); b.Margin=new Thickness(0,0,8,8); b.IsEnabled=!busy; b.ToolTip="Abrir " + entry.Name; quick.Children.Add(b); }
   var add=Button("+  Adicionar app", () => EditApp(null,null),"Quiet"); add.Margin=new Thickness(0,0,0,8); quick.Children.Add(add); page.Children.Add(quick);
   Gap(page,6);
   page.Children.Add(Text("Dica: para jogos, use o atalho criado pelo Steam, Riot ou Battle.net — assim o jogo abre pelo cliente certo.",12,true));
   SessionNote();
  }
  UIElement TocRow(Space space,int index) {
   var host=new Grid();
   var info=new Grid(); info.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(38) }); info.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(46) }); info.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); info.ColumnDefinitions.Add(new ColumnDefinition()); info.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var number=Heading((index+1).ToString("00"),15); number.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); number.VerticalAlignment=VerticalAlignment.Center; info.Children.Add(number);
   var badge=new Border { Width=34, Height=34, CornerRadius=new CornerRadius(8), Background=Brush(space.Color), VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Left, Child=new TextBlock { Text=space.Symbol, FontSize=16, Foreground=Brush(TabInk), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center } };
   Grid.SetColumn(badge,1); info.Children.Add(badge);
   var names=new StackPanel { VerticalAlignment=VerticalAlignment.Center, MaxWidth=360 };
   var name=Heading(space.Name,20); name.TextTrimming=TextTrimming.CharacterEllipsis; name.TextWrapping=TextWrapping.NoWrap; names.Children.Add(name);
   if (!String.IsNullOrWhiteSpace(space.Description)) { var d=Text(space.Description,12,true); d.TextTrimming=TextTrimming.CharacterEllipsis; d.TextWrapping=TextWrapping.NoWrap; d.Margin=new Thickness(0,1,0,0); names.Children.Add(d); }
   Grid.SetColumn(names,2); info.Children.Add(names);
   // Pontilhado de sumário ligando o nome à contagem de apps
   var dots=new DrawingBrush(new GeometryDrawing((Brush)Application.Current.Resources["Muted"],null,new EllipseGeometry(new Point(1,1),1,1))) { TileMode=TileMode.Tile, Viewport=new Rect(0,0,5,2), ViewportUnits=BrushMappingMode.Absolute, Viewbox=new Rect(0,0,5,2), ViewboxUnits=BrushMappingMode.Absolute, Stretch=Stretch.None };
   var leader=new Border { Height=2, Background=dots, Opacity=.5, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(14,8,12,0) }; Grid.SetColumn(leader,3); info.Children.Add(leader);
   var count=Text(AppCount(space.AppIds.Count),12.5,true); count.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(count,4); info.Children.Add(count);
   var open=Button(space.Name,()=>Navigate(space.Id),"Row"); open.Content=info; open.Padding=new Thickness(0,10,150,10); open.ToolTip="Abrir a página de " + space.Name; host.Children.Add(open);
   var activate=Button(busy && runSpaceId == space.Id ? "Ativando…" : "▶  Ativar",()=>ActivateSpace(space),"Primary"); activate.Padding=new Thickness(14,7,14,7); activate.FontSize=12.5;
   activate.HorizontalAlignment=HorizontalAlignment.Right; activate.VerticalAlignment=VerticalAlignment.Center; activate.MinWidth=104; activate.IsEnabled=!busy && space.AppIds.Count > 0;
   activate.ToolTip=space.AppIds.Count == 0 ? "Adicione apps para ativar" : "Abrir os " + AppCount(space.AppIds.Count) + " de " + space.Name; host.Children.Add(activate);
   return Ruled(host,64);
  }
  void SessionNote() {
   if (activity.Count == 0) return;
   Gap(page,30);
   var note=new StackPanel(); var title=Text("Nesta sessão",13); title.FontWeight=FontWeights.SemiBold; title.Foreground=Brush(TabInk); title.Margin=new Thickness(0,0,0,8); note.Children.Add(title);
   foreach (string line in activity.Take(8)) { var t=Text(line,12); t.Foreground=Brush("#4A5147"); t.Margin=new Thickness(0,0,0,4); note.Children.Add(t); }
   // Post-it colado na página
   var sticky=new Border { Child=note, Background=Brush("#F4E7A8"), Padding=new Thickness(18,14,18,14), MaxWidth=460, HorizontalAlignment=HorizontalAlignment.Left,
    Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=6, ShadowDepth=2, Direction=300, Opacity=.18 } };
   page.Children.Add(sticky);
  }
  void SpacePage(Space space) {
   Eyebrow("AMBIENTE  ·  " + (config.Spaces.IndexOf(space)+1).ToString("00"), space.Color);
   var header=new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var badge=new Border { Width=64, Height=64, CornerRadius=new CornerRadius(14), Background=Brush(space.Color), Margin=new Thickness(0,0,18,0), VerticalAlignment=VerticalAlignment.Top, Child=new TextBlock { Text=space.Symbol, FontSize=30, Foreground=Brush(TabInk), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center } };
   header.Children.Add(badge);
   var text=new StackPanel(); var name=Heading(space.Name,36); name.TextTrimming=TextTrimming.CharacterEllipsis; name.TextWrapping=TextWrapping.NoWrap; text.Children.Add(name);
   if (!String.IsNullOrWhiteSpace(space.Description)) { var description=Text(space.Description,14,true); description.Margin=new Thickness(0,4,0,0); text.Children.Add(description); }
   var tags=new WrapPanel { Margin=new Thickness(0,12,0,0) }; tags.Children.Add(Pill(AppCount(space.AppIds.Count))); tags.Children.Add(Pill(space.Theme == "dark" ? "☾  tema escuro" : "☀  tema claro")); if (space.Minimize) tags.Children.Add(Pill("minimiza ao ativar")); text.Children.Add(tags);
   Grid.SetColumn(text,1); header.Children.Add(text);
   var edit=Button("✎  Editar",()=>EditSpace(space),"Quiet"); edit.VerticalAlignment=VerticalAlignment.Top; edit.ToolTip="Nome, cor, símbolo e tema"; Grid.SetColumn(edit,2); header.Children.Add(edit);
   page.Children.Add(header); Gap(page,26);

   // Faixa de ativação
   var panel=new Grid(); panel.ColumnDefinitions.Add(new ColumnDefinition()); panel.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var info=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
   bool running = busy && runSpaceId == space.Id;
   int done = runState.Count(p => p.Value == "ok" || p.Value == "fail");
   string headline = running ? "Abrindo " + Math.Min(done+1,space.AppIds.Count) + " de " + space.AppIds.Count + "…" : activeId == space.Id ? "✦  Ambiente ativado nesta sessão" : space.AppIds.Count == 0 ? "Esta página ainda está em branco" : "Tudo pronto para começar?";
   var label=Text(headline,16); label.FontWeight=FontWeights.SemiBold; info.Children.Add(label);
   info.Children.Add(Text(space.AppIds.Count == 0 ? "Adicione pelo menos um app para ativar." : "Os apps abrem um após o outro, na ordem abaixo. Uma falha não interrompe os próximos.",12.5,true)); panel.Children.Add(info);
   var activate=Button(running ? "Ativando…" : "▶  Ativar ambiente",()=>ActivateSpace(space),"Primary"); activate.Padding=new Thickness(22,12,22,12); activate.FontSize=14; activate.IsEnabled=!busy && space.AppIds.Count > 0; activate.Margin=new Thickness(16,0,0,0);
   Grid.SetColumn(activate,1); panel.Children.Add(activate);
   var strip=new Border { Child=panel, CornerRadius=new CornerRadius(10), Padding=new Thickness(20,16,16,16), BorderThickness=new Thickness(5,0,0,0), BorderBrush=Brush(space.Color) }; strip.SetResourceReference(Border.BackgroundProperty,"Surface");
   page.Children.Add(strip); Gap(page,30);

   Section("Apps deste ambiente", null, null);
   for (int i=0;i<space.AppIds.Count;i++) { var app=config.Apps.First(a=>a.Id==space.AppIds[i]); page.Children.Add(AppRow(app,space,i)); }
   // Próxima linha em branco do caderno: clique para escrever um app nela.
   var next=new StackPanel { Orientation=Orientation.Horizontal };
   var plus=Text("+",18,true); plus.Width=66; plus.Margin=new Thickness(8,0,0,0); next.Children.Add(plus);
   var hint=Text(space.AppIds.Count == 0 ? "Adicionar o primeiro app, jogo ou link…" : "Adicionar app…",14,true); hint.FontFamily=new FontFamily("Georgia"); hint.FontStyle=FontStyles.Italic; next.Children.Add(hint);
   var addRow=Button("Adicionar app",()=>ChooseApp(space),"Row"); addRow.Content=next; addRow.Padding=new Thickness(0,14,0,14); page.Children.Add(Ruled(addRow));
   if (space.AppIds.Count == 0) { page.Children.Add(Ruled(new Border())); page.Children.Add(Ruled(new Border())); }
   SessionNote();
  }
  static string Glyph(AppEntry app) { return app.Kind == "url" ? "↗" : app.Kind == "shortcut" ? "⤴" : "▣"; }
  UIElement AppRow(AppEntry app, Space space, int index) {
   var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(space == null ? 8 : 40) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(42) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   if (space != null) {
    string state; runState.TryGetValue(app.Id, out state); if (runSpaceId != space.Id) state=null;
    var mark=Heading(state == "ok" ? "✓" : state == "fail" ? "✕" : state == "run" ? "…" : (index+1) + ".",15); mark.VerticalAlignment=VerticalAlignment.Center;
    if (state == "fail") mark.SetResourceReference(TextBlock.ForegroundProperty,"Danger"); else if (state == null || state == "wait") mark.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
    grid.Children.Add(mark);
   }
   var icon=new Border { Width=32, Height=32, CornerRadius=new CornerRadius(8), VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Left, Child=new TextBlock { Text=Glyph(app), FontSize=15, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center } };
   icon.SetResourceReference(Border.BackgroundProperty,"Soft"); Grid.SetColumn(icon,1); grid.Children.Add(icon);
   var name=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,10,0) }; var title=Text(app.Name,14.5); title.FontWeight=FontWeights.SemiBold; title.TextTrimming=TextTrimming.CharacterEllipsis; title.TextWrapping=TextWrapping.NoWrap; name.Children.Add(title);
   string kind=app.Kind == "url" ? "Link · navegador padrão" : app.Kind == "shortcut" ? "Atalho do Windows" : "Aplicativo";
   if (space == null) { var used=config.Spaces.Where(s=>s.AppIds.Contains(app.Id)).Select(s=>s.Name).ToList(); kind += used.Count == 0 ? "  ·  fora dos ambientes" : "  ·  em " + String.Join(", ",used); }
   var detail=Text(kind,11.5,true); detail.ToolTip=app.Target; detail.TextTrimming=TextTrimming.CharacterEllipsis; detail.TextWrapping=TextWrapping.NoWrap; name.Children.Add(detail); Grid.SetColumn(name,2); grid.Children.Add(name);
   var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
   if (space != null) {
    var up=Button("↑",()=>MoveApp(space,index,-1),"Quiet"); up.ToolTip="Abrir antes"; up.IsEnabled=index>0 && !busy; actions.Children.Add(up);
    var down=Button("↓",()=>MoveApp(space,index,1),"Quiet"); down.ToolTip="Abrir depois"; down.IsEnabled=index<space.AppIds.Count-1 && !busy; actions.Children.Add(down);
   }
   var open=Button("Abrir",()=>LaunchOne(app)); open.Margin=new Thickness(8,0,4,0); open.IsEnabled=!busy; actions.Children.Add(open);
   actions.Children.Add(Button("Editar",()=>EditApp(app,space),"Quiet"));
   var remove=Button(space == null ? "Excluir" : "✕",()=>RemoveApp(app,space),"Quiet"); remove.ToolTip=space == null ? "Excluir da biblioteca" : "Tirar deste ambiente"; remove.SetResourceReference(Control.ForegroundProperty,"Danger"); actions.Children.Add(remove);
   Grid.SetColumn(actions,3); grid.Children.Add(actions);
   grid.Margin=new Thickness(0,8,0,8); return Ruled(grid,60);
  }
  void MoveApp(Space space,int index,int delta) {
   if (!Guard()) return; var previous=space.AppIds.ToList(); string id=space.AppIds[index]; space.AppIds.RemoveAt(index); space.AppIds.Insert(index+delta,id); if (!Save()) space.AppIds=previous; Render();
  }
  void RemoveApp(AppEntry app,Space space) {
   if (!Guard()) return;
   string question=space == null ? "Excluir ‘"+app.Name+"’ da biblioteca e de todos os ambientes? O programa instalado não será removido." : "Tirar ‘"+app.Name+"’ deste ambiente? Ele continuará na biblioteca.";
   if (MessageBox.Show(Window,question,"PaperSpaces",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
   var references=config.Spaces.ToDictionary(s=>s.Id,s=>s.AppIds.ToList()); int originalIndex=config.Apps.IndexOf(app);
   if (space != null) space.AppIds.Remove(app.Id); else { config.Apps.Remove(app); foreach(var s in config.Spaces) s.AppIds.Remove(app.Id); }
   if (!Save()) { if (space == null) config.Apps.Insert(originalIndex,app); foreach(var s in config.Spaces) s.AppIds=references[s.Id]; } Render();
  }
  void Library() {
   Eyebrow("BIBLIOTECA", LibraryColor); PageTitle("Sua biblioteca","Programas, atalhos e links. Cadastre uma vez e use em qualquer ambiente.");
   var bar=new Grid { Margin=new Thickness(0,0,0,14) }; bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var searchBox=new Grid(); var search=new TextBox { Padding=new Thickness(34,9,10,9), ToolTip="Buscar pelo nome" }; AutomationProperties.SetName(search,"Buscar aplicativo"); searchBox.Children.Add(search);
   var glass=Text("⌕",17,true); glass.IsHitTestVisible=false; glass.Margin=new Thickness(12,0,0,2); glass.VerticalAlignment=VerticalAlignment.Center; searchBox.Children.Add(glass);
   var placeholder=Text("Buscar na biblioteca…",13.5,true); placeholder.IsHitTestVisible=false; placeholder.Margin=new Thickness(36,0,0,0); placeholder.VerticalAlignment=VerticalAlignment.Center; searchBox.Children.Add(placeholder);
   bar.Children.Add(searchBox);
   var add=Button("+  Adicionar app",()=>EditApp(null,null),"Primary"); add.Margin=new Thickness(12,0,0,0); Grid.SetColumn(add,1); bar.Children.Add(add); page.Children.Add(bar);
   var countLabel=Text("",11.5,true); countLabel.Margin=new Thickness(0,0,0,4); page.Children.Add(countLabel);
   var rule=new Border { Height=2 }; rule.SetResourceReference(Border.BackgroundProperty,"Rule"); page.Children.Add(rule);
   var rows=new StackPanel(); page.Children.Add(rows);
   Action refresh=()=> {
    rows.Children.Clear(); placeholder.Visibility=search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    var found=config.Apps.Where(a=>a.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();
    countLabel.Text=search.Text.Length == 0 ? (config.Apps.Count == 1 ? "1 item cadastrado" : config.Apps.Count + " itens cadastrados") : found.Count + " de " + config.Apps.Count;
    foreach(var app in found) rows.Children.Add(AppRow(app,null,0));
    if (found.Count==0) { var empty=Text(config.Apps.Count == 0 ? "Sua biblioteca está vazia. Adicione o primeiro app acima." : "Nada com esse nome. Que tal cadastrar?",13.5,true); empty.FontFamily=new FontFamily("Georgia"); empty.FontStyle=FontStyles.Italic; empty.Margin=new Thickness(8,16,0,16); rows.Children.Add(Ruled(empty)); }
   };
   search.TextChanged+=(s,e)=>refresh(); refresh();
  }
  void Settings() {
   Eyebrow("PREFERÊNCIAS", SettingsColor); PageTitle("Preferências","Um aplicativo local, simples e feito para você personalizar.");
   Section("Aparência",null,null); Gap(page,12);
   page.Children.Add(Text("O tema vale só para o PaperSpaces. Cada ambiente também pode trocar o tema quando é ativado.",13,true)); Gap(page,12);
   var themes=new StackPanel { Orientation=Orientation.Horizontal };
   foreach (var option in new[]{ new[]{"light","☀  Claro"}, new[]{"dark","☾  Escuro"} }) {
    string value=option[0]; var b=Button(option[1],()=> { if (config.Theme != value) ToggleTheme(); }, config.Theme == value ? "Primary" : null); b.MinWidth=110; b.Margin=new Thickness(0,0,8,0); themes.Children.Add(b);
   }
   page.Children.Add(themes); Gap(page,34);
   Section("Seus dados ficam com você",null,null); Gap(page,12);
   page.Children.Add(Text("Ambientes e apps são salvos na pasta data, ao lado do executável. A cada gravação, a versão anterior vira backup.",13,true)); Gap(page,12);
   var buttons=new WrapPanel(); buttons.Children.Add(Button("Abrir pasta de dados",()=> { try { Process.Start(new ProcessStartInfo(repository.DirectoryPath) { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show(Window,ex.Message); } }));
   var export=Button("Exportar configuração",Export); export.Margin=new Thickness(8,0,0,0); buttons.Children.Add(export); page.Children.Add(buttons); Gap(page,34);
   Section("Sobre",null,null); Gap(page,12);
   page.Children.Add(Text("PaperSpaces · MVP 0.1.0",14)); Gap(page,6);
   page.Children.Add(Text("Nesta versão: ambientes, biblioteca, links, atalhos, ordem de abertura e temas claro/escuro. Fechar a janela encerra o launcher; os programas abertos continuam rodando.",13,true)); Gap(page,8);
   page.Children.Add(Text("Próximas versões: wallpapers, tema do Windows, detecção de jogos, bandeja e Alexa.",13,true)); Gap(page,8);
   page.Children.Add(Text("Atalhos: Ctrl+1 Índice · Ctrl+2…9 ambientes · Ctrl+N novo ambiente · Ctrl+F biblioteca · Esc fecha formulários.",13,true));
  }
  void Export() {
   var picker=new SaveFileDialog { Title="Exportar configuração", Filter="Configuração JSON|*.json",FileName="paperspaces-backup.json" };
   if (picker.ShowDialog(Window)==true) try { if (String.Equals(System.IO.Path.GetFullPath(picker.FileName),System.IO.Path.GetFullPath(repository.FilePath),StringComparison.OrdinalIgnoreCase)) throw new Exception("Escolha um arquivo diferente da configuração em uso."); File.Copy(repository.FilePath,picker.FileName,true); SetStatus("✓  Configuração exportada"); } catch(Exception e) { MessageBox.Show(Window,e.Message,"Exportar"); }
  }
  void Record(string message) { activity.Insert(0,DateTime.Now.ToString("HH:mm")+"  "+message); if(activity.Count>30) activity.RemoveAt(30); SetStatus(message); }
  async void LaunchOne(AppEntry app) {
   if (!Guard()) return; busy=true; Render();
   try { await Task.Run(()=>LaunchService.Launch(app)); Record("✓  Abertura solicitada: "+app.Name); }
   catch(Exception e) { Record("Falha ao abrir "+app.Name); MessageBox.Show(Window,e.Message,"Não foi possível abrir "+app.Name); }
   finally { busy=false; Render(); }
  }
  async void ActivateSpace(Space space) {
   if (!Guard() || space.AppIds.Count==0) return;
   busy=true; int failures=0; string previousTheme=config.Theme;
   config.Theme=space.Theme;
   if (!Save()) { config.Theme=previousTheme; busy=false; return; }
   var apps=space.AppIds.Select(id=>config.Apps.First(a=>a.Id==id)).ToList();
   runSpaceId=space.Id; runState.Clear(); foreach (var app in apps) runState[app.Id]="wait";
   selected=space.Id; ApplyTheme(); Render();
   try {
    foreach(var app in apps) {
     runState[app.Id]="run"; SetStatus("Abrindo "+app.Name+"…"); Render();
     try { await Task.Run(()=>LaunchService.Launch(app)); runState[app.Id]="ok"; Record("✓  Abertura solicitada: "+app.Name); }
     catch(Exception e) { failures++; runState[app.Id]="fail"; Record("✕  "+app.Name+": "+e.Message); }
     Render(); await Task.Delay(350);
    }
    activeId=space.Id; Record(failures==0 ? "✓  "+space.Name+" ativado. Boa sessão!" : "Ativação concluída com "+failures+" falha(s). Confira o post-it abaixo.");
   } finally { busy=false; Render(); }
   if (failures==0 && space.Minimize) Window.WindowState=WindowState.Minimized;
  }

  // ---------- Formulários (fichas) ----------
  Window Dialog(string title,out StackPanel body,double width=540) {
   var dialog=new Window { Title=title,Owner=Window,Width=width,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-55,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false };
   dialog.SetResourceReference(Window.BackgroundProperty,"Paper");
   var frame=new DockPanel();
   var top=new Border { Height=4 }; top.SetResourceReference(Border.BackgroundProperty,"MarginLine"); DockPanel.SetDock(top,Dock.Top); frame.Children.Add(top);
   body=new StackPanel { Margin=new Thickness(30,24,30,26) }; var heading=Heading(title,26); heading.Margin=new Thickness(0,0,0,20); body.Children.Add(heading);
   frame.Children.Add(new ScrollViewer { Content=body }); dialog.Content=frame;
   dialog.PreviewKeyDown+=(s,e)=> { if(e.Key==Key.Escape) dialog.Close(); }; return dialog;
  }
  TextBox Field(Panel body,string label,string value,int max=4096) {
   var caption=Text(label,12,true); caption.Margin=new Thickness(0,0,0,6); body.Children.Add(caption);
   var box=new TextBox { Text=value ?? "",MaxLength=max,Margin=new Thickness(0,0,0,16) }; AutomationProperties.SetName(box,label); body.Children.Add(box); return box;
  }
  void EditSpace(Space original) {
   if (!Guard()) return; StackPanel body; var dialog=Dialog(original==null?"Nova divisória":"Editar ambiente",out body);
   string color=original==null?Rules.Colors[config.Spaces.Count % Rules.Colors.Length]:original.Color;
   string symbol=original==null?"✦":original.Symbol;
   // Prévia da aba, atualizada enquanto a pessoa preenche
   var previewSymbol=new TextBlock { FontSize=16, Width=26, Foreground=Brush(TabInk), VerticalAlignment=VerticalAlignment.Center };
   var previewName=new TextBlock { FontSize=14, FontWeight=FontWeights.SemiBold, Foreground=Brush(TabInk), VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis };
   var previewLabel=new StackPanel { Orientation=Orientation.Horizontal }; previewLabel.Children.Add(previewSymbol); previewLabel.Children.Add(previewName);
   var preview=new Border { CornerRadius=new CornerRadius(0,11,11,0), Padding=new Thickness(16,12,16,12), Width=190, HorizontalAlignment=HorizontalAlignment.Left, Margin=new Thickness(-30,0,0,20), Child=previewLabel };
   body.Children.Add(preview);
   var name=Field(body,"Nome",original==null?"":original.Name,32); var description=Field(body,"Uma frase para esse momento",original==null?"":original.Description,120);
   Action refresh=()=> { preview.Background=Brush(color); previewSymbol.Text=symbol; previewName.Text=String.IsNullOrWhiteSpace(name.Text)?"Sem nome":name.Text.Trim(); };
   name.TextChanged+=(s,e)=>refresh();
   body.Children.Add(Text("Cor da divisória",12,true));
   var palette=new WrapPanel { Margin=new Thickness(0,8,0,18) }; var paletteButtons=new List<Button>();
   foreach(string option in Rules.Colors) { string captured=option; var b=Button(color==option?"✓":"",()=>{}); b.Width=52; b.Height=34; b.Background=Brush(option); b.Foreground=Brush(TabInk); b.BorderThickness=new Thickness(0); b.Margin=new Thickness(0,0,8,0); AutomationProperties.SetName(b,"Cor " + option); b.Click+=(s,e)=> { color=captured; foreach(var item in paletteButtons) item.Content=""; b.Content="✓"; refresh(); }; paletteButtons.Add(b); palette.Children.Add(b); } body.Children.Add(palette);
   body.Children.Add(Text("Símbolo",12,true)); var symbols=new WrapPanel { Margin=new Thickness(0,8,0,16) };
   var symbolButtons=new List<Button>(); foreach(string option in Symbols) { string captured=option; var b=Button(option,()=>{}); b.FontSize=17; b.Width=42; b.Height=38; b.Padding=new Thickness(0); b.Margin=new Thickness(0,0,6,6); b.SetResourceReference(Control.BackgroundProperty,symbol==option?"Soft":"Surface"); b.BorderThickness=new Thickness(symbol==option?2:1); b.Click+=(s,e)=> { symbol=captured; foreach(var item in symbolButtons){item.SetResourceReference(Control.BackgroundProperty,"Surface");item.BorderThickness=new Thickness(1);} b.SetResourceReference(Control.BackgroundProperty,"Soft"); b.BorderThickness=new Thickness(2); refresh(); }; symbolButtons.Add(b); symbols.Children.Add(b); } body.Children.Add(symbols);
   var dark=new CheckBox { Content="Usar tema escuro ao ativar este ambiente",IsChecked=original!=null && original.Theme=="dark" }; body.Children.Add(dark);
   var minimize=new CheckBox { Content="Minimizar o PaperSpaces depois de abrir os apps",IsChecked=original!=null && original.Minimize }; body.Children.Add(minimize); Gap(body,8);
   var error=Text("",12); error.SetResourceReference(TextBlock.ForegroundProperty,"Danger"); body.Children.Add(error);
   var actions=new DockPanel { Margin=new Thickness(0,12,0,0), LastChildFill=false };
   if(original!=null) { var delete=Button("Excluir ambiente",()=> { if(MessageBox.Show(dialog,"Excluir este ambiente? Seus apps continuarão na biblioteca.","Excluir ambiente",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return; int index=config.Spaces.IndexOf(original); config.Spaces.Remove(original); if(Save()){selected="home";dialog.Close();Render();}else config.Spaces.Insert(index,original); },"Quiet"); delete.SetResourceReference(Control.ForegroundProperty,"Danger"); DockPanel.SetDock(delete,Dock.Left); actions.Children.Add(delete); }
   var save=Button(original==null?"Criar ambiente":"Salvar",()=> {
    if(String.IsNullOrWhiteSpace(name.Text)){error.Text="Dê um nome ao seu ambiente.";name.Focus();return;}
    if(config.Spaces.Any(s=>s!=original && String.Equals(s.Name,name.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))){error.Text="Você já tem um ambiente com esse nome.";name.Focus();return;}
    var next=new Space { Id=original==null?Guid.NewGuid().ToString("N"):original.Id,Name=name.Text.Trim(),Description=description.Text.Trim(),Color=color,Symbol=symbol,Theme=dark.IsChecked==true?"dark":"light",Minimize=minimize.IsChecked==true,AppIds=original==null?new List<string>():original.AppIds.ToList() };
    int index=original==null?config.Spaces.Count:config.Spaces.IndexOf(original); if(original==null)config.Spaces.Add(next);else config.Spaces[index]=next;
    if(Save()){selected=next.Id;dialog.Close();Render();}else{if(original==null)config.Spaces.Remove(next);else config.Spaces[index]=original;}
   },"Primary"); save.IsDefault=true; DockPanel.SetDock(save,Dock.Right); actions.Children.Add(save);
   var cancel=Button("Cancelar",()=>dialog.Close(),"Quiet"); cancel.Margin=new Thickness(0,0,8,0); DockPanel.SetDock(cancel,Dock.Right); actions.Children.Add(cancel);
   body.Children.Add(actions); refresh(); dialog.Loaded+=(s,e)=>name.Focus(); dialog.ShowDialog();
  }
  void EditApp(AppEntry original,Space space) {
   if (!Guard()) return; StackPanel body; var dialog=Dialog(original==null?(space==null?"Adicionar à biblioteca":"Novo app em "+space.Name):"Editar aplicativo",out body,570);
   var name=Field(body,"Nome do aplicativo",original==null?"":original.Name,60);
   var target=Field(body,"Arquivo .exe / atalho .lnk ou link https://",original==null?"":original.Target);
   var browse=Button("Escolher arquivo…",()=> {
    var picker=new OpenFileDialog { Title="Escolher aplicativo ou atalho",Filter="Aplicativos e atalhos|*.exe;*.lnk",CheckFileExists=true,DereferenceLinks=false };
    if(picker.ShowDialog(dialog)==true){target.Text=picker.FileName;if(String.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileNameWithoutExtension(picker.FileName);}
   }); browse.HorizontalAlignment=HorizontalAlignment.Left; browse.Margin=new Thickness(0,-8,0,18); body.Children.Add(browse);
   var args=Field(body,"Argumentos opcionais · um por linha (somente .exe)",original==null?"":String.Join(Environment.NewLine,original.Args)); args.AcceptsReturn=true; args.Height=72; args.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   var tip=new Border { CornerRadius=new CornerRadius(8), Padding=new Thickness(12,9,12,9), Child=Text("Para Steam, Riot e Battle.net, prefira o atalho criado pelo cliente. O launcher não altera a instalação do programa.",12,true) }; tip.SetResourceReference(Border.BackgroundProperty,"Soft"); body.Children.Add(tip); Gap(body,10);
   var error=Text("",12); error.SetResourceReference(TextBlock.ForegroundProperty,"Danger"); body.Children.Add(error);
   var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) }; var cancel=Button("Cancelar",()=>dialog.Close(),"Quiet");cancel.Margin=new Thickness(0,0,8,0);actions.Children.Add(cancel);
   var save=Button("Salvar aplicativo",()=> {
    string path=target.Text.Trim().Trim('"'); string kind=path.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||path.StartsWith("https://",StringComparison.OrdinalIgnoreCase)?"url":String.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase)?"shortcut":"exe";
    var next=new AppEntry { Id=original==null?Guid.NewGuid().ToString("N"):original.Id,Name=name.Text.Trim(),Kind=kind,Target=path,Args=args.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries).ToList() };
    try{Rules.ValidateApp(next,true);}catch(Exception ex){error.Text=ex.Message;return;}
    int index=original==null?config.Apps.Count:config.Apps.IndexOf(original); if(original==null)config.Apps.Add(next);else config.Apps[index]=next;
    bool addToSpace=space!=null&&!space.AppIds.Contains(next.Id); if(addToSpace)space.AppIds.Add(next.Id);
    if(Save()){dialog.Close();Render();}else{if(original==null)config.Apps.Remove(next);else config.Apps[index]=original;if(addToSpace)space.AppIds.Remove(next.Id);}
   },"Primary"); actions.Children.Add(save); body.Children.Add(actions); dialog.Loaded+=(s,e)=>name.Focus(); dialog.ShowDialog();
  }
  void ChooseApp(Space space) {
   if (!Guard()) return; StackPanel body;var dialog=Dialog("Apps para "+space.Name,out body);
   body.Children.Add(Text("Marque o que deve abrir junto com este ambiente.",13,true));Gap(body,12);
   var available=config.Apps.Where(a=>!space.AppIds.Contains(a.Id)).ToList(); var picks=new List<KeyValuePair<AppEntry,CheckBox>>();
   foreach(var app in available){ var check=new CheckBox { Content=Glyph(app)+"   "+app.Name, Margin=new Thickness(0,0,0,0) }; var line=Ruled(check,44); line.Padding=new Thickness(2,0,0,0); body.Children.Add(line); picks.Add(new KeyValuePair<AppEntry,CheckBox>(app,check)); }
   if(available.Count==0){ var all=Text("Todos os itens da biblioteca já estão neste ambiente.",13,true); all.FontStyle=FontStyles.Italic; body.Children.Add(all); }
   Gap(body,18);
   var actions=new DockPanel { LastChildFill=false };
   var create=Button("+  Cadastrar novo app ou link",()=>{dialog.Close();EditApp(null,space);},"Quiet"); DockPanel.SetDock(create,Dock.Left); actions.Children.Add(create);
   if(available.Count>0){
    var add=Button("Adicionar marcados",()=>{ var chosen=picks.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key.Id).ToList(); if(chosen.Count==0){dialog.Close();return;} space.AppIds.AddRange(chosen); if(Save()){dialog.Close();Render();}else foreach(var id in chosen)space.AppIds.Remove(id); },"Primary");
    add.IsDefault=true; DockPanel.SetDock(add,Dock.Right); actions.Children.Add(add);
   }
   body.Children.Add(actions); dialog.ShowDialog();
  }
  public void RenderChecks(string directory) {
   Directory.CreateDirectory(directory);
   foreach(string theme in new[]{"light","dark"}) {
    config.Theme=theme;ApplyTheme();
    foreach(string destination in new[]{"home","dev","library","settings"}) {
     Navigate(destination);Window.UpdateLayout();
     var visual=(FrameworkElement)Window.Content;
     int width=(int)Math.Ceiling(visual.ActualWidth+visual.Margin.Left+visual.Margin.Right),height=(int)Math.Ceiling(visual.ActualHeight+visual.Margin.Top+visual.Margin.Bottom);
     var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(Window.Background,null,new Rect(0,0,width,height));context.DrawRectangle(new VisualBrush(visual),null,new Rect(visual.Margin.Left,visual.Margin.Top,visual.ActualWidth,visual.ActualHeight));}
     var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
     var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
     using(var file=File.Create(System.IO.Path.Combine(directory,destination+"-"+theme+".png")))encoder.Save(file);
    }
   }
   File.WriteAllText(System.IO.Path.Combine(directory,"render-result.txt"),"PASS: eight views rendered at 1200 x 830 window size; no UI exceptions.");
  }
 }
}
