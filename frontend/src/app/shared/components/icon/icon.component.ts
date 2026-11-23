import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const paths: Record<string, string> = {
  activity: 'M3 10v4m4-7v10m5-14v20m5-16v12m4-8v4',
  mic: 'M12 15a3 3 0 0 0 3-3V5a3 3 0 0 0-6 0v7a3 3 0 0 0 3 3Zm-7-4v1a7 7 0 0 0 14 0v-1M12 19v3m-4 0h8',
  square: 'M5 5h14v14H5z',
  'arrow-right': 'M4 12h16m-6-6 6 6-6 6',
  leaf: 'M12 21C3 18 2 12 4 7c4 0 7 2 8 6 1-4 4-6 8-6 2 5 1 11-8 14Zm0 0V10m0 3c-3-4-2-7 0-11 3 4 4 7 0 11Z',
  radio: 'M8.5 8.5a5 5 0 0 0 0 7m7-7a5 5 0 0 1 0 7M5 5a10 10 0 0 0 0 14M19 5a10 10 0 0 1 0 14M12 11v2',
  building: 'M3 22V7h7m0 15V2h8v20M18 10h3v12M1 22h22M6 10v1m0 3v1m0 3v1m7-14v1m2-1v1m-2 3v1m2-1v1m-2 3v1m2-1v1m-2 3v1m2-1v1',
  info: 'M12 8v.01M12 11v6M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z',
  shield: 'm12 2 9 4v6c0 5-9 10-9 10S3 17 3 12V6l9-4Zm-4 10 3 3 5-6',
  'map-pin': 'M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0ZM15 10a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  volume: 'M3 9h4l5-5v16l-5-5H3V9Zm13-2a7 7 0 0 1 0 10m3-13a11 11 0 0 1 0 16',
  check: 'm5 12 4 4L19 6',
  plus: 'M12 5v14M5 12h14',
  minus: 'M5 12h14',
  locate: 'M12 2v3m0 14v3M2 12h3m14 0h3M19 12a7 7 0 1 1-14 0 7 7 0 0 1 14 0ZM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  megaphone: 'M3 9h5l9-5v16l-9-5H3V9Zm3 6 1 6h4l-2-6M21 8l2-1m-2 5h2m-2 4 2 1',
  send: 'm3 3 19 9-19 9 4-9-4-9Zm4 9h15',
  'align-left': 'M3 5h18M3 10h14M3 15h18M3 20h12',
  'chevron-down': 'm6 9 6 6 6-6',
  bus: 'M5 17h14V5c0-2-14-2-14 0v12Zm0-8h14M5 17v4m14-4v4M8 13h.01M16 13h.01',
  truck: 'M1 4h14v13H1V4Zm14 5h5l3 5v3h-8M8 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0Zm14 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  x: 'm6 6 12 12M6 18 18 6',
  user: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM4 21v-3c0-5 16-5 16 0v3H4Z',
  sun: 'M12 3v2m0 14v2M3 12h2m14 0h2m-3.5-6.5 1.4-1.4M6.1 17.9l1.4-1.4m0-9-1.4-1.4m11.8 11.8 1.4 1.4M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z',
  cloud: 'M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z',
  'cloud-rain': 'M16 13v6m-4-4v6m-4-2v6M20 16.58A5 5 0 0 0 18 7h-1.26A8 8 0 1 0 4 15.25',
  wind: 'M17.7 7.7A2.5 2.5 0 1 1 15 11H2m18.7 3.7A2.5 2.5 0 1 0 18 11H2m13.7 7.7A2.5 2.5 0 1 1 13 15H2',
};

@Component({
  selector: 'app-icon',
  template: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="path()" /></svg>',
  styles: ':host { display: inline-flex; width: 1.1em; height: 1.1em; flex: 0 0 auto; vertical-align: -.15em; } svg { width: 100%; height: 100%; overflow: visible; }',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IconComponent {
  readonly name = input<string>('activity');
  readonly path = computed(() => paths[this.name()] ?? paths['activity']);
}
