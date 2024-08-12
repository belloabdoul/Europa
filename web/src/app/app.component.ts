import { Component } from '@angular/core';
import {
  IonApp,
  IonSplitPane,
  IonMenu,
  IonContent,
  IonRouterOutlet,
  IonHeader,
  IonToolbar,
  IonTitle,
  IonButton,
  IonMenuToggle,
  IonIcon,
  IonGrid,
  IonRow,
} from '@ionic/angular/standalone';
import { SearchFormComponent } from './components/search-form/search-form.component';
import { ResultsComponent } from './components/results/results.component';
import { ProgressComponent } from './components/progress/progress.component';
import { addIcons } from 'ionicons';
import { close, menu } from 'ionicons/icons';
import { ErrorComponent } from './components/error/error.component';
import { IElectronAPI } from 'interface';

declare global {
  interface Window {
    electronAPI: IElectronAPI;
  }
}

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  standalone: true,
  imports: [
    IonRow,
    IonGrid,
    IonIcon,
    IonButton,
    IonTitle,
    IonToolbar,
    IonHeader,
    IonRouterOutlet,
    IonApp,
    IonSplitPane,
    IonMenu,
    IonContent,
    SearchFormComponent,
    ResultsComponent,
    ProgressComponent,
    IonMenuToggle,
    ErrorComponent,
  ],
})
export class AppComponent {
  public labels = ['Family', 'Friends', 'Notes', 'Work', 'Travel', 'Reminders'];
  constructor() {
    addIcons({ close, menu });
  }
}
