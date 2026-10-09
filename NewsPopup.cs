using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public partial class ResetPopup {
    public string NewsText="尚未取得来源消息", NewsMeta="等待同步", NewsLink="";
    public bool HasSignal;
    string messageKey="";
    int scroll, contentHeight;
    bool newsPage, flipping, scrollbarDrag;
    double flipFrom, flipTo, flipValue;
    long flipStarted;
    Bitmap frontImage, backImage;
    Bitmap flipSurface;
    int[] frontPixels,backPixels,flipPixels;
    public double MaxFlipPaintMilliseconds {get;private set;}
    public int FlipPaintCount {get;private set;}
    int[] ReadPixels(Bitmap bitmap){
        var data=bitmap.LockBits(new Rectangle(0,0,Width,Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try{var pixels=new int[Width*Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);return pixels;}finally{bitmap.UnlockBits(data);}
    }
    readonly Font newsFont=new Font("Microsoft YaHei UI",11.3f,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Rectangle newsBox=new Rectangle(23,80,306,139),bodyBox=new Rectangle(36,112,277,96);
    public bool IsNewsPage {get{return newsPage;}}
    public bool IsFlipping {get{return flipping;}}
    public int NewsScroll {get{return scroll;}}
    public event EventHandler NewsSourceClicked;
    public void SetNews(string key,string text,string meta,string link){
        if(messageKey==key&&NewsText==text&&NewsMeta==meta&&NewsLink==link)return;
        if(messageKey!=key){scroll=0;messageKey=key;}
        NewsText=String.IsNullOrWhiteSpace(text)?"暂无可展示的来源消息":text;NewsMeta=meta;NewsLink=link;
        if(Visible&&!flipping&&newsPage)Invalidate();
    }
    void DrawButton(Graphics g,string text){
        using(var path=Rounded(new Rectangle(235,245,94,24),6))using(var brush=new SolidBrush(ColorTranslator.FromHtml("#13281D")))using(var pen=new Pen(border)){g.FillPath(brush,path);g.DrawPath(pen,path);}
        DrawText(g,text,"Microsoft YaHei UI",11,green,244,247,82,19);
    }
    void DrawNews(Graphics g){
        g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Rounded(new Rectangle(0,0,Width-1,Height-1),14))using(var pen=new Pen(border))g.DrawPath(pen,p);
        DrawText(g,"❯ CODEX RESET","Consolas",13,green,23,21,140,19);
        DrawText(g,"2 / 2","Consolas",11,muted,292,21,37,19);
        DrawText(g,HasSignal?"有新的官方重置信号":"暂无官方重置信号","Microsoft YaHei UI",18,amber,23,49,306,25);
        using(var p=Rounded(newsBox,9))using(var brush=new SolidBrush(ColorTranslator.FromHtml("#1A1910")))using(var pen=new Pen(ColorTranslator.FromHtml("#665327"))){g.FillPath(brush,p);g.DrawPath(pen,p);}
        DrawText(g,"最近消息","Microsoft YaHei UI",11,green,36,88,90,18);
        DrawText(g,NewsMeta,"Microsoft YaHei UI",10,muted,134,88,180,18);
        using(var format=new StringFormat()){format.Trimming=StringTrimming.None;
            contentHeight=(int)Math.Ceiling(g.MeasureString(NewsText,newsFont,bodyBox.Width,format).Height);
            scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,contentHeight-bodyBox.Height)));
            var saved=g.Save();g.SetClip(bodyBox);
            using(var brush=new SolidBrush(white))g.DrawString(NewsText,newsFont,brush,new RectangleF(bodyBox.X,bodyBox.Y-scroll,bodyBox.Width,Math.Max(bodyBox.Height,contentHeight+4)),format);
            g.Restore(saved);
        }
        if(contentHeight>bodyBox.Height){
            float h=Math.Max(18,bodyBox.Height*(float)bodyBox.Height/contentHeight),y=bodyBox.Y+(bodyBox.Height-h)*scroll/(contentHeight-bodyBox.Height);
            using(var brush=new SolidBrush(ColorTranslator.FromHtml("#665327")))g.FillRectangle(brush,319,y,4,h);
        }
        DrawText(g,"● "+UpdateText,"Microsoft YaHei UI",10,Offline?amber:muted,23,224,182,16);
        DrawText(g,"查看原文 ↗","Microsoft YaHei UI",10,String.IsNullOrEmpty(NewsLink)?muted:green,242,224,87,16);
        DrawText(g,"Data: codex-reset.com","Consolas",10,muted,23,249,210,15);
        DrawButton(g,"↶ 返回监控");
    }
    Bitmap CaptureFace(bool back){var image=new Bitmap(Width,Height);using(var g=Graphics.FromImage(image)){g.Clear(BackColor);if(back)DrawNews(g);else DrawFront(g);}return image;}
    public void FlipPage(){
        if(flipping)return;Pinned=true;scrollbarDrag=false;StopWheelHook();
        if(TestMode&&TestInstant){newsPage=!newsPage;flipValue=newsPage?1:0;Invalidate();return;}
        ReleaseFaces();frontImage=CaptureFace(false);backImage=CaptureFace(true);
        frontPixels=ReadPixels(frontImage);backPixels=ReadPixels(backImage);flipPixels=new int[Width*Height];flipSurface=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);MaxFlipPaintMilliseconds=0;FlipPaintCount=0;
        flipFrom=newsPage?1:0;flipTo=newsPage?0:1;flipValue=flipFrom;flipStarted=clock.ElapsedMilliseconds;flipping=true;UpdateCardRegion();animation.Interval=10;animation.Start();Invalidate();
    }
    void AdvanceFlip(long now){
        if(!flipping)return;double t=Math.Min(1,(now-flipStarted)/460.0);double eased=t*t*(3-2*t);flipValue=flipFrom+(flipTo-flipFrom)*eased;
        if(t>=1){flipping=false;newsPage=flipTo==1;ReleaseFaces();if(newsPage&&Visible)StartWheelHook();}UpdateCardRegion();Invalidate();
    }
    void UpdateCardRegion(){
        // Clip the native window itself: painting a narrower card alone leaves an opaque backdrop.
        using(var path=Rounded(new Rectangle(0,0,Width,Height),14)){
            Region next;
            if(flipping){
                path.Flatten(null,.5f);double angle=flipValue*Math.PI;double faceAngle=flipValue<.5?angle:angle-Math.PI;
                var points=path.PathPoints;
                for(int i=0;i<points.Length;i++)points[i]=Project(points[i].X,points[i].Y,faceAngle);
                using(var projected=new GraphicsPath()){projected.AddPolygon(points);next=new Region(projected);}
            }else next=new Region(path);
            var previous=Region;Region=next;if(previous!=null)previous.Dispose();
        }
    }
    PointF Project(float x,float y,double angle){
        double dx=x-Width/2.0,z=dx*Math.Sin(angle),scale=1000/(1000+z);
        // Fit the perspective inside the existing window without enlarging its hit area.
        double fit=1-0.15*Math.Abs(Math.Sin(angle));
        return new PointF((float)(Width/2.0+dx*Math.Cos(angle)*scale*fit),(float)(Height/2.0+(y-Height/2.0)*scale*fit));
    }
    bool PaintFlip(Graphics g){
        if(!flipping)return false;g.Clear(BackColor);double angle=flipValue*Math.PI;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var pixels=flipValue<.5?frontPixels:backPixels;double faceAngle=flipValue<.5?angle:angle-Math.PI;
        double sine=Math.Sin(faceAngle),cosine=Math.Cos(faceAngle),fit=1-.15*Math.Abs(sine);
        Array.Clear(flipPixels,0,flipPixels.Length);
        // Inverse perspective mapping: one bitmap upload per frame, instead of many GDI+ strip transforms.
        if(Math.Abs(cosine)>.0001){
            for(int x=0;x<Width;x++){
                double q=(x+.5-Width/2.0)/(fit*cosine),dx=q/(1-q*sine/1000),sourceX=dx+Width/2.0;
                if(sourceX<0||sourceX>=Width)continue;
                int sx=(int)sourceX;double scale=fit/(1+dx*sine/1000);
                int top=Math.Max(0,(int)Math.Ceiling(Height/2.0-Height/2.0*scale)),bottom=Math.Min(Height,(int)Math.Ceiling(Height/2.0+Height/2.0*scale));
                double sy=Height/2.0+(top+.5-Height/2.0)/scale,step=1/scale;
                for(int y=top;y<bottom;y++,sy+=step){int row=(int)sy;if(row>=0&&row<Height)flipPixels[y*Width+x]=pixels[row*Width+sx];}
            }
        }
        var data=flipSurface.LockBits(new Rectangle(0,0,Width,Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try{Marshal.Copy(flipPixels,0,data.Scan0,flipPixels.Length);}finally{flipSurface.UnlockBits(data);}
        g.DrawImageUnscaled(flipSurface,0,0);
        using(var shade=new SolidBrush(Color.FromArgb((int)(50*Math.Sin(angle)),0,0,0)))g.FillPolygon(shade,new[]{Project(0,0,faceAngle),Project(Width,0,faceAngle),Project(Width,Height,faceAngle),Project(0,Height,faceAngle)});
        watch.Stop();MaxFlipPaintMilliseconds=Math.Max(MaxFlipPaintMilliseconds,watch.Elapsed.TotalMilliseconds);FlipPaintCount++;
        return true;
    }
    public void ScrollNews(int delta){if(!newsPage||flipping)return;scroll=Math.Max(0,Math.Min(Math.Max(0,contentHeight-bodyBox.Height),scroll-delta/120*36));Invalidate();}
    bool HandleNewsMouse(MouseEventArgs e){
        if(flipping)return true;
        if(new Rectangle(235,245,94,24).Contains(e.Location)){FlipPage();return true;}
        if(!newsPage)return false;
        if(new Rectangle(242,220,87,20).Contains(e.Location)&&!String.IsNullOrEmpty(NewsLink)){if(NewsSourceClicked!=null)NewsSourceClicked(this,EventArgs.Empty);}
        else if(e.X>=315&&e.Y>=bodyBox.Top&&e.Y<=bodyBox.Bottom&&contentHeight>bodyBox.Height){scrollbarDrag=true;ScrollTo(e.Y);}
        else{Pinned=true;Invalidate();}
        return true;
    }
    void ScrollTo(int y){scroll=(int)(Math.Max(0,Math.Min(1,(y-bodyBox.Top)/(double)bodyBox.Height))*(contentHeight-bodyBox.Height));Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(scrollbarDrag&&e.Button==MouseButtons.Left)ScrollTo(e.Y);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);scrollbarDrag=false;}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(newsBox.Contains(e.Location))ScrollNews(e.Delta);}
    // The popup does not steal focus. Handle the wheel only while the cursor is in its message box.
    delegate IntPtr WheelProc(int code,IntPtr message,IntPtr data);
    WheelProc wheelProc;IntPtr wheelHook;
    [StructLayout(LayoutKind.Sequential)]struct MouseData{public Point point;public uint mouseData,flags,time;public IntPtr extra;}
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,WheelProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)]static extern IntPtr GetModuleHandle(string module);
    void StartWheelHook(){if(wheelHook!=IntPtr.Zero||TestMode)return;wheelProc=Wheel;
        wheelHook=SetWindowsHookEx(14,wheelProc,GetModuleHandle(null),0);
    }
    IntPtr Wheel(int code,IntPtr message,IntPtr data){
        if(code>=0&&message.ToInt32()==0x020A&&Visible&&newsPage&&!flipping){var m=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));if(newsBox.Contains(PointToClient(m.point))){ScrollNews((short)(m.mouseData>>16));return new IntPtr(1);}}
        return CallNextHookEx(wheelHook,code,message,data);
    }
    void StopWheelHook(){if(wheelHook!=IntPtr.Zero){UnhookWindowsHookEx(wheelHook);wheelHook=IntPtr.Zero;}}
    void ReleaseFaces(){if(frontImage!=null){frontImage.Dispose();frontImage=null;}if(backImage!=null){backImage.Dispose();backImage=null;}if(flipSurface!=null){flipSurface.Dispose();flipSurface=null;}frontPixels=null;backPixels=null;flipPixels=null;}
    void DisposeNews(){StopWheelHook();ReleaseFaces();newsFont.Dispose();}
}
