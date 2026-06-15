import '@ionic/core'; // Ensures the global Ionic/Stencil types are loaded first

/**
 * ⚠️ TEMPORARY FIX FOR IONIC / STENCIL COMPILER CLASH (TS2320)
 * 
 * Tracking Issue: https://github.com/ionic-team/ionic-framework/issues/30650
 * 
 * Context: 
 * The Angular esbuild compiler plugin actively checks template files and 
 * bypasses the standard `skipLibCheck: true` flag in tsconfig. This exposes 
 * a multiple-inheritance conflict in Ionic's internal types:
 * - Parent 1 (`HTMLStencilElement`) types `autocorrect` as `string`.
 * - Parent 2 (`IonInput` / `IonSearchbar`) types it strictly as `'on' | 'off'`.
 * 
 * Why this specific fix:
 * We cannot use `string` or `'on' | 'off'` here because TypeScript refuses to let a 
 * child broaden or violate conflicting parent rules. By setting `autocorrect: any` 
 * directly on the child interfaces, `any` acts as a universal adapter that satisfies 
 * both parents simultaneously.
 * 
 * This surgical approach is preferred because it silences the compilation error 
 * without resorting to wiping out strict typing for the entire `<ion-input>` JSX element.
 */
declare global {
  // This explicitly targets ONLY the conflicting property on the specific elements.
  // All other properties (value, disabled, type, etc.) remain 100% strictly typed!
  interface HTMLIonInputElement {
    autocorrect: any;
  }

  interface HTMLIonSearchbarElement {
    autocorrect: any;
  }
}