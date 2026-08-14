import { Test, TestingModule } from '@nestjs/testing';

import { DocumentType } from './enums/document-type.enum';
import { FacephiController } from './facephi.controller';
import { FacephiService } from './facephi.service';

describe('FacephiController', () => {
  let controller: FacephiController;
  let service: FacephiService;

  const headers = { requestid: 'req-1' };

  const validationResponse = { isValid: true };

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

  it('should call service.getDocumentByIdT24 with the idT24 from the body', async () => {
    const response = { hasDocument: true, documentType: 'CED' };
    const dto = { idT24: '123456789' };

    (service.getDocumentByIdT24 as jest.Mock).mockResolvedValue(response);

    expect(
      await controller.getFacephiDocument(dto as any, headers as any),
    ).toEqual(response);

    expect(service.getDocumentByIdT24).toHaveBeenCalledWith(
      '123456789',
      headers,
    );
  });
});
