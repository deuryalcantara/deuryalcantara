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

  const headers = { requestid: 'req-1' };

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

  const rejectedResponse = {
    ...approvedResponse,
    facialAuthenticationResult: 1,
    facialAuthenticationLog: 'Negative',
    passiveLivenessResult: 17,
  };

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
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
          useValue: {
            get: jest.fn().mockReturnValue(baseUrl),
          },
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

    service = module.get<FacephiService>(FacephiService);
    httpService = module.get<HttpService>(HttpService);
    logger = module.get<Logger>(Logger);

    (httpService.axiosRef.post as jest.Mock).mockReset();
    (httpService.axiosRef.get as jest.Mock).mockReset();
  });

  it('should be defined', () => {
    expect(service).toBeDefined();
  });

  describe('validateBiometric', () => {
    it('should return isValid true when downstream approves', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const result = await service.validateBiometric(
        payload as any,
        headers as any,
      );

      expect(result).toEqual({ isValid: true });
    });

    it('should return isValid false when downstream responds 422', async () => {
      (httpService.axiosRef.post as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 422',
        response: { status: 422, data: rejectedResponse },
      });

      const result = await service.validateBiometric(
        payload as any,
        headers as any,
      );

      expect(result).toEqual({ isValid: false });
      expect(logger.error).not.toHaveBeenCalled();
    });

    it('should not expose the facephi codes to the consumer', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const result = await service.validateBiometric(
        payload as any,
        headers as any,
      );

      expect(Object.keys(result)).toEqual(['isValid']);
    });

    it('should call post with the correct url and payload', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      await service.validateBiometric(payload as any, headers as any);

      expect(httpService.axiosRef.post).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/validate`,
        payload,
        expect.objectContaining({
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            requestId: 'req-1',
          }),
        }),
      );
    });

    it('should forward headers and use first value when headers are arrays', async () => {
      (httpService.axiosRef.post as jest.Mock).mockResolvedValue({
        data: approvedResponse,
      });

      const arrayHeaders = {
        requestid: ['req-first', 'req-second'],
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
        expect.objectContaining({
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            requestId: 'req-first',
            Authorization: 'Bearer first',
            app_version: '2.1.0',
            device: 'ios',
            user: 'user-a',
            devicetoken: 'token-a',
          }),
        }),
      );
    });

    it('should propagate a 502 from downstream', async () => {
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
      const module: TestingModule = await Test.createTestingModule({
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
    it('should call the validate-face endpoint and return isValid true', async () => {
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

      expect(result).toEqual({ isValid: true });
      expect(httpService.axiosRef.post).toHaveBeenCalledWith(
        `${baseUrl}/api/v1/facephi/validate-face`,
        faceOnlyPayload,
        expect.any(Object),
      );
    });

    it('should return isValid false when downstream responds 422', async () => {
      (httpService.axiosRef.post as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 422',
        response: { status: 422, data: rejectedResponse },
      });

      const result = await service.validateFaceOnly(
        { idT24: '1', bestImageToken: 'y' } as any,
        headers as any,
      );

      expect(result).toEqual({ isValid: false });
    });

    it('should propagate a 404 when the customer has no stored document', async () => {
      // Un 404 no es "no aprobado": es que no hay documento contra el cual validar.
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

    it('should propagate the downstream error', async () => {
      (httpService.axiosRef.get as jest.Mock).mockRejectedValue({
        isAxiosError: true,
        message: 'Request failed with status code 500',
        response: { status: 500, data: { message: 'error' } },
      });

      await expect(
        service.getDocumentByIdT24('123456789', headers as any),
      ).rejects.toBeInstanceOf(HttpException);
    });
  });
});
