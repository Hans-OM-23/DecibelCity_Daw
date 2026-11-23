export interface Department {
  id_departamento: number;
  ubigeo: string;
  nombre_departamento: string;
}

export interface Province {
  id_provincia: number;
  id_departamento: number;
  ubigeo: string;
  nombre_provincia: string;
}

export interface District {
  id_distrito: number;
  id_provincia: number;
  ubigeo: string;
  nombre_distrito: string;
}

export interface TerritoriesResponse {
  departamentos: Department[];
  provincias: Province[];
  distritos: District[];
}

export interface UserProfile {
  id_usuario: number;
  nombre_usuario: string;
  nombres: string;
  apellidos: string;
  telefono: string;
  direccion: string;
  id_distrito: number;
  nombre_distrito: string;
  id_provincia: number;
  nombre_provincia: string;
  id_departamento: number;
  nombre_departamento: string;
  rol: string;
  fecha_registro: string;
}

export interface LoginPayload {
  nombre_usuario: string;
  contrasena: string;
  turnstile_token?: string;
}

export interface AuthConfig {
  turnstile_enabled: boolean;
  turnstile_site_key: string;
}

export interface RegisterPayload {
  nombres: string;
  apellidos: string;
  telefono: string;
  direccion: string;
  id_departamento: number;
  id_provincia: number;
  id_distrito: number;
  nombre_usuario: string;
  contrasena: string;
}

export interface ApiErrorResponse {
  detail: string | { type: string; loc: (string | number)[]; msg: string }[];
}
