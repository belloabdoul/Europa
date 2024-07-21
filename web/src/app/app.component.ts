import { Component } from '@angular/core';
import {
  IonApp,
  IonSplitPane,
  IonMenu,
  IonContent,
  IonRouterOutlet,
} from '@ionic/angular/standalone';
import { SearchFormComponent } from './components/search-form/search-form.component';

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  standalone: true,
  imports: [
    IonRouterOutlet,
    IonApp,
    IonSplitPane,
    IonMenu,
    IonContent,
    SearchFormComponent,
  ],
})
export class AppComponent {
  public labels = ['Family', 'Friends', 'Notes', 'Work', 'Travel', 'Reminders'];
  constructor() {
  }
}
