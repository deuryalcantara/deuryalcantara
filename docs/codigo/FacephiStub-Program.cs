// Stub local de Facephi Identity Validation V2.
// Permite probar POST /facephi/validate de punta a punta sin depender del
// ambiente real de Facephi ni de un bestImageToken válido.
//
// Uso:
//   dotnet new web -o FacephiStub
//   (reemplaza el Program.cs generado por este archivo)
//   cd FacephiStub && dotnet run          -> http://localhost:5099
//
// Luego apunta el micro a este stub, en appsettings.Development.json:
//   "FACEPHI_IDENTITY_API_BASE_URL": "http://localhost:5099"
//
// El escenario se elige por lo que envíes en token1:
//   "reject"   -> facialAuthenticationResult = 1  (NEGATIVE)  -> tu API: 422 Rejected
//   "nolive"   -> passiveLivenessResult      = 17 (NoLive)    -> tu API: 422 Rejected
//   "retry"    -> passiveLivenessResult      = 4  (BadQuality)-> tu API: 422 Retry
//   "error"    -> serviceResultCode          = 1              -> tu API: 502 Error
//   "http502"  -> Facephi responde HTTP 502                   -> tu API: 502 Error
//   "timeout"  -> Facephi tarda 60 s                          -> tu API: 502/504
//   cualquier otro -> POSITIVE + Live                         -> tu API: 200 Approved

using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/onboarding/v2/identity", async (HttpRequest request) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);

    var token1 = document.RootElement.TryGetProperty("token1", out var tokenElement)
        ? tokenElement.GetString() ?? string.Empty
        : string.Empty;

    var hasTracking = document.RootElement.TryGetProperty("tracking", out _);

    // Evidencia para la demo: demuestra que el micro envía los headers exigidos
    // por la documentación y que omite "tracking" cuando no hay datos.
    Console.WriteLine("──────────────────────────────────────────────");
    Console.WriteLine($"[stub] x-api-key : {request.Headers["x-api-key"]}");
    Console.WriteLine($"[stub] family    : {request.Headers["family"]}");
    Console.WriteLine($"[stub] tracking  : {(hasTracking ? "presente" : "omitido")}");

    if (token1.Contains("timeout"))
    {
        await Task.Delay(TimeSpan.FromSeconds(60));
    }

    if (token1.Contains("http502"))
    {
        Console.WriteLine("[stub] escenario : HTTP 502 de Facephi");
        return Results.Json(
            new
            {
                status = 502,
                title = "Bad Gateway",
                detail = "Server got an invalid response."
            },
            statusCode: 502);
    }

    var (facial, liveness, resultCode, escenario) = token1 switch
    {
        var t when t.Contains("reject") => (1, 3, 0, "NEGATIVE: el rostro no coincide"),
        var t when t.Contains("nolive") => (3, 17, 0, "NoLive: sin prueba de vida"),
        var t when t.Contains("retry") => (3, 4, 0, "BadQuality: recapturar"),
        var t when t.Contains("error") => (0, 0, 1, "serviceResultCode != 0"),
        _ => (3, 3, 0, "POSITIVE + Live: aprobado")
    };

    Console.WriteLine($"[stub] escenario : {escenario}");

    return Results.Ok(new
    {
        serviceTransactionId = Guid.NewGuid().ToString(),
        serviceResultCode = resultCode,
        serviceResultLog = "[identity] stub",
        serviceTime = "120",
        facialAuthenticationResult = facial,
        facialAuthenticationLog = facial == 3 ? "Positive" : "Negative",
        facialAuthenticationSimilarity = facial == 3 ? 0.99214232 : 0.1132,
        passiveLivenessResult = liveness,
        passiveLivenessLog = liveness == 3 ? "Live" : "NoLive"
    });
});

app.Run("http://localhost:5099");
