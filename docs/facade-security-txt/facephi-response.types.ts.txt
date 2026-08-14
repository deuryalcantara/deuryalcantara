/**
 * Rutas del microservicio onboarding-micro-person.
 */
export const FacephiBackendUrls = {
  validateBiometric: '/api/v1/facephi/validate',
  validateFaceOnly: '/api/v1/facephi/validate-face',
  documentByIdT24: '/api/v1/facephi/document',
} as const;

/**
 * Respuesta de los endpoints de validación biométrica.
 *
 * El microservicio ya interpreta los códigos de Facephi y devuelve el
 * veredicto: el facade no evalúa nada, sólo reenvía.
 */
export interface ValidateBiometricResponse {
  isValid: boolean;
}

/**
 * Indica si el cliente ya tiene el token del documento almacenado.
 * Determina si la app puede usar validate-face en lugar del flujo completo.
 */
export interface FacephiDocumentResponse {
  hasDocument: boolean;
  documentType: string | null;
}
