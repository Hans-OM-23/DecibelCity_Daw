export interface Hotspot {
  id: string;
  name: string;
  area: string;
  district: string;
  sector: string;
  decibels: number;
  source: string;
  source_label: string;
  description: string;
  time_label: string;
  x: number;
  y: number;
  reference_limit: number;
  illustrative: boolean;
  /** Approximate OpenStreetMap position; the noise level is illustrative. */
  latitude: number | null;
  longitude: number | null;
}

export interface NoiseSource { value: string; label: string; }

export interface NoiseReportCreate {
  location: string;
  /** OpenStreetMap id: N, W or R followed by the number, e.g. W813046785. */
  place_id: string;
  source: string;
  description: string;
  decibels: number | null;
}

export interface NoiseReport extends NoiseReportCreate {
  id: string;
  created_at: string;
  status: 'received';
  latitude: number;
  longitude: number;
}
