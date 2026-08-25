// ─────────────────────────────────────────────────────────────────────────────
// CAMBIOS EN LOS DOS HANDLERS EXISTENTES
//
// En la versión mínima el control se invoca directamente desde el handler, sin
// behaviour de MediatR. Se pierde el que la regla sea estructural (hay que
// acordarse de replicar los cuatro puntos en el segundo handler), pero se gana
// algo importante:
//
//   Dentro del handler ya sabemos si serviceResultCode != 0, así que podemos
//   distinguir "rechazo biométrico" de "fallo del servicio de FacePhi" SIN
//   cambiar el contrato HTTP. La app sigue recibiendo 200 { isValid: false }
//   igual que hoy, y el contador no se mueve por una caída de FacePhi.
//
// Son cuatro puntos por handler, marcados abajo como (1) a (4).
// ─────────────────────────────────────────────────────────────────────────────


// ═════════════════════════════════════════════════════════════════════════════
// A) ValidateFaceOnlyCmdHandler   —   handler completo, ya modificado
//    src/micro-person-api/Application/Facephi/Commands/ValidateFaceOnlyCmd.cs
// ═════════════════════════════════════════════════════════════════════════════

public class ValidateFaceOnlyCmdHandler(
    IFacePhiService facePhi,
    IFacePhiBiometricRepository repository,
    IIdentityValidationEvaluator identityValidationEvaluator,
    IBiometricAttemptControlService attemptControl,          // ← (1) nueva dependencia
    IOptions<AppSettings> settings,
    ILogger<ValidateFaceOnlyCmdHandler> logger
) : IRequestHandler<ValidateFaceOnlyCmd, ValidateIdentityV2Result>
{
    private const int FacialAuthenticationPositive = 3;

    private const string Flow = BiometricAttemptControlService.FlowValidateFace;

    private readonly IFacePhiService _facePhi = facePhi;
    private readonly IFacePhiBiometricRepository _repository = repository;
    private readonly IIdentityValidationEvaluator _identityValidationEvaluator = identityValidationEvaluator;
    private readonly IBiometricAttemptControlService _attemptControl = attemptControl;   // ← (1)
    private readonly AppSettings _settings = settings.Value;
    private readonly ILogger<ValidateFaceOnlyCmdHandler> _logger = logger;

    public async Task<ValidateIdentityV2Result> Handle(
        ValidateFaceOnlyCmd cmd,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting face-only validation for IdT24: {IdT24}", cmd.IdT24);

        // ── (2) PUERTA DE ENTRADA ────────────────────────────────────────────
        // Va antes de todo lo demás. Si el cliente está bloqueado, lanza
        // BiometricAttemptsBlockedException y no se ejecuta ni la consulta a
        // Mongo ni la llamada a FacePhi.
        await _attemptControl.EnsureNotBlockedAsync(cmd.IdT24, Flow, cancellationToken);

        var biometric = await _repository.GetByIdT24Async(cmd.IdT24, cancellationToken);

        // No basta con que exista el registro: tiene que tener el token del documento.
        // Esto NO consume intento: FacePhi todavía no se ha invocado.
        if (string.IsNullOrWhiteSpace(biometric?.Token1))
        {
            _logger.LogWarning("No stored document found | IdT24: {IdT24}", cmd.IdT24);

            throw new NotFoundException("Customer does not have a stored document.");
        }

        // ── (3) LA LLAMADA A FACEPHI, ENVUELTA ───────────────────────────────
        // Si revienta (red, timeout, 5xx), se audita como error técnico y se
        // relanza. El contador no se toca: no fue culpa del cliente.
        PassiveLivenessResponse result;

        try
        {
            result = await _facePhi.EvaluatePassiveLivenessToken(
                new PassiveLivenessRequest
                {
                    Token1 = biometric.Token1,
                    BestImageToken = cmd.BestImageToken,
                    Method = biometric.Method.ToString(),
                    TrackingToken = cmd.Tracking?.ExtraData,
                    OperationId = cmd.Tracking?.OperationId
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _attemptControl.RegisterTechnicalErrorAsync(
                cmd.IdT24, Flow, ex.GetType().Name, cancellationToken);

            throw;
        }

        _logger.LogInformation(
            "LivenessLog: {LivenessLog} | AuthenticationLog: {AuthenticationLog} | IdT24: {IdT24}",
            result.passiveLivenessLog, result.facialAuthenticationLog, cmd.IdT24);

        var approved = false;
        var similarity = result.facialAuthenticationSimilarity;

        if (result.ServiceResultCode != 0)
        {
            _logger.LogWarning(
                "Unsuccessful service result | ServiceResultCode: {ServiceResultCode} | " +
                "TransactionId: {TransactionId} | IdT24: {IdT24}",
                result.ServiceResultCode, result.ServiceTransactionId, cmd.IdT24);
        }
        else
        {
            // La prueba de vida es el control principal de este flujo:
            // el cliente ya no presenta el documento físico, sólo su rostro.
            var isAlive = result.passiveLivenessResult == FacephiLivenessResult.Live;

            if (_settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK)
            {
                var meetsSimilarityThreshold =
                    _identityValidationEvaluator.MeetsSimilarityThreshold(similarity);

                approved = meetsSimilarityThreshold && isAlive;

                _logger.LogInformation(
                    "Custom liveness check result | Similarity: {Similarity:F4} | " +
                    "MeetsSimilarityThreshold: {MeetsSimilarityThreshold} | IsAlive: {IsAlive} | IdT24: {IdT24}",
                    similarity, meetsSimilarityThreshold, isAlive, cmd.IdT24);
            }
            else
            {
                approved = result.facialAuthenticationResult == FacialAuthenticationPositive && isAlive;
            }
        }

        var validationStatus = approved
            ? FacialValidationStatus.Approved
            : FacialValidationStatus.Rejected;

        _logger.LogInformation(
            "Face-only validation result: {Status} | Similarity: {Similarity:F4} | IdT24: {IdT24}",
            validationStatus, similarity, cmd.IdT24);

        // ── (4) REGISTRO DEL RESULTADO ───────────────────────────────────────
        // Aquí está la clave de por qué la versión mínima no necesita cambiar el
        // contrato HTTP: dentro del handler sabemos que serviceResultCode != 0
        // significa fallo del servicio, no rechazo del cliente. La app sigue
        // recibiendo 200 { isValid: false }, pero el contador no se mueve.
        if (result.ServiceResultCode != 0)
        {
            await _attemptControl.RegisterTechnicalErrorAsync(
                cmd.IdT24,
                Flow,
                $"serviceResultCode={result.ServiceResultCode}",
                cancellationToken);
        }
        else if (approved)
        {
            await _attemptControl.RegisterSuccessAsync(
                cmd.IdT24, Flow, result.ServiceTransactionId, cancellationToken);
        }
        else
        {
            await _attemptControl.RegisterRejectionAsync(
                cmd.IdT24, Flow, result.ServiceTransactionId, cancellationToken);
        }

        var trackingToken = cmd.Tracking?.ExtraData;

        if (!string.IsNullOrWhiteSpace(trackingToken))
        {
            var reason = (approved
                ? FacePhiTrackingReason.None
                : FacePhiTrackingReason.FacialAuthenticationNotPassed).ToApiString();

            await _facePhi.FinishTrackingAsync(approved, reason, trackingToken, cancellationToken);
        }

        _logger.LogInformation(
            "Face-only validation completed for IdT24: {IdT24} with Status: {Status} " +
            "and TransactionId: {TransactionId}",
            cmd.IdT24, validationStatus, result.ServiceTransactionId);

        return new ValidateIdentityV2Result(validationStatus, result);
    }
}


// ═════════════════════════════════════════════════════════════════════════════
// B) ValidateIdentityV2CmdHandler   —   exactamente los mismos cuatro puntos
//    src/micro-person-api/Application/Facephi/Commands/ValidateIdentityV2Cmd.cs
// ═════════════════════════════════════════════════════════════════════════════
//
// (1) Añadir al constructor primario y al campo:
//
//         IBiometricAttemptControlService attemptControl,
//         ...
//         private readonly IBiometricAttemptControlService _attemptControl = attemptControl;
//
//     Y la constante del flujo:
//
//         private const string Flow = BiometricAttemptControlService.FlowValidateBiometric;
//
// (2) Como PRIMERA línea del Handle, antes de cualquier otra cosa:
//
//         await _attemptControl.EnsureNotBlockedAsync(cmd.IdT24, Flow, cancellationToken);
//
// (3) Envolver la llamada a FacePhi en el mismo try/catch:
//
//         try
//         {
//             result = await _facePhi.EvaluatePassiveLivenessToken(...);
//         }
//         catch (Exception ex)
//         {
//             await _attemptControl.RegisterTechnicalErrorAsync(
//                 cmd.IdT24, Flow, ex.GetType().Name, cancellationToken);
//             throw;
//         }
//
// (4) Después de calcular validationStatus y ANTES del FinishTrackingAsync,
//     el mismo bloque de tres ramas:
//
//         if (result.ServiceResultCode != 0)
//             await _attemptControl.RegisterTechnicalErrorAsync(
//                 cmd.IdT24, Flow, $"serviceResultCode={result.ServiceResultCode}", cancellationToken);
//         else if (approved)
//             await _attemptControl.RegisterSuccessAsync(
//                 cmd.IdT24, Flow, result.ServiceTransactionId, cancellationToken);
//         else
//             await _attemptControl.RegisterRejectionAsync(
//                 cmd.IdT24, Flow, result.ServiceTransactionId, cancellationToken);
//
//
// NOTA sobre el tipo de "result": arriba se declaró como PassiveLivenessResponse
// para poder sacarlo del try. Sustitúyelo por el tipo real que devuelve
// EvaluatePassiveLivenessToken en el micro (míralo en IFacePhiService).
//
// NOTA sobre GetFacePhiDocumentQry: NO se toca. Consultar si hay documento
// almacenado no invoca a FacePhi, luego no es un intento y no debe contar ni
// bloquearse.
