// src/facephi/facephi.service.spec.ts

import { Logger } from '@common/logger/services/logger/logger.service';
import { HttpService } from '@nestjs/axios';
import { HttpException, InternalServerErrorException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Test, TestingModule } from '@nestjs/testing';

import { DocumentType } from './enums/document-type.enum';
import { FacephiService } from './facephi.service';

describe('FacephiService', () => {
  let service: FacephiService;
  let httpService: HttpService;
  let logger: Logger;

  const baseUrl = 'http://mock-base-url';

  const buildConfig = (mockEnabled = 'false') => ({
    get: jest.fn((key: string) => {
      if (key === 'ONBOARDING_PERSON_BASE_URL') {
        return baseUrl;
      }

      if (key === 'FACEPHI_MOCK_ENABLED') {
        return mockEnabled;
      }

      return undefined;
    }),
  });

  const payload = {
    idT24: '123456789',
    token1: 'document-token',
    bestImageToken: 'best-image-token',
    documentType: DocumentType.CED,
    tracking: {
      extraData: 'tracking-extra-data',
      operationId: '123e4567-e89b-12d3-a456-426614174000',
    },
  };

  const headers = { 'x-api-key': 'key', requestid: 'req-1' };

  const approvedResponse = {
    serviceTransactionId: '2db602ee-3564-4304-af95-92a52eaae12d',
    serviceResultCode: 0,
    serviceResultLog: '[identity] Service executed ok',
    serviceTime: '2235',
    facialAuthenticationResult: 3,
    facialAuthenticationLog: 'Positive',
    facialAuthenticationSimilarity: 0.99214232,
    passiveLivenessResult: 3,
    passiveLivenessLog: 'Live',
  };

  const buildModule = async (mockEnabled = 'false'): Promise<TestingModule> =>
    Test.createTestingModule({
      providers: [
        FacephiService,
        {
          provide: HttpService,
          useValue: {
            axiosRef: {
              post: jest.fn(),
              get: jest.fn(),
            },
          },
        },
        {
          provide: ConfigService,
          useValue: buildConfig(mockEnabled),
        },
        {
          provide: Logger,
          useValue: {
            log: jest.fn(),
            error: jest.fn(),
          },
        },
      ],
    }).compile();

  beforeEach(async () => {
    const module = await buildModule();

    service = module.get<FacephiService>(FacephiService);
    httpService = module.get<HttpService>(HttpService);
    logger = module.get<Logger>(Logger);
  });

  it('should be defined', () => {
    expect(service).toBeDefined();
  });

  describe('validateBiometric', () => {
    it('should call post with the correct url, payload and headers', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const result = await service.validateBiometric(
        payload as any,
        headers as any,
      );

      expect(result).toEqual(approvedResponse);
      expect(httpService.axiosRef.post).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/validate`,
        payload,
        expect.objectContaining({
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            'X-API-Key': 'key',
            requestId: 'req-1',
          }),
        }),
      );
    });

    it('should forward headers and use the first value when headers are arrays', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const arrayHeaders = {
        requestid: ['req-first', 'req-second'],
        'x-api-key': ['key-first', 'key-second'],
        authorization: ['Bearer first', 'Bearer second'],
        app_version: ['2.1.0', '2.0.0'],
        device: ['ios', 'android'],
        user: ['user-a', 'user-b'],
        devicetoken: ['token-a', 'token-b'],
      };

      await service.validateBiometric(payload as any, arrayHeaders as any);

      expect(httpService.axiosRef.post).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/validate`,
        payload,
        {
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            requestId: 'req-first',
            'X-API-Key': 'key-first',
            Authorization: 'Bearer first',
            app_version: '2.1.0',
            device: 'ios',
            user: 'user-a',
            devicetoken: 'token-a',
          }),
        },
      );
    });

    it('should propagate a 422 from downstream without logging it as an error', async () => {
      const rejectedResponse = {
        ...approvedResponse,
        facialAuthenticationResult: 1,
        facialAuthenticationLog: 'Negative',
        passiveLivenessResult: 17,
      };

      (httpService.axiosRef.post as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 422',
        response: { status: 422, data: rejectedResponse },
      });

      await expect(
        service.validateBiometric(payload as any, headers as any),
      ).rejects.toMatchObject({ status: 422 });

      // Un rechazo biometrico es un resultado de negocio, no una falla.
      expect(logger.error).not.toHaveBeenCalled();
    });

    it('should propagate the downstream status when it fails', async () => {
      (httpService.axiosRef.post as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 502',
        response: { status: 502, data: { message: 'Bad Gateway' } },
      });

      await expect(
        service.validateBiometric(payload as any, headers as any),
      ).rejects.toBeInstanceOf(HttpException);

      expect(logger.error).toHaveBeenCalled();
    });

    it('should throw InternalServerError when the failure is not an axios error', async () => {
      (httpService.axiosRef.post as jest.Mock).mockRejectedValue(
        new Error('boom'),
      );

      await expect(
        service.validateBiometric(payload as any, headers as any),
      ).rejects.toBeInstanceOf(InternalServerErrorException);
    });

    it('should throw InternalServerError when the base url is not configured', async () => {
      const module = await Test.createTestingModule({
        providers: [
          FacephiService,
          {
            provide: HttpService,
            useValue: { axiosRef: { post: jest.fn(), get: jest.fn() } },
          },
          {
            provide: ConfigService,
            useValue: { get: jest.fn().mockReturnValue(undefined) },
          },
          {
            provide: Logger,
            useValue: { log: jest.fn(), error: jest.fn() },
          },
        ],
      }).compile();

      const serviceWithoutUrl = module.get<FacephiService>(FacephiService);

      await expect(
        serviceWithoutUrl.validateBiometric(payload as any, headers as any),
      ).rejects.toBeInstanceOf(InternalServerErrorException);
    });
  });

  describe('validateFaceOnly', () => {
    it('should call the validate-face endpoint', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const faceOnlyPayload = {
        idT24: '123456789',
        bestImageToken: 'best-image-token',
      };

      const result = await service.validateFaceOnly(
        faceOnlyPayload as any,
        headers as any,
      );

      expect(result).toEqual(approvedResponse);
      expect(httpService.axiosRef.post).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/validate-face`,
        faceOnlyPayload,
        expect.any(Object),
      );
    });

    it('should propagate a 404 when the customer has no stored document', async () => {
      (httpService.axiosRef.post as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 404',
        response: { status: 404, data: { message: 'not found' } },
      });

      await expect(
        service.validateFaceOnly({ idT24: '1' } as any, headers as any),
      ).rejects.toMatchObject({ status: 404 });
    });
  });

  describe('getDocumentByIdT24', () => {
    it('should call get with the encoded idT24', async () => {
      (httpService.axiosRef.get as jest.Mock).mockResolvedValue({
        data: { hasDocument: true, documentType: 'CED' },
      });

      const result = await service.getDocumentByIdT24(
        '123456789',
        headers as any,
      );

      expect(result).toEqual({ hasDocument: true, documentType: 'CED' });
      expect(httpService.axiosRef.get).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/document/123456789`,
        expect.any(Object),
      );
    });
  });

  describe('mocked mode', () => {
    it('should not call downstream when FACEPHI_MOCK_ENABLED is true', async () => {
      const module = await buildModule('true');

      const mockedService = module.get<FacephiService>(FacephiService);
      const mockedHttp = module.get<HttpService>(HttpService);

      // El mock decide al azar: se acepta la respuesta aprobada o el 422.
      await mockedService
        .validateBiometric(payload as any, headers as any)
        .then((response) => {
          expect(response.serviceResultCode).toBe(0);
          expect(response.facialAuthenticationResult).toBe(3);
        })
        .catch((error) => {
          expect(error).toBeInstanceOf(HttpException);
          expect(error.getStatus()).toBe(422);
        });

      expect(mockedHttp.axiosRef.post).not.toHaveBeenCalled();
    });
  });
});
