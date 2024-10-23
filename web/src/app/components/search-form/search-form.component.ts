import {
  Component,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  ViewChild,
  OnDestroy,
} from '@angular/core';
import {
  IonList,
  IonItem,
  IonIcon,
  IonLabel,
  IonInput,
  IonButton,
  IonItemGroup,
  IonToggle,
  IonSelect,
  IonSelectOption,
  IonPopover,
  IonContent,
  IonChip,
  IonText,
  IonNote,
} from '@ionic/angular/standalone';
import { FormsModule } from '@angular/forms';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FileSearchType } from 'src/app/shared/models/file-search-type';
import { SearchParameters } from 'src/app/shared/models/search-parameters';
import { addIcons } from 'ionicons';
import {
  add,
  close,
  closeCircle,
  create,
  save,
  ban,
  search,
} from 'ionicons/icons';
import { SearchService } from 'src/app/shared/services/search/search.service';
import { CommonModule, KeyValuePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { File } from 'src/app/shared/models/file';
import { SearchParametersErrors } from 'src/app/shared/models/search-parameters-errors';
import { PerceptualHashAlgorithm } from 'src/app/shared/models/perceptual-hash-algorithm';

@Component({
  selector: 'app-search-form',
  templateUrl: './search-form.component.html',
  styleUrls: ['./search-form.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    IonNote,
    IonText,
    IonContent,
    IonPopover,
    IonItemGroup,
    IonButton,
    IonInput,
    IonList,
    IonItem,
    IonIcon,
    IonLabel,
    IonSelect,
    IonChip,
    IonSelectOption,
    IonToggle,
    MatTooltipModule,
    FormsModule,
    KeyValuePipe,
    CommonModule,
  ],
})
export class SearchFormComponent implements OnDestroy {
  searchParameters: SearchParameters;
  searchParametersErrors: SearchParametersErrors;
  maxValue: number = Number.MAX_SAFE_INTEGER;

  // Handle manual directory popover
  manuallyAddedDirectory: string;
  isOpen: boolean;
  @ViewChild('manuallyAddedFolderInput') popover: IonPopover | undefined;

  // Handle the presentation for the enum in the select
  fileSearchType: typeof FileSearchType = FileSearchType;
  perceptualHashAlgorithm: typeof PerceptualHashAlgorithm =
    PerceptualHashAlgorithm;

  // Handle the appearance of the cancel button when a search is running
  isSearchRunning: boolean;

  // Subscription for managing result
  searchSubsription: Subscription | undefined;

  constructor(
    private cd: ChangeDetectorRef,
    private searchService: SearchService
  ) {
    addIcons({ add, close, closeCircle, create, save, ban, search });

    this.searchParameters = new SearchParameters();
    this.searchParametersErrors = new SearchParametersErrors();
    this.manuallyAddedDirectory = '';
    this.isOpen = false;
    this.isSearchRunning = false;
  }

  ngOnDestroy(): void {
    this.searchSubsription?.unsubscribe();
  }

  async selectDirectory(): Promise<void> {
    let directory: string = await window.electronAPI?.selectDirectory();
    if (directory != '' && !this.searchParameters.folders.includes(directory)) {
      this.searchParameters.folders.push(directory);
      this.clearErrorMessages('folders');
      this.cd.markForCheck();
    }
  }

  showPopOver(event: MouseEvent): void {
    this.popover!.event = event;
    this.isOpen = true;
  }

  closePopOver() {
    this.isOpen = false;
  }

  manuallyAddDirectory(): void {
    this.manuallyAddedDirectory = this.manuallyAddedDirectory.trim();
    if (
      this.manuallyAddedDirectory != '' &&
      !this.searchParameters.folders.includes(this.manuallyAddedDirectory)
    ) {
      this.searchParameters.folders.push(this.manuallyAddedDirectory);
    }
    this.manuallyAddedDirectory = '';
    this.clearErrorMessages('folders');
    this.popover?.dismiss();
  }

  removeDirectory(event: MouseEvent): void {
    var target = event.target as HTMLElement;
    var directory = target.previousElementSibling?.textContent!.trim()!;
    const index = this.searchParameters.folders.indexOf(directory, 0);
    if (index > -1) {
      this.searchParameters.folders.splice(index, 1);
    }
  }

  addExtension(event: Event): void {
    const input = event.target as HTMLInputElement;
    const value = (input.value || '').trim();
    var extensionsType = input.closest('ion-input')?.id;
    var added = false;

    // Add our extension
    if (value) {
      if (
        extensionsType == 'extensionsToInclude' &&
        !this.searchParameters.includedFileTypes.includes(value)
      ) {
        this.searchParameters.includedFileTypes.push(value);
        added = true;
      } else if (
        extensionsType == 'extensionsToExclude' &&
        !this.searchParameters.excludedFileTypes.includes(value)
      ) {
        this.searchParameters.excludedFileTypes.push(value);
        added = true;
      }

      if (added) {
        // Create the chip element
        const chip = document.createElement('ion-chip');
        chip.slot = 'start';
        chip.outline = true;

        // Make the input value the label of the chip
        chip.innerHTML = `
        <ion-label>${value}</ion-label>
        <ion-icon name="close-circle"></ion-icon>
      `;

        // Listen for on click of the chip's close icon and remove it
        chip.addEventListener('click', (event) => this.removeExtension(event));

        // add the chip element to the main group
        var parent = input.parentElement as HTMLDivElement;

        parent?.insertBefore(chip, input);
      }
    }

    // Clear the input value
    input.value = '';
  }

  removeExtensionFromList(extension: string, extensionsType: string): boolean {
    if (extensionsType == 'extensionsToInclude') {
      const index = this.searchParameters.includedFileTypes.indexOf(
        extension,
        0
      );
      if (index > -1) {
        return (
          this.searchParameters.includedFileTypes.splice(index, 1)[0] ==
          extension
        );
      }
    } else if (extensionsType == 'extensionsToExclude') {
      const index = this.searchParameters.excludedFileTypes.indexOf(
        extension,
        0
      );
      if (index > -1) {
        return (
          this.searchParameters.excludedFileTypes.splice(index, 1)[0] ==
          extension
        );
      }
    }
    return false;
  }

  removeExtension(event: Event): void {
    if (event.type == 'click') {
      const target = event.target as HTMLElement;

      if (target.tagName.toLowerCase() == 'ion-icon') {
        const chip = target.parentElement;
        const extensionsType = chip?.closest('ion-input')?.id;

        this.removeExtensionFromList(
          chip?.firstElementChild?.textContent!,
          extensionsType!
        );

        chip?.parentNode?.removeChild(chip);
      }
    } else if (event.type == 'keydown') {
      const input = event.target as HTMLInputElement;
      const parent = input.parentElement as HTMLDivElement;

      // only remove a chip if the value is empty
      if (input.value === '') {
        const chips = parent.querySelectorAll('ion-chip');

        if (chips.length > 0) {
          const chip = chips[chips.length - 1];

          if (chip.outline === false) {
            const extensionsType = chip?.closest('ion-input')?.id;

            this.removeExtensionFromList(
              chip?.firstElementChild?.textContent!,
              extensionsType!
            );

            chip.parentNode?.removeChild(chip);
          } else {
            chip.outline = false;
          }
        }
      }
    }
  }

  restoreChip(event: Event): void {
    const input = event.target as HTMLInputElement;
    const parent = input.parentElement as HTMLDivElement;
    const chips = parent.querySelectorAll('ion-chip');

    if (chips.length > 0) {
      const chip = chips[chips.length - 1];

      if (chip.outline === false) {
        chip.outline = true;
      }
    }
  }

  clearErrorMessages(field: string) {
    if (field == 'minSize') {
      if (this.searchParameters.minSize == null) {
        this.searchParametersErrors.minSize.length = 0;
        this.searchParametersErrors.maxSize.length = 0;
      } else if (
        this.searchParameters.maxSize == null ||
        this.searchParameters.minSize <= this.searchParameters.maxSize
      ) {
        this.searchParametersErrors.minSize.length = 0;
        this.searchParametersErrors.maxSize.length = 0;
      }
    } else if (field == 'maxSize') {
      if (this.searchParameters.maxSize == null) {
        this.searchParametersErrors.minSize.length = 0;
        this.searchParametersErrors.maxSize.length = 0;
      } else if (
        this.searchParameters.minSize == null ||
        this.searchParameters.minSize <= this.searchParameters.maxSize
      ) {
        this.searchParametersErrors.minSize.length = 0;
        this.searchParametersErrors.maxSize.length = 0;
      }
    } else ((this.searchParametersErrors as any)[field] as string[]).length = 0;
  }

  async launchSearch() {
    this.isSearchRunning = true;
    var error = await this.searchService.startConnection();
    if (error == '') {
      console.log('Connection started');
      this.searchService.addNotificationListener();

      this.searchSubsription = this.searchService
        .launchSearch(this.searchParameters)
        .subscribe(async (result) => {
          var error = await this.searchService.stopConnection();

          if (error == '') console.log('Connection stopped');
          else console.log(`Connection not stopped ${error}`);

          this.isSearchRunning = false;

          if (result.duplicatesGroups != null) {
            this.searchService.sendResults(result.duplicatesGroups as File[][]);

            this.searchParametersErrors = new SearchParametersErrors();
          } else {
            for (let key of Object.keys(this.searchParametersErrors)) {
              if (result[key] != null) {
                (this.searchParametersErrors as any)[key] = result[key];
              } else {
                (this.searchParametersErrors as any)[key] = [];
              }
            }

            this.searchService.sendSearchParameters(null);
          }

          this.cd.markForCheck();
        });
    } else console.log(`Connection not started : ${error}`);
  }

  cancelSearch() {
    this.searchSubsription?.unsubscribe();
    this.searchService.stopConnection();
    this.searchService.sendSearchParameters(null);
    this.isSearchRunning = false;
  }
}
