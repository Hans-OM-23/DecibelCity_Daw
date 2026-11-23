import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import type { LatLngBoundsExpression, Map as LeafletMap, Marker, TileLayer } from 'leaflet';
import { MapPoint, MapPosition, UnresolvedMapPoint } from '../../../core/interfaces/maps.interface';
import { LoadedMap, MapsService, MapsUnavailableError } from '../../../core/services/maps.service';
import { IconComponent } from '../icon/icon.component';

interface RenderedMarker {
  id: string;
  marker: Marker;
  position: MapPosition;
  label: HTMLSpanElement;
}

/** Interactive OpenStreetMap map (Leaflet). Points without coordinates are listed, never guessed. */
@Component({
  selector: 'app-map',
  imports: [IconComponent],
  templateUrl: './map.component.html',
  styleUrl: './map.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapComponent {
  readonly points = input<readonly MapPoint[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly pointSelected = output<string>();
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly shown = signal(0);
  readonly unresolved = signal<UnresolvedMapPoint[]>([]);
  private readonly service = inject(MapsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private readonly mapCreated = signal(false);
  private map?: LeafletMap;
  private loaded?: LoadedMap;
  private tiles?: TileLayer;
  private markers: RenderedMarker[] = [];
  private resizeObserver?: ResizeObserver;
  private tileTimer?: ReturnType<typeof setTimeout>;
  private setupVersion = 0;
  private destroyed = false;

  constructor() {
    afterNextRender(() => void this.initialize());
    effect(() => {
      const points = this.points();
      if (this.mapCreated()) untracked(() => this.renderPoints(points));
    });
    effect(() => {
      const selectedId = this.selectedId();
      untracked(() => this.selectMarker(selectedId));
    });
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.setupVersion++;
      this.disposeMap();
    });
  }

  retry(): void {
    if (this.state() === 'loading') return;
    void this.initialize(true);
  }

  private async initialize(retry = false): Promise<void> {
    const version = ++this.setupVersion;
    this.disposeMap();
    this.state.set('loading');
    this.error.set('');
    try {
      const loaded = await this.service.loadMap(retry);
      if (this.destroyed || version !== this.setupVersion) return;
      const { config, leaflet: L } = loaded;
      const { south, west, north, east } = config.bounds;
      const element = this.canvas().nativeElement;
      const map = L.map(element, {
        center: [config.center.lat, config.center.lng],
        zoom: 14,
        minZoom: 11,
        maxBounds: L.latLngBounds([south, west], [north, east]).pad(0.25),
        maxBoundsViscosity: 0.8,
        scrollWheelZoom: false, // Avoids trapping page scroll; zoom with buttons, keyboard or pinch.
      });
      map.attributionControl.setPrefix('<a href="https://leafletjs.com" target="_blank" rel="noopener noreferrer">Leaflet</a>');
      const tiles = L.tileLayer(config.tile_url, { attribution: config.attribution, maxZoom: 19 });
      tiles.once('load', () => {
        if (this.destroyed || version !== this.setupVersion) return;
        clearTimeout(this.tileTimer);
        this.state.set('ready');
      });
      tiles.addTo(map);
      this.tileTimer = setTimeout(() => {
        if (!this.destroyed && version === this.setupVersion && this.state() === 'loading') {
          this.fail(new MapsUnavailableError('tiles', 'No pudimos cargar el mapa. Revisa tu conexión y vuelve a intentar.'));
        }
      }, 20000);
      this.resizeObserver = new ResizeObserver(() => map.invalidateSize());
      this.resizeObserver.observe(element);
      this.map = map;
      this.tiles = tiles;
      this.loaded = loaded;
      this.mapCreated.set(true);
    } catch (error: unknown) {
      if (!this.destroyed && version === this.setupVersion) this.fail(error);
    }
  }

  private renderPoints(points: readonly MapPoint[]): void {
    const map = this.map;
    const L = this.loaded?.leaflet;
    if (!map || !L) return;
    this.clearMarkers();
    const unresolved: UnresolvedMapPoint[] = [];
    for (const point of points) {
      const position = point.position;
      if (!this.service.isValidPosition(position)) {
        unresolved.push({ id: point.id, title: point.title, reason: 'Este punto todavía no tiene coordenadas.' });
        continue;
      }
      const label = document.createElement('span');
      label.className = 'map-label';
      label.textContent = point.label || '●'; // textContent: point data never becomes HTML.
      const title = `${point.title}${point.label ? `, ${point.label}` : ''}`;
      const marker = L.marker([position.lat, position.lng], {
        icon: L.divIcon({ className: 'map-label-marker', html: label, iconSize: [0, 0] }),
        title,
        alt: title,
        keyboard: true,
        riseOnHover: true,
      }).addTo(map);
      marker.on('click', () => this.pointSelected.emit(point.id));
      this.markers.push({ id: point.id, marker, position: { ...position }, label });
    }
    this.unresolved.set(unresolved);
    this.shown.set(this.markers.length);
    if (!this.markers.length) return;
    if (this.selectedId()) {
      this.selectMarker(this.selectedId());
    } else if (this.markers.length === 1) {
      map.setView([this.markers[0].position.lat, this.markers[0].position.lng], 16);
    } else {
      const bounds: LatLngBoundsExpression = this.markers.map(({ position }) => [position.lat, position.lng] as [number, number]);
      map.fitBounds(bounds, { padding: [70, 70], maxZoom: 16 });
    }
  }

  private selectMarker(selectedId: string | null): void {
    this.markers.forEach(entry => {
      const selected = entry.id === selectedId;
      entry.label.classList.toggle('selected', selected);
      entry.marker.setZIndexOffset(selected ? 1000 : 0);
      if (selected) this.map?.panTo([entry.position.lat, entry.position.lng]);
    });
  }

  private fail(error: unknown): void {
    if (this.destroyed) return;
    this.disposeMap();
    this.error.set(this.service.messageFor(error));
    this.state.set('error');
  }

  private clearMarkers(): void {
    this.markers.forEach(({ marker }) => marker.off().remove());
    this.markers = [];
  }

  private disposeMap(): void {
    clearTimeout(this.tileTimer);
    this.resizeObserver?.disconnect();
    this.resizeObserver = undefined;
    this.clearMarkers();
    this.tiles?.off();
    this.map?.remove();
    this.map = undefined;
    this.tiles = undefined;
    this.loaded = undefined;
    this.mapCreated.set(false);
    this.shown.set(0);
    this.unresolved.set([]);
  }
}
