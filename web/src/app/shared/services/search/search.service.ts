import { Injectable } from '@angular/core';
import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  JsonHubProtocol,
} from '@microsoft/signalr';
import { catchError, Observable, Subject, of } from 'rxjs';
import { Notification } from '../../models/notification';
import { SearchParameters } from '../../models/search-parameters';
import { HttpClient } from '@angular/common/http';
import { File } from '../../models/file';

@Injectable({
  providedIn: 'root',
})
export class SearchService {
  private connection: HubConnection | undefined;

  private notification: Subject<Notification> = new Subject();
  public notification$: Observable<Notification> =
    this.notification.asObservable();

  private searchParameters: Subject<SearchParameters | null> = new Subject();
  public searchParameters$: Observable<SearchParameters | null> =
    this.searchParameters.asObservable();

  private similarFiles: Subject<File[][]> = new Subject();
  public similarFiles$: Observable<File[][]> = this.similarFiles.asObservable();

  private apiUrl: string = 'https://localhost:7001/';
  private duplicatesApiUrl: string = `${this.apiUrl}duplicates/`;

  constructor(private http: HttpClient) {}

  sendSearchParameters(searchParameters: SearchParameters | null): void {
    this.searchParameters.next(searchParameters);
  }

  async startConnection(): Promise<any> {
    const url = `${this.apiUrl}notifications/`;
    this.connection = new HubConnectionBuilder()
      .withUrl(url, {
        transport: HttpTransportType.WebSockets,
      })
      .withHubProtocol(new JsonHubProtocol())
      .withStatefulReconnect()
      .build();

    try {
      await this.connection.start();
      return '';
    } catch (error: any) {
      return error;
    }
  }

  addNotificationListener() {
    this.connection!.on('notify', (notification: Notification) => {
      this.notification.next(notification);
    });
  }

  async stopConnection(): Promise<any> {
    try {
      await this.connection?.stop();
      return '';
    } catch (error: any) {
      return error;
    }
  }

  launchSearch(searchParameters: SearchParameters | null): Observable<any> {
    this.sendSearchParameters(searchParameters);
    this.sendResults([]);
    const url = `${this.duplicatesApiUrl}findDuplicates`;
    return this.http.post<any>(url, searchParameters).pipe(
      catchError((error) => {
        return of(error.error);
      })
    );
  }

  sendResults(similarFiles: File[][]): void {
    this.similarFiles.next(similarFiles);
  }
}
