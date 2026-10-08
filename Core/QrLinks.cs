using QRCoder;
namespace Encore.Core;
public static class QrLinks {
    public static string Svg(string path, HttpContext context, IConfiguration config) {
        var baseUrl=(config["ENCORE_PUBLIC_URL"] ?? $"{context.Request.Scheme}://{context.Request.Host}").TrimEnd('/');
        if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new RuleException("ENCORE_PUBLIC_URL должен содержать адрес сайта без пути и параметров.",503);
        using var generator=new QRCodeGenerator();
        using var data=generator.CreateQrCode(baseUrl+path,QRCodeGenerator.ECCLevel.Q);
        using var qr=new SvgQRCode(data);
        return qr.GetGraphic(6);
    }
}
