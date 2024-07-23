import {
  ChangeDetectorRef,
  Component,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import {
  IonContent,
  IonFooter,
  IonToolbar,
  IonGrid,
  IonRow,
  IonCol,
  IonList,
  IonItem,
  IonLabel,
} from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';
import { File } from 'src/app/shared/models/file';
import { FileSearchType } from 'src/app/shared/models/file-search-type';
import { NotificationType } from 'src/app/shared/models/notification-type';
import { SearchService } from 'src/app/shared/services/search/search.service';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  CellFocusedEvent,
} from 'ag-grid-community';
import { ElectronService } from 'src/app/shared/services/electron/electron.service';

@Component({
  selector: 'app-results',
  templateUrl: './results.component.html',
  styleUrls: ['./results.component.scss'],
  standalone: true,
  imports: [
    IonLabel,
    IonItem,
    IonList,
    IonGrid,
    IonToolbar,
    IonFooter,
    IonContent,
    IonRow,
    IonCol,
    AgGridAngular,
  ],
})
export class ResultsComponent implements OnInit, OnDestroy {
  // Each step message and its progress in number of hashes processed
  step1Text: string;
  step1Progress: string;

  step2Text: string;
  step2Progress: string;

  step3Text: string;
  step3Progress: string;

  // The exceptions which happens during the search
  exceptions: string[];

  // The list of similar files found
  similarFiles: File[];

  // Subscriber for getting and processing notifications, results and search parameters
  notificationSubscription: Subscription | undefined;
  searchParametersSubscription: Subscription | undefined;
  similarFilesSubscription: Subscription | undefined;

  // // Make all upper side of cell thicker for first element
  // // in each group except the first group
  // cellClassRules = {
  //   'isFirst' : params
  // }
  // The columns for our result list
  colDefs: ColDef[] = [
    {
      field: 'path',
      headerName: 'Path',
      sortable: false,
      suppressMovable: true,
      tooltipField: 'path',
      cellClassRules: {
        'is-last': (params) => (params.data as File).isLastInGroup == true,
      },
    },
    {
      field: 'type',
      headerName: 'Type',
      sortable: false,
      suppressMovable: true,
      // cellClassRules: {
      //   'is-last': (params) => (params.data as File).isLastInGroup == true,
      // },
    },
    {
      field: 'size',
      headerName: 'Size',
      sortable: false,
      suppressMovable: true,
      // cellClassRules: {
      //   'is-last': (params) => (params.data as File).isLastInGroup == true,
      // },
    },
    {
      field: 'lastModified',
      headerName: 'Last modified',
      sortable: false,
      suppressMovable: true,
      // cellClassRules: {
      //   'is-last': (params) => (params.data as File).isLastInGroup == true,
      // },
    },
    {
      field: 'delete',
      headerName: 'Delete?',
      sortable: false,
      suppressMovable: true,
      // cellClassRules: {
      //   'is-last': (params) => (params.data as File).isLastInGroup == true,
      // },
    },
  ];

  // Accessor to ag grid component
  @ViewChild('results') files: AgGridAngular | undefined;

  constructor(
    private cd: ChangeDetectorRef,
    private searchService: SearchService,
    private electronService: ElectronService
  ) {
    this.step1Text = '';
    this.step1Progress = '';

    this.step2Text = '';
    this.step2Progress = '';

    this.step3Text = '';
    this.step3Progress = '';

    this.exceptions = [];
    this.similarFiles = [];
  }

  ngOnInit() {
    this.searchParametersSubscription =
      this.searchService.searchParameters.subscribe((searchParameters) => {
        if (searchParameters == null) {
          this.step1Text = 'Search cancelled by the user';
        } else if (searchParameters.fileSearchType == FileSearchType.All) {
          this.step1Text = 'Generating partial hash';
          this.step1Progress = '0';

          this.step3Text = 'Generating full hash';
          this.step3Progress = '0';
        } else if (searchParameters.fileSearchType == FileSearchType.Images) {
          this.step1Text = 'Generating perceptual hash';
          this.step1Progress = '0';

          this.step2Text = 'Finding similar images';
          this.step2Progress = '0';

          this.step3Text = 'Grouping similar images';
          this.step3Progress = '0';
        }

        this.exceptions = [];
        this.cd.markForCheck();
      });

    this.notificationSubscription = this.searchService.notification.subscribe(
      (notification) => {
        if (notification.type == NotificationType.HashGenerationProgress)
          this.step1Progress = notification.result;
        else if (notification.type == NotificationType.SimilaritySearchProgress)
          this.step2Progress = notification.result;
        else if (notification.type == NotificationType.TotalProgress)
          this.step3Progress = notification.result;
        else this.exceptions.push(notification.result);
        this.cd.markForCheck();
      }
    );

    this.similarFilesSubscription = this.searchService.similarFiles.subscribe(
      (similarFiles) => {
        this.similarFiles = similarFiles.reduce(
          (previousValue, currentValue) => {
            previousValue[previousValue.length - 1].isLastInGroup = true;
            return previousValue.concat(currentValue);
          }
        );
      }
    );
  }

  ngOnDestroy(): void {
    this.searchParametersSubscription?.unsubscribe();
    this.notificationSubscription?.unsubscribe();
    this.similarFilesSubscription?.unsubscribe();
  }

  async setSelectedRow(event: CellFocusedEvent) {
    // Since the click also trigger the focus event which happens first we select the row
    // only on focus and use the click the trigger the showing of the content
    event.api.getRowNode(event.rowIndex?.toString()!)?.setSelected(true);
  }

  async openFileInDefaultApplication() {
    const path = (this.files?.api.getSelectedRows()[0] as File).path;
    await this.electronService.ipcRenderer
      .invoke('shell:openFileInDefaultApplication', [path])
      .catch((error) => {
        console.log(`Opening ${path} in default app failed : ${error}`);
      });
  }

  showContentIfPossible() {
    // const path = (this.files?.api.getSelectedRows()[0] as File).path;
    const row = this.files?.api.getSelectedRows()[0];
    console.log(row);
  }
}
