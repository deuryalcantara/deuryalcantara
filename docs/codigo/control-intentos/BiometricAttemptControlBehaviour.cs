// Ruta destino:
// src/micro-person-api/Application/Common/Behaviours/BiometricAttemptControlBehaviour.cs
// (junto a ValidationBehaviour, PerformanceBehaviour y UnhandledExceptionBehaviour)
//
// Por qué un behaviour y no código dentro de cada handler:
//   1. El control es idéntico para validate y validate-face. Duplicarlo en dos
//      handlers garantiza que algún día uno de los dos se quede sin actualizar.
//   2. La regla "no se llama a FacePhi estando bloqueado" queda estructural: el
//      handler ni siquiera se ejecuta, no depende de que alguien recuerde poner
//      el return temprano en el sitio correcto.
//   3. Un tercer flujo biométrico futuro sólo tiene que implementar la interfaz.
//
// ORDEN DE REGISTRO (importante): después de ValidationBehaviour. Una petición
// con idT24 vacío debe morir como 400 de validación, no consumir un intento.

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Infrastructure.Biometric;

namespace onboarding_micro_person.Application.Common.Behaviours
{
    public class BiometricAttemptControlBehaviour<TRequest, TResponse>(
        IBiometricAttemptControlService attemptControl,
        IHttpContextAccessor httpContextAccessor,
        ILogger<BiometricAttemptControlBehaviour<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IBiometricAttemptControlled
    {
        private readonly IBiometricAttemptControlService _attemptControl = attemptControl;
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
        private readonly ILogger<BiometricAttemptControlBehaviour<TRequest, TResponse>> _logger = logger;

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            // 1) Puerta de entrada. Si está bloqueado lanza 423 y next() nunca corre,
            //    así que no hay llamada a FacePhi.
            await _attemptControl.EnsureNotBlockedAsync(request.IdT24, request.Flow, cancellationToken);

            TResponse response;

            try
            {
                response = await next();
            }
            catch (BiometricAttemptsBlockedException)
            {
                // No debería llegar aquí (ya se comprobó arriba), pero si algún
                // handler la lanzara, pasa intacta: un bloqueo no es un fallo más.
                throw;
            }
            catch (ValidationException)
            {
                // Petición mal formada: culpa del llamador, no intento del cliente.
                throw;
            }
            catch (NotFoundException)
            {
                // "No hay documento almacenado" no es un rechazo biométrico:
                // FacePhi ni siquiera se invocó. No incrementa ni se audita como
                // error técnico.
                throw;
            }
            catch (Exception ex)
            {
                // Cualquier otra excepción es error técnico: se audita, NO incrementa.
                await _attemptControl.RegisterTechnicalErrorAsync(
                    request.IdT24,
                    request.Flow,
                    ex.GetType().Name,
                    BuildContext(request, serviceTransactionId: null),
                    cancellationToken);

                throw;
            }

            // 2) Resultado del intento. Se apoya en el tipo concreto que devuelven
            //    los dos handlers; si mañana aparece otro tipo de resultado,
            //    conviene extraer una interfaz IBiometricValidationOutcome.
            if (response is ValidateIdentityV2Result outcome)
            {
                var context = BuildContext(request, outcome.Data?.ServiceTransactionId);

                if (outcome.Status == FacialValidationStatus.Approved)
                {
                    await _attemptControl.RegisterSuccessAsync(
                        request.IdT24, request.Flow, context, cancellationToken);
                }
                else
                {
                    await _attemptControl.RegisterRejectionAsync(
                        request.IdT24, request.Flow, context, cancellationToken);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Biometric attempt control could not interpret response of type {ResponseType} | IdT24: {IdT24}",
                    typeof(TResponse).Name, request.IdT24);
            }

            return response;
        }

        private BiometricAttemptContext BuildContext(TRequest request, string? serviceTransactionId) =>
            new()
            {
                RequestId = _httpContextAccessor.HttpContext?.Request.Headers["requestId"].FirstOrDefault(),
                OperationId = request.TrackingOperationId,
                ServiceTransactionId = serviceTransactionId
            };
    }
}
