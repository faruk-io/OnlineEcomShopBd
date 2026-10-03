import { Pipe, PipeTransform } from '@angular/core';
import { formatBdt } from './bdt';

@Pipe({ name: 'bdt' })
export class BdtPipe implements PipeTransform {
  transform(value: number | null | undefined, withSymbol = true): string {
    return formatBdt(value, withSymbol);
  }
}
