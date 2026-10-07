using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Reset")]
[assembly: AssemblyProduct("Codex Reset")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

internal static class Launcher {
    [STAThread]
    static int Main(string[] args) {
        try {
            var assembly=Assembly.GetExecutingAssembly();
            string version;
            using(var stream=File.OpenRead(assembly.Location))using(var hash=SHA256.Create())version=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").Substring(0,20);
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexReset");
            if(args.Length==1&&(args[0]=="--self-test"||args[0]=="--ui-test")&&!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_RESET_TEST_OUT")))root=Path.Combine(Environment.GetEnvironmentVariable("CODEX_RESET_TEST_OUT"),"launcher-runtime");
            var directory=Path.Combine(root,"app",version);Directory.CreateDirectory(directory);
            foreach(var name in assembly.GetManifestResourceNames()) {
                if(!name.StartsWith("payload."))continue;
                var path=Path.Combine(directory,name.Substring(8));
                if(File.Exists(path))continue;
                using(var input=assembly.GetManifestResourceStream(name))using(var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read))input.CopyTo(output);
            }
            string mode="";
            if(args.Length==1&&args[0]=="--self-test")mode=" -SelfTest";
            else if(args.Length==1&&args[0]=="--ui-test")mode=" -UiTest";
            else if(args.Length>0)throw new ArgumentException("不支持的启动参数");
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"));
            info.Arguments="-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File \""+Path.Combine(directory,"CodexReset.ps1")+"\""+mode;
            info.UseShellExecute=false;info.CreateNoWindow=true;info.WorkingDirectory=directory;
            Environment.SetEnvironmentVariable("CODEX_RESET_DATA_DIR",root);
            using(var process=Process.Start(info)){if(mode.Length>0){process.WaitForExit();return process.ExitCode;}}
            return 0;
        }catch(Exception ex){var test=Environment.GetEnvironmentVariable("CODEX_RESET_TEST_OUT");if(!String.IsNullOrEmpty(test)&&args.Length>0){File.WriteAllText(Path.Combine(test,"launcher-error.txt"),ex.ToString());}else MessageBox.Show("启动失败："+ex.Message,"Codex Reset",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}
