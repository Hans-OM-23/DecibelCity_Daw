import { SelectedPlace } from './maps.interface';

export interface ReportDraft {
  location: string;
  selectedPlace: SelectedPlace | null;
  source: string;
  includeDecibels: boolean;
  decibels: number | null;
  description: string;
}
