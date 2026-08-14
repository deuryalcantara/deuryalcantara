import { IsNotEmpty, IsNumberString } from 'class-validator';

/**
 * La consulta va por POST y no por GET para que el idT24 viaje en el cuerpo:
 * en la ruta quedaría en claro en los logs de acceso y el middleware de
 * cifrado no puede actuar sobre una URL.
 */
export class GetFacephiDocumentDto {
  @IsNotEmpty()
  @IsNumberString()
  idT24: string;
}
