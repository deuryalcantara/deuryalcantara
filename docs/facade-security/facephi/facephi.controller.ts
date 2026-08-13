// src/facephi/facephi.controller.ts

import {
  Body,
  Controller,
  Get,
  Headers,
  HttpCode,
  HttpStatus,
  Param,
  Post,
} from '@nestjs/common';
import { ApiHeader, ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';

import { RequestHeaders } from 'src/modules/softtoken/types/softtoken-response.types';
import { ValidateBiometricDto } from './dto/validate-biometric.dto';
import { ValidateFaceOnlyDto } from './dto/validate-face-only.dto';
import { FacephiService } from './facephi.service';
import {
  FacephiDocumentResponse,
  FacephiValidationResponse,
} from './types/facephi-response.types';

@ApiTags('facephi')
@Controller('facephi')
export class FacephiController {
  constructor(private readonly facephiService: FacephiService) {}

  @ApiHeader({ name: 'X-API-Key', required: true })
  @ApiOperation({
    summary: 'Valida la fotografia del documento contra la captura facial',
  })
  @ApiResponse({
    status: 200,
    description: 'Validacion biometrica aprobada',
  })
  @ApiResponse({
    status: 422,
    description:
      'Validacion biometrica no aprobada: el rostro no coincide, no se detecto vida, o la captura no pudo evaluarse',
  })
  @HttpCode(HttpStatus.OK)
  @Post('validate-biometric')
  validateBiometric(
    @Body() validateBiometricDto: ValidateBiometricDto,
    @Headers() headers: RequestHeaders,
  ): Promise<FacephiValidationResponse> {
    return this.facephiService.validateBiometric(validateBiometricDto, headers);
  }

  @ApiHeader({ name: 'X-API-Key', required: true })
  @ApiOperation({
    summary: 'Valida la captura facial contra el documento ya almacenado',
  })
  @ApiResponse({
    status: 200,
    description: 'Validacion biometrica aprobada',
  })
  @ApiResponse({
    status: 404,
    description: 'El cliente no tiene documento almacenado',
  })
  @ApiResponse({
    status: 422,
    description: 'Validacion biometrica no aprobada',
  })
  @HttpCode(HttpStatus.OK)
  @Post('validate-face')
  validateFaceOnly(
    @Body() validateFaceOnlyDto: ValidateFaceOnlyDto,
    @Headers() headers: RequestHeaders,
  ): Promise<FacephiValidationResponse> {
    return this.facephiService.validateFaceOnly(validateFaceOnlyDto, headers);
  }

  @ApiHeader({ name: 'X-API-Key', required: true })
  @ApiOperation({
    summary: 'Consulta si el cliente ya tiene documento biometrico almacenado',
  })
  @ApiResponse({
    status: 200,
    description:
      'Indica si el cliente puede validar solo con la captura facial',
  })
  @Get('document/:idT24')
  getDocumentByIdT24(
    @Param('idT24') idT24: string,
    @Headers() headers: RequestHeaders,
  ): Promise<FacephiDocumentResponse> {
    return this.facephiService.getDocumentByIdT24(idT24, headers);
  }
}
