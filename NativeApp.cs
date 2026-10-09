using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class NativeApp : Form {
    readonly Color green=ColorTranslator.FromHtml("#43ef8b"),amber=ColorTranslator.FromHtml("#e7b658");
    internal readonly ResetPopup Details=new ResetPopup();
    internal readonly AuthorCard Author=new AuthorCard();
    internal readonly QuotaIndicator Gauge=new QuotaIndicator();
    internal readonly NotifyIcon Tray=new NotifyIcon();
    internal new readonly ResetMenu Menu=new ResetMenu();
    internal readonly RefreshWheel Wheel=new RefreshWheel();
    internal ToolStripMenuItem FiveItem,WeekItem,AutostartItem;
    readonly Label label=new Label();readonly ToolTip tip=new ToolTip();readonly Timer timer=new Timer();
    readonly HttpClient client=new HttpClient();readonly CodexQuotaClient quotaClient=new CodexQuotaClient();
    readonly string statePath,cachePath,startupPath,mode;readonly bool test;
    internal string Period="five";internal AccountQuota Quota;internal string QuotaFailure;
    Dictionary<string,object> data;DateTimeOffset? manual,lastFetch;
    DateTimeOffset nextFetch=DateTimeOffset.UtcNow,nextQuota=DateTimeOffset.UtcNow,lastAttempt=DateTimeOffset.MinValue,rateLimitUntil=DateTimeOffset.MinValue;
    Task<HttpResponseMessage> fetch;Task<string> quotaTask;string failure,renderState;int refreshHours=2;bool suppressed,drag,moved,disposed;Point origin;
    internal NativeApp(bool testing,string testMode){
        test=testing;mode=testMode;var root=test?NativeTests.Output:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexReset");Directory.CreateDirectory(root);
        statePath=Path.Combine(root,"settings.json");cachePath=Path.Combine(root,"cache.json");startupPath=Path.Combine(test?root:Environment.GetFolderPath(Environment.SpecialFolder.Startup),"CodexReset.lnk");
        Text="Codex Reset";FormBorderStyle=FormBorderStyle.None;Size=new Size(244,34);BackColor=ColorTranslator.FromHtml("#0b1210");TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Right-264,area.Bottom-44);
        try{var s=NativeData.Decode(File.ReadAllText(statePath));refreshHours=NativeData.Hours(NativeData.Get(s,"refreshHours"));Period=NativeData.Text(s,"quotaPeriod")=="week"?"week":"five";suppressed=Object.Equals(NativeData.Get(s,"hideAuthorReminder"),true);DateTimeOffset t;if(DateTimeOffset.TryParse(NativeData.Text(s,"manual"),out t))manual=t;if(NativeData.Get(s,"x")!=null){var p=new Point(Convert.ToInt32(NativeData.Get(s,"x")),Convert.ToInt32(NativeData.Get(s,"y")));foreach(var screen in Screen.AllScreens)if(screen.WorkingArea.Contains(p)){Location=p;break;}}}catch{}
        try{data=NativeData.Cache(File.ReadAllText(cachePath));failure="缓存 · 等待联网更新";}catch{}
        label.Dock=DockStyle.Fill;label.TextAlign=ContentAlignment.MiddleCenter;label.Font=new Font("Consolas",11);label.ForeColor=green;Controls.Add(label);
        Paint+=(s,e)=>{using(var pen=new Pen(green))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);};
        Author.SetPreference(suppressed);Author.ReminderChanged+=(s,e)=>{suppressed=Author.Suppressed;SaveSettings();};Author.BilibiliClicked+=(s,e)=>OpenUrl("https://space.bilibili.com/309229096");Author.GithubClicked+=(s,e)=>OpenUrl("https://github.com/Evan-Luxx");LocationChanged+=(s,e)=>{if(Author.Visible)Author.PositionAbove(Bounds);};
        Details.SourceClicked+=(s,e)=>OpenUrl("https://aihot.news/codex-reset");Details.NewsSourceClicked+=(s,e)=>OpenUrl(Details.NewsLink);
        Tray.Icon=QuotaIndicator.MakeIcon(-1,false);Tray.Text="Codex Reset";Tray.Visible=true;Gauge.Tray=Tray;
        var periods=new ToolStripMenuItem("显示额度");FiveItem=new ToolStripMenuItem("5 小时余量",null,(s,e)=>{Period="five";SaveSettings();UpdateView();});WeekItem=new ToolStripMenuItem("每周余量",null,(s,e)=>{Period="week";SaveSettings();UpdateView();});periods.DropDownItems.AddRange(new ToolStripItem[]{FiveItem,WeekItem});Menu.Items.Add(periods);
        Add("刷新个人额度",()=>{if(DateTimeOffset.UtcNow>=nextQuota.AddSeconds(-105))StartQuota();});Add("显示 / 隐藏小区域",()=>{if(Visible)Hide();else Show();});Add("打开 / 固定面板",OpenPanel);
        Add("刷新数据",()=>{if(fetch==null&&DateTimeOffset.UtcNow>=lastAttempt.AddMinutes(1)&&DateTimeOffset.UtcNow>=rateLimitUntil)StartFetch();});
        var refresh=new ToolStripMenuItem("自动更新数据");Wheel.InitializeHours(refreshHours);var host=new ToolStripControlHost(Wheel){Margin=Padding.Empty,Padding=Padding.Empty};var drop=new ToolStripDropDown();drop.Items.Add(host);refresh.DropDown=drop;Menu.Items.Add(refresh);
        Wheel.ValueChanged+=(s,e)=>{refreshHours=Wheel.Hours;nextFetch=DateTimeOffset.UtcNow.AddHours(refreshHours);if(rateLimitUntil>nextFetch)nextFetch=rateLimitUntil;SaveSettings();UpdateView();};
        AutostartItem=new ToolStripMenuItem("开机自启"){Checked=File.Exists(startupPath)};AutostartItem.Click+=(s,e)=>{try{SetAutostart(!File.Exists(startupPath));}catch{MessageBox.Show("无法更改开机自启设置，请检查启动目录权限。","Codex Reset");}finally{AutostartItem.Checked=File.Exists(startupPath);}};Menu.Items.Add(AutostartItem);
        Add("设置我的时间",SetPersonalTime);Add("清除个人时间",()=>{manual=null;SaveSettings();UpdateView();});Add("关于作者",()=>Author.Open(Bounds));Add("退出",Close);
        Menu.Opening+=(s,e)=>{Details.Suspended=true;AutostartItem.Checked=File.Exists(startupPath);};Menu.Closed+=(s,e)=>Details.Suspended=false;Menu.ApplyTheme();Tray.ContextMenuStrip=Menu;Details.ContextMenuStrip=Menu;label.ContextMenuStrip=Menu;
        Tray.MouseMove+=(s,e)=>{var p=Cursor.Position;Details.Arm(p,new Rectangle(p.X-18,p.Y-18,36,36));};Tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)OpenPanel();};label.MouseEnter+=(s,e)=>Details.Arm(new Point(Right-20,Top),Bounds);
        label.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){drag=true;moved=false;origin=e.Location;}};label.MouseMove+=(s,e)=>{if(drag){int dx=e.X-origin.X,dy=e.Y-origin.Y;if(Math.Abs(dx)+Math.Abs(dy)>3){moved=true;Location=new Point(Left+dx,Top+dy);}}};label.MouseUp+=(s,e)=>{if(drag){drag=false;if(moved)SaveSettings();else OpenPanel();}};
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;client.Timeout=TimeSpan.FromSeconds(20);client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexResetDesktop/1.1.2");
        if(mode=="--ui-test"){data=null;manual=null;failure=null;Quota=NativeData.Quota(NativeTests.QuotaFixture);}
        timer.Interval=1000;timer.Tick+=(s,e)=>Tick();Shown+=(s,e)=>{if(!suppressed)Author.Open(Bounds);BeginInvoke(new Action(()=>Details.Prepare()));if(mode=="--ui-test")BeginInvoke(new Action(()=>NativeTests.UiTest(this)));if(mode=="--smoke-test"){var smoke=new Timer{Interval=26000};smoke.Tick+=(ss,ee)=>{smoke.Stop();smoke.Dispose();NativeTests.Smoke(this,data!=null&&failure==null);};smoke.Start();}};
        timer.Start();UpdateView();
    }
    void Add(string text,Action action){Menu.Items.Add(text,null,(s,e)=>action());}
    static void OpenUrl(string value){Uri uri;if(Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="https")try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch{}}
    void OpenPanel(){var p=Cursor.Position;Details.TogglePinned(p,new Rectangle(p.X-18,p.Y-18,36,36));}
    void SetPersonalTime(){Details.Suspended=true;using(var dialog=new Form{Text="个人额度重置时间",Size=new Size(360,155),StartPosition=FormStartPosition.CenterScreen,BackColor=BackColor,ForeColor=green,TopMost=true}){
        var picker=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm:ss",Location=new Point(15,15),Width=310,Value=manual.HasValue?manual.Value.LocalDateTime:DateTime.Now.AddHours(5)};var save=new Button{Text="保存（本机时间）",Location=new Point(15,55),Width=190};save.Click+=(s,e)=>{manual=new DateTimeOffset(picker.Value);SaveSettings();dialog.Close();};dialog.Controls.AddRange(new Control[]{picker,save});try{dialog.ShowDialog();}finally{Details.Suspended=false;}}UpdateView();}
    internal void SaveSettings(){try{File.WriteAllText(statePath,NativeData.Json.Serialize(new Dictionary<string,object>{{"refreshHours",refreshHours},{"x",Left},{"y",Top},{"quotaPeriod",Period},{"hideAuthorReminder",suppressed},{"manual",manual.HasValue?manual.Value.ToString("o"):null}}),new System.Text.UTF8Encoding(true));}catch{}}
    internal void SetAutostart(bool enabled){if(!enabled){if(File.Exists(startupPath))File.Delete(startupPath);return;}object shell=null,shortcut=null;try{var type=Type.GetTypeFromProgID("WScript.Shell");shell=Activator.CreateInstance(type);shortcut=type.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{startupPath});var st=shortcut.GetType();foreach(var pair in new Dictionary<string,object>{{"TargetPath",Application.ExecutablePath},{"Arguments",""},{"WorkingDirectory",Path.GetDirectoryName(Application.ExecutablePath)},{"WindowStyle",7},{"Description","Codex Reset 开机自启"}})st.InvokeMember(pair.Key,System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{pair.Value});st.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null);}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
    void StartFetch(){if(fetch!=null)return;lastAttempt=DateTimeOffset.UtcNow;nextFetch=lastAttempt.AddMinutes(1);fetch=client.GetAsync("https://aihot.news/codex-reset");}
    void StartQuota(){if(quotaTask!=null||mode=="--ui-test")return;nextQuota=DateTimeOffset.UtcNow.AddMinutes(2);var exe=NativeData.FindCodex();if(exe==null){Quota=null;QuotaFailure="未找到 Codex，请安装或加入 PATH";return;}quotaTask=quotaClient.ReadAsync(exe);}
    void Tick(){
        if(quotaTask!=null&&quotaTask.IsCompleted){try{Quota=NativeData.Quota(quotaTask.GetAwaiter().GetResult());QuotaFailure=null;}catch(Exception ex){if(ex.GetBaseException() is TimeoutException)QuotaFailure="查询超时 · 旧数据，稍后重试";else{Quota=null;QuotaFailure="查询失败 · 请检查 Codex 登录状态";}}finally{quotaTask=null;}}
        if(fetch!=null&&fetch.IsCompleted){HttpResponseMessage response=null;try{response=fetch.GetAwaiter().GetResult();if((int)response.StatusCode==429){var retry=response.Headers.RetryAfter;nextFetch=DateTimeOffset.UtcNow.AddMinutes(15);if(retry!=null){if(retry.Delta.HasValue)nextFetch=DateTimeOffset.UtcNow.Add(retry.Delta.Value);else if(retry.Date.HasValue)nextFetch=retry.Date.Value;}if(nextFetch<DateTimeOffset.UtcNow.AddMinutes(1))nextFetch=DateTimeOffset.UtcNow.AddMinutes(1);rateLimitUntil=nextFetch;throw new HttpRequestException("限流");}response.EnsureSuccessStatusCode();var parsed=NativeData.PublicData(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());if(data!=null&&DateTimeOffset.Parse(NativeData.Text(parsed,"Last"))>DateTimeOffset.Parse(NativeData.Text(data,"Last")))Tray.ShowBalloonTip(5000,"Codex 重置消息","AIHOT 新增了额度重置记录，请在 Codex 内核实。",ToolTipIcon.Info);data=parsed;failure=null;lastFetch=DateTimeOffset.UtcNow;nextFetch=lastFetch.Value.AddHours(refreshHours);try{File.WriteAllText(cachePath,NativeData.Json.Serialize(data),new System.Text.UTF8Encoding(true));}catch{}}catch{failure="联网更新失败 · 保留缓存，稍后重试";}finally{if(response!=null)response.Dispose();fetch=null;}}
        if(mode!="--ui-test"){if(quotaTask==null&&DateTimeOffset.UtcNow>=nextQuota)StartQuota();if(fetch==null&&DateTimeOffset.UtcNow>=nextFetch)StartFetch();}UpdateView();
    }
    internal void UpdateView(){
        var now=DateTimeOffset.UtcNow;Details.Offline=failure!=null;Details.HasSignal=Object.Equals(NativeData.Get(data,"Signal"),true);Details.StatusText=data==null?"等待下一次重置信号":NativeData.Text(data,"Status");Details.SubText=data==null?"暂无官方时间 · 持续监控中":NativeData.Text(data,"Estimate");Details.ScopeText="全局监控";Details.TimeLabel="上次额度重置 / 北京日期";Details.TimeText=data==null?"--.--  --:--":DateTimeOffset.Parse(NativeData.Text(data,"Last")).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy.MM.dd");Details.UpdateText=lastFetch.HasValue?"已更新 "+lastFetch.Value.ToLocalTime().ToString("HH:mm")+" · "+refreshHours+"h":data==null?"正在连接":"缓存 · 尚未联网核验";
        if(failure!=null){Details.SubText="离线 · 显示缓存，稍后重试";Details.UpdateText="更新失败 · 保留缓存";}if(manual.HasValue){Details.ScopeText="个人倒计时";Details.StatusText="我的额度恢复时间";Details.SubText="手动设置 · 本机时间";Details.TimeLabel="剩余时间 / 到期后请核实额度";Details.TimeText=NativeData.Remaining(manual.Value,now);}
        var alert=NativeData.Get(data,"Alert");if(alert!=null)Details.SetNews(NativeData.Text(alert,"Key"),NativeData.Text(alert,"Text"),failure==null?NativeData.Text(alert,"Meta"):"中文缓存 · 等待同步","https://aihot.news/codex-reset");else Details.SetNews("","正在等待中文来源消息，请稍后刷新。","中文网页 · 等待同步","https://aihot.news/codex-reset");
        label.Text="❯ Codex · "+(manual.HasValue?"个人 "+NativeData.Remaining(manual.Value,now):failure!=null?"离线 · 查看详情":data==null?"暂无已确认数据":NativeData.Text(data,"Status"));label.ForeColor=data!=null&&failure==null&&!manual.HasValue?amber:failure!=null?amber:green;
        string state=Details.StatusText+Details.SubText+Details.TimeText+Details.UpdateText+Details.ScopeText;if(state!=renderState){renderState=state;Details.Invalidate();tip.SetToolTip(label,"全局重置监控 · 北京时间\r\n"+Details.TimeText+"\r\n"+Details.StatusText+"\r\n"+Details.SubText+"\r\n适用范围："+NativeData.Text(data,"Scope")+"\r\n发卡不代表额度恢复；预计时间仅供参考。");}
        bool stale=QuotaFailure!=null||(Quota!=null&&(now-Quota.Updated).TotalMinutes>5);var five=Quota==null?null:Quota.Five;var week=Quota==null?null:Quota.Week;Details.SetPanelQuota(five==null?-1:five.Remaining,week==null?-1:week.Remaining,Old(five,stale),Old(week,stale));Details.SetFiveResetText(NativeData.ResetTime(five));var selected=Period=="week"?week:five;stale=Old(selected,stale);Gauge.SetQuota(selected==null?-1:selected.Remaining,stale);FiveItem.Checked=Period=="five";WeekItem.Checked=Period=="week";
        string status=selected!=null?selected.Remaining.ToString("0.#")+"% 剩余"+(stale?" · 旧数据":""):QuotaFailure!=null?"查询失败":Quota!=null?"暂不可用":"正在查询";if((five!=null&&five.Remaining==0)||(week!=null&&week.Remaining==0))status+=" · 额度耗尽";string tray="Codex "+(Period=="week"?"周":"5h")+" "+status;Tray.Text=tray.Substring(0,Math.Min(63,tray.Length));
    }
    static bool Old(QuotaWindow window,bool stale){return stale||(window!=null&&window.Reset.HasValue&&window.Reset.Value<=DateTimeOffset.UtcNow);}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;SaveSettings();timer.Stop();quotaClient.Dispose();client.Dispose();if(fetch!=null)fetch.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();});Gauge.Dispose();Tray.Visible=false;if(Tray.Icon!=null)Tray.Icon.Dispose();Tray.Dispose();Menu.Dispose();Author.Dispose();Details.Dispose();tip.Dispose();timer.Dispose();label.Font.Dispose();}base.Dispose(disposing);}
}
