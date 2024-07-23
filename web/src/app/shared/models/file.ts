export class File {
  name: string = '';
  type: string = '';
  path: string = '';
  size: number = 0;
  dateModified: Date = new Date();
  isLastInGroup: boolean = false;
  delete: boolean = false;
}
