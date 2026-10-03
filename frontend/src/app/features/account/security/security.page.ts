import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiError, MfaSetup, MfaStatus } from '../../../core/models/api.models';
import { errorMessage, isApiError } from '../../../core/interceptors/error.interceptor';
import { AuthService, MfaProof } from '../../../core/services/auth.service';
import { SeoService } from '../../../core/services/seo.service';
import { ToastService } from '../../../core/services/toast.service';
import { QrCodeComponent } from '../../../shared/qr-code.component';
import { applyServerErrors, fieldError, normaliseRecoveryCode, totpDigits } from '../../auth/auth-forms';

const LOW_CODES = 3;

/** Groups a base32 secret in blocks of four for manual entry ("ABCD EFGH ..."). */
export function groupSecret(secret: string): string {
  return (secret.replace(/\s+/g, '').match(/.{1,4}/g) ?? []).join(' ');
}

@Component({
  selector: 'app-security',
  imports: [ReactiveFormsModule, QrCodeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="sec-h">
      <h1 id="sec-h">Security</h1>
      <h2 class="sub">Two-step verification</h2>

      @if (adminNotice()) {
        <div class="alert alert-warn" role="alert" data-testid="admin-mfa-notice">
          <strong>Administrators must use two-step verification.</strong> Set it up to open the admin panel.
        </div>
      }
      @if (loadError()) { <div class="alert alert-error" role="alert">{{ loadError() }}</div> }

      <!-- (d) recovery codes, shown once -->
      @if (recoveryCodes(); as codes) {
        <div class="codes-box" role="region" aria-labelledby="rc-h">
          <h3 id="rc-h">Save your recovery codes</h3>
          <p>Each code works once if you lose access to your authenticator app. They will <strong>not be shown again</strong>. Store them somewhere safe, such as a password manager.</p>
          <ol class="codes" data-testid="recovery-codes">
            @for (c of codes; track c) { <li data-testid="recovery-code">{{ c }}</li> }
          </ol>
          <div class="row">
            <button type="button" id="copy-codes" class="btn btn-outline btn-sm" (click)="copyCodes()">Copy codes</button>
            <span role="status" class="muted">{{ copyMsg() }}</span>
          </div>
          <label class="check"><input id="ack-codes" data-testid="ack-codes" type="checkbox" [checked]="acknowledged()" (change)="acknowledged.set(!acknowledged())" /> I have saved these codes</label>
          <button type="button" id="dismiss-codes" data-testid="dismiss-codes" class="btn btn-primary" [disabled]="!acknowledged()" (click)="dismissCodes()">Done</button>
        </div>
      } @else if (status(); as s) {
        @if (!s.enabled) {
          @if (!setup()) {
            <!-- (a) off -->
            <p>Two-step verification adds a 6-digit code from an authenticator app to your password, so a stolen password alone is not enough to sign in.</p>
            @if (s.required) { <p class="muted">It is required for your role.</p> }
            @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }
            <button type="button" id="mfa-setup" data-testid="mfa-setup" class="btn btn-primary" [disabled]="busy()" (click)="startSetup()">Set up two-step verification</button>
          } @else {
            <!-- (b) setup started -->
            <ol class="steps">
              <li>Install an authenticator app (Google Authenticator, Microsoft Authenticator, Authy, 1Password...).</li>
              <li>Scan this QR code, or type the key below into the app.</li>
            </ol>
            @if (setup(); as st) {
              <div class="qr"><app-qr-code [value]="st.otpAuthUri" label="QR code for your authenticator app" /></div>
              <p class="manual">Can't scan? Enter this key manually:
                <code id="mfa-secret" data-testid="mfa-secret" class="secret">{{ secretText() }}</code>
              </p>
            }
            <form [formGroup]="enableForm" (ngSubmit)="enable()" novalidate>
              <div class="field">
                <label for="enable-code">6-digit code from the app</label>
                <input id="enable-code" data-testid="enable-code" class="input code" type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7" spellcheck="false"
                  [attr.aria-invalid]="!!enableErr()" [attr.aria-describedby]="enableErr() ? 'enable-err' : null" />
                @if (enableErr(); as m) { <span id="enable-err" class="error-text" role="alert">{{ m }}</span> }
              </div>
              <button type="submit" id="enable-submit" data-testid="enable-submit" class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Checking…' : 'Turn on' }}</button>
            </form>
          }
        } @else {
          <!-- (c) enabled -->
          <p class="ok" data-testid="mfa-enabled">Two-step verification is <strong>on</strong>@if (s.enabledAt) { since <time [attr.datetime]="s.enabledAt">{{ enabledDate() }}</time> }.</p>
          <p data-testid="recovery-remaining" [class.low]="lowCodes()">
            Recovery codes remaining: <strong>{{ s.recoveryCodesRemaining }}</strong>
            @if (lowCodes()) { <span class="alert alert-warn inline" role="status">Running low. Generate new codes soon.</span> }
          </p>

          <div class="block">
            @if (!regenOpen()) {
              <button type="button" id="regen-open" class="btn btn-outline" (click)="regenOpen.set(true)">Regenerate recovery codes</button>
            } @else {
              <form [formGroup]="regenForm" (ngSubmit)="regenerate()" novalidate>
                <p class="muted">This invalidates your old codes. Enter a current code from your authenticator app.</p>
                <div class="field">
                  <label for="regen-code">Authenticator code</label>
                  <input id="regen-code" class="input code" type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7" spellcheck="false"
                    [attr.aria-invalid]="!!regenErr()" [attr.aria-describedby]="regenErr() ? 'regen-err' : null" />
                  @if (regenErr(); as m) { <span id="regen-err" class="error-text" role="alert">{{ m }}</span> }
                </div>
                <button type="submit" id="regen-submit" class="btn btn-primary" [disabled]="busy()">Generate new codes</button>
                <button type="button" class="btn btn-ghost" (click)="regenOpen.set(false)">Cancel</button>
              </form>
            }
          </div>

          <div class="block">
            @if (s.required) {
              <p class="muted" data-testid="mfa-required-note">Two-step verification is mandatory for your role, so it cannot be turned off.</p>
            } @else if (!disableOpen()) {
              <button type="button" id="disable-open" class="btn btn-outline" (click)="disableOpen.set(true)">Turn off</button>
            } @else {
              <form [formGroup]="disableForm" (ngSubmit)="disable()" novalidate>
                <p class="muted">Turning it off signs you out everywhere. Confirm with your password and a code.</p>
                @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }
                <div class="field">
                  <label for="disable-password">Password</label>
                  <input id="disable-password" class="input" type="password" formControlName="password" autocomplete="current-password"
                    [attr.aria-invalid]="!!disableErr('password')" [attr.aria-describedby]="disableErr('password') ? 'dp-err' : null" />
                  @if (disableErr('password'); as m) { <span id="dp-err" class="error-text" role="alert">{{ m }}</span> }
                </div>
                @if (!useRecovery()) {
                  <div class="field">
                    <label for="disable-code">Authenticator code</label>
                    <input id="disable-code" class="input code" type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7" spellcheck="false"
                      [attr.aria-invalid]="!!disableErr('code')" [attr.aria-describedby]="disableErr('code') ? 'dc-err' : null" />
                    @if (disableErr('code'); as m) { <span id="dc-err" class="error-text" role="alert">{{ m }}</span> }
                  </div>
                } @else {
                  <div class="field">
                    <label for="disable-recovery">Recovery code</label>
                    <input id="disable-recovery" class="input code" type="text" formControlName="recoveryCode" autocomplete="off" autocapitalize="characters" maxlength="24" spellcheck="false"
                      [attr.aria-invalid]="!!disableErr('recoveryCode')" [attr.aria-describedby]="disableErr('recoveryCode') ? 'dr-err' : null" />
                    @if (disableErr('recoveryCode'); as m) { <span id="dr-err" class="error-text" role="alert">{{ m }}</span> }
                  </div>
                }
                <button type="button" class="btn btn-ghost btn-sm" (click)="toggleRecovery()">{{ useRecovery() ? 'Use an authenticator code instead' : 'Use a recovery code instead' }}</button>
                <div class="row">
                  <button type="submit" id="disable-submit" class="btn btn-primary" [disabled]="busy()">Turn off two-step verification</button>
                  <button type="button" class="btn btn-ghost" (click)="disableOpen.set(false)">Cancel</button>
                </div>
              </form>
            }
          </div>
        }
      } @else if (!loadError()) {
        <p class="muted" role="status">Loading…</p>
      }
    </section>
  `,
  styles: `
    .sub { font-size: 1.1rem; }
    .block { margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--border); }
    .ok { color: var(--success); }
    .low strong { color: var(--danger); }
    .inline { display: inline-block; margin: 0 0 0 .5rem; padding: .2rem .6rem; }
    .qr { width: 12rem; max-width: 100%; margin: .75rem 0; border: 1px solid var(--border); border-radius: var(--radius); }
    .secret { display: block; margin-top: .25rem; padding: .5rem .75rem; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius); font-family: ui-monospace, monospace; font-size: 1.05rem; letter-spacing: .08em; word-break: break-all; user-select: all; }
    .code { letter-spacing: .15em; font-family: ui-monospace, monospace; max-width: 14rem; }
    .codes-box { border: 2px solid var(--warn); border-radius: var(--radius-lg); padding: 1rem; background: var(--warn-bg); }
    .codes { list-style: none; display: grid; grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr)); gap: .4rem; padding: 0; margin: .75rem 0; font-family: ui-monospace, monospace; font-size: 1.05rem; }
    .codes li { background: var(--surface); border: 1px solid var(--border-strong); border-radius: var(--radius); padding: .4rem .6rem; text-align: center; }
    .check { display: flex; gap: .5rem; align-items: center; margin: .75rem 0; font-weight: 600; }
    .row { display: flex; gap: .75rem; align-items: center; flex-wrap: wrap; }
  `,
})
export class SecurityPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  protected readonly status = signal<MfaStatus | null>(null);
  protected readonly setup = signal<MfaSetup | null>(null);
  protected readonly recoveryCodes = signal<string[] | null>(null);   // memory only, cleared on dismiss / destroy
  protected readonly acknowledged = signal(false);
  protected readonly copyMsg = signal('');
  protected readonly busy = signal(false);
  protected readonly loadError = signal('');
  protected readonly formError = signal('');
  protected readonly regenOpen = signal(false);
  protected readonly disableOpen = signal(false);
  protected readonly useRecovery = signal(false);

  private readonly reason = this.route.snapshot.queryParamMap.get('reason');
  protected readonly adminNotice = computed(() => this.reason === 'admin-mfa' && !(this.status()?.enabled && this.auth.user()?.mfaSession));
  protected readonly secretText = computed(() => groupSecret(this.setup()?.secret ?? ''));
  protected readonly lowCodes = computed(() => (this.status()?.recoveryCodesRemaining ?? 99) <= LOW_CODES);
  protected readonly enabledDate = computed(() => {
    const at = this.status()?.enabledAt;
    return at ? new Date(at).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }) : '';
  });

  protected readonly enableForm = this.fb.nonNullable.group({ code: ['', Validators.required] });
  protected readonly regenForm = this.fb.nonNullable.group({ code: ['', Validators.required] });
  protected readonly disableForm = this.fb.nonNullable.group({ password: ['', Validators.required], code: [''], recoveryCode: [''] });

  constructor() {
    inject(SeoService).set({ title: 'Security', noindex: true });
    inject(DestroyRef).onDestroy(() => this.recoveryCodes.set(null));
    this.reload();
  }

  protected enableErr = () => fieldError(this.enableForm, 'code', { code: 'Code' });
  protected regenErr = () => fieldError(this.regenForm, 'code', { code: 'Code' });
  protected disableErr = (name: string) => fieldError(this.disableForm, name, { password: 'Password', code: 'Code', recoveryCode: 'Recovery code' });

  private reload(): void {
    this.auth.mfaStatus().subscribe({
      next: (s) => {
        this.status.set(s);
        this.loadError.set('');
        // setupPending: a secret exists but was never confirmed; it is only returned by setup, so restart to show it again.
      },
      error: (e) => this.loadError.set(errorMessage(e, 'Could not load your security settings.')),
    });
  }

  protected startSetup(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    this.auth.mfaSetup().subscribe({
      next: (s) => {
        this.busy.set(false);
        this.setup.set(s);
        this.enableForm.reset();
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(errorMessage(e));
        if (isApiError(e) && e.status === 409) this.reload();
      },
    });
  }

  protected enable(): void {
    this.enableForm.markAllAsTouched();
    const digits = totpDigits(this.enableForm.controls.code.value);
    if (digits.length !== 6) {
      this.enableForm.controls.code.setErrors({ server: 'Enter the 6-digit code from your authenticator app.' });
      return;
    }
    if (this.busy()) return;
    this.busy.set(true);
    this.auth.mfaEnable(digits).subscribe({
      next: (codes) => {
        this.busy.set(false);
        this.setup.set(null);
        this.acknowledged.set(false);
        this.recoveryCodes.set(codes);
        this.toast.success('Two-step verification is on');
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.enableForm.controls.code.setErrors({ server: this.codeProblem(e) });
        this.enableForm.controls.code.markAsTouched();
      },
    });
  }

  protected regenerate(): void {
    this.regenForm.markAllAsTouched();
    const digits = totpDigits(this.regenForm.controls.code.value);
    if (digits.length !== 6) {
      this.regenForm.controls.code.setErrors({ server: 'Enter the 6-digit code from your authenticator app.' });
      return;
    }
    if (this.busy()) return;
    this.busy.set(true);
    this.auth.mfaRegenerateRecoveryCodes(digits).subscribe({
      next: (codes) => {
        this.busy.set(false);
        this.regenOpen.set(false);
        this.regenForm.reset();
        this.acknowledged.set(false);
        this.recoveryCodes.set(codes);
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.regenForm.controls.code.setErrors({ server: this.codeProblem(e) });
        this.regenForm.controls.code.markAsTouched();
      },
    });
  }

  protected toggleRecovery(): void {
    this.useRecovery.update((v) => !v);
    this.disableForm.patchValue({ code: '', recoveryCode: '' });
  }

  protected disable(): void {
    this.disableForm.markAllAsTouched();
    this.formError.set('');
    const { password, code, recoveryCode } = this.disableForm.getRawValue();
    let proof: MfaProof;
    if (this.useRecovery()) {
      const rc = normaliseRecoveryCode(recoveryCode);
      if (!rc) return void this.disableForm.controls.recoveryCode.setErrors({ server: 'Enter a recovery code.' });
      proof = { recoveryCode: rc };
    } else {
      const digits = totpDigits(code);
      if (digits.length !== 6) return void this.disableForm.controls.code.setErrors({ server: 'Enter the 6-digit code from your authenticator app.' });
      proof = { code: digits };
    }
    if (!password || this.busy()) return;
    this.busy.set(true);
    this.auth.mfaDisable(password, proof).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('Two-step verification is off. Please sign in again.');
        void this.router.navigateByUrl('/login');
      },
      error: (e) => {
        this.busy.set(false);
        if (isApiError(e) && e.errors?.['code'] && this.useRecovery()) {
          const { code: c, ...rest } = e.errors;
          e = { ...e, errors: { ...rest, recoveryCode: c } } satisfies ApiError;
        }
        const msg = applyServerErrors(this.disableForm, e);
        const fieldErrors = isApiError(e) && e.errors && Object.keys(e.errors).length > 0;
        this.formError.set(fieldErrors ? '' : msg);
      },
    });
  }

  protected async copyCodes(): Promise<void> {
    const codes = this.recoveryCodes();
    if (!codes || !this.browser || typeof navigator === 'undefined' || !navigator.clipboard) {
      this.copyMsg.set('Copying is not available here. Select the codes and copy them manually.');
      return;
    }
    try {
      await navigator.clipboard.writeText(codes.join('\n'));
      this.copyMsg.set('Copied');
    } catch {
      this.copyMsg.set('Could not copy. Select the codes and copy them manually.');
    }
  }

  protected dismissCodes(): void {
    if (!this.acknowledged()) return;
    this.recoveryCodes.set(null);
    this.acknowledged.set(false);
    this.copyMsg.set('');
  }

  private codeProblem(e: unknown): string {
    if (isApiError(e) && e.errors) {
      const m = Object.values(e.errors).flat()[0];
      if (m) return m;
    }
    return errorMessage(e, 'That code is not valid.');
  }
}
