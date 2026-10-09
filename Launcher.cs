using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
[assembly: AssemblyTitle("Codex Reset")]
[assembly: AssemblyProduct("Codex Reset")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]
[assembly: AssemblyInformationalVersion("1.1.2")]
internal static class Launcher {
    [STAThread] static int Main(string[] args) {
        bool test=args.Length==1&&(args[0]=="--self-test"||args[0]=="--ui-test"||args[0]=="--smoke-test");
        try {
            if(args.Length>0&&!test)throw new ArgumentException("不支持的启动参数");
            if(test&&args[0]=="--self-test"){NativeTests.SelfTest();return 0;}
            using(var mutex=new Mutex(false,"Local\\CodexResetDesktop"+(test?"NativeTest":""))){
                bool acquired;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(!acquired)return 0;
                try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var app=new NativeApp(test,args.Length==0?"":args[0]))Application.Run(app);}finally{mutex.ReleaseMutex();}
            }return NativeTests.Failed?1:0;
        }catch(Exception ex){if(test){Directory.CreateDirectory(NativeTests.Output);File.WriteAllText(Path.Combine(NativeTests.Output,"native-result.txt"),"FAIL: "+ex.Message);}else MessageBox.Show("启动失败："+ex.Message,"Codex Reset",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}
