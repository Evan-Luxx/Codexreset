using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public partial class ResetPopup : Form {
    public string StatusText="等待下一次重置信号", SubText="暂无官方时间 · 持续监控中", TimeText="--.--  --:--", UpdateText="正在连接", ScopeText="全局监控", TimeLabel="上次确认重置 / 北京时间";
    public bool Offline, Pinned, Suspended, TestMode, TestInstant;
    double fiveQuota=-1,weekQuota=-1,fiveShown=-1,weekShown=-1,fiveFrom,weekFrom;long quotaStarted;
    bool panelQuotaAnimating,fiveStale,weekStale;
    public double FiveQuota {get{return fiveQuota;}}
    public double WeekQuota {get{return weekQuota;}}
    public void SetPanelQuota(double five,double week,bool staleFive,bool staleWeek){
        if(fiveQuota==five&&weekQuota==week&&fiveStale==staleFive&&weekStale==staleWeek)return;
        fiveFrom=fiveShown;weekFrom=weekShown;fiveQuota=five;weekQuota=week;fiveStale=staleFive;weekStale=staleWeek;
        if(!Visible||TestInstant){fiveShown=five;weekShown=week;panelQuotaAnimating=false;}
        else{if(fiveFrom<0||five<0)fiveShown=five;if(weekFrom<0||week<0)weekShown=week;quotaStarted=clock.ElapsedMilliseconds;panelQuotaAnimating=true;animation.Interval=16;animation.Start();}
        if(Visible)Invalidate();
    }
    void DrawPanelQuota(Graphics g,double amount,bool stale,int y,string name){
        var color=amount<0?muted:amount<=10?Color.FromArgb(239,104,104):amount<=30?amber:green;
        string value=amount<0?"--":amount.ToString("0")+"%";
        DrawText(g,name,"Microsoft YaHei UI",10,muted,240,y,48,17);
        DrawText(g,value,"Consolas",12,color,285,y,44,17);
        for(int i=0;i<10;i++)using(var brush=new SolidBrush(amount>=0&&amount>i*10?color:Color.FromArgb(40,59,49)))g.FillRectangle(brush,240+i*9,y+23,6,14);
        DrawText(g,amount<0?"暂不可用":stale?"旧数据":"剩余","Microsoft YaHei UI",9,stale?amber:muted,240,y+41,90,14);
    }
    readonly System.Collections.Generic.Dictionary<string,Font> fonts=new System.Collections.Generic.Dictionary<string,Font>();
    public event EventHandler SourceClicked;
    readonly Timer animation=new Timer();
    readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    readonly Color green=ColorTranslator.FromHtml("#43EF8B"), muted=ColorTranslator.FromHtml("#94A59B"), white=ColorTranslator.FromHtml("#E0EBE5"), amber=ColorTranslator.FromHtml("#E7B658"), border=ColorTranslator.FromHtml("#294438");
    Point anchor, rest; Rectangle anchorBounds; long armAt=-1, leaveAt=-1, changeAt; double from, target, progress; bool mouseWasDown;
    public double RevealProgress {get{return progress;}}
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    public static Icon CreateTrayIcon(){using(var b=new Bitmap(32,32)){using(var g=Graphics.FromImage(b)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(ColorTranslator.FromHtml("#43EF8B"),3)){g.DrawArc(p,7,7,18,18,35,285);g.DrawLine(p,24,7,24,14);g.DrawLine(p,24,14,18,12);}}IntPtr h=b.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}}}
    public ResetPopup(){
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None; ClientSize=new Size(352,283); BackColor=ColorTranslator.FromHtml("#0B1210");
        DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); Text="Codex Reset";
        using(var p=Rounded(new Rectangle(0,0,Width,Height),14)){Region=new Region(p);}
        animation.Interval=16;animation.Tick+=Tick;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
    static GraphicsPath Rounded(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static Point Fit(Point a,Rectangle area,Size size){int x=Math.Max(area.Left,Math.Min(area.Right-size.Width,a.X-size.Width+42));int y=a.Y-size.Height-20;if(y<area.Top)y=a.Y+24;return new Point(x,Math.Max(area.Top,Math.Min(area.Bottom-size.Height,y)));}
    public void Arm(Point a,Rectangle bounds){
        animation.Interval=16;animation.Start();
        anchor=a;anchorBounds=bounds;rest=Fit(a,Screen.FromPoint(a).WorkingArea,Size);
        if(armAt<0)armAt=clock.ElapsedMilliseconds;leaveAt=-1;
        if(Visible && target<1)Transition(1);
    }
    public void TogglePinned(Point a,Rectangle bounds){
        if(Pinned){Pinned=false;Dismiss();return;}
        Arm(a,bounds);Pinned=true;armAt=-1;Invalidate();Transition(1);
    }
    public void OpenForTest(Point a){TestMode=true;Arm(a,new Rectangle(a.X-16,a.Y-16,32,32));Pinned=true;armAt=-1;Transition(1);}
    public void Dismiss(){Pinned=false;armAt=-1;leaveAt=-1;Transition(0);}
    void Transition(double value){if(target==value&&Visible)return;animation.Interval=16;animation.Start();from=progress;target=value;changeAt=clock.ElapsedMilliseconds;if(value==1&&!Visible){Opacity=.01;Location=new Point(rest.X,rest.Y+12);Show();if(newsPage)StartWheelHook();}if(TestMode&&TestInstant){progress=value;Apply();}}
    void Apply(){if(progress<=0&&target==0){Hide();StopWheelHook();flipping=false;UpdateCardRegion();ReleaseFaces();animation.Stop();return;}Location=new Point(rest.X,rest.Y+(int)Math.Round(12*(1-progress)));Opacity=Math.Max(.01,Math.Min(1,progress));}
    void Tick(object sender,EventArgs e){
        long now=clock.ElapsedMilliseconds;Point mouse=Cursor.Position;
        bool atAnchor=anchorBounds.Contains(mouse);
        bool inside=Visible&&Bounds.Contains(mouse);
        bool corridor=Visible&&new Rectangle(rest.X,rest.Y,Width,Height+Math.Max(24,anchor.Y-(rest.Y+Height)+18)).Contains(mouse);
        if(!Suspended&&!TestMode){
            if(armAt>=0&&!Visible){if(!atAnchor)armAt=-1;else if(now-armAt>=250){armAt=-1;Transition(1);}}
            if(Visible&&!Pinned){if(atAnchor||inside||corridor)leaveAt=-1;else if(leaveAt<0)leaveAt=now;else if(now-leaveAt>=350)Dismiss();}
            bool down=(GetAsyncKeyState(1)&0x8000)!=0;
            if(Visible&&Pinned&&down&&!mouseWasDown&&!inside&&!atAnchor)Dismiss();mouseWasDown=down;
            if(Visible&&(GetAsyncKeyState(27)&0x8000)!=0)Dismiss();
        }
        if(progress!=target){double t=Math.Min(1,(now-changeAt)/(target==1?200.0:140.0));double eased=target==1?1-Math.Pow(1-t,3):t*t*t;progress=from+(target-from)*eased;if(t>=1)progress=target;Apply();}
        AdvanceFlip(now);
        if(panelQuotaAnimating){double t=Math.Min(1,(now-quotaStarted)/360.0),ease=1-Math.Pow(1-t,3);if(fiveFrom>=0&&fiveQuota>=0)fiveShown=fiveFrom+(fiveQuota-fiveFrom)*ease;if(weekFrom>=0&&weekQuota>=0)weekShown=weekFrom+(weekQuota-weekFrom)*ease;if(t>=1)panelQuotaAnimating=false;if(Visible&&!flipping&&!newsPage)Invalidate();}
        if(progress==target&&!flipping&&!panelQuotaAnimating){if(!Visible&&armAt<0)animation.Stop();else animation.Interval=50;}
    }
    public void Prepare(){var handle=Handle;using(var bitmap=new Bitmap(Width,Height))DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));}
    void DrawText(Graphics g,string s,string family,float size,Color color,int x,int y,int w,int h){string key=family+":"+size;Font f;if(!fonts.TryGetValue(key,out f)){f=new Font(family,size,FontStyle.Regular,GraphicsUnit.Pixel);fonts.Add(key,f);}using(var brush=new SolidBrush(color))using(var format=new StringFormat()){format.Trimming=StringTrimming.EllipsisCharacter;format.FormatFlags=StringFormatFlags.NoWrap;format.LineAlignment=StringAlignment.Center;g.DrawString(s,f,brush,new RectangleF(x,y,w,h),format);}}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(PaintFlip(e.Graphics))return;if(newsPage)DrawNews(e.Graphics);else DrawFront(e.Graphics);}
    void DrawFront(Graphics g){g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Rounded(new Rectangle(0,0,Width-1,Height-1),14))using(var pen=new Pen(border)){g.DrawPath(pen,p);}
        DrawText(g,"❯ CODEX RESET","Consolas",13,green,23,21,114,19);
        DrawText(g,ScopeText,"Noto Sans SC",11,muted,149,21,110,19);
        DrawText(g,Pinned?"固定":"⋯","Noto Sans SC",11,muted,292,21,37,19);
        DrawText(g,StatusText,"Noto Sans SC",18,white,23,56,211,32);
        DrawText(g,SubText,"Noto Sans SC",11,amber,23,93,211,18);
        DrawText(g,TimeLabel,"Noto Sans SC",10,muted,23,126,211,16);
        DrawText(g,TimeText,"Consolas",27,green,23,145,211,39);
        DrawPanelQuota(g,fiveShown,fiveStale,57,"5 小时");DrawPanelQuota(g,weekShown,weekStale,137,"7 天");
        using(var p=new Pen(border)){g.DrawLine(p,23,200,329,200);}
        DrawText(g,"● "+UpdateText,"Noto Sans SC",11,Offline?amber:muted,23,217,175,16);
        DrawText(g,"查看来源 ↗","Noto Sans SC",11,green,242,217,87,16);
        DrawText(g,"Data: codex-reset.com","Consolas",10,muted,23,249,210,15);
        DrawButton(g,"重置消息 ↻");
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(HandleNewsMouse(e))return;if(e.Y>=207&&e.Y<=239&&e.X>230){if(SourceClicked!=null)SourceClicked(this,EventArgs.Empty);}else{Pinned=true;Invalidate();}}
    protected override void Dispose(bool disposing){if(disposing){animation.Stop();animation.Dispose();DisposeNews();foreach(var f in fonts.Values)f.Dispose();fonts.Clear();if(Region!=null)Region.Dispose();}base.Dispose(disposing);}
}

public sealed class AuthorCard : Form {
    readonly Timer timer=new Timer();readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    readonly Font font=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel),title=new Font("Consolas",13,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Color green=ColorTranslator.FromHtml("#43EF8B"),muted=ColorTranslator.FromHtml("#94A59B"),border=ColorTranslator.FromHtml("#294438");
    long lastTick;double lifetime,reveal;
    public bool Suppressed {get;private set;}
    public event EventHandler ReminderChanged,BilibiliClicked,GithubClicked;
    public AuthorCard(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(320,160);BackColor=ColorTranslator.FromHtml("#0B1210");DoubleBuffered=true;
        using(var p=Shape()){Region=new Region(p);}timer.Interval=20;timer.Tick+=Animate;Text="关于作者";}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
    System.Drawing.Drawing2D.GraphicsPath Shape(){var p=new System.Drawing.Drawing2D.GraphicsPath();int d=24;p.AddArc(0,0,d,d,180,90);p.AddArc(Width-d-1,0,d,d,270,90);p.AddArc(Width-d-1,Height-d-1,d,d,0,90);p.AddArc(0,Height-d-1,d,d,90,90);p.CloseFigure();return p;}
    public void SetPreference(bool value){Suppressed=value;Invalidate();}
    public void ToggleReminder(){Suppressed=!Suppressed;if(ReminderChanged!=null)ReminderChanged(this,EventArgs.Empty);Invalidate();}
    public void PositionAbove(Rectangle anchor){var area=Screen.FromRectangle(anchor).WorkingArea;int x=Math.Max(area.Left,Math.Min(area.Right-Width,anchor.Right-Width));int y=anchor.Top-Height-10;if(y<area.Top)y=anchor.Bottom+10;Location=new Point(x,Math.Max(area.Top,Math.Min(area.Bottom-Height,y)));}
    public void Open(Rectangle anchor){PositionAbove(anchor);lifetime=0;reveal=0;lastTick=clock.ElapsedMilliseconds;Opacity=.01;Show();timer.Interval=20;timer.Start();}
    public void Dismiss(){timer.Stop();Hide();}
    void Animate(object s,EventArgs e){long now=clock.ElapsedMilliseconds;double elapsed=now-lastTick;lastTick=now;reveal=Math.Min(1,reveal+elapsed/200);Opacity=Math.Max(.01,reveal);if(!Bounds.Contains(Cursor.Position))lifetime+=elapsed;if(lifetime>=10000)Dismiss();else if(reveal==1)timer.Interval=100;}
    void TextAt(Graphics g,string text,Font f,Color color,Rectangle rectangle){using(var brush=new SolidBrush(color))g.DrawString(text,f,brush,rectangle);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=Shape())using(var pen=new Pen(border))g.DrawPath(pen,path);
        TextAt(g,"❯ CODEX RESET",title,green,new Rectangle(20,18,190,20));TextAt(g,"×",title,muted,new Rectangle(287,15,20,23));
        TextAt(g,"制作：Evan-Luxx",font,ColorTranslator.FromHtml("#E0EBE5"),new Rectangle(20,47,270,22));
        TextAt(g,"B站主页 ↗",font,green,new Rectangle(20,77,100,22));TextAt(g,"GitHub ↗",font,green,new Rectangle(145,77,110,22));
        using(var pen=new Pen(border)){g.DrawLine(pen,20,109,300,109);g.DrawRectangle(pen,21,127,12,12);}if(Suppressed)using(var pen=new Pen(green,2)){g.DrawLines(pen,new[]{new Point(23,133),new Point(27,137),new Point(32,129)});}
        TextAt(g,"不再提醒",font,muted,new Rectangle(42,125,100,22));TextAt(g,"自动收起",font,muted,new Rectangle(242,125,70,22));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(e.X>=280&&e.Y<42)Dismiss();else if(new Rectangle(18,120,120,28).Contains(e.Location))ToggleReminder();else if(new Rectangle(20,73,110,28).Contains(e.Location)){if(BilibiliClicked!=null)BilibiliClicked(this,EventArgs.Empty);}else if(new Rectangle(145,73,110,28).Contains(e.Location)){if(GithubClicked!=null)GithubClicked(this,EventArgs.Empty);}}
    protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();font.Dispose();title.Dispose();if(Region!=null)Region.Dispose();}base.Dispose(disposing);}
}

public sealed class ResetMenu : ContextMenuStrip {
    readonly Font menuFont=new Font("Microsoft YaHei UI",9f,FontStyle.Regular);
    public ResetMenu(){Font=menuFont;Renderer=new ResetMenuRenderer();ShowImageMargin=false;ShowCheckMargin=true;BackColor=ColorTranslator.FromHtml("#0B1210");ForeColor=ColorTranslator.FromHtml("#E0EBE5");Padding=new Padding(6);DropShadowEnabled=false;}
    public void ApplyTheme(){Style(this);}
    void Style(ToolStrip strip){
        strip.Renderer=Renderer;strip.Font=menuFont;strip.BackColor=BackColor;strip.ForeColor=ForeColor;strip.Padding=new Padding(6);
        var dropdown=strip as ToolStripDropDownMenu;
        if(dropdown!=null){dropdown.ShowImageMargin=false;dropdown.ShowCheckMargin=true;dropdown.DropShadowEnabled=false;}
        foreach(ToolStripItem item in strip.Items){item.ForeColor=ForeColor;item.Font=menuFont;if(!(item is ToolStripSeparator))item.Padding=new Padding(5,6,12,6);var menu=item as ToolStripMenuItem;if(menu!=null&&menu.HasDropDownItems)Style(menu.DropDown);}
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)menuFont.Dispose();}
}
public sealed class ResetMenuRenderer : ToolStripRenderer {
    static readonly Color background=ColorTranslator.FromHtml("#0B1210"),border=ColorTranslator.FromHtml("#294438"),hover=ColorTranslator.FromHtml("#173426"),green=ColorTranslator.FromHtml("#43EF8B"),text=ColorTranslator.FromHtml("#E0EBE5"),muted=ColorTranslator.FromHtml("#94A59B");
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(background);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var pen=new Pen(border))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(e.Item.Selected&&e.Item.Enabled){using(var brush=new SolidBrush(hover))e.Graphics.FillRectangle(brush,new Rectangle(1,1,e.Item.Width-2,e.Item.Height-2));}}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=!e.Item.Enabled?muted:e.Item.Selected?green:text;base.OnRenderItemText(e);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(border))e.Graphics.DrawLine(pen,9,e.Item.Height/2,e.Item.Width-9,e.Item.Height/2);}
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=e.Item.Enabled?green:muted;base.OnRenderArrow(e);}
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){
        var r=e.ImageRectangle;float x=r.Left+r.Width/2f,y=r.Top+r.Height/2f;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var pen=new Pen(green,1.8f)){pen.StartCap=pen.EndCap=LineCap.Round;e.Graphics.DrawLines(pen,new[]{new PointF(x-5,y),new PointF(x-1,y+4),new PointF(x+5,y-4)});}
    }
}

public class QuotaIndicator : Control {
    readonly Timer timer=new Timer();
    readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    double value=-1, target=-1, from=-1;
    long started;
    bool stale;
    public bool TestInstant;
    public NotifyIcon Tray;
    public double DisplayedPercent {get{return value;}}
    public QuotaIndicator(){DoubleBuffered=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;timer.Interval=30;timer.Tick+=Animate;}
    public void SetQuota(double percent,bool outdated){
        percent=percent<0?-1:Math.Max(0,Math.Min(100,percent));
        bool changed=stale!=outdated;stale=outdated;
        if(target==percent){if(TestInstant && value!=target){timer.Stop();value=target;RefreshIcon();}else if(changed)RefreshIcon();return;}
        from=value;target=percent;started=clock.ElapsedMilliseconds;
        if(TestInstant || from<0 || target<0){timer.Stop();value=target;RefreshIcon();}
        else{timer.Start();RefreshIcon();}
    }
    void Animate(object sender,EventArgs e){double t=Math.Min(1,(clock.ElapsedMilliseconds-started)/360.0);value=from+(target-from)*(1-Math.Pow(1-t,3));if(t>=1){value=target;timer.Stop();}RefreshIcon();}
    void RefreshIcon(){Invalidate();if(Tray!=null){var previous=Tray.Icon;Tray.Icon=MakeIcon(value,stale);if(previous!=null)previous.Dispose();}}
    public static Color QuotaColor(double value){return ColorTranslator.FromHtml(value<0?"#94A59B":value<=10?"#F07878":value<=30?"#E7B658":"#43EF8B");}
    public static void DrawSegments(Graphics g,RectangleF bounds,double percent,bool outdated){
        g.SmoothingMode=SmoothingMode.AntiAlias;
        float s=Math.Min(bounds.Width,bounds.Height),cx=bounds.X+bounds.Width/2,cy=bounds.Y+bounds.Height/2;
        int lit=percent<=0?0:Math.Min(12,(int)Math.Ceiling(percent*12/100));
        for(int i=0;i<12;i++){
            double a=(i*30-90)*Math.PI/180;
            var color=i<lit?QuotaColor(percent):ColorTranslator.FromHtml("#294438");
            if(percent<0)color=ColorTranslator.FromHtml("#65766C");
            if(outdated&&i<lit)color=Color.FromArgb(130,color);
            using(var pen=new Pen(color,Math.Max(1.2f,s*.065f))){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,cx+(float)Math.Cos(a)*s*.29f,cy+(float)Math.Sin(a)*s*.29f,cx+(float)Math.Cos(a)*s*.41f,cy+(float)Math.Sin(a)*s*.41f);}
        }
        if(percent<0){using(var font=new Font("Consolas",s*.36f,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(QuotaColor(-1)))using(var format=new StringFormat()){format.Alignment=StringAlignment.Center;format.LineAlignment=StringAlignment.Center;g.DrawString("?",font,brush,bounds,format);}}
        else if(outdated||percent==0){using(var brush=new SolidBrush(outdated?ColorTranslator.FromHtml("#E7B658"):QuotaColor(0)))g.FillEllipse(brush,cx-s*.055f,cy-s*.055f,s*.11f,s*.11f);}
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    public static Icon MakeIcon(double percent,bool outdated){using(var bitmap=new Bitmap(32,32)){using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);DrawSegments(g,new RectangleF(0,0,32,32),percent,outdated);}IntPtr h=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}}}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);DrawSegments(e.Graphics,new RectangleF(0,0,Width,Height),value,stale);}
    protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();Tray=null;}base.Dispose(disposing);}
}
