import { FileSearchType } from './file-search-type';

export class SearchParameters {
  folders: string[] = [];
  fileSearchType: FileSearchType | null = null;
  degreeOfSimilarity: number | null = null;
  includeSubFolders: boolean = true;
  minSize: number | null = null;
  maxSize: number | null = null;
  includedFileTypes: string[] = [];
  excludedFileTypes: string[] = [];
}
