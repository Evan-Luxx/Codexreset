using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
internal static class NativeTests {
    internal static bool Failed;
    internal static string Output {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-output","native");}}
    internal const string QuotaFixture="{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":28,\"windowDurationMins\":300,\"resetsAt\":1893456000},\"secondary\":{\"usedPercent\":62,\"windowDurationMins\":10080,\"resetsAt\":1894060800}}}}";
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,"异常数据未拒绝");}
    static void Result(string name,string value){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,name),value);}
    internal static void SelfTest(){
        var now=DateTimeOffset.UtcNow;Check(NativeData.Remaining(now.AddSeconds(3661),now)=="01:01:01","小时倒计时");Check(NativeData.Remaining(now.AddDays(2).AddSeconds(1),now)=="2天 00:00:01","跨日倒计时");Check(NativeData.Remaining(now.AddSeconds(-1),now).StartsWith("时间已到"),"到期");
        var q=NativeData.Quota(QuotaFixture);Check(q.Five.Remaining==72&&q.Week.Remaining==38,"双窗口");Check(NativeData.ResetTime(q.Five)==q.Five.Reset.Value.ToLocalTime().ToString("HH:mm"),"本机恢复时间");
        q=NativeData.Quota("{\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"secondary\":{\"usedPercent\":100,\"windowDurationMins\":300},\"primary\":{\"usedPercent\":5,\"windowDurationMins\":10080}}}}}");Check(q.Five.Remaining==0&&q.Week.Remaining==95,"窗口顺序或耗尽");
        q=NativeData.Quota("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300}}}}");Check(q.Week==null&&NativeData.ResetTime(q.Five)=="--:--","缺失窗口");
        foreach(var raw in new[]{"{}","not-json","{\"error\":{}}",QuotaFixture.Replace("28","101"),QuotaFixture.Replace("300","15").Replace("10080","15"),QuotaFixture.Replace("1893456000","-1")})Reject(()=>NativeData.Quota(raw));
        string fixture="[{\"_1\":2},\"loaderData\",{\"_3\":4},\"codex-reset\",{\"_5\":6,\"_7\":8,\"_11\":-5},\"schemaVersion\",1,\"stats\",{\"_9\":10},\"lastResetDate\",\"2026-01-01\",\"current\"]";
        string html="streamController.enqueue("+NativeData.Json.Serialize(fixture)+")";var d=NativeData.PublicData(html);Check(NativeData.Text(d,"Last").StartsWith("2026-01-01")&&Object.Equals(NativeData.Get(d,"Signal"),false),"公开日期或空事件");Check(NativeData.Cache(NativeData.Json.Serialize(d))!=null,"缓存往返");Reject(()=>NativeData.PublicData(html.Replace("schemaVersion","unknown")));Reject(()=>NativeData.PublicData(html.Replace("2026-01-01","2099-01-01")));Reject(()=>NativeData.PublicData("invalid"));
        string eventFixture="[{\"_1\":2},\"loaderData\",{\"_3\":4},\"codex-reset\",{\"_5\":6,\"_7\":8,\"_11\":12},\"schemaVersion\",1,\"stats\",{\"_9\":10},\"lastResetDate\",\"2026-01-01\",\"current\",{\"_13\":14,\"_15\":16,\"_17\":18},\"type\",\"reset_credit\",\"title\",\"虚构发卡消息\",\"displayLabel\",\"测试事件\"]";
        d=NativeData.PublicData("streamController.enqueue("+NativeData.Json.Serialize(eventFixture)+")");Check(NativeData.Text(d,"Status")=="虚构发卡消息"&&NativeData.Text(NativeData.Get(d,"Alert"),"Text").Contains("发卡不代表额度恢复"),"事件消息或预测提示");Reject(()=>NativeData.PublicData("streamController.enqueue("+NativeData.Json.Serialize(eventFixture.Replace("reset_credit","unknown"))+")"));
        for(int i=1;i<=24;i++)Check(NativeData.Hours(i)==i,"刷新范围");Check(NativeData.Hours(25)==2&&NativeData.Hours(null)==2,"刷新默认值");
        Result("self-test.txt","PASS: countdown, expiry, local reset time, swapped quota windows, zero/missing/invalid quota, structured page parsing, future date rejection, cache roundtrip, refresh range");
    }
    static void Pump(int ms){var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(10);}}
    static void Capture(Control control,string name){using(var b=new Bitmap(control.Width,control.Height)){control.DrawToBitmap(b,new Rectangle(Point.Empty,control.Size));b.Save(Path.Combine(Output,name));}}
    internal static void UiTest(NativeApp app){try{
        Directory.CreateDirectory(Output);app.Author.Dismiss();app.Details.TestInstant=true;app.Gauge.TestInstant=true;app.Details.OpenForTest(new Point(800,600));app.UpdateView();Check(app.Details.FiveQuota==72&&app.Details.WeekQuota==38,"面板双周期");Capture(app.Details,"panel.png");Capture(app,"widget.png");
        app.WeekItem.PerformClick();Check(app.Tray.Text.Contains("周")&&app.Gauge.DisplayedPercent==38&&app.Details.FiveQuota==72,"菜单周期切换");var saved=NativeData.Decode(File.ReadAllText(Path.Combine(Output,"settings.json")));Check(NativeData.Text(saved,"quotaPeriod")=="week","设置保存");
        app.QuotaFailure="测试超时";app.UpdateView();Check(app.Tray.Text.Contains("旧数据"),"超时旧数据");app.Quota=null;app.UpdateView();Check(app.Details.FiveQuota==-1&&app.Details.WeekQuota==-1&&app.Details.FiveResetText=="--:--","未知额度");
        app.Details.SetNews("fiction",new String('测',1800),"虚构消息","");app.Details.TestInstant=false;app.Details.FlipPage();Check(app.Details.IsFlipping,"翻转启动");Pump(700);Check(app.Details.IsNewsPage&&!app.Details.IsFlipping,"翻转完成");app.Details.ScrollNews(-120);Check(app.Details.NewsScroll>0,"滚动");Capture(app.Details,"news.png");app.Details.SetNews("new","新的虚构消息","测试","");Check(app.Details.NewsScroll==0,"新消息归零");app.Details.TestInstant=true;app.Details.FlipPage();Check(!app.Details.IsNewsPage,"返回");
        app.Wheel.InitializeHours(1);app.Wheel.Roll(-120);Check(app.Wheel.Hours==24&&app.Wheel.Animating,"滚轮循环动画");Pump(400);Check(!app.Wheel.Animating,"滚轮停稳");
        app.Author.ToggleReminder();saved=NativeData.Decode(File.ReadAllText(Path.Combine(Output,"settings.json")));Check(Object.Equals(NativeData.Get(saved,"hideAuthorReminder"),app.Author.Suppressed),"作者提醒保存");
        app.SetAutostart(true);Check(File.Exists(Path.Combine(Output,"CodexReset.lnk")),"自启创建");app.SetAutostart(false);Check(!File.Exists(Path.Combine(Output,"CodexReset.lnk")),"自启移除");
        var fit=ResetPopup.Fit(new Point(5,5),new Rectangle(0,0,1920,1080),app.Details.Size);Check(fit.X>=0&&fit.Y>=0,"屏幕边缘");app.Details.Dismiss();Check(!app.Details.Visible,"收起");Result("ui-test.txt","PASS: native WinForms rendering, dual quota, tray menu, isolated settings, stale/unknown data, animated flip, scroll, wheel animation, author preference, autostart, edge positioning, dismiss");
    }catch(Exception ex){Failed=true;Result("ui-test.txt","FAIL: "+ex.Message);}finally{app.SetAutostart(false);app.Close();}}
    internal static void Smoke(NativeApp app,bool fetched){try{Check(fetched,"公开页面联网刷新失败");app.Details.TestInstant=true;app.Details.OpenForTest(new Point(800,600));Check(app.Details.Visible,"面板打开");Result("smoke-test.txt","PASS: native HTTP refresh and panel display; account quota remains in memory only");}catch(Exception ex){Failed=true;Result("smoke-test.txt","FAIL: "+ex.Message);}finally{app.Close();}}
}
