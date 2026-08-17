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
import { GetFacephiDocumentDto } from './dto/get-facephi-document.dto';
import { ValidateBiometricDto } from './dto/validate-biometric.dto';
import { ValidateFaceOnlyDto } from './dto/validate-face-only.dto';
import { FacephiService } from './facephi.service';
import {
  FacephiDocumentResponse,
  ValidateBiometricResponse,
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
    description:
      'isValid indica si la validacion biometrica fue aprobada o no',
  })
  @ApiResponse({ status: 400, description: 'Datos de entrada invalidos' })
  @ApiResponse({ status: 502, description: 'Facephi no disponible' })
  @ApiResponse({ status: 504, description: 'Timeout invocando Facephi' })
  @HttpCode(HttpStatus.OK)
  @Post('validate-biometric')
  validateBiometric(
    @Body() validateBiometricDto: ValidateBiometricDto,
    @Headers() headers: RequestHeaders,
  ): Promise<ValidateBiometricResponse> {
    return this.facephiService.validateBiometric(validateBiometricDto, headers);
  }

  @ApiHeader({ name: 'X-API-Key', required: true })
  @ApiOperation({
    summary: 'Valida la captura facial contra el documento ya almacenado',
  })
  @ApiResponse({
    status: 200,
    description:
      'isValid indica si la validacion biometrica fue aprobada o no',
  })
  @ApiResponse({ status: 400, description: 'Datos de entrada invalidos' })
  @ApiResponse({
    status: 404,
    description: 'El cliente no tiene documento almacenado',
  })
  @ApiResponse({ status: 502, description: 'Facephi no disponible' })
  @HttpCode(HttpStatus.OK)
  @Post('validate-face')
  validateFaceOnly(
    @Body() validateFaceOnlyDto: ValidateFaceOnlyDto,
    @Headers() headers: RequestHeaders,
  ): Promise<ValidateBiometricResponse> {
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
  @ApiResponse({ status: 400, description: 'Datos de entrada invalidos' })
  @Get('document/:idT24')
  getFacephiDocumentByIdT24(
    @Param('idT24') idT24: string,
    @Headers() headers: RequestHeaders,
  ): Promise<FacephiDocumentResponse> {
    return this.facephiService.getDocumentByIdT24(idT24, headers);
  }

  /**
   * TEMPORAL — misma consulta que el GET de arriba, pero recibiendo el idT24
   * en el cuerpo.
   *
   * Existe porque el middleware de cifrado no opera sobre peticiones sin
   * cuerpo, así que la app no puede consumir el GET. Se retira en cuanto el
   * GET quede operativo; hasta entonces los dos delegan en el mismo metodo
   * del service, sin duplicar logica.
   */
  @ApiHeader({ name: 'X-API-Key', required: true })
  @ApiOperation({
    summary: 'TEMPORAL: consulta de documento almacenado por POST',
    description:
      'Alternativa al GET mientras la app no pueda consumirlo por el cifrado. Se retirara cuando el GET quede operativo.',
  })
  @ApiResponse({
    status: 200,
    description:
      'Indica si el cliente puede validar solo con la captura facial',
  })
  @ApiResponse({ status: 400, description: 'Datos de entrada invalidos' })
  @HttpCode(HttpStatus.OK)
  @Post('document')
  getFacephiDocument(
    @Body() getFacephiDocumentDto: GetFacephiDocumentDto,
    @Headers() headers: RequestHeaders,
  ): Promise<FacephiDocumentResponse> {
    return this.facephiService.getDocumentByIdT24(
      getFacephiDocumentDto.idT24,
      headers,
    );
  }
}
