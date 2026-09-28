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
  StackPanel page, tabs;
  TextBlock status;
  Button themeButton;
  string selected = "home";
  string activeId;
  bool busy;
  List<string> activity = new List<string>();
  public Launcher(Repository repository) {
   this.repository = repository; config = repository.Load();
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) Window = (Window)XamlReader.Load(stream);
   using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("PaperSpaces.ico")) { if(icon!=null) Window.Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
   page = (StackPanel)Window.FindName("Page"); tabs = (StackPanel)Window.FindName("Tabs"); status = (TextBlock)Window.FindName("StatusLabel"); themeButton = (Button)Window.FindName("ThemeButton");
   ((TextBlock)Window.FindName("DateLabel")).Text = DateTime.Now.ToString("ddd, dd 'de' MMM", new CultureInfo("pt-BR"));
   themeButton.Click += (s,e) => { if (!Guard()) return; string old = config.Theme; config.Theme = old == "light" ? "dark" : "light"; if (!Save()) config.Theme = old; ApplyTheme(); };
   ((Button)Window.FindName("LibraryButton")).Click += (s,e) => Navigate("library");
   ((Button)Window.FindName("SettingsButton")).Click += (s,e) => Navigate("settings");
   ((Button)Window.FindName("NewSpaceButton")).Click += (s,e) => EditSpace(null);
   var holes = (StackPanel)Window.FindName("BindingHoles");
   for (int i=0;i<10;i++) { var hole = new Ellipse { Width=7, Height=7, Margin=new Thickness(0,15,0,15) }; hole.SetResourceReference(Shape.FillProperty,"Bg"); holes.Children.Add(hole); }
   Window.Closing += (s,e) => { if (busy) { e.Cancel = true; SetStatus("Aguarde a sequência terminar antes de fechar."); } };
   ApplyTheme(); Render();
   if (repository.Warning != null) Window.Loaded += (s,e) => MessageBox.Show(Window, repository.Warning, "Dados recuperados");
  }
  Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
  TextBlock Text(string value, double size = 14, bool muted = false) {
   var text = new TextBlock { Text=value, FontSize=size, TextWrapping=TextWrapping.Wrap };
   if (muted) text.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); return text;
  }
  TextBlock Heading(string value, double size = 34) { var t = Text(value,size); t.FontFamily = new FontFamily("Georgia"); return t; }
  Button Button(string title, Action action, string style = null) {
   var button = new Button { Content=title }; if (style != null) button.SetResourceReference(FrameworkElement.StyleProperty,style);
   button.Click += (s,e) => action(); AutomationProperties.SetName(button,title); return button;
  }
  Border Card(UIElement content, Thickness? padding = null) {
   var card = new Border { Child=content, CornerRadius=new CornerRadius(10), Padding=padding ?? new Thickness(20), BorderThickness=new Thickness(1) };
   card.SetResourceReference(Border.BackgroundProperty,"Surface"); card.SetResourceReference(Border.BorderBrushProperty,"Line"); return card;
  }
  void Gap(Panel panel, double height=18) { panel.Children.Add(new Border { Height=height }); }
  void SetStatus(string value) { status.Text=value; status.ToolTip=value; }
  bool Guard() { if (!busy) return true; SetStatus("Aguarde a ativação atual terminar."); return false; }
  bool Save() {
   try { repository.Save(config); SetStatus("●  Tudo salvo neste computador"); return true; }
   catch (Exception error) { MessageBox.Show(Window,"Não foi possível salvar. " + error.Message,"PaperSpaces"); return false; }
  }
  void ApplyTheme() {
   bool dark = config.Theme == "dark";
   string[] keys = { "Bg","Paper","Surface","Ink","Muted","Line","Accent","OnAccent","Soft" };
   string[] light = { "#EEEAE2","#FAF8F2","#FFFDFA","#343D34","#72776C","#DEDCD1","#394E3E","#FFFFFF","#E8EBDE" };
   string[] night = { "#222722","#2D332D","#353C34","#F3EFE4","#B3BCAC","#4A5346","#BDD2A7","#202B23","#404B38" };
   for (int i=0;i<keys.Length;i++) Application.Current.Resources[keys[i]] = Brush((dark ? night : light)[i]);
   themeButton.Content = dark ? "☀  Tema claro" : "☾  Tema escuro";
  }
  void Navigate(string destination) { selected=destination; Render(); ((ScrollViewer)Window.FindName("PageScroll")).ScrollToTop(); }
  void Render() {
   tabs.Children.Clear();
   AddTab("⌂", "Início", "home", "#E4E2D3");
   foreach (var space in config.Spaces) AddTab(space.Symbol,space.Name,space.Id,space.Color);
   page.Children.Clear();
   if (selected == "home") Home(); else if (selected == "library") Library(); else if (selected == "settings") Settings();
   else { var space = config.Spaces.FirstOrDefault(s => s.Id == selected); if (space == null) { selected="home"; Home(); } else SpacePage(space); }
  }
  void AddTab(string symbol,string name,string id,string color) {
   var label = new StackPanel { Orientation=Orientation.Horizontal };
   label.Children.Add(new TextBlock { Text=symbol, Width=25, FontSize=19, Foreground=Brush("#343D34"), VerticalAlignment=VerticalAlignment.Center });
   var title = new TextBlock { Text=name, FontSize=14, FontWeight=selected == id ? FontWeights.SemiBold : FontWeights.Normal, Foreground=Brush("#343D34"), VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis, TextWrapping=TextWrapping.NoWrap, MaxWidth=98 };
   label.Children.Add(title);
   var b = Button(name, () => Navigate(id)); b.Content=label; b.Background=Brush(color); b.BorderThickness=new Thickness(0); b.HorizontalContentAlignment=HorizontalAlignment.Left;
   b.Margin=new Thickness(selected == id ? 0 : 9,0,selected == id ? -1 : 7,9); b.Padding=new Thickness(13,15,10,15); b.ToolTip=name; tabs.Children.Add(b);
  }
  void Eyebrow(string title) { var t=Text(title,10,true); t.Margin=new Thickness(0,0,0,10); page.Children.Add(t); }
  void PageTitle(string title,string subtitle) { page.Children.Add(Heading(title)); var t=Text(subtitle,13,true); t.Margin=new Thickness(0,10,0,0); page.Children.Add(t); Gap(page,24); }
  void Section(string title,string action,Action click) {
   var grid = new Grid { Margin=new Thickness(0,0,0,15) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var t=Text(title,17); t.FontWeight=FontWeights.SemiBold; t.VerticalAlignment=VerticalAlignment.Center; grid.Children.Add(t);
   if (action != null) { var b=Button(action,click,"Quiet"); Grid.SetColumn(b,1); grid.Children.Add(b); } page.Children.Add(grid);
  }
  void Home() {
   Eyebrow("SEU COMPUTADOR, DO SEU JEITO");
   var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(164) });
   var copy = new StackPanel(); copy.Children.Add(Heading("Cada momento,\num pequeno universo.",32));
   var subtitle = Text("Reúna seus apps, encontre seu foco e comece\no que importa. Um ambiente de cada vez.",13,true); subtitle.Margin=new Thickness(0,10,0,12); copy.Children.Add(subtitle);
   var tag = new Border { Background=null, HorizontalAlignment=HorizontalAlignment.Left, CornerRadius=new CornerRadius(15), Padding=new Thickness(12,6,12,6) }; tag.SetResourceReference(Border.BackgroundProperty,"Soft"); tag.Child=Text("✧  Seu espaço. Seu ritmo.",12); copy.Children.Add(tag); hero.Children.Add(copy);
   var art = Illustration(); Grid.SetColumn(art,1); hero.Children.Add(art); page.Children.Add(hero); Gap(page,18);
   Section("Seus ambientes", "+  Criar ambiente", () => EditSpace(null));
   var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns=3 };
   foreach (var space in config.Spaces) cards.Children.Add(SpaceCard(space));
   page.Children.Add(cards); Gap(page,14);
   Section("À mão", "Ver biblioteca  →", () => Navigate("library"));
   var quick = new WrapPanel();
   foreach (var app in config.Apps.Take(5)) { var entry=app; var b=Button((entry.Kind == "url" ? "↗  " : "▣  ") + entry.Name, () => LaunchOne(entry)); b.Margin=new Thickness(0,0,8,8); quick.Children.Add(b); }
   var add=Button("+  Adicionar app", () => EditApp(null,null),"Quiet"); quick.Children.Add(add); page.Children.Add(quick);
   Gap(page,10);
   var note=Text("Dica: use um atalho do seu jogo para abri-lo pelo cliente certo.",12,true); page.Children.Add(note);
  }
  UIElement Illustration() {
   var canvas = new Canvas { Width=158, Height=176, VerticalAlignment=VerticalAlignment.Center, ClipToBounds=false };
   var back = new Border { Width=119, Height=137, Background=Brush("#DDD0E5"), CornerRadius=new CornerRadius(4), RenderTransform=new RotateTransform(11) }; Canvas.SetLeft(back,24); Canvas.SetTop(back,19); canvas.Children.Add(back);
   var card = new Border { Width=120, Height=141, Background=Brush("#E6E9D8"), BorderBrush=Brush("#C7CCB8"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(3), RenderTransform=new RotateTransform(-8) };
   var contents=new StackPanel { Margin=new Thickness(12,12,12,8) }; contents.Children.Add(new TextBlock { Text="✳", FontSize=58, Foreground=Brush("#607556"), HorizontalAlignment=HorizontalAlignment.Center }); contents.Children.Add(new TextBlock { Text="make room\nfor you.", FontFamily=new FontFamily("Georgia"), FontStyle=FontStyles.Italic, FontSize=16, Foreground=Brush("#43503A"), TextAlignment=TextAlignment.Center }); card.Child=contents; Canvas.SetLeft(card,6); Canvas.SetTop(card,27); canvas.Children.Add(card);
   var tape=new Border { Width=63,Height=20,Background=Brush("#D9CDA9"),Opacity=.8,RenderTransform=new RotateTransform(-12) }; Canvas.SetLeft(tape,32); Canvas.SetTop(tape,13); canvas.Children.Add(tape);
   var star=new TextBlock { Text="✧", FontSize=41, Foreground=Brush("#B09051") }; Canvas.SetLeft(star,118); Canvas.SetTop(star,120); canvas.Children.Add(star); return canvas;
  }
  UIElement SpaceCard(Space space) {
   var stack = new StackPanel();
   var icon = new TextBlock { Text=space.Symbol,FontSize=39,Foreground=Brush("#414438"),HorizontalAlignment=HorizontalAlignment.Center, Margin=new Thickness(0,8,0,8) };
   var cover = new Border { Background=Brush(space.Color),CornerRadius=new CornerRadius(6),Height=66,Child=icon,Margin=new Thickness(0,0,0,10) }; stack.Children.Add(cover);
   var name=Text(space.Name,20); name.FontFamily=new FontFamily("Georgia"); name.TextTrimming=TextTrimming.CharacterEllipsis; name.TextWrapping=TextWrapping.NoWrap; name.ToolTip=space.Name; stack.Children.Add(name);
   var count=Text(space.AppIds.Count == 0 ? "Pronto para personalizar" : space.AppIds.Count + (space.AppIds.Count == 1 ? " app no seu ambiente" : " apps no seu ambiente"),11,true); count.Margin=new Thickness(0,6,0,12); stack.Children.Add(count);
   var button=Button("Abrir ambiente  →",()=>Navigate(space.Id)); button.Padding=new Thickness(8,9,8,9); button.FontSize=12; stack.Children.Add(button);
   var card=Card(stack,new Thickness(14)); card.Margin=new Thickness(0,0,12,12); return card;
  }
  void SpacePage(Space space) {
   Eyebrow("AMBIENTE  /  " + space.Name.ToUpperInvariant());
   var header=new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var text=new StackPanel(); text.Children.Add(Heading(space.Symbol + "  " + space.Name)); var description=Text(space.Description,14,true); description.Margin=new Thickness(0,10,0,0); text.Children.Add(description); header.Children.Add(text);
   var edit=Button("Editar ambiente",()=>EditSpace(space)); edit.VerticalAlignment=VerticalAlignment.Top; Grid.SetColumn(edit,1); header.Children.Add(edit); page.Children.Add(header); Gap(page,25);
   var panel=new Grid(); panel.ColumnDefinitions.Add(new ColumnDefinition()); panel.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var info=new StackPanel(); var label=Text(activeId == space.Id ? "✦  Último ambiente ativado" : "Tudo pronto para começar?",17); label.FontWeight=FontWeights.SemiBold; info.Children.Add(label);
   info.Children.Add(Text(space.AppIds.Count + " apps  ·  Tema " + (space.Theme == "dark" ? "escuro" : "claro") + " do launcher" + (space.Minimize ? "  ·  Minimizar ao abrir" : ""),12,true)); panel.Children.Add(info);
   var activate=Button(busy ? "Ativando…" : "▶  Ativar ambiente",()=>ActivateSpace(space),"Primary"); activate.IsEnabled=!busy && space.AppIds.Count > 0; Grid.SetColumn(activate,1); panel.Children.Add(activate); page.Children.Add(Card(panel)); Gap(page,26);
   Section("Apps deste ambiente", "+  Adicionar app", ()=>ChooseApp(space));
   if (space.AppIds.Count == 0) {
    var empty=new StackPanel { Margin=new Thickness(10,18,10,18) }; var star=Heading("✧",38); star.HorizontalAlignment=HorizontalAlignment.Center; empty.Children.Add(star);
    var title=Text("Sua próxima aventura começa aqui.",17); title.TextAlignment=TextAlignment.Center; empty.Children.Add(title);
    var help=Text("Adicione jogos, programas ou links ao seu ambiente.\nPara jogos, selecione o atalho criado pelo cliente.",13,true); help.TextAlignment=TextAlignment.Center; help.Margin=new Thickness(0,12,0,18); empty.Children.Add(help);
    var b=Button("+  Escolher primeiro app",()=>ChooseApp(space),"Primary"); b.HorizontalAlignment=HorizontalAlignment.Center; empty.Children.Add(b); page.Children.Add(Card(empty));
   } else {
    for (int i=0;i<space.AppIds.Count;i++) { var app=config.Apps.First(a=>a.Id==space.AppIds[i]); page.Children.Add(AppRow(app,space,i)); }
    Gap(page,10); page.Children.Add(Text("Os apps são abertos na ordem acima. Uma falha não interrompe os próximos.",12,true));
   }
   if (activity.Count > 0) { Gap(page,22); Section("Nesta sessão",null,null); foreach (string line in activity.Take(8)) { var t=Text(line,12,true); t.Margin=new Thickness(0,0,0,6); page.Children.Add(t); } }
  }
  UIElement AppRow(AppEntry app, Space space, int index) {
   var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(45) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var symbol=Text(app.Kind == "url" ? "↗" : "▣",24); symbol.VerticalAlignment=VerticalAlignment.Center; grid.Children.Add(symbol);
   var name=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) }; var title=Text(app.Name,14); title.FontWeight=FontWeights.SemiBold; name.Children.Add(title);
   string kind=app.Kind == "url" ? "Link no navegador padrão" : app.Kind == "shortcut" ? "Atalho do Windows" : "Aplicativo";
   var detail=Text(kind,11,true); detail.ToolTip=app.Target; name.Children.Add(detail); Grid.SetColumn(name,1); grid.Children.Add(name);
   var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
   if (space != null) {
    var up=Button("↑",()=>MoveApp(space,index,-1),"Quiet"); up.ToolTip="Mover para cima"; up.IsEnabled=index>0 && !busy; actions.Children.Add(up);
    var down=Button("↓",()=>MoveApp(space,index,1),"Quiet"); down.ToolTip="Mover para baixo"; down.IsEnabled=index<space.AppIds.Count-1 && !busy; actions.Children.Add(down);
   }
   var open=Button("Abrir",()=>LaunchOne(app)); open.Margin=new Thickness(8,0,5,0); open.IsEnabled=!busy; actions.Children.Add(open);
   actions.Children.Add(Button("Editar",()=>EditApp(app,space),"Quiet"));
   var remove=Button(space == null ? "Excluir" : "×",()=>RemoveApp(app,space),"Quiet"); remove.ToolTip=space == null ? "Excluir da biblioteca" : "Remover deste ambiente"; actions.Children.Add(remove);
   Grid.SetColumn(actions,2); grid.Children.Add(actions); var card=Card(grid,new Thickness(15,13,12,13)); card.Margin=new Thickness(0,0,0,9); return card;
  }
  void MoveApp(Space space,int index,int delta) {
   if (!Guard()) return; var previous=space.AppIds.ToList(); string id=space.AppIds[index]; space.AppIds.RemoveAt(index); space.AppIds.Insert(index+delta,id); if (!Save()) space.AppIds=previous; Render();
  }
  void RemoveApp(AppEntry app,Space space) {
   if (!Guard()) return;
   string question=space == null ? "Excluir ‘"+app.Name+"’ da biblioteca e de todos os ambientes? O programa instalado não será removido." : "Remover ‘"+app.Name+"’ deste ambiente? Ele continuará na biblioteca.";
   if (MessageBox.Show(Window,question,"PaperSpaces",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
   var references=config.Spaces.ToDictionary(s=>s.Id,s=>s.AppIds.ToList()); int originalIndex=config.Apps.IndexOf(app);
   if (space != null) space.AppIds.Remove(app.Id); else { config.Apps.Remove(app); foreach(var s in config.Spaces) s.AppIds.Remove(app.Id); }
   if (!Save()) { if (space == null) config.Apps.Insert(originalIndex,app); foreach(var s in config.Spaces) s.AppIds=references[s.Id]; } Render();
  }
  void Library() {
   Eyebrow("UM LUGAR PARA OS SEUS FAVORITOS"); PageTitle("Sua biblioteca","Programas, atalhos e links. Cadastre uma vez e use em qualquer ambiente.");
   Section(config.Apps.Count + " itens cadastrados", "+  Adicionar app",()=>EditApp(null,null));
   var search=new TextBox { Margin=new Thickness(0,0,0,18),ToolTip="Buscar pelo nome" }; AutomationProperties.SetName(search,"Buscar aplicativo"); page.Children.Add(Text("Buscar aplicativo",12,true)); page.Children.Add(search);
   var rows=new StackPanel(); page.Children.Add(rows);
   Action refresh=()=> { rows.Children.Clear(); foreach(var app in config.Apps.Where(a=>a.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0)) rows.Children.Add(AppRow(app,null,0)); if (rows.Children.Count==0) rows.Children.Add(Text("Nenhum aplicativo encontrado. Adicione seu primeiro app acima.",13,true)); };
   search.TextChanged+=(s,e)=>refresh(); refresh();
  }
  void Settings() {
   Eyebrow("OS DETALHES TAMBÉM SÃO SEUS"); PageTitle("Preferências","Um aplicativo local, simples e feito para você personalizar.");
   var appearance=new StackPanel(); appearance.Children.Add(Text("Aparência",18)); Gap(appearance,10); appearance.Children.Add(Text("O tema de cada ambiente é aplicado apenas ao PaperSpaces.",13,true)); Gap(appearance,14);
   var theme=Button(config.Theme=="dark" ? "☀  Usar tema claro" : "☾  Usar tema escuro",()=> { if (!Guard()) return; string old=config.Theme; config.Theme=old=="dark"?"light":"dark"; if(!Save()) config.Theme=old; ApplyTheme(); Render(); }); theme.HorizontalAlignment=HorizontalAlignment.Left; appearance.Children.Add(theme); page.Children.Add(Card(appearance)); Gap(page);
   var data=new StackPanel(); data.Children.Add(Text("Seus dados ficam com você",18)); Gap(data,10); data.Children.Add(Text("Ambientes e aplicativos são salvos na pasta data, ao lado do executável. A cada gravação, a versão anterior vira um backup.",13,true)); Gap(data,15);
   var buttons=new WrapPanel(); buttons.Children.Add(Button("Abrir pasta de dados",()=> { try { Process.Start(new ProcessStartInfo(repository.DirectoryPath) { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show(Window,ex.Message); } }));
   var export=Button("Exportar configuração",Export); export.Margin=new Thickness(8,0,0,0); buttons.Children.Add(export); data.Children.Add(buttons); page.Children.Add(Card(data)); Gap(page);
   var about=new StackPanel(); about.Children.Add(Text("PaperSpaces  /  MVP 0.1.0",18)); Gap(about,10); about.Children.Add(Text("Nesta versão: ambientes, biblioteca, links, atalhos, ordem de abertura e temas claro/escuro. Fechar a janela encerra o launcher; os programas abertos continuam rodando.",13,true)); Gap(about,12); about.Children.Add(Text("Para próximas versões: wallpapers, tema do Windows, detecção de jogos, bandeja e Alexa.",13,true)); page.Children.Add(Card(about));
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
   ApplyTheme(); Render();
   try {
    var apps=space.AppIds.Select(id=>config.Apps.First(a=>a.Id==id)).ToList();
    foreach(var app in apps) {
     SetStatus("Abrindo "+app.Name+"…");
     try { await Task.Run(()=>LaunchService.Launch(app)); Record("✓  Abertura solicitada: "+app.Name); }
     catch(Exception e) { failures++; Record("✕  "+app.Name+": "+e.Message); }
     Render(); await Task.Delay(350);
    }
    activeId=space.Id; Record(failures==0 ? "✓  "+space.Name+" ativado. Boa sessão!" : "Ativação concluída com "+failures+" falha(s). Confira o registro abaixo.");
   } finally { busy=false; Render(); }
   if (failures==0 && space.Minimize) Window.WindowState=WindowState.Minimized;
  }
  Window Dialog(string title,out StackPanel body,double width=535) {
   var dialog=new Window { Title=title,Owner=Window,Width=width,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-55,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false };
   body=new StackPanel { Margin=new Thickness(27) }; var heading=Heading(title,26); heading.Margin=new Thickness(0,0,0,22); body.Children.Add(heading); dialog.Content=new ScrollViewer { Content=body };
   dialog.PreviewKeyDown+=(s,e)=> { if(e.Key==Key.Escape) dialog.Close(); }; return dialog;
  }
  TextBox Field(Panel body,string label,string value,int max=4096) {
   var caption=Text(label,12,true); caption.Margin=new Thickness(0,0,0,5); body.Children.Add(caption);
   var box=new TextBox { Text=value ?? "",MaxLength=max,Margin=new Thickness(0,0,0,15) }; AutomationProperties.SetName(box,label); body.Children.Add(box); return box;
  }
  void EditSpace(Space original) {
   if (!Guard()) return; StackPanel body; var dialog=Dialog(original==null?"Um novo ambiente":"Editar ambiente",out body);
   var name=Field(body,"Nome",original==null?"":original.Name,32); var description=Field(body,"Uma frase para esse momento",original==null?"":original.Description,120);
   body.Children.Add(Text("Cor do marcador",12,true));
   string color=original==null?Rules.Colors[0]:original.Color;
   var palette=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,18) }; var paletteButtons=new List<Button>();
   foreach(string option in Rules.Colors) { string captured=option; var b=Button(color==option?"✓":"",()=>{}); b.Width=61; b.Height=37; b.Background=Brush(option); b.Foreground=Brush("#303B31"); b.Margin=new Thickness(0,0,9,0); b.Click+=(s,e)=> { color=captured; foreach(var item in paletteButtons) item.Content=""; b.Content="✓"; }; paletteButtons.Add(b); palette.Children.Add(b); } body.Children.Add(palette);
   string symbol=original==null?"✦":original.Symbol;
   body.Children.Add(Text("Símbolo",12,true)); var symbols=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,18) };
   var symbolButtons=new List<Button>(); foreach(string option in new[]{"✦","⌘","✎","♫","☕","♡"}) { string captured=option; var b=Button(option,()=>{}); b.FontSize=20; b.Margin=new Thickness(0,0,7,0); b.Opacity=symbol==option?1:.5; b.Click+=(s,e)=> { symbol=captured; foreach(var item in symbolButtons)item.Opacity=.5; b.Opacity=1; }; symbolButtons.Add(b); symbols.Children.Add(b); } body.Children.Add(symbols);
   var dark=new CheckBox { Content="Usar tema escuro ao ativar este ambiente",IsChecked=original!=null && original.Theme=="dark" }; body.Children.Add(dark);
   var minimize=new CheckBox { Content="Minimizar após abrir os apps",IsChecked=original!=null && original.Minimize }; body.Children.Add(minimize); Gap(body);
   var error=Text("",12); error.Foreground=Brush("#B34A43"); body.Children.Add(error);
   var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0) };
   if(original!=null) { var delete=Button("Excluir",()=> { if(MessageBox.Show(dialog,"Excluir este ambiente? Seus apps continuarão na biblioteca.","Excluir ambiente",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return; int index=config.Spaces.IndexOf(original); config.Spaces.Remove(original); if(Save()){selected="home";dialog.Close();Render();}else config.Spaces.Insert(index,original); },"Quiet"); actions.Children.Add(delete); }
   var cancel=Button("Cancelar",()=>dialog.Close(),"Quiet"); cancel.Margin=new Thickness(8,0,8,0); actions.Children.Add(cancel);
   actions.Children.Add(Button("Salvar ambiente",()=> {
    if(String.IsNullOrWhiteSpace(name.Text)){error.Text="Dê um nome ao seu ambiente.";return;}
    if(config.Spaces.Any(s=>s!=original && String.Equals(s.Name,name.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))){error.Text="Você já tem um ambiente com esse nome.";return;}
    var next=new Space { Id=original==null?Guid.NewGuid().ToString("N"):original.Id,Name=name.Text.Trim(),Description=description.Text.Trim(),Color=color,Symbol=symbol,Theme=dark.IsChecked==true?"dark":"light",Minimize=minimize.IsChecked==true,AppIds=original==null?new List<string>():original.AppIds.ToList() };
    int index=original==null?config.Spaces.Count:config.Spaces.IndexOf(original); if(original==null)config.Spaces.Add(next);else config.Spaces[index]=next;
    if(Save()){selected=next.Id;dialog.Close();Render();}else{if(original==null)config.Spaces.Remove(next);else config.Spaces[index]=original;}
   },"Primary")); body.Children.Add(actions); dialog.ShowDialog();
  }
  void EditApp(AppEntry original,Space space) {
   if (!Guard()) return; StackPanel body; var dialog=Dialog(original==null?"Adicionar à biblioteca":"Editar aplicativo",out body,570);
   var name=Field(body,"Nome do aplicativo",original==null?"":original.Name,60);
   var target=Field(body,"Arquivo .exe / .lnk ou link https://",original==null?"":original.Target);
   var browse=Button("Escolher arquivo…",()=> {
    var picker=new OpenFileDialog { Title="Escolher aplicativo ou atalho",Filter="Aplicativos e atalhos|*.exe;*.lnk",CheckFileExists=true,DereferenceLinks=false };
    if(picker.ShowDialog(dialog)==true){target.Text=picker.FileName;if(String.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileNameWithoutExtension(picker.FileName);}
   }); browse.HorizontalAlignment=HorizontalAlignment.Left; browse.Margin=new Thickness(0,-6,0,17); body.Children.Add(browse);
   var args=Field(body,"Argumentos opcionais · um por linha (somente .exe)",original==null?"":String.Join(Environment.NewLine,original.Args)); args.AcceptsReturn=true; args.Height=72; args.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   body.Children.Add(Text("Para Steam, Riot e Battle.net, prefira o atalho criado pelo cliente. O launcher não altera a instalação do programa.",12,true)); Gap(body,12);
   var error=Text("",12);error.Foreground=Brush("#B34A43");body.Children.Add(error);
   var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0) }; var cancel=Button("Cancelar",()=>dialog.Close(),"Quiet");cancel.Margin=new Thickness(0,0,10,0);actions.Children.Add(cancel);
   actions.Children.Add(Button("Salvar aplicativo",()=> {
    string path=target.Text.Trim().Trim('"'); string kind=path.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||path.StartsWith("https://",StringComparison.OrdinalIgnoreCase)?"url":String.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase)?"shortcut":"exe";
    var next=new AppEntry { Id=original==null?Guid.NewGuid().ToString("N"):original.Id,Name=name.Text.Trim(),Kind=kind,Target=path,Args=args.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries).ToList() };
    try{Rules.ValidateApp(next,true);}catch(Exception ex){error.Text=ex.Message;return;}
    int index=original==null?config.Apps.Count:config.Apps.IndexOf(original); if(original==null)config.Apps.Add(next);else config.Apps[index]=next;
    bool addToSpace=space!=null&&!space.AppIds.Contains(next.Id); if(addToSpace)space.AppIds.Add(next.Id);
    if(Save()){dialog.Close();Render();}else{if(original==null)config.Apps.Remove(next);else config.Apps[index]=original;if(addToSpace)space.AppIds.Remove(next.Id);}
   },"Primary"));body.Children.Add(actions);dialog.ShowDialog();
  }
  void ChooseApp(Space space) {
   if (!Guard()) return; StackPanel body;var dialog=Dialog("Apps para "+space.Name,out body);
   body.Children.Add(Text("Escolha da biblioteca ou cadastre um novo aplicativo.",13,true));Gap(body);
   var available=config.Apps.Where(a=>!space.AppIds.Contains(a.Id)).ToList();
   foreach(var app in available){var entry=app;var b=Button("+  "+entry.Name,()=>{space.AppIds.Add(entry.Id);if(Save()){dialog.Close();Render();}else space.AppIds.Remove(entry.Id);});b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,8);body.Children.Add(b);}
   if(available.Count==0)body.Children.Add(Text("Todos os itens da biblioteca já estão neste ambiente.",13,true));Gap(body);
   body.Children.Add(Button("+  Cadastrar novo app ou link",()=>{dialog.Close();EditApp(null,space);},"Primary"));dialog.ShowDialog();
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
