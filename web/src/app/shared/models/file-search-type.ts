export enum FileSearchType {
  All,
  Audios,
  Images,
}

export const FileSearchTypeToLabelMapping: Record<
  FileSearchType | string,
  FileSearchType
> = {
  [FileSearchType.All]: FileSearchType.All,
  [FileSearchType.Audios]: FileSearchType.Audios,
  [FileSearchType.Images]: FileSearchType.All,
};
