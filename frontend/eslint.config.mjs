// @ts-check
// Flat ESLint config: TypeScript + Angular templates (external .html and inline `template:` strings).
// Correctness, accessibility and the project's Angular conventions (standalone, OnPush, signals) are errors.
// Pure style is left to Prettier / .editorconfig, so no stylistic rules are enabled here.
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

export default tseslint.config(
  {
    ignores: ['dist/**', '.angular/**', 'node_modules/**', 'coverage/**', 'e2e-results/**', 'playwright-report/**', 'test-results/**', 'blob-report/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [...tseslint.configs.recommended, ...tseslint.configs.stylistic, ...angular.configs.tsRecommended],
    // Lint the markup inside inline `template: \`...\`` strings with the template rules below.
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'app', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: ['app', 'adm'], style: 'kebab-case' }],
      // Project conventions (CLAUDE.md): standalone components only, OnPush everywhere, no input aliasing.
      // (Angular 22: OnPush is the default, so this rule flags components that opt back out with ChangeDetectionStrategy.Default.)
      '@angular-eslint/prefer-standalone': 'error',
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@angular-eslint/no-input-rename': 'error',
      '@angular-eslint/no-output-rename': 'error',
      '@angular-eslint/prefer-signals': 'error',
      '@angular-eslint/prefer-inject': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      // `cond ? a.delete(x) : a.add(x)` and `x.includes(v) || x.push(v)` are used deliberately as compact statements.
      '@typescript-eslint/no-unused-expressions': ['error', { allowTernary: true, allowShortCircuit: true }],
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrors: 'none' }],
      // Stylistic TS rules that only produce churn on this code base (the codebase consistently uses
      // `T[]`/`Array<T>` and interfaces/types interchangeably); correctness rules above stay on.
      '@typescript-eslint/array-type': 'off',
      '@typescript-eslint/consistent-type-definitions': 'off',
      '@typescript-eslint/consistent-indexed-object-style': 'off',
    },
  },
  {
    // Specs legitimately poke at privates / use loose fakes.
    files: ['**/*.spec.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-non-null-assertion': 'off',
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error',
      '@angular-eslint/template/eqeqeq': 'error',
      '@angular-eslint/template/no-negated-async': 'error',
    },
  },
  {
    // Pointer-only affordances that have a keyboard equivalent elsewhere: the dialog backdrop is dismissed with Escape
    // (host keydown), and combobox options are driven from the focused input via aria-activedescendant (WAI-ARIA combobox).
    // (inline templates are linted as virtual `<component>.ts/*.html` files)
    files: ['src/app/features/builder/part-picker.component.ts/*.html', 'src/app/layout/search-box.component.ts/*.html'],
    rules: {
      '@angular-eslint/template/click-events-have-key-events': 'off',
      '@angular-eslint/template/interactive-supports-focus': 'off',
    },
  },
);
