using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
internal sealed class QuotaWindow { public double Remaining; public DateTimeOffset? Reset; }
internal sealed class AccountQuota { public QuotaWindow Five,Week; public DateTimeOffset Updated=DateTimeOffset.UtcNow; }
internal static class NativeData {
    internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=4000000,RecursionLimit=100};
    internal static object Get(object node,string key){var d=node as Dictionary<string,object>;object v;return d!=null && d.TryGetValue(key,out v)?v:null;}
    internal static string Text(object node,string key){return Convert.ToString(Get(node,key),CultureInfo.InvariantCulture);}
    internal static object Decode(string raw){return Json.DeserializeObject(raw);}
    internal static string Remaining(DateTimeOffset target,DateTimeOffset now){double seconds=Math.Ceiling((target-now).TotalSeconds);if(seconds<=0)return "时间已到 · 请核实额度";var t=TimeSpan.FromSeconds(seconds);return (t.TotalDays>=1?Math.Floor(t.TotalDays)+"天 ":"")+String.Format("{0:00}:{1:00}:{2:00}",t.Hours,t.Minutes,t.Seconds);}
    internal static int Hours(object value){int h;return Int32.TryParse(Convert.ToString(value),out h)&&h>=1&&h<=24?h:2;}
    internal static AccountQuota Quota(string raw){
        var message=Decode(raw);if(Get(message,"error")!=null)throw new FormatException("额度查询失败");var result=Get(message,"result");
        var buckets=Get(result,"rateLimitsByLimitId");var bucket=buckets!=null?Get(buckets,"codex"):Get(result,"rateLimits");
        if(bucket==null || (buckets==null && Text(bucket,"limitId")!="" && Text(bucket,"limitId")!="codex"))throw new FormatException("没有 Codex 额度");
        var q=new AccountQuota();foreach(string key in new[]{"primary","secondary"}){
            var w=Get(bucket,key);int minutes;if(!Int32.TryParse(Text(w,"windowDurationMins"),out minutes)||(minutes!=300&&minutes!=10080)||Get(w,"usedPercent")==null)continue;
            double used;if(!Double.TryParse(Text(w,"usedPercent"),NumberStyles.Float,CultureInfo.InvariantCulture,out used)||Double.IsNaN(used)||Double.IsInfinity(used)||used<0||used>100)throw new FormatException("额度百分比无效");
            var window=new QuotaWindow{Remaining=100-used};if(Get(w,"resetsAt")!=null){long seconds;if(!Int64.TryParse(Text(w,"resetsAt"),out seconds)||seconds<=0)throw new FormatException("额度恢复时间无效");window.Reset=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(seconds);}
            if(minutes==300)q.Five=window;else q.Week=window;
        }if(q.Five==null&&q.Week==null)throw new FormatException("未提供已知额度窗口");return q;
    }
    internal static string ResetTime(QuotaWindow w){return w==null||!w.Reset.HasValue?"--:--":w.Reset.Value.ToLocalTime().ToString("HH:mm");}
    internal static string FindCodex(){
        foreach(var p in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{var file=Path.Combine(p.Trim('"'),"codex.exe");if(File.Exists(file))return file;}catch{}}
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        try{return Directory.Exists(root)?Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault():null;}catch{return null;}
    }
    static object Expand(object[] table,int index,int depth){
        if(index==-5)return null;if(index<0||index>=table.Length||depth>40)throw new FormatException("AIHOT 数据引用异常");var node=table[index];var dict=node as Dictionary<string,object>;
        if(dict!=null){var output=new Dictionary<string,object>();foreach(var pair in dict){var m=Regex.Match(pair.Key,@"^_(\d+)$");if(!m.Success)throw new FormatException("AIHOT 对象结构异常");int k=Int32.Parse(m.Groups[1].Value);if(k>=table.Length||!(table[k] is string))throw new FormatException("AIHOT 字段异常");output[(string)table[k]]=Expand(table,Convert.ToInt32(pair.Value),depth+1);}return output;}
        var list=node as object[];if(list!=null)return list.Select(v=>Expand(table,Convert.ToInt32(v),depth+1)).ToArray();return node;
    }
    internal static Dictionary<string,object> PublicData(string html){
        var match=Regex.Match(html,"streamController.enqueue\\((\"(?:\\\\.|[^\"\\\\])*\")\\)");if(!match.Success)throw new FormatException("AIHOT 页面缺少结构化数据");
        var table=Decode((string)Decode(match.Groups[1].Value)) as object[];if(table==null)throw new FormatException("AIHOT 数据无效");
        var source=Get(Get(Expand(table,0,0),"loaderData"),"codex-reset");var stats=Get(source,"stats");var date=Text(stats,"lastResetDate");
        if(Text(source,"schemaVersion")!="1"||!Regex.IsMatch(date,@"^\d{4}-\d{2}-\d{2}$"))throw new FormatException("AIHOT 数据版本或日期异常");var last=DateTimeOffset.Parse(date+"T00:00:00+08:00",CultureInfo.InvariantCulture);if(last>DateTimeOffset.UtcNow.AddMinutes(5))throw new FormatException("AIHOT 重置日期异常");
        var ev=Get(source,"current");string status="暂无新的重置消息",scope="适用范围以来源为准",estimate="暂无预计时间",text="暂无新的重置或发卡消息。";
        if(ev!=null){if(Text(ev,"type")!="direct_reset"&&Text(ev,"type")!="reset_credit")throw new FormatException("AIHOT 事件类型未知");status=Text(ev,"title");if(status=="")status=Text(ev,"displayLabel");scope=Text(Get(ev,"presentation"),"audienceZh");if(scope=="")scope=Text(ev,"scope");estimate=Text(Get(ev,"estimate"),"label");if(estimate=="")estimate=Text(Get(ev,"schedule"),"label");if(estimate=="")estimate="时间以来源确认为准";
            text=Text(ev,"displayLabel")+" · "+status+"\r\n"+estimate+"\r\n适用范围："+scope+"\r\n"+Text(Get(ev,"estimate"),"reason");var posts=Get(ev,"posts") as object[];if(posts!=null)foreach(var post in posts)text+="\r\n\r\n"+Text(post,"fullText");text+="\r\n\r\n发卡不代表额度恢复；预计时间不代表已到账。请在 Codex 内核实。";
        }
        return new Dictionary<string,object>{{"Source","aihot"},{"Last",last.ToString("o")},{"Signal",ev!=null},{"Status",status},{"Scope",scope},{"Estimate",estimate},{"Stats",stats},{"Updated",Get(source,"checkedAt")},{"Alert",new Dictionary<string,object>{{"Key",Text(ev,"id")},{"Text",text},{"Meta","AIHOT 整理 · 非 OpenAI 官方"},{"Link","https://aihot.news/codex-reset"},{"HasSignal",ev!=null}}}};
    }
    internal static Dictionary<string,object> Cache(string raw){var d=Decode(raw) as Dictionary<string,object>;DateTimeOffset last;if(Text(d,"Source")!="aihot"||!DateTimeOffset.TryParse(Text(d,"Last"),out last)||last>DateTimeOffset.UtcNow.AddMinutes(5))throw new FormatException("缓存无效");return d;}
}
