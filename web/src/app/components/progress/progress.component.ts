import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnDestroy,
  OnInit,
} from '@angular/core';
import { BehaviorSubject, Observable, Subscription } from 'rxjs';
import { FileSearchType } from 'src/app/shared/models/file-search-type';
import { NotificationType } from 'src/app/shared/models/notification-type';
import { SearchService } from 'src/app/shared/services/search/search.service';
import {
  IonToolbar,
  IonFooter,
  IonRow,
  IonGrid,
  IonCol,
} from '@ionic/angular/standalone';
import { AsyncPipe } from '@angular/common';

@Component({
  selector: 'app-progress',
  templateUrl: './progress.component.html',
  styleUrls: ['./progress.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [IonCol, IonGrid, IonRow, IonFooter, IonToolbar, AsyncPipe],
})
export class ProgressComponent implements OnInit, OnDestroy {
  // Each step message and its progress in number of hashes processed
  private step1Text: BehaviorSubject<string> = new BehaviorSubject('');
  step1Text$: Observable<string>;

  private step1Progress: BehaviorSubject<string> = new BehaviorSubject('');
  step1Progress$: Observable<string>;

  private step2Text: BehaviorSubject<string> = new BehaviorSubject('');
  step2Text$: Observable<string>;

  private step2Progress: BehaviorSubject<string> = new BehaviorSubject('');
  step2Progress$: Observable<string>;

  private step3Text: BehaviorSubject<string> = new BehaviorSubject('');
  step3Text$: Observable<string>;

  private step3Progress: BehaviorSubject<string> = new BehaviorSubject('');
  step3Progress$: Observable<string>;

  // Subscriber for getting and processing notifications and search parameters
  notificationSubscription: Subscription | undefined;
  searchParametersSubscription: Subscription | undefined;

  constructor(
    private cd: ChangeDetectorRef,
    private searchService: SearchService
  ) {
    this.step1Text$ = this.step1Text.asObservable();
    this.step1Progress$ = this.step1Progress.asObservable();

    this.step2Text$ = this.step2Text.asObservable();
    this.step2Progress$ = this.step2Progress.asObservable();

    this.step3Text$ = this.step3Text.asObservable();
    this.step3Progress$ = this.step3Progress.asObservable();
  }

  ngOnInit() {
    this.searchParametersSubscription =
      this.searchService.searchParameters$.subscribe((searchParameters) => {
        if (searchParameters == null) {
          this.step1Text.next('Search cancelled');
        } else if (searchParameters.fileSearchType == FileSearchType.All) {
          this.step1Text.next('Generating partial hash');
          this.step1Progress.next('0');
          this.step2Text.next('');
          this.step2Progress.next('0');
          this.step3Text.next('Generating full hash');
          this.step3Progress.next('0');
        } else if (searchParameters.fileSearchType == FileSearchType.Images) {
          this.step1Text.next('Generating perceptual hash');
          this.step1Progress.next('0');

          this.step2Text.next('Finding similar images');
          this.step2Progress.next('0');

          this.step3Text.next('Grouping similar images');
          this.step3Progress.next('0');
        }
      });

    this.notificationSubscription = this.searchService.notification$.subscribe(
      (notification) => {
        if (notification.type == NotificationType.HashGenerationProgress)
          this.step1Progress.next(notification.result);
        else if (notification.type == NotificationType.SimilaritySearchProgress)
          this.step2Progress.next(notification.result);
        else if (notification.type == NotificationType.TotalProgress)
          this.step3Progress.next(notification.result);
      }
    );
  }

  ngOnDestroy(): void {
    this.searchParametersSubscription?.unsubscribe();
    this.notificationSubscription?.unsubscribe();
  }
}
