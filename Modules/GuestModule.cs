using Encore.Core;
namespace Encore.Modules;
public static class GuestModule {
    private static void SetCookie(HttpContext context,GuestSession session) => context.Response.Cookies.Append("encore.guest",session.Id,new CookieOptions {HttpOnly=true,SameSite=SameSiteMode.Strict,Secure=context.Request.IsHttps,Expires=session.Expires,IsEssential=true});
    public static void MapGuestModule(this WebApplication app) {
        app.MapGet("/api/tables",(MusicStore store)=>Results.Ok(store.TableNumbers()));
        app.MapGet("/api/qr",(HttpContext context,IConfiguration config)=>Results.Text(QrLinks.Svg("/",context,config),"image/svg+xml"));
        app.MapPost("/api/guest/select",(TableInput input,MusicStore store,HttpContext context)=> {
            var session=store.CreateSession(store.TableToken(input.TableNumber));
            SetCookie(context,session);return Results.Ok(new {table=session.Table,session.Expires});
        }).RequireRateLimiting("writes");
        app.MapGet("/table/{token}",(string token,MusicStore store,HttpContext context)=> {SetCookie(context,store.CreateSession(token));return Results.Redirect("/");});
        app.MapPost("/api/guest/demo",(MusicStore store,HttpContext context,IWebHostEnvironment env)=> {
            if(!env.IsDevelopment()) return Results.NotFound();
            SetCookie(context,store.CreateSession(store.DemoToken())); return Results.Ok(new {table=1});
        }).RequireRateLimiting("writes");
        app.MapGet("/api/guest/session",(MusicStore store,HttpContext context,IWebHostEnvironment env)=> {
            try {var s=store.Guest(context.Request.Cookies["encore.guest"]);return Results.Ok(new {table=(int?)s.Table,s.Expires,demo=env.IsDevelopment()});}
            catch(RuleException) {return Results.Ok(new {table=(int?)null,expires=(DateTimeOffset?)null,demo=env.IsDevelopment()});}
        });
        app.MapGet("/api/catalog",(string? query,string? genre,MusicStore store)=>Results.Ok(store.Catalog(query,genre)));
        app.MapGet("/api/queue",(MusicStore store,HttpContext context)=> {
            int? table=null;try {table=store.Guest(context.Request.Cookies["encore.guest"]).Table;}catch(RuleException){}
            return Results.Ok(store.Snapshot(table));
        });
        app.MapPost("/api/orders",(OrderInput input,MusicStore store,HttpContext context)=>Results.Ok(store.Add(store.Guest(context.Request.Cookies["encore.guest"]),input.TrackId))).RequireRateLimiting("writes");
        app.MapPost("/api/orders/{id}/vote",(string id,MusicStore store,HttpContext context)=> {store.Vote(store.Guest(context.Request.Cookies["encore.guest"]),id);return Results.Ok(new {ok=true});}).RequireRateLimiting("writes");
    }
    public record TableInput(int TableNumber);
    public record OrderInput(int TrackId);
}
