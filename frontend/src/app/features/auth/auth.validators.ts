import { ValidatorFn } from '@angular/forms';

/** Cuenta caracteres Unicode como el contrato Python; solo recorta campos normalizados. */
export function textLength(min: number, max: number, trim = false): ValidatorFn {
  return (control) => {
    const value = String(control.value ?? '');
    const length = Array.from(trim ? value.trim() : value).length;
    return length >= min && length <= max ? null : { textLength: { min, max } };
  };
}

export function trimmedPattern(pattern: RegExp): ValidatorFn {
  return (control) => pattern.test(String(control.value ?? '').trim()) ? null : { pattern: true };
}
