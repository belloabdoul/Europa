import { KeyValuePipe } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
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
} from '@ionic/angular/standalone';
import { FormsModule } from '@angular/forms';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  FileSearchType,
  FileSearchTypeToLabelMapping,
} from 'src/app/shared/models/file-search-type';
import { SearchParameters } from 'src/app/shared/models/search-parameters';
import { addIcons } from 'ionicons';
import { add, close, closeCircle, create, save } from 'ionicons/icons';

@Component({
  selector: 'app-search-form',
  templateUrl: './search-form.component.html',
  styleUrls: ['./search-form.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
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
    KeyValuePipe,
    IonSelectOption,
    IonToggle,
    MatTooltipModule,
    FormsModule,
  ],
})
export class SearchFormComponent implements OnInit {
  searchParameters: SearchParameters;
  maxValue: number = Number.MAX_SAFE_INTEGER;

  // Handle the presentation for the enum in the select
  fileSearchType: typeof FileSearchType = FileSearchType;

  fileSearchTypeLabels: (string | FileSearchType)[] = Object.values(
    this.fileSearchType
  ).filter((value) => {
    console.log(typeof value);
    return typeof value == 'string';
  });

  fileSearchTypeMapper: typeof FileSearchTypeToLabelMapping =
    FileSearchTypeToLabelMapping;

  constructor() {
    addIcons({ add, close, closeCircle, create, save });

    this.searchParameters = new SearchParameters();
  }

  ngOnInit(): void {}

  addExtension(event: Event): void {
    const input = event.target as HTMLInputElement;
    const value = (input.value || '').trim();
    var extensionsType = input.closest('ion-input')?.id;
    var added = false;

    // Add our extension
    if (value) {
      if (
        extensionsType == 'extensionsToInclude' &&
        !this.searchParameters.includedFileTypes.has(value)
      ) {
        this.searchParameters.includedFileTypes.add(value);
        added = true;
      } else if (
        extensionsType == 'extensionsToExclude' &&
        !this.searchParameters.excludedFileTypes.has(value)
      ) {
        this.searchParameters.excludedFileTypes.add(value);
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
      return this.searchParameters.includedFileTypes.delete(extension);
    } else if (extensionsType == 'extensionsToExclude') {
      return this.searchParameters.excludedFileTypes.delete(extension);
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
}
