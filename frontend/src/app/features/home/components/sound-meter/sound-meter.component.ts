import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { SoundMeterService } from '../../services/sound-meter.service';

@Component({
  selector: 'app-sound-meter',
  imports: [IconComponent],
  providers: [SoundMeterService],
  templateUrl: './sound-meter.component.html',
  styleUrl: './sound-meter.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SoundMeterComponent {
  readonly meter = inject(SoundMeterService);
}
