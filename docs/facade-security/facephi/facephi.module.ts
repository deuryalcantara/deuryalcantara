// src/facephi/facephi.module.ts

import { CommonModule } from '@common/common.module';
import { Module } from '@nestjs/common';

import { FacephiController } from './facephi.controller';
import { FacephiService } from './facephi.service';

@Module({
  imports: [CommonModule],
  controllers: [FacephiController],
  providers: [FacephiService],
})
export class FacephiModule {}
