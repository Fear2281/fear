using Encore.Core;
using QRCoder;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
namespace Encore.Modules;
public static class DjModule {
    public static void MapDjModule(this WebApplication app) {
        app.MapPost("/api/dj/login",async (LoginInput input,HttpContext context,IConfiguration config,IWebHostEnvironment env)=> {
            var expected=config["ENCORE_DJ_PASSWORD"];
            if(string.IsNullOrEmpty(expected) && env.IsDevelopment()) expected="encore-demo";
            if(string.IsNullOrEmpty(expected)) return Results.Json(new {error="Задайте ENCORE_DJ_PASSWORD на сервере."},statusCode:503);
            if(input.Password == null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(input.Password)),SHA256.HashData(Encoding.UTF8.GetBytes(expected)))) return Results.Json(new {error="Неверный пароль."},statusCode:401);
            await context.SignInAsync("dj",new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Диджей")},"dj")));return Results.Ok(new {ok=true});
        }).RequireRateLimiting("login");
        app.MapGet("/api/dj/session",(HttpContext c)=>Results.Ok(new {authenticated=c.User.Identity?.IsAuthenticated==true}));
        var group=app.MapGroup("/api/dj").RequireAuthorization();
        group.MapPost("/logout",async (HttpContext c)=>{await c.SignOutAsync("dj");return Results.Ok(new {ok=true});});
        group.MapGet("/state",(MusicStore store)=>Results.Ok(store.Snapshot()));
        group.MapPost("/orders/{id}/{action}",(string id,string action,MusicStore store)=> {store.Action(id,action);return Results.Ok(new {ok=true});});
        group.MapPut("/settings",(Settings value,MusicStore store)=> {store.Update(value);return Results.Ok(new {ok=true});});
        group.MapGet("/tables",(MusicStore store)=>Results.Ok(store.Tables()));
        group.MapGet("/tables/{number:int}/qr",(int number, MusicStore store,HttpContext c,IConfiguration config)=> {
            var baseUrl=(config["ENCORE_PUBLIC_URL"] ?? $"{c.Request.Scheme}://{c.Request.Host}").TrimEnd('/');
            using var generator=new QRCodeGenerator();
            using var data=generator.CreateQrCode($"{baseUrl}/table/{store.TableToken(number)}",QRCodeGenerator.ECCLevel.Q);
            using var qr=new SvgQRCode(data);
            return Results.Text(qr.GetGraphic(5),"image/svg+xml");
        });
        group.MapGet("/export",(MusicStore store)=>Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(store.Csv())).ToArray(),"text/csv; charset=utf-8","encore-report.csv"));
    }
    public record LoginInput(string? Password);
}
