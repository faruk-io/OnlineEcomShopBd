import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE, BACKGROUND, SILENT_ERRORS } from '../config';
import { BuildItemRequest, BuildReport, BuilderSlot, SavedBuild } from '../models/api.models';

/** PC Builder API. Prices and compatibility are decided by the server on every call. */
@Injectable({ providedIn: 'root' })
export class BuilderService {
  private readonly http = inject(HttpClient);

  slots(): Observable<BuilderSlot[]> {
    return this.http.get<BuilderSlot[]>(`${API_BASE}/pc-builder/slots`);
  }
  evaluate(items: BuildItemRequest[]): Observable<BuildReport> {
    return this.http.post<BuildReport>(`${API_BASE}/pc-builder/evaluate`, { items }, { context: new HttpContext().set(BACKGROUND, true).set(SILENT_ERRORS, true) });
  }
  save(name: string | null, items: BuildItemRequest[]): Observable<SavedBuild> {
    return this.http.post<SavedBuild>(`${API_BASE}/pc-builder/builds`, { name, items }, { context: new HttpContext().set(SILENT_ERRORS, true) });
  }
  load(code: string): Observable<SavedBuild> {
    return this.http.get<SavedBuild>(`${API_BASE}/pc-builder/builds/${encodeURIComponent(code)}`, { context: new HttpContext().set(SILENT_ERRORS, true) });
  }
}
