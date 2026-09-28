using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PaperSpaces {
    [DataContract] public class AppEntry {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Kind;
        [DataMember] public string Target;
        [DataMember] public List<string> Args = new List<string>();
    }
    [DataContract] public class Space {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Description;
        [DataMember] public string Color;
        [DataMember] public string Symbol;
        [DataMember] public string Theme;
        [DataMember] public bool Minimize;
        [DataMember] public List<string> AppIds = new List<string>();
    }
    [DataContract] public class Configuration {
        [DataMember] public int Version = 1;
        [DataMember] public string Theme = "light";
        [DataMember] public List<Space> Spaces = new List<Space>();
        [DataMember] public List<AppEntry> Apps = new List<AppEntry>();
    }
    public static class Rules {
        public static readonly string[] Colors = { "#D6BEED", "#BFD5B3", "#EDC8AA", "#B9D4DC", "#EDBDCE" };
        public static void ValidateApp(AppEntry app, bool checkFile) {
            if (app == null || String.IsNullOrWhiteSpace(app.Id)) throw new Exception("Aplicativo sem identificador.");
            if (String.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 60) throw new Exception("Use um nome de 1 a 60 caracteres.");
            if (String.IsNullOrWhiteSpace(app.Target) || app.Target.Length > 4096 || app.Target.Any(Char.IsControl)) throw new Exception("Informe um caminho ou endereço válido.");
            if (app.Args == null || app.Args.Count > 50 || app.Args.Any(a => a == null || a.Length > 4096 || a.IndexOf('\0') >= 0)) throw new Exception("Argumentos inválidos.");
            if (app.Kind == "url") {
                Uri uri;
                if (!Uri.TryCreate(app.Target, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)) throw new Exception("Use um link completo começando com https:// ou http://.");
                if (app.Args.Count != 0) throw new Exception("Links não recebem argumentos separados.");
            } else if (app.Kind == "exe" || app.Kind == "shortcut") {
                string extension = Path.GetExtension(app.Target);
                if (!Path.IsPathRooted(app.Target) || !String.Equals(extension, app.Kind == "exe" ? ".exe" : ".lnk", StringComparison.OrdinalIgnoreCase)) throw new Exception("Selecione um arquivo .exe ou um atalho .lnk com caminho completo.");
                if (app.Kind == "shortcut" && app.Args.Count != 0) throw new Exception("Configure os argumentos nas propriedades do próprio atalho.");
                if (checkFile && !File.Exists(app.Target)) throw new Exception("Arquivo não encontrado. Edite o cadastro e selecione o programa novamente.");
            } else throw new Exception("Tipo de aplicativo desconhecido.");
        }
        public static void Validate(Configuration config) {
            if (config == null || config.Version != 1) throw new Exception("Esta versão do arquivo de configuração não é compatível.");
            if (config.Theme != "light" && config.Theme != "dark") throw new Exception("Tema inválido.");
            if (config.Apps == null || config.Spaces == null || config.Apps.Count > 500 || config.Spaces.Count > 100) throw new Exception("Configuração inválida ou acima do limite.");
            var ids = new HashSet<string>();
            foreach (var app in config.Apps) { ValidateApp(app, false); if (!ids.Add(app.Id)) throw new Exception("Aplicativos com identificadores repetidos."); }
            var spaces = new HashSet<string>();
            foreach (var s in config.Spaces) {
                if (s == null || String.IsNullOrWhiteSpace(s.Id) || !spaces.Add(s.Id) || String.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 32) throw new Exception("Ambiente inválido.");
                if (!Colors.Contains(s.Color) || (s.Theme != "light" && s.Theme != "dark") || s.Description == null || s.Description.Length > 120 || s.Symbol == null || s.Symbol.Length > 8) throw new Exception("Aparência do ambiente inválida.");
                if (s.AppIds == null || s.AppIds.Count > 100 || s.AppIds.Distinct().Count() != s.AppIds.Count || s.AppIds.Any(id => !ids.Contains(id))) throw new Exception("Um ambiente referencia aplicativos inválidos.");
            }
        }
        // Windows CommandLineToArgvW-compatible quoting; never a shell command.
        public static string Quote(string value) {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
    }
    public class Repository {
        public readonly string DirectoryPath;
        public string FilePath { get { return Path.Combine(DirectoryPath, "config.json"); } }
        public string Warning;
        public Repository(string path) { DirectoryPath = path; }
        public static Configuration Defaults() {
            var c = new Configuration();
            string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
            if (File.Exists(notepad)) c.Apps.Add(new AppEntry { Id = "notepad", Name = "Bloco de Notas", Kind = "exe", Target = notepad });
            c.Apps.Add(new AppEntry { Id = "github", Name = "GitHub", Kind = "url", Target = "https://github.com" });
            c.Spaces.Add(new Space { Id = "gaming", Name = "Gaming", Description = "Um lugar para a próxima aventura.", Color = Rules.Colors[0], Symbol = "✦", Theme = "dark" });
            c.Spaces.Add(new Space { Id = "dev", Name = "Dev", Description = "Ideias abertas. Foco no que você cria.", Color = Rules.Colors[1], Symbol = "⌘", Theme = "light", AppIds = c.Apps.Select(a => a.Id).ToList() });
            c.Spaces.Add(new Space { Id = "study", Name = "Study", Description = "Uma página de cada vez.", Color = Rules.Colors[2], Symbol = "✎", Theme = "light", AppIds = c.Apps.Where(a => a.Id == "notepad").Select(a => a.Id).ToList() });
            return c;
        }
        public static Configuration Deserialize(string file) {
            using (var input = File.OpenRead(file)) {
                if (input.Length > 4 * 1024 * 1024) throw new Exception("Arquivo de configuração muito grande.");
                var c = (Configuration)new DataContractJsonSerializer(typeof(Configuration)).ReadObject(input);
                Rules.Validate(c); return c;
            }
        }
        public Configuration Load() {
            Directory.CreateDirectory(DirectoryPath);
            if (!File.Exists(FilePath)) { var initial = Defaults(); Save(initial); return initial; }
            try { return Deserialize(FilePath); }
            catch (Exception error) {
                string backup = FilePath + ".bak";
                if (!File.Exists(backup)) throw new Exception("Não foi possível ler seus dados. O arquivo original foi preservado em " + FilePath + ". " + error.Message);
                Configuration recovered;
                try { recovered = Deserialize(backup); } catch { throw new Exception("A configuração e o backup não puderam ser lidos. Seus arquivos foram preservados em " + DirectoryPath); }
                File.Copy(FilePath, FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
                Warning = "Recuperamos o último backup. A configuração anterior foi preservada na pasta de dados.";
                Save(recovered); File.Copy(FilePath, backup, true); return recovered;
            }
        }
        public void Save(Configuration c) {
            Rules.Validate(c); Directory.CreateDirectory(DirectoryPath);
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    new DataContractJsonSerializer(typeof(Configuration)).WriteObject(stream, c); stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class LaunchService {
        public static ProcessStartInfo Prepare(AppEntry app) {
            Rules.ValidateApp(app, true);
            var info = new ProcessStartInfo { FileName = app.Target, UseShellExecute = app.Kind != "exe" };
            if (app.Kind == "exe") { info.Arguments = String.Join(" ", app.Args.Select(Rules.Quote)); info.WorkingDirectory = Path.GetDirectoryName(app.Target); }
            return info;
        }
        public static void Launch(AppEntry app) { using (var process = Process.Start(Prepare(app))) { } }
    }
}
