import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
} from '@angular/core';
import {
  IonRow,
  IonCol,
  IonLabel,
  IonIcon,
  IonGrid,
  IonCheckbox,
} from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';
import { File } from 'src/app/shared/models/file';
import { SearchService } from 'src/app/shared/services/search/search.service';
import { addIcons } from 'ionicons';
import { folderOpen, apps } from 'ionicons/icons';
import { CdkMenu, CdkMenuItem, CdkContextMenuTrigger } from '@angular/cdk/menu';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-results',
  templateUrl: './results.component.html',
  styleUrls: ['./results.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    IonCheckbox,
    IonGrid,
    IonIcon,
    IonLabel,
    IonRow,
    IonCol,
    CdkMenu,
    CdkMenuItem,
    CdkContextMenuTrigger,
    ScrollingModule,
    FormsModule,
    DatePipe,
  ],
})
export class ResultsComponent implements OnInit, OnDestroy {
  // The list of similar files found
  similarFiles: File[];

  // Subscriber for getting results
  similarFilesSubscription: Subscription | undefined;

  // Screen height
  screenHeight: number;

  // Target path of click, double click or context menu click
  path: string;

  constructor(
    private searchService: SearchService,
    private cd: ChangeDetectorRef
  ) {
    this.similarFiles = [];
    addIcons({ folderOpen, apps });
    this.screenHeight = window.innerHeight;
    this.path = '';
  }

  ngOnInit() {
    this.similarFilesSubscription = this.searchService.similarFiles$.subscribe(
      (similarFiles) => {
        if (similarFiles.length == 0) this.similarFiles.length == 0;
        else {
          this.similarFiles = similarFiles.reduce(
            (previousValue, currentValue) => {
              previousValue[previousValue.length - 1].isLastInGroup = true;
              return previousValue.concat(currentValue);
            }
          );
        }
        this.cd.detectChanges();
      }
    );
  }

  ngOnDestroy(): void {
    this.similarFilesSubscription?.unsubscribe();
  }

  @HostListener('window:resize', ['$event.target.innerWidth'])
  onResize(width: number) {
    this.screenHeight = width;
  }

  async setSelectedRow(event: MouseEvent) {
    const row = event.target as HTMLElement;
    this.path = row.children.item(1)?.innerHTML!;
    row.focus();
  }

  async openFileInDefaultApplication(event: MouseEvent) {
    let path: string;
    let type = (event.target as HTMLElement).tagName.toLowerCase();
    if (type == 'ion-row')
      path = (event.target as HTMLElement).children.item(1)?.innerHTML!;
    else if (type == 'ion-col') path = '';
    else path = this.path;

    if (path != '') {
      await window.electronAPI
        ?.openFileInDefaultApplication(path)
        .catch((error: any) => {
          console.log(`Opening ${path} in default app failed : ${error}`);
        });
      this.path = '';
    }
  }

  showContentIfPossible(event: MouseEvent) {
    const row = event.target as HTMLElement;
    const path = row.children.item(1)?.innerHTML!;
    row.focus();
  }

  async openFileLocation() {
    const path = this.path;
    await window.electronAPI?.openFileLocation(path).catch((error: any) => {
      console.log(`Opening ${path} folder failed : ${error}`);
    });
    this.path = '';
  }
}
