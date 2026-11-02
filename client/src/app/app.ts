import { Component, inject } from '@angular/core';
import { Chat } from './chat/chat';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [Chat],
  template: `
    @if (!auth.ready()) {
      <main class="center"><p>Loading…</p></main>
    } @else if (!auth.signedIn) {
      <main class="center">
        <h1>RCM Assistant</h1>
        <p>Sign in with your work account to continue.</p>
        @if (auth.error()) {
          <p class="err" role="alert">{{ auth.error() }}</p>
        }
        <button type="button" (click)="auth.signIn()">Sign in with Microsoft</button>
      </main>
    } @else {
      @if (auth.enabled()) {
        <div class="bar">
          <span>{{ auth.account()?.name }}</span>
          <button type="button" (click)="auth.signOut()">Sign out</button>
        </div>
      }
      <main><app-chat /></main>
    }
  `,
  styles: `
    main {
      display: flex;
      height: 100dvh;
      max-width: 48rem;
      margin: 0 auto;
      padding: 1rem;
    }
    main.center {
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 0.75rem;
      text-align: center;
    }
    .bar {
      position: fixed;
      top: 0.5rem;
      right: 1rem;
      display: flex;
      gap: 0.75rem;
      align-items: center;
      font-size: 0.85rem;
      z-index: 5;
    }
    .err { color: #b00020; }
    button { padding: 0.5rem 1rem; cursor: pointer; }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
}
