using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DaiseyTracker;

/// <summary>Tiny localhost web dashboard (HttpListener, no packages). Opened from the tray.</summary>
sealed class Dashboard : IDisposable
{
    readonly Store _store;
    readonly HttpListener _http = new();
    string _url = "";

    public Dashboard(Store store)
    {
        _store = store;
        for (int port = 5757; port < 5767; port++)
        {
            try
            {
                _http.Prefixes.Clear();
                _http.Prefixes.Add($"http://localhost:{port}/");
                _http.Start();
                _url = $"http://localhost:{port}/";
                break;
            }
            catch (HttpListenerException) { }
        }
        if (_url != "") Task.Run(Loop);
    }

    public void Open()
    {
        if (_url != "") Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
    }

    async Task Loop()
    {
        while (_http.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); } catch { return; }
            try { Handle(ctx); }
            catch (Exception ex) { Paths.LogError("dashboard", ex); ctx.Response.StatusCode = 500; }
            finally { ctx.Response.Close(); }
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        string body, type;
        if (ctx.Request.Url!.AbsolutePath == "/api/summary")
        {
            var q = ctx.Request.QueryString["date"];
            var day = DateTime.TryParse(q, out var d) ? d : DateTime.Today;
            body = JsonSerializer.Serialize(Summary.Compute(_store, day));
            type = "application/json";
        }
        else
        {
            body = Page;
            type = "text/html; charset=utf-8";
        }
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = type;
        ctx.Response.OutputStream.Write(bytes);
    }

    public void Dispose()
    {
        try { _http.Close(); } catch { }
    }

    const string Page = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Daisey Tracker</title>
<style>
:root{--bg:#fff;--fg:#1c1c1e;--mut:#6b6b70;--card:#f3f3f5;--bar:#4f6bed}
@media(prefers-color-scheme:dark){:root{--bg:#161618;--fg:#ececee;--mut:#9a9aa0;--card:#232326;--bar:#7b93ff}}
body{margin:0;background:var(--bg);color:var(--fg);font:15px system-ui,sans-serif}
main{max-width:720px;margin:0 auto;padding:24px 16px}
h1{font-size:20px;margin:0 0 16px}h2{font-size:14px;color:var(--mut);margin:24px 0 8px;text-transform:uppercase;letter-spacing:.05em}
.nav{display:flex;gap:8px;align-items:center;margin-bottom:8px}
button{background:var(--card);color:var(--fg);border:0;border-radius:8px;padding:6px 12px;font:inherit;cursor:pointer}
.row{display:grid;grid-template-columns:140px 1fr 70px;gap:8px;align-items:center;margin:6px 0}
.track{background:var(--card);border-radius:6px;height:14px}.fill{background:var(--bar);height:14px;border-radius:6px}
.n{text-align:right;color:var(--mut);font-variant-numeric:tabular-nums}.empty{color:var(--mut)}
.big{font-size:32px;font-weight:600}
</style></head><body><main>
<h1>Daisey Tracker</h1>
<div class="nav"><button id="prev">&larr;</button><b id="date"></b><button id="next">&rarr;</button><button id="today">Today</button></div>
<div class="big" id="total"></div>
<h2>Category</h2><div id="cat"></div>
<h2>Project</h2><div id="proj"></div>
<h2>Audio (separate, not in totals)</h2><div id="aud"></div>
<script>
let day=new Date();
const iso=d=>d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0');
const fmt=m=>m>=60?Math.floor(m/60)+'h '+Math.round(m%60)+'m':Math.round(m)+'m';
function bars(id,obj){
  const el=document.getElementById(id),e=Object.entries(obj).filter(([,v])=>v>0),max=Math.max(1,...e.map(([,v])=>v));
  el.innerHTML=e.length?e.map(([k,v])=>`<div class="row"><span>${k.replace(/[<&]/g,'')}</span><div class="track"><div class="fill" style="width:${v/max*100}%"></div></div><span class="n">${fmt(v)}</span></div>`).join(''):'<div class="empty">nothing yet</div>';
}
async function load(){
  document.getElementById('date').textContent=iso(day);
  const s=await (await fetch('/api/summary?date='+iso(day))).json();
  document.getElementById('total').textContent=fmt(Object.values(s.categoryMinutes).reduce((a,b)=>a+b,0))+' tracked';
  bars('cat',s.categoryMinutes);bars('proj',s.projectMinutes);bars('aud',s.audioMinutesByProcess);
}
const go=n=>{day.setDate(day.getDate()+n);load()};
prev.onclick=()=>go(-1);next.onclick=()=>go(1);today.onclick=()=>{day=new Date();load()};
load();setInterval(load,10000);
</script></main></body></html>
""";
}
