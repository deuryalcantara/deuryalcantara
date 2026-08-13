// src/facephi/facephi.service.ts

import { Logger } from '@common/logger/services/logger/logger.service';
import { HttpService } from '@nestjs/axios';
import {
  HttpException,
  HttpStatus,
  Injectable,
  InternalServerErrorException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { randomInt, randomUUID } from 'crypto';

import {
  AxiosLikeError,
  RequestHeaders,
} from 'src/modules/softtoken/types/softtoken-response.types';
import { ValidateBiometricDto } from './dto/validate-biometric.dto';
import { ValidateFaceOnlyDto } from './dto/validate-face-only.dto';
import {
  FacephiBackendUrls,
  FacephiDocumentResponse,
  FacephiValidationResponse,
} from './types/facephi-response.types';

const SERVICE_NAME = 'FacephiService';

@Injectable()
export class FacephiService {
  constructor(
    private readonly httpService: HttpService,
    private readonly configService: ConfigService,
    private readonly logger: Logger,
  ) {}

  /**
   * Valida la fotografía del documento contra la captura facial del cliente.
   * El microservicio responde 200 si aprueba y 422 si no; ese estado se
   * propaga tal cual al consumidor.
   */
  async validateBiometric(
    payload: ValidateBiometricDto,
    headers: RequestHeaders,
  ): Promise<FacephiValidationResponse> {
    if (this.isMockEnabled()) {
      return this.buildMockedValidation('validateBiometric', headers);
    }

    return this.post<ValidateBiometricDto, FacephiValidationResponse>(
      FacephiBackendUrls.validateBiometric,
      payload,
      headers,
      'validateBiometric',
    );
  }

  /**
   * Valida únicamente la captura facial, usando el documento que el
   * microservicio ya tiene almacenado para el cliente.
   */
  async validateFaceOnly(
    payload: ValidateFaceOnlyDto,
    headers: RequestHeaders,
  ): Promise<FacephiValidationResponse> {
    if (this.isMockEnabled()) {
      return this.buildMockedValidation('validateFaceOnly', headers);
    }

    return this.post<ValidateFaceOnlyDto, FacephiValidationResponse>(
      FacephiBackendUrls.validateFaceOnly,
      payload,
      headers,
      'validateFaceOnly',
    );
  }

  /**
   * Indica si el cliente ya tiene documento almacenado, para que la app sepa
   * si puede ir por validate-face o debe pedir la captura del documento.
   */
  async getDocumentByIdT24(
    idT24: string,
    headers: RequestHeaders,
  ): Promise<FacephiDocumentResponse> {
    const methodName = 'getDocumentByIdT24';
    const path = `${FacephiBackendUrls.documentByIdT24}/${encodeURIComponent(idT24)}`;

    if (this.isMockEnabled()) {
      this.logger.log(`Respuesta simulada de ${path}`, {
        requestId: this.getRequestId(headers) || undefined,
        serviceName: SERVICE_NAME,
        method: methodName,
      });

      return { hasDocument: randomInt(0, 2) === 1, documentType: 'CED' };
    }

    const requestId = this.getRequestId(headers);
    const baseUrl = this.getBaseUrl();
    const url = `${baseUrl}${path}`;

    this.logger.log(`Invocando endpoint downstream ${path}`, {
      requestId: requestId || undefined,
      serviceName: SERVICE_NAME,
      method: methodName,
      description: `Invocando endpoint downstream ${path}`,
    });

    try {
      const { data } = await this.httpService.axiosRef.get<FacephiDocumentResponse>(
        url,
        {
          headers: this.buildForwardHeaders(headers, requestId),
        },
      );

      return data;
    } catch (error) {
      this.handleUpstreamError(error, methodName, requestId, path);
    }
  }

  private async post<TPayload, TResponse>(
    path: string,
    payload: TPayload,
    headers: RequestHeaders,
    methodName: string,
  ): Promise<TResponse> {
    const requestId = this.getRequestId(headers);
    const baseUrl = this.getBaseUrl();
    const url = `${baseUrl}${path}`;

    this.logger.log(`Invocando endpoint downstream ${path}`, {
      requestId: requestId || undefined,
      serviceName: SERVICE_NAME,
      method: methodName,
      description: `Invocando endpoint downstream ${path}`,
    });

    try {
      const { data } = await this.httpService.axiosRef.post<TResponse>(
        url,
        payload,
        {
          headers: this.buildForwardHeaders(headers, requestId),
        },
      );

      return data;
    } catch (error) {
      this.handleUpstreamError(error, methodName, requestId, path);
    }
  }

  private handleUpstreamError(
    error: unknown,
    methodName: string,
    requestId: string | null,
    path: string,
  ): never {
    if (this.isAxiosLikeError(error)) {
      const status = error.response?.status ?? HttpStatus.BAD_GATEWAY;
      const errorPayload = error.response?.data ?? {
        message: `No fue posible completar la operacion en ${path}`,
      };

      // Un 422 no es un fallo de integración: es el resultado de negocio de
      // una validación biométrica no aprobada. Se propaga igual, pero no se
      // registra como error para no ensuciar las alertas.
      if (status === HttpStatus.UNPROCESSABLE_ENTITY) {
        this.logger.log(`Validacion biometrica no aprobada en ${path}`, {
          requestId: requestId || undefined,
          serviceName: SERVICE_NAME,
          method: methodName,
          description: `Validacion biometrica no aprobada en ${path}`,
        });

        throw new HttpException(errorPayload, status);
      }

      this.logger.error(`Error invocando endpoint downstream ${path}`, {
        requestId: requestId || undefined,
        serviceName: SERVICE_NAME,
        method: methodName,
        description: `Error invocando endpoint downstream ${path}`,
        error: JSON.stringify({
          status,
          data: errorPayload,
          message: error.message,
        }),
      });

      throw new HttpException(errorPayload, status);
    }

    this.logger.error(`Error inesperado invocando endpoint downstream ${path}`, {
      requestId: requestId || undefined,
      serviceName: SERVICE_NAME,
      method: methodName,
      description: `Error inesperado invocando endpoint downstream ${path}`,
      error: JSON.stringify(error),
    });

    throw new InternalServerErrorException(
      'Error interno procesando la solicitud de validacion biometrica',
    );
  }

  /**
   * Respuesta simulada para pruebas sin depender del microservicio ni de
   * tokens reales de Facephi. Reproduce el contrato completo, incluido el 422
   * del rechazo, para que la app pueda integrarse contra el comportamiento real.
   */
  private buildMockedValidation(
    methodName: string,
    headers: RequestHeaders,
  ): FacephiValidationResponse {
    const approved = randomInt(0, 2) === 1;

    const response: FacephiValidationResponse = {
      serviceTransactionId: randomUUID(),
      serviceResultCode: 0,
      serviceResultLog: '[identity] mocked response',
      serviceTime: '120',
      facialAuthenticationResult: approved ? 3 : 1,
      facialAuthenticationLog: approved ? 'Positive' : 'Negative',
      facialAuthenticationSimilarity: approved ? 0.9921 : 0.1132,
      passiveLivenessResult: approved ? 3 : 17,
      passiveLivenessLog: approved ? 'Live' : 'NoLive',
    };

    this.logger.log('Respuesta simulada de validacion biometrica', {
      requestId: this.getRequestId(headers) || undefined,
      serviceName: SERVICE_NAME,
      method: methodName,
      description: `Respuesta simulada: ${approved ? 'aprobada' : 'no aprobada'}`,
    });

    if (!approved) {
      throw new HttpException(response, HttpStatus.UNPROCESSABLE_ENTITY);
    }

    return response;
  }

  private isMockEnabled(): boolean {
    return this.configService.get<string>('FACEPHI_MOCK_ENABLED') === 'true';
  }

  private getBaseUrl(): string {
    const baseUrl = this.configService.get<string>('ONBOARDING_PERSON_BASE_URL');

    if (!baseUrl) {
      throw new InternalServerErrorException('No se configuro la URL base');
    }

    return baseUrl.replace(/\/+$/, '');
  }

  private buildForwardHeaders(
    headers: RequestHeaders,
    requestId: string | null,
  ): Record<string, string> {
    const forwardHeaders: Record<string, string> = {
      'Content-Type': 'application/json',
    };

    if (requestId) {
      forwardHeaders.requestId = requestId;
    }

    const xApiKey = this.getHeaderValue(headers, 'x-api-key');
    if (xApiKey) {
      forwardHeaders['X-API-Key'] = xApiKey;
    }

    const authorization = this.getHeaderValue(headers, 'authorization');
    if (authorization) {
      forwardHeaders.Authorization = authorization;
    }

    const passThroughHeaders = ['app_version', 'device', 'user', 'devicetoken'];
    for (const header of passThroughHeaders) {
      const value = this.getHeaderValue(headers, header);
      if (value) {
        forwardHeaders[header] = value;
      }
    }

    return forwardHeaders;
  }

  private getRequestId(headers: RequestHeaders): string | null {
    return (
      this.getHeaderValue(headers, 'requestid') ||
      this.getHeaderValue(headers, 'requestId')
    );
  }

  private getHeaderValue(headers: RequestHeaders, name: string): string | null {
    const value = headers[name];

    if (Array.isArray(value)) {
      return value[0] || null;
    }

    return value || null;
  }

  private isAxiosLikeError(error: unknown): error is AxiosLikeError {
    if (!error || typeof error !== 'object') {
      return false;
    }

    return Boolean((error as AxiosLikeError).isAxiosError);
  }
}
