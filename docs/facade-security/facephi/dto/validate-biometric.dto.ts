// src/facephi/dto/validate-biometric.dto.ts
//
// Cambio respecto a la versión base: se eliminó "method".
// El microservicio ya no lo recibe: lo deriva de FacePhiAuthenticateMethod
// en el servidor, para que el cliente no pueda enviar un método inválido.

import { Type } from 'class-transformer';
import {
  IsEnum,
  IsNotEmpty,
  IsNumberString,
  IsOptional,
  IsString,
  ValidateNested,
} from 'class-validator';

import { DocumentType } from '../enums/document-type.enum';

export class BiometricTrackingDto {
  @IsOptional()
  @IsString()
  extraData?: string;

  @IsOptional()
  @IsString()
  operationId?: string;
}

export class ValidateBiometricDto {
  @IsNotEmpty()
  @IsNumberString()
  idT24: string;

  /** Token del recorte de la foto del documento, generado por el widget SelphID. */
  @IsNotEmpty()
  @IsString()
  token1: string;

  /** bestImage tokenizada de la captura facial, generada por el widget Selphi. */
  @IsNotEmpty()
  @IsString()
  bestImageToken: string;

  @IsNotEmpty()
  @IsEnum(DocumentType)
  documentType: DocumentType;

  @IsOptional()
  @ValidateNested()
  @Type(() => BiometricTrackingDto)
  tracking?: BiometricTrackingDto;
}
