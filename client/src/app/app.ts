import { Component } from '@angular/core';
import { Chat } from './chat/chat';

@Component({
  selector: 'app-root',
  imports: [Chat],
  template: '<main><app-chat /></main>',
  styles: `
    main {
      display: flex;
      height: 100dvh;
      max-width: 48rem;
      margin: 0 auto;
      padding: 1rem;
    }
  `,
})
export class App {}
