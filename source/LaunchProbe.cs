using System.IO;
using System.Linq;
class LaunchProbe { static int Main(string[] args) { if(args.Length<1)return 1;File.WriteAllLines(args[0],args.Skip(1));return 0; } }
