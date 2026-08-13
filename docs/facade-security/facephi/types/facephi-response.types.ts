// src/facephi/types/facephi-response.types.ts

/**
 * Rutas del microservicio onboarding-micro-person.
 * Verificar en Swagger antes de desplegar: el grupo quedó en /api/v1/facephi.
 */
export const FacephiBackendUrls = {
  validateBiometric: '/api/v1/facephi/validate',
  validateFaceOnly: '/api/v1/facephi/validate-face',
  documentByIdT24: '/api/v1/facephi/document',
} as const;

/**
 * Respuesta de Facephi Identity Validation V2, tal como la reenvía el
 * microservicio sin modificar.
 *
 * facialAuthenticationResult: 0 NONE | 1 NEGATIVE | 3 POSITIVE |
 *                             4 POSE EXCEED | 5 INVALID EXTRACTIONS
 * passiveLivenessResult:      3 Live | 17 NoLive | el resto = no evaluable
 * serviceResultCode:          0 = el servicio se ejecutó (NO significa aprobado)
 */
export interface FacephiValidationResponse {
  serviceTransactionId: string;
  serviceResultCode: number;
  serviceResultLog: string;
  serviceTime: string;
  facialAuthenticationResult: number;
  facialAuthenticationLog: string;
  facialAuthenticationSimilarity: number;
  passiveLivenessResult: number;
  passiveLivenessLog: string;
}

/**
 * Indica si el cliente ya tiene el token del documento almacenado.
 * Determina si la app puede usar validate-face en lugar del flujo completo.
 */
export interface FacephiDocumentResponse {
  hasDocument: boolean;
  documentType: string | null;
}
