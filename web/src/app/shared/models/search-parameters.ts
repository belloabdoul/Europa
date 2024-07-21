import { FileSearchType } from './file-search-type';

export class SearchParameters {
  folders: Set<string> = new Set();
  includeSubFolders: boolean = true;
  fileSearchType: FileSearchType = FileSearchType.All;
  minSize: number | null = null;
  maxSize: number | null = null;
  degreeOfSimilarity: number | null = null;
  includedFileTypes: Set<string> = new Set();
  excludedFileTypes: Set<string> = new Set();
}
