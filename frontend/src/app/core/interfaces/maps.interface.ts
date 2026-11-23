export interface MapPosition {
  lat: number;
  lng: number;
}

export interface MapBounds {
  south: number;
  west: number;
  north: number;
  east: number;
}

/** Public map configuration. OpenStreetMap needs no API keys. */
export interface MapsConfig {
  tile_url: string;
  attribution: string;
  center: MapPosition;
  bounds: MapBounds;
}

export interface MapPoint {
  id: string;
  title: string;
  label?: string;
  position?: MapPosition | null;
}

/** Search suggestion from the backend (Photon). Verified again on the server when saving. */
export interface PlaceSuggestion {
  place_id: string;
  name: string;
  address: string;
  lat: number;
  lng: number;
}

export interface SelectedPlace {
  placeId: string;
  address: string;
  position: MapPosition;
}

export interface UnresolvedMapPoint {
  id: string;
  title: string;
  reason: string;
}
