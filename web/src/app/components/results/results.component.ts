import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
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
import { FileSearchType } from 'src/app/shared/models/file-search-type';
import { NotificationType } from 'src/app/shared/models/notification-type';
import { SearchService } from 'src/app/shared/services/search/search.service';

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
  ],
})
export class ResultsComponent implements OnInit, OnDestroy {
  step1Text: string;
  step2Text: string;
  step3Text: string;
  step1Progress: string;
  step2Progress: string;
  step3Progress: string;
  exceptions: string[];
  notificationSubscription: Subscription | undefined;
  searchParametersSubscription: Subscription | undefined;

  constructor(
    private cd: ChangeDetectorRef,
    private searchService: SearchService
  ) {
    this.step1Text = '';
    this.step2Text = '';
    this.step3Text = '';
    this.step1Progress = '';
    this.step2Progress = '';
    this.step3Progress = '';
    this.exceptions = [];
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
  }

  ngOnDestroy(): void {
    this.searchParametersSubscription?.unsubscribe();
    this.notificationSubscription?.unsubscribe();
  }
}
