// src/facephi/dto/validate-face-only.dto.ts
//
// Para clientes que ya tienen el documento almacenado en el microservicio:
// no se envía token1 ni documentType, se reutilizan los guardados.

import { Type } from 'class-transformer';
import {
  IsNotEmpty,
  IsNumberString,
  IsOptional,
  IsString,
  ValidateNested,
} from 'class-validator';

import { BiometricTrackingDto } from './validate-biometric.dto';

export class ValidateFaceOnlyDto {
  @IsNotEmpty()
  @IsNumberString()
  idT24: string;

  /** bestImage tokenizada de la captura facial, generada por el widget Selphi. */
  @IsNotEmpty()
  @IsString()
  bestImageToken: string;

  @IsOptional()
  @ValidateNested()
  @Type(() => BiometricTrackingDto)
  tracking?: BiometricTrackingDto;
}
