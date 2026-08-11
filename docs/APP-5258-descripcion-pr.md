# APP-5258 · Integración de validación biométrica Facephi (Identity Validation V2)

## Resumen

Se expone `POST /facephi/validate` en `onboarding-micro-person`, que integra el servicio
**Identity Validation V2** de Facephi (`POST /onboarding/v2/identity`). El servicio compara la foto del rostro
del documento contra la captura facial del cliente y ejecuta la prueba de vida pasiva en una sola llamada.

El endpoint es **genérico de Facephi**, sin semántica de Soft Token, según lo definido por el equipo en el chat
de integración. Su primer consumidor será APP-5223.

## Contexto

- Ticket: **APP-5258** (épica APP-5222). Bloquea **APP-5223** — Enrolamiento de Soft Token mediante validación
  biométrica Facephi.
- Facephi ya estaba integrado en este microservicio para onboarding. Este cambio **reutiliza** `IFacePhiService`
  y la configuración existente; no agrega un cliente nuevo.

## Alcance: qué cambió respecto a la descripción original del ticket

El ticket describía cinco endpoints y un flujo con persistencia por pasos en MongoDB. Identity Validation V2
recibe todos los datos en una sola llamada y devuelve el resultado, sin estado intermedio, por lo que:

| Descrito en el ticket | Estado |
|---|---|
| `POST /facephi/validate` | **Implementado** |
| `POST /facephi/enrollment` | No aplica: no hay registro que crear sin persistencia |
| `POST /facephi/document/front` | No aplica: `token1` se envía en la misma llamada |
| `POST /facephi/document/back` | No aplica: **`token2` no existe en Identity V2** |
| `GET /facephi/enrollment/{idT24}` | No aplica: la operación no tiene estado |
| Base de datos MongoDB nueva | No requerida |

También se descartó `authenticateUser/v2` en favor de Identity V2, porque el flujo de Soft Token vuelve a pedir
al cliente que escanee su cédula; `authenticateUser/v2` habría exigido persistir un template biométrico por
usuario.

## Contrato del endpoint

**Request**

```json
{
  "token1": "<imagen base64 del frente de la cédula, o TokenFaceImage>",
  "bestImageToken": "<bestImage tokenizada del widget Selphi>",
  "method": "3",
  "tracking": {
    "extraData": "<token del SDK>",
    "operationId": "<UUID>"
  }
}
```

- `token1`, `bestImageToken` y `method` son obligatorios. `method` sólo admite `"3"` (imagen abierta del
  documento) o `"5"` (`TokenFaceImage` del widget SelphID).
- `tracking` es opcional y **se omite del payload hacia Facephi** cuando no viene, en lugar de enviarse con
  nulos.

**Respuestas**

El cuerpo es la respuesta de Facephi **sin modificar**. Lo único que aporta el microservicio es el código HTTP:

| Código | Cuándo |
|---|---|
| `200` | Rostro POSITIVE, prueba de vida Live y similitud sobre el umbral configurado |
| `422` | Rechazo biométrico, similitud insuficiente, o captura no evaluable |
| `400` | Falla la validación del request |
| `502` / `504` | Facephi no disponible, respuesta inválida o timeout |

## Por qué se agregó el mapeo de códigos HTTP

Facephi responde **HTTP 200 con `serviceResultCode: 0` incluso cuando rechaza al cliente**. Su documentación
describe el código `1` (NEGATIVE) como *"el proceso se ejecutó correctamente, la comparación del patrón facial
no coincide"*, y la tabla de `serviceResultCode` sólo documenta el valor `0`: no existe un código que signifique
"rechazado".

Sin este mapeo, cada consumidor tendría que conocer las tablas de códigos biométricos de Facephi para saber si
la persona pasó. Un error en esa interpretación activaría el segundo factor de autenticación de un cliente que
Facephi rechazó, sin ninguna señal de error.

## Regla de decisión

| Condición | Resultado |
|---|---|
| `serviceResultCode != 0` | Error |
| `passiveLivenessResult` 10 o 15 (error interno / licencia) | Error |
| `facialAuthenticationResult == 1` (NEGATIVE), o `passiveLivenessResult` 17 (NoLive) o 1 (Spoof) | Rechazo |
| `facialAuthenticationResult == 3` y `passiveLivenessResult == 3`, y similitud ≥ umbral | Aprobado |
| Cualquier otro caso | Recaptura |

Se usa **lista blanca**: sólo el valor `3` aprueba. La documentación de `evaluatePassiveLiveness` muestra un
rechazo NoLive que devuelve `0` en lugar de `17`, así que descartar únicamente el `17` dejaría pasar rechazos.

El umbral de similitud es configuración (`IDENTITY_VALIDATION_APPROVED_SIMILARITY_VALUE`) y se aplica **además**
del veredicto de Facephi, nunca en su lugar.

## Archivos

**Nuevos**

- `Common/Enums/FacePhi/ValidationOutcome.cs` — veredicto interno; determina el código HTTP y no se serializa.
- `Common/Models/Biometric/ValidateIdentityV2Result.cs` — resultado del comando: veredicto + respuesta de Facephi.
- `Common/Interfaces/Biometric/IIdentityValidationEvaluator.cs`
- `Services/Biometric/IdentityValidationEvaluator.cs` — aplica el umbral de similitud de este flujo.
- `Common/Exceptions/FacephiIntegrationException.cs`

**Modificados**

- `Endpoints/FacePhi.cs` — expone `POST /facephi/validate` y mapea el veredicto al código HTTP.
- `Application/Facephi/Commands/ValidateIdentityV2Cmd.cs` — comando, validador y handler.
- `Services/Biometric/FacePhiService.cs` — headers `x-api-key` y `family`, propagación de errores HTTP y de red.
- `Program.cs` — registro del evaluador.
- `AppSettings` y `appsettings.json` — llave del umbral.
- `tests/UnitTests/Application/Facephi/` — carpeta renombrada (antes `SoftTokenFacephi`) y tests.

## Pruebas

`dotnet test --filter "TestCategory=Facephi"`

Cubierto:

- Validador: campos obligatorios, `method` aceptado y rechazado, `operationId` que no es UUID.
- Mapeo del request hacia Facephi, campo por campo.
- Regla de decisión: diez combinaciones de códigos de Facephi.
- Umbral: rechazo por similitud insuficiente, y que **no se consulte el umbral cuando Facephi ya rechazó**.
- Deserialización del ejemplo de respuesta de la documentación, que es lo único que detecta un binding roto de
  nombres JSON.

## Consideraciones de seguridad

- Los logs registran método, códigos de resultado y `serviceTransactionId`. **No registran** `token1`,
  `bestImageToken` ni `extraData`, que son datos biométricos.
- El endpoint devuelve los códigos de Facephi, no los tokens.
- La API key no se versiona: viene de configuración por ambiente.

## Pendiente / requiere confirmación

1. **Prueba punta a punta con Facephi real.** Requiere un `bestImageToken` válido, que sólo genera el widget
   Selphi, y la API key del ambiente de pruebas. Lo verificado hasta ahora es el contrato contra la
   documentación oficial y el flujo completo contra un stub local.
2. **Valor del umbral de similitud.** Debe definirlo riesgo. Provisionalmente se usa el mismo de onboarding.
3. **Autenticación del endpoint**: pendiente definir si lleva `[Authorize]` o queda interno tras el gateway.
4. **Header `family`**: la documentación en español indica `OnBoarding` y la inglesa `Onboarding`; falta
   confirmar cuál acepta el ambiente.
5. **Bypass de QA.** `EvaluateFinalResult` de onboarding fuerza SUCCESS en el ambiente QA. Este flujo no lo
   reutiliza, pero conviene decidir explícitamente si la activación del segundo factor debe poder saltarse la
   validación por configuración.

## Notas para quien revise

- El cuerpo de la respuesta es intencionalmente el JSON de Facephi tal cual, para no introducir vocabulario
  propio en el contrato. El veredicto vive en el handler, donde está cubierto por pruebas.
- Identity V2 rompe la convención de nombres de los otros endpoints de Facephi: usa
  `facialAuthenticationResult` y `passiveLivenessResult`, mientras que `authenticateUser/v2` y
  `evaluatePassiveLiveness` usan el prefijo `service`. Los modelos no son intercambiables entre endpoints.
- Identity V2 compara la cara del documento contra el selfie; **no valida la autenticidad del documento**. Si el
  flujo lo requiere, es un servicio distinto de Facephi.
