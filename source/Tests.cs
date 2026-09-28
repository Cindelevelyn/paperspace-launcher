using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PaperSpaces {
 public static class Tests {
  [DllImport("shell32.dll", SetLastError=true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int count);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr ptr);
  static void Assert(bool condition,string message) { if(!condition)throw new Exception(message); }
  static void Reject(Action action) { bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,"Entrada inválida foi aceita"); }
  public static int Run(string directory, bool processTest = false) {
   Directory.CreateDirectory(directory);var log=new List<string>();int failures=0;
   Action<string,Action> test=(name,action)=>{try{action();log.Add("PASS "+name);}catch(Exception e){failures++;log.Add("FAIL "+name+": "+e.Message);}};
   var repo=new Repository(Path.Combine(directory,"repository-"+Guid.NewGuid().ToString("N")));
   test("Defaults and JSON persistence",()=>{var c=repo.Load();Assert(c.Spaces.Count==3,"Ambientes iniciais");c.Spaces[0].Name="Jogos e café";repo.Save(c);Assert(repo.Load().Spaces[0].Name=="Jogos e café","Roundtrip Unicode");Assert(File.Exists(repo.FilePath+".bak"),"Backup ausente");});
   test("Reject unsupported configuration without overwrite",()=>{string before=File.ReadAllText(repo.FilePath);var c=repo.Load();c.Version=99;Reject(()=>repo.Save(c));Assert(File.ReadAllText(repo.FilePath)==before,"Arquivo modificado");});
   test("Corrupt configuration recovers backup and preserves original",()=>{File.WriteAllText(repo.FilePath,"{broken json");var c=repo.Load();Rules.Validate(c);Assert(repo.Warning!=null,"Aviso de recuperação ausente");Assert(Directory.GetFiles(repo.DirectoryPath,"*.invalid-*").Length==1,"Original não preservado");});
   test("Broken file without backup is preserved",()=>{var r=new Repository(Path.Combine(directory,"broken-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(r.DirectoryPath);File.WriteAllText(r.FilePath,"broken");Reject(()=>r.Load());Assert(File.ReadAllText(r.FilePath)=="broken","Original sobrescrito");});
   test("Reject dangerous or malformed URL schemes",()=>{foreach(var url in new[]{"javascript:alert(1)","file:///C:/Windows/notepad.exe","https://", "https://user:password@example.com"})Reject(()=>Rules.ValidateApp(new AppEntry{Id="test",Name="Test",Kind="url",Target=url},true));});
   test("Reject missing executable and non-executable file",()=>{Reject(()=>LaunchService.Prepare(new AppEntry{Id="test",Name="Test",Kind="exe",Target=Path.Combine(directory,"missing.exe")}));Reject(()=>Rules.ValidateApp(new AppEntry{Id="test",Name="Test",Kind="exe",Target=Path.Combine(directory,"script.ps1")},false));});
   test("Reject orphan app references and repeated ids",()=>{var c=Repository.Defaults();c.Spaces[0].AppIds.Add("does-not-exist");Reject(()=>Rules.Validate(c));c=Repository.Defaults();c.Apps.Add(c.Apps[0]);Reject(()=>Rules.Validate(c));});
   test("Windows argument quoting roundtrip",()=>{
    var values=new[]{"","hello world","C:\\some folder\\", "a\"b", "two\\\\\"quotes", "áé 漢字", "& $(no-shell)"};
    string command="test.exe "+String.Join(" ",values.Select(Rules.Quote));int count;IntPtr result=CommandLineToArgvW(command,out count);Assert(result!=IntPtr.Zero,"Parser failed");
    try{Assert(count==values.Length+1,"Wrong argument count");for(int i=0;i<values.Length;i++)Assert(Marshal.PtrToStringUni(Marshal.ReadIntPtr(result,(i+1)*IntPtr.Size))==values[i],"Argument changed: "+i);}finally{LocalFree(result);}
   });
   test("Executable launch uses no command shell",()=>{var app=new AppEntry{Id="test",Name="Test",Kind="exe",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","notepad.exe"),Args=new List<string>{"C:\\a b\\c.txt"}};var info=LaunchService.Prepare(app);Assert(!info.UseShellExecute,"Shell was enabled");Assert(info.Arguments==Rules.Quote(app.Args[0]),"Argument changed");});
   if(processTest) test("Actual child process receives paths with spaces and quotes",()=>{
    string probe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LaunchProbe.exe");string output=Path.Combine(directory,"child-result.txt");
    Assert(File.Exists(probe),"Build the test probe first");
    var values=new[]{"two words","C:\\folder with space\\","a\"b","ação"};
    var app=new AppEntry{Id="probe",Name="Probe",Kind="exe",Target=probe,Args=new[]{output}.Concat(values).ToList()};
    using(var process=Process.Start(LaunchService.Prepare(app))){Assert(process.WaitForExit(10000),"Child process timed out");Assert(process.ExitCode==0,"Child process failed");}
    Assert(File.ReadAllLines(output).SequenceEqual(values),"Actual process received different arguments");
   });
   if(!processTest)log.Add("INFO Child-process test is opt-in: --self-test-process (requires LaunchProbe.exe).");
   log.Add(failures==0?"ALL SELECTED TESTS PASSED":failures+" TESTS FAILED");File.WriteAllLines(Path.Combine(directory,"results.txt"),log);return failures==0?0:1;
  }
}
}
