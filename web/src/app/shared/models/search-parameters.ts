import { FileSearchType } from './file-search-type';

export class SearchParameters {
  folders: string[] = [];
  includeSubFolders: boolean = true;
  fileSearchType: FileSearchType | null = null;
  minSize: number | null = null;
  maxSize: number | null = null;
  degreeOfSimilarity: number | null = null;
  includedFileTypes: string[] = [];
  excludedFileTypes: string[] = [];
}
