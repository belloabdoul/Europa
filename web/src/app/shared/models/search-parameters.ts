import { FileSearchType } from './file-search-type';
import { PerceptualHashAlgorithm } from './perceptual-hash-algorithm';

export class SearchParameters {
  folders: string[] = [];
  fileSearchType: FileSearchType | null = null;
  degreeOfSimilarity: number | null = null;
  perceptualHashAlgorithm: PerceptualHashAlgorithm | null = null;
  includeSubFolders: boolean = true;
  minSize: number | null = null;
  maxSize: number | null = null;
  includedFileTypes: string[] = [];
  excludedFileTypes: string[] = [];
}
