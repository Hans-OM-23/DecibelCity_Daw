import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';

type MeterState = 'idle' | 'starting' | 'recording' | 'stopped' | 'error';

@Injectable()
export class SoundMeterService {
  private readonly destroyRef = inject(DestroyRef);
  readonly state = signal<MeterState>('idle');
  readonly isExample = signal(true);
  readonly level = signal(68);
  readonly peak = signal(72);
  readonly average = signal(64);
  readonly error = signal('');
  readonly unit = computed(() => this.isExample() ? 'dBA' : 'dBFS');
  readonly progress = computed(() => this.isExample() ? 54 : Math.max(0, Math.min(100, ((this.level() + 96) / 96) * 100)));
  readonly canStart = computed(() => this.state() !== 'starting' && this.state() !== 'recording');
  readonly status = computed(() => ({
    idle: 'Vista de ejemplo', starting: 'Esperando permiso', recording: 'Micrófono activo',
    stopped: 'Medición detenida', error: 'Micrófono no disponible',
  })[this.state()]);

  private context?: AudioContext;
  private stream?: MediaStream;
  private source?: MediaStreamAudioSourceNode;
  private frameId?: number;
  private requestId = 0;

  constructor() {
    this.destroyRef.onDestroy(() => this.stop());
  }

  async start(): Promise<void> {
    if (!this.canStart()) return;
    this.error.set('');
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia || typeof AudioContext === 'undefined') {
      this.state.set('error');
      this.error.set('Tu navegador no permite acceder al micrófono. Abre esta página mediante HTTPS o localhost en un navegador compatible.');
      return;
    }

    const request = ++this.requestId;
    this.state.set('starting');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
        video: false,
      });
      if (request !== this.requestId) {
        stream.getTracks().forEach(track => track.stop());
        return;
      }
      this.stream = stream;
      const context = new AudioContext();
      this.context = context;
      await context.resume();
      if (request !== this.requestId) return;

      const analyser = context.createAnalyser();
      analyser.fftSize = 2048;
      const samples = new Float32Array(analyser.fftSize);
      this.source = context.createMediaStreamSource(stream);
      this.source.connect(analyser);
      // The microphone never connects to speakers, a recorder or a network request.
      stream.getAudioTracks().forEach(track => track.addEventListener('ended', () => {
        if (request === this.requestId) this.stop();
      }, { once: true }));
      this.level.set(-96);
      this.peak.set(-96);
      this.average.set(-96);
      this.isExample.set(false);
      this.state.set('recording');
      let previousSample = -Infinity;
      let energy = 0;
      let count = 0;

      const sample = (now: number): void => {
        if (request !== this.requestId || this.state() !== 'recording') return;
        if (now - previousSample >= 120) {
          previousSample = now;
          analyser.getFloatTimeDomainData(samples);
          let sum = 0;
          for (const value of samples) sum += value * value;
          const meanSquare = sum / samples.length;
          const decibels = Math.max(-96, Math.min(0, 10 * Math.log10(Math.max(meanSquare, 1e-12))));
          energy += meanSquare;
          count++;
          this.level.set(Math.round(decibels));
          this.peak.update(peak => Math.max(peak, Math.round(decibels)));
          this.average.set(Math.round(Math.max(-96, Math.min(0, 10 * Math.log10(Math.max(energy / count, 1e-12))))));
        }
        this.frameId = requestAnimationFrame(sample);
      };
      this.frameId = requestAnimationFrame(sample);
    } catch (error: unknown) {
      if (request !== this.requestId) return;
      this.release();
      const name = error instanceof DOMException ? error.name : '';
      this.error.set(name === 'NotAllowedError'
        ? 'El acceso al micrófono está desactivado. Puedes habilitarlo en los permisos de tu navegador y volver a intentar.'
        : name === 'NotFoundError'
          ? 'No encontramos un micrófono conectado. Conecta uno y vuelve a intentar.'
          : 'No se pudo iniciar el micrófono. Revisa que esté disponible y vuelve a intentar.');
      this.state.set('error');
    }
  }

  stop(): void {
    this.requestId++;
    this.release();
    this.state.set(this.isExample() ? 'idle' : 'stopped');
  }

  private release(): void {
    if (this.frameId !== undefined) cancelAnimationFrame(this.frameId);
    this.frameId = undefined;
    this.source?.disconnect();
    this.source = undefined;
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = undefined;
    if (this.context && this.context.state !== 'closed') void this.context.close().catch(() => undefined);
    this.context = undefined;
  }
}
