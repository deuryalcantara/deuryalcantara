// Ruta destino:
// src/micro-person-api/Common/Exceptions/Handlers/BiometricAttemptsBlockedExceptionHandler.cs
//
// El micro ya tiene manejo de excepciones (IExceptionHandler / middleware). Hay
// dos formas de integrar esto; usa la que corresponda a lo que encuentres:
//
//  A) Si existe un CustomExceptionHandler con un diccionario de mapeos
//     (patrón de la plantilla Clean Architecture), añade la entrada:
//         { typeof(BiometricAttemptsBlockedException), HandleAttemptsBlockedException }
//     y copia el cuerpo de TryHandleAsync como método privado.
//
//  B) Si no, registra este handler ANTES del genérico:
//         builder.Services.AddExceptionHandler<BiometricAttemptsBlockedExceptionHandler>();
//     (el orden importa: gana el primero que devuelve true).
//
// Lo que no es negociable es la salida: status 423, cabecera Retry-After y un
// cuerpo del que la app pueda sacar cuánto falta. Un 423 sin Retry-After obliga
// a Móvil a inventarse el temporizador.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using onboarding_micro_person.Common.Exceptions;

namespace onboarding_micro_person.Common.Exceptions.Handlers
{
    public class BiometricAttemptsBlockedExceptionHandler(
        ILogger<BiometricAttemptsBlockedExceptionHandler> logger) : IExceptionHandler
    {
        private const int StatusLocked = StatusCodes.Status423Locked;

        private readonly ILogger<BiometricAttemptsBlockedExceptionHandler> _logger = logger;

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (exception is not BiometricAttemptsBlockedException blocked)
            {
                return false;
            }

            _logger.LogWarning(
                "Returning 423 Locked | IdT24: {IdT24} | BlockedUntil: {BlockedUntil} | RetryAfter: {RetryAfter}",
                blocked.IdT24, blocked.BlockedUntil, blocked.RetryAfterSeconds);

            httpContext.Response.StatusCode = StatusLocked;
            httpContext.Response.Headers.RetryAfter = blocked.RetryAfterSeconds.ToString();

            var problemDetails = new ProblemDetails
            {
                Status = StatusLocked,
                Title = "Biometric validation temporarily blocked",
                Detail = "The customer exceeded the allowed number of failed biometric attempts.",
                Type = "https://tools.ietf.org/html/rfc4918#section-11.3",
                Instance = httpContext.Request.Path
            };

            // Extensiones: lo que la app necesita para pintar la pantalla.
            // isValid viaja también aquí para que el cliente pueda leer siempre
            // el mismo campo, responda 200 o 423.
            problemDetails.Extensions["code"] = BiometricAttemptsBlockedException.ErrorCode;
            problemDetails.Extensions["isValid"] = false;
            problemDetails.Extensions["blocked"] = true;
            problemDetails.Extensions["blockedUntil"] = blocked.BlockedUntil;
            problemDetails.Extensions["retryAfterSeconds"] = blocked.RetryAfterSeconds;

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

            return true;
        }
    }
}
