import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { NotificationType } from 'src/app/shared/models/notification-type';
import { SearchService } from 'src/app/shared/services/search/search.service';
import {
  IonButton,
  IonIcon,
  IonPopover,
  IonContent,
  IonItem,
  IonInput,
  IonGrid,
  IonRow,
  IonList,
  IonCol,
  IonCheckbox,
  IonItemGroup,
  IonLabel,
  IonText,
  IonInfiniteScroll,
  IonInfiniteScrollContent,
} from '@ionic/angular/standalone';
import { MatTooltip } from '@angular/material/tooltip';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { alert, close } from 'ionicons/icons';
import { addIcons } from 'ionicons';

@Component({
  selector: 'app-error',
  templateUrl: './error.component.html',
  styleUrls: ['./error.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [
    IonInfiniteScrollContent,
    IonInfiniteScroll,
    IonText,
    IonLabel,
    IonItemGroup,
    IonCheckbox,
    IonCol,
    IonList,
    IonRow,
    IonGrid,
    IonInput,
    IonItem,
    IonContent,
    IonPopover,
    IonButton,
    IonIcon,
    MatTooltip,
    ScrollingModule,
  ],
})
export class ErrorComponent implements OnInit, OnDestroy {
  errorSubsription: Subscription | undefined;

  @ViewChild('errorsList') popover: IonPopover | undefined;

  // The exceptions which happens during the search
  errors: string[];
  isOpen: boolean;

  // Screen height
  screenHeight: number;

  constructor(private searchService: SearchService) {
    this.errors = [];
    this.isOpen = false;
    addIcons({ alert, close });
    this.screenHeight = window.innerHeight;
    console.log(this.screenHeight);
  }

  @HostListener('window:resize', ['$event.target.innerWidth'])
  onResize(width: number) {
    this.screenHeight = width;
  }

  ngOnInit() {
    this.errorSubsription = this.searchService.notification$.subscribe(
      (notification) => {
        if (notification.type == NotificationType.Exception) {
          console.log(notification.result);
          if (notification.result.length == 0) this.errors.length = 0;
          else this.errors.push(notification.result);
        }
      }
    );
  }

  ngOnDestroy(): void {
    this.errorSubsription?.unsubscribe();
  }

  showPopOver(event: MouseEvent) {
    this.popover!.event = event;
    this.isOpen = true;
  }

  closePopOver() {
    this.isOpen = false;
  }

  deleteError(event: MouseEvent) {
    throw new Error('Method not implemented.');
  }
}
