import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { SoundMeterComponent } from './components/sound-meter/sound-meter.component';

@Component({
  selector: 'app-home',
  imports: [RouterLink, IconComponent, SoundMeterComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {}
