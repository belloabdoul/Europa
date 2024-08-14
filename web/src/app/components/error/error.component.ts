import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
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
  IonList,
  IonLabel,
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
    IonLabel,
    IonList,
    IonInput,
    IonItem,
    IonContent,
    IonPopover,
    IonButton,
    IonIcon,
    MatTooltip,
    ScrollingModule,
    CommonModule,
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

  constructor(
    private cd: ChangeDetectorRef,
    private searchService: SearchService
  ) {
    this.errors = [];
    this.isOpen = false;
    addIcons({ alert, close });
    this.screenHeight = window.innerHeight;
  }

  @HostListener('window:resize', ['$event.target.innerWidth'])
  onResize(width: number) {
    this.screenHeight = width;
  }

  ngOnInit() {
    this.errorSubsription = this.searchService.notification$.subscribe(
      (notification) => {
        if (notification.type == NotificationType.Exception) {
          this.errors = [...this.errors, notification.result];
          this.cd.markForCheck();
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
    const message = (
      event.target as HTMLElement
    ).previousElementSibling?.textContent?.trim()!;
    const index = this.errors.indexOf(message);
    this.errors.splice(index, 1);
    this.errors = [...this.errors];
  }
}
