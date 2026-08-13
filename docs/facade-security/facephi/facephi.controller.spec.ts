// src/facephi/facephi.controller.spec.ts

import { Test, TestingModule } from '@nestjs/testing';

import { DocumentType } from './enums/document-type.enum';
import { FacephiController } from './facephi.controller';
import { FacephiService } from './facephi.service';

describe('FacephiController', () => {
  let controller: FacephiController;
  let service: FacephiService;

  const headers = { 'x-api-key': 'key' };

  const validationResponse = {
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

  beforeEach(async () => {
    const mockService = {
      validateBiometric: jest.fn(),
      validateFaceOnly: jest.fn(),
      getDocumentByIdT24: jest.fn(),
    };

    const module: TestingModule = await Test.createTestingModule({
      controllers: [FacephiController],
      providers: [
        {
          provide: FacephiService,
          useValue: mockService,
        },
      ],
    }).compile();

    controller = module.get<FacephiController>(FacephiController);
    service = module.get<FacephiService>(FacephiService);
  });

  it('should be defined', () => {
    expect(controller).toBeDefined();
  });

  it('should call service.validateBiometric', async () => {
    const dto = {
      idT24: '123456789',
      token1: 'document-token',
      bestImageToken: 'best-image-token',
      documentType: DocumentType.CED,
    };

    (service.validateBiometric as jest.Mock).mockResolvedValue(
      validationResponse,
    );

    expect(
      await controller.validateBiometric(dto as any, headers as any),
    ).toEqual(validationResponse);

    expect(service.validateBiometric).toHaveBeenCalledWith(dto, headers);
  });

  it('should call service.validateFaceOnly', async () => {
    const dto = {
      idT24: '123456789',
      bestImageToken: 'best-image-token',
    };

    (service.validateFaceOnly as jest.Mock).mockResolvedValue(
      validationResponse,
    );

    expect(
      await controller.validateFaceOnly(dto as any, headers as any),
    ).toEqual(validationResponse);

    expect(service.validateFaceOnly).toHaveBeenCalledWith(dto, headers);
  });

  it('should call service.getDocumentByIdT24', async () => {
    const response = { hasDocument: true, documentType: 'CED' };

    (service.getDocumentByIdT24 as jest.Mock).mockResolvedValue(response);

    expect(
      await controller.getDocumentByIdT24('123456789', headers as any),
    ).toEqual(response);

    expect(service.getDocumentByIdT24).toHaveBeenCalledWith(
      '123456789',
      headers,
    );
  });
});
